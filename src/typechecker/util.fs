(*
   Copyright 2008-2014 Nikhil Swamy and Microsoft Research

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*)
#light "off"
// (c) Microsoft Corporation. All rights reserved

module FStar.TypeChecker.Util
open FStar
open FStar.Util
open FStar.TypeChecker
open FStar.Syntax
open FStar.TypeChecker.Env
open FStar.TypeChecker.Rel
open FStar.Syntax.Syntax
open FStar.Ident
open FStar.Syntax.Subst
open FStar.TypeChecker.Common

module SS = FStar.Syntax.Subst
module S = FStar.Syntax.Syntax
module U = FStar.Syntax.Util
module N = FStar.TypeChecker.Normalize
module P = FStar.Syntax.Print

//Reporting errors
let report env errs =
    Errors.report (Env.get_range env)
                  (Errors.failed_to_prove_specification errs)

(************************************************************************)
(* Unification variables *)
(************************************************************************)
let is_type t = match (compress t).n with 
    | Tm_type _ -> true
    | _ -> false
                
let t_binders env = 
    Env.binders env |> List.filter (fun (x, _) -> is_type x.sort)

//new unification variable
let new_uvar_aux env k = 
    let bs = if !Options.full_context_dependency
             then Env.binders env 
             else t_binders env in
    Rel.new_uvar (Env.get_range env) bs k

let new_uvar env k = fst (new_uvar_aux env k)

let as_uvar : typ -> uvar = function
    | {n=Tm_uvar(uv, _)} -> uv
    | _ -> failwith "Impossible"

let new_implicit_var env k =
    let t, u = new_uvar_aux env k in
    let g = {Rel.trivial_guard with implicits=[(env, as_uvar u, t, k, u.pos)]} in
    t, (as_uvar u, u.pos), g

let check_uvars r t =
  let uvs = Free.uvars t in
  if not (Util.set_is_empty uvs)
  then
    let us = List.map (fun (x, _) -> Print.uvar_to_string x) (Util.set_elements uvs) |> String.concat ", " in
    (* ignoring the hide_uvar_nums and print_implicits flags here *)
    let hide_uvar_nums_saved = !Options.hide_uvar_nums in
    let print_implicits_saved = !Options.print_implicits in
    Options.hide_uvar_nums := false;
    Options.print_implicits := true;
    Errors.report r
      (Util.format2 "Unconstrained unification variables %s in type signature %s; \
       please add an annotation" us (Print.term_to_string t));
    Options.hide_uvar_nums := hide_uvar_nums_saved;
    Options.print_implicits := print_implicits_saved


(************************************************************************)
(* Extracting annotations from a term *)
(************************************************************************)
let force_sort' s = match !s.tk with
    | None -> failwith (Util.format2 "(%s) Impossible: Forced tk not present on %s" (Range.string_of_range s.pos) (Print.term_to_string s))
    | Some tk -> tk
    
let force_sort s = mk (force_sort' s) None s.pos

let extract_let_rec_annotation env {lbunivs=univ_vars; lbtyp=t; lbdef=e} = 
  match t.n with
   | Tm_unknown ->
     if univ_vars <> [] then failwith "Impossible: non-empty universe variables but the type is unknown";
     let r = Env.get_range env in
     let mk_binder scope a = match a.sort.n with
        | Tm_unknown ->
          let k, _ = U.type_u() in
          let t =  Rel.new_uvar e.pos scope k |> fst in 
          {a with sort=t}, false
        | _ -> a, true in

    let rec aux vars e : typ * bool =
      match e.n with
      | Tm_meta(e, _) -> aux vars e
      | Tm_ascribed(e, t, _) -> t, true

      | Tm_abs(bs, body) ->
        let scope, bs, check = bs |> List.fold_left (fun (scope, bs, check) (a, imp) -> 
              let tb, c = mk_binder scope a in
              let b = (tb, imp) in
              let bs = bs@[b] in
              let scope = scope@[b] in
              scope, bs, c || check)
           (vars,[],false) in

        let res, check_res = aux scope body in
        let c = Util.ml_comp res r in //let rec without annotations default to being in the ML monad; TODO: revisit this
        let t = Util.arrow bs c in 
        if debug env Options.High then Util.print2 "(%s) Using type %s\n" (Range.string_of_range r) (Print.term_to_string t);
        t, check_res || check

      | _ ->Rel.new_uvar r vars Util.ktype0 |> fst, false in

     let t, b = aux (t_binders env)  e in 
     [], t, b

  | _ -> 
    let univ_vars, t = open_univ_vars univ_vars t in 
    univ_vars, t, false

(************************************************************************)
(* Utilities on patterns  *)
(************************************************************************)
let is_implicit = function Some Implicit -> true | _ -> false
let as_imp = function
    | Some Implicit -> true
    | _ -> false

(*
    Turns a (possibly disjunctive) pattern p into a triple:
 *)
let pat_as_exps allow_implicits env p
                        : (list<bv>          (* pattern-bound variables (which may appear in the branch of match) *)
                         * list<term>        (* expressions corresponding to each arm of the disjunct *)
                         * pat)   =          (* decorated pattern, with all the missing implicit args in p filled in *)
  
      let rec pat_as_arg_with_env allow_wc_dependence env (p:pat) :
                                    (list<bv>    //all pattern-bound vars including wild-cards, in proper order
                                    * list<bv>   //just the accessible vars, for the disjunctive pattern test
                                    * list<bv>   //just the wildcards
                                    * Env.env    //env extending with the pattern-bound variables
                                    * term       //the pattern as a term/typ
                                    * pat) =     //the elaborated pattern itself
        match p.v with
           | Pat_constant c ->
             let e = mk (Tm_constant c) None p.p in
             ([], [], [], env, e, p)

           | Pat_dot_term(x, _) ->
             let k, _ = Util.type_u () in
             let t = new_uvar env k in
             let x = {x with sort=t} in
             let e, u = Rel.new_uvar p.p (Env.binders env) t in
             let p = {p with v=Pat_dot_term(x, e)} in
             ([], [], [], env, e, p)

           | Pat_wild x ->
             let t, _ = Util.type_u() in
             let x = {x with sort=new_uvar env t} in
             let env = if allow_wc_dependence then Env.push_bv env x else env in
             let e = mk (Tm_name x) None p.p in
             ([x], [], [x], env, e, p)

           | Pat_var x ->
             let t, _ = Util.type_u() in
             let x = {x with sort=new_uvar env t} in
             let env = Env.push_bv env x in
             let e = mk (Tm_name x) None p.p in
             ([x], [x], [], env, e, p)

           | Pat_cons(fv, pats) ->
               let (b, a, w, env, args, pats) = pats |> List.fold_left (fun (b, a, w, env, args, pats) (p, imp) ->
                   let (b', a', w', env, te, pat) = pat_as_arg_with_env allow_wc_dependence env p in
                   let arg = if imp then iarg te else arg te in
                   (b'::b, a'::a, w'::w, env, arg::args, (pat, imp)::pats))  
                 ([], [], [], env, [], []) in
               let e = mk (Tm_meta(mk_Tm_app (Syntax.fv_to_tm fv) (args |> List.rev) None p.p, Meta_desugared Data_app)) None p.p in
               (List.rev b |> List.flatten,
                List.rev a |> List.flatten,
                List.rev w |> List.flatten,
                env,
                e,
                {p with v=Pat_cons(fv, List.rev pats)})

           | Pat_disj _ -> failwith "impossible" in

    let rec elaborate_pat env p = //Adds missing implicit patterns to constructor patterns
        let maybe_dot a r = 
            if allow_implicits
            then withinfo (Pat_dot_term(a, tun)) tun.n r
            else withinfo (Pat_var a) tun.n r in
        match p.v with
           | Pat_cons(fv, pats) ->
               let pats = List.map (fun (p, imp) -> elaborate_pat env p, imp) pats in
               let _, t = Env.lookup_datacon env (fst fv).v in
               let f, _ = Util.arrow_formals t in
               let rec aux formals pats = match formals, pats with
                | [], [] -> []
                | [], _::_ -> raise (Error("Too many pattern arguments", range_of_lid (fst fv).v))
                | _::_, [] -> //fill the rest with dot patterns (if allowed), if all the remaining formals are implicit
                    formals |> List.map (fun (t, imp) -> match imp with 
                        | Some Implicit ->
                          let a = Syntax.new_bv (Some (Syntax.range_of_bv t)) tun in
                          let r = range_of_lid (fst fv).v in
                          maybe_dot a r, true

                        | _ -> 
                          raise (Error(Util.format1 "Insufficient pattern arguments (%s)" (Print.pat_to_string p), range_of_lid (fst fv).v))) 

                | f::formals', (p, p_imp)::pats' ->
                    begin match f with
                        | (_, Some Implicit) when p_imp ->
                            (p, true)::aux formals' pats'

                        | (_, Some Implicit) ->
                            let a = Syntax.new_bv (Some p.p) tun in 
                            let p = maybe_dot a (range_of_lid (fst fv).v) in
                            (p, true)::aux formals' pats

                        | (_, imp) ->
                            (p, as_imp imp)::aux formals' pats'
                    end in
               {p with v=Pat_cons(fv, aux f pats)}

        | _ -> p in

    let one_pat allow_wc_dependence env p =
        let p = elaborate_pat env p in
        let b, a, w, env, arg, p = pat_as_arg_with_env allow_wc_dependence env p in
        match b |> Util.find_dup bv_eq with
            | Some x -> raise (Error(Errors.nonlinear_pattern_variable x, p.p))
            | _ -> b, a, w, arg, p in

   let top_level_pat_as_args env (p:pat) : (list<bv>                    (* pattern bound variables *)
                                            * list<arg>                 (* pattern sub-terms *)
                                            * pat)  =                   (* decorated pattern *)
        match p.v with
           | Pat_disj [] -> failwith "impossible"

           | Pat_disj (q::pats) ->
              let b, a, _, te, q = one_pat false env q in //in disjunctive patterns, the wildcards are not accessible even for typing
              let w, args, pats = List.fold_right (fun p (w, args, pats) ->
                  let b', a', w', arg, p = one_pat false env p in
                  if not (Util.multiset_equiv bv_eq a a')
                  then raise (Error(Errors.disjunctive_pattern_vars a a', Env.get_range env))
                  else (w'@w, S.arg arg::args, p::pats))
                  pats ([], [], []) in
              b@w, S.arg te::args, {p with v=Pat_disj(q::pats)}

           | _ ->
             let b, _, _, arg, p = one_pat true env p in //in single pattersn, the wildcards are available, at least for typing
             b, [S.arg arg], p in

    let b, args, p = top_level_pat_as_args env p in
    let exps = args |> List.map fst in
    b, exps, p

let decorate_pattern env p exps =
    let qq = p in
    let rec aux p e : pat  =
        let pkg q t = withinfo q t p.p in
        let e = Util.unmeta e in
        match p.v, e.n with
            | _, Tm_uinst(e, _) -> aux p e

            | Pat_constant _, Tm_constant _ -> 
              pkg p.v (force_sort' e)

            | Pat_var x, Tm_name y ->
              if not (bv_eq x y)
              then failwith (Util.format2 "Expected pattern variable %s; got %s" (Print.bv_to_string x) (Print.bv_to_string y));
              if Env.debug env <| Options.Other "Pat"
              then Util.print2 "Pattern variable %s introduced at type %s\n" (Print.bv_to_string x) (Normalize.term_to_string env y.sort);
              let s = Normalize.normalize [Normalize.Beta] env y.sort in
              let x = {x with sort=s} in
              pkg (Pat_var x) s.n

            | Pat_wild x, Tm_name y ->
              if bv_eq x y |> not
              then failwith (Util.format2 "Expected pattern variable %s; got %s" (Print.bv_to_string x) (Print.bv_to_string y));
              let s = Normalize.normalize [Normalize.Beta] env y.sort in
              let x = {x with sort=s} in
              pkg (Pat_wild x) s.n

            | Pat_dot_term(x, _), _ ->
              let s = force_sort e in
              let x = {x with sort=s} in
              pkg (Pat_dot_term(x, e)) s.n

            | Pat_cons(fv, []), Tm_fvar fv' ->
              if not (Syntax.fv_eq fv fv')
              then failwith (Util.format2 "Expected pattern constructor %s; got %s" (fst fv).v.str (fst fv').v.str);
              pkg (Pat_cons(fv', [])) (force_sort' e)

            | Pat_cons(fv, argpats), Tm_app({n=Tm_fvar(fv')}, args) 
            | Pat_cons(fv, argpats), Tm_app({n=Tm_uinst({n=Tm_fvar(fv')}, _)}, args) ->

              if fv_eq fv fv' |> not
              then failwith (Util.format2 "Expected pattern constructor %s; got %s" (fst fv).v.str (fst fv').v.str);

              let fv = fv' in
              let rec match_args matched_pats args argpats = match args, argpats with
                | [], [] -> pkg (Pat_cons(fv, List.rev matched_pats)) (force_sort' e)
                | arg::args, (argpat, _)::argpats ->
                  begin match arg, argpat.v with
                        | (e, Some Implicit), Pat_dot_term _ -> (* implicit value argument *)
                          let x = Syntax.new_bv (Some p.p) (force_sort e) in
                          let q = withinfo (Pat_dot_term(x, e)) x.sort.n p.p in
                          match_args ((q, true)::matched_pats) args argpats
 
                        | (e, imp), _ ->
                          let pat = aux argpat e, as_imp imp in
                          match_args (pat::matched_pats) args argpats
                 end

                | _ -> failwith (Util.format2 "Unexpected number of pattern arguments: \n\t%s\n\t%s\n" (Print.pat_to_string p) (Print.term_to_string e)) in

              match_args [] args argpats

           | _ -> failwith (Util.format3 "(%s) Impossible: pattern to decorate is %s; expression is %s\n" (Range.string_of_range qq.p) (Print.pat_to_string qq)
                    (exps |> List.map Print.term_to_string |> String.concat "\n\t")) in

    match p.v, exps with
        | Pat_disj ps, _ when (List.length ps = List.length exps) ->
          let ps = List.map2 aux ps exps in
          withinfo (Pat_disj ps) tun.n p.p

        | _, [e] ->
          aux p e

        | _ -> failwith "Unexpected number of patterns"

 let rec decorated_pattern_as_term (pat:pat) : list<bv> * term =
    let topt = Some pat.sort in
    let mk f : term = mk f topt pat.p in

    let pat_as_arg (p, i) =
        let vars, te = decorated_pattern_as_term p in
        vars, (te, as_implicit i) in

    match pat.v with
        | Pat_disj _ -> failwith "Impossible" (* these are only on top-level patterns *)

        | Pat_constant c ->
          [], mk (Tm_constant c)

        | Pat_wild x
        | Pat_var x  ->
          [x], mk (Tm_name x)

        | Pat_cons(fv, pats) ->
            let vars, args = pats |> List.map pat_as_arg |> List.unzip in
            let vars = List.flatten vars in
            vars,  mk (Tm_app(Syntax.fv_to_tm fv, args))

        | Pat_dot_term(x, e) ->
            [], e

//DTuple u1 (\_:u1. u2) (\_:u1 u2. u3) ...
// where ui:Type(i)?
let mk_basic_dtuple_type env n =
  let r = Env.get_range env in
  let l = Util.mk_dtuple_lid n r in
  let us, k = Env.lookup_lid env l in
  let t = Syntax.mk_Tm_uinst (Syntax.fvar None l r) us in
  let vars = Env.binders env in
 
  match k.n with
    | Tm_arrow(bs, c) -> 
        let bs, _ = Subst.open_comp bs c in 
        let args, _ = bs |> List.fold_left (fun (out, subst) (a, _) ->
          let k = Subst.subst subst a.sort in
          let arg = match k.n with
            | Tm_type _ -> 
              Rel.new_uvar r vars k |> fst

            | Tm_arrow(bs, {n=Total k}) ->
              Util.abs bs (Rel.new_uvar r vars Util.ktype0 |> fst)  //NS: Which universe to pick?

            | _ -> failwith "Impossible" in
          let subst = NT(a, arg)::subst in
          (S.arg arg::out, subst)) ([], []) in
      mk_Tm_app t (List.rev args) None r

    | _ -> failwith "Impossible"

(*********************************************************************************************)
(* Utils related to monadic computations *)
(*********************************************************************************************)
type lcomp_with_binder = option<bv> * lcomp

let destruct_comp c : (typ * typ * typ) =
  let wp, wlp = match c.effect_args with
    | [(wp, _); (wlp, _)] -> wp, wlp
    | _ -> failwith (Util.format2 "Impossible: Got a computation %s with effect args [%s]" c.effect_name.str
      (List.map (fun (x, _) -> Print.term_to_string x) c.effect_args |> String.concat ", ")) in
  c.result_typ, wp, wlp

let lift_comp c m lift =
  let _, wp, wlp = destruct_comp c in
  {effect_name=m;
   result_typ=c.result_typ;
   effect_args=[arg (lift c.result_typ wp); arg (lift c.result_typ wlp)];
   flags=[]}

let norm_eff_name =
   let cache = Util.smap_create 20 in
   fun env (l:lident) ->
       let rec find l =
           match Env.lookup_effect_abbrev env l with
            | None -> None
            | Some (_, c) ->
                let l = (Util.comp_to_comp_typ c).effect_name in
                match find l with
                    | None -> Some l
                    | Some l' -> Some l' in
       let res = match Util.smap_try_find cache l.str with
            | Some l -> l
            | None ->
              begin match find l with
                        | None -> l
                        | Some m -> Util.smap_add cache l.str m;
                                    m
              end in
       res


let join_effects env l1 l2 =
  let m, _, _ = Env.join env (norm_eff_name env l1) (norm_eff_name env l2) in
  m

let join_lcomp env c1 c2 =
  if Util.is_total_lcomp c1
  && Util.is_total_lcomp c2
  then Const.effect_Tot_lid
  else join_effects env c1.eff_name c2.eff_name

let lift_and_destruct env c1 c2 =
  let c1 = Normalize.unfold_effect_abbrev env c1 in
  let c2 = Normalize.unfold_effect_abbrev env c2 in
  let m, lift1, lift2 = Env.join env c1.effect_name c2.effect_name in
  let m1 = lift_comp c1 m lift1 in
  let m2 = lift_comp c2 m lift2 in
  let md = Env.get_effect_decl env m in
  let a, kwp = Env.wp_signature env md.mname in
  (md, a, kwp), (destruct_comp m1), destruct_comp m2

let is_pure_effect env l =
  let l = norm_eff_name env l in
  lid_equals l Const.effect_PURE_lid

let is_pure_or_ghost_effect env l =
  let l = norm_eff_name env l in
  lid_equals l Const.effect_PURE_lid
  || lid_equals l Const.effect_GHOST_lid

let mk_comp md result wp wlp flags =
  mk_Comp ({effect_name=md.mname;
             result_typ=result;
             effect_args=[S.arg wp; S.arg wlp];
             flags=flags})

let lcomp_of_comp c0 =
    let c = Util.comp_to_comp_typ c0 in
    {eff_name = c.effect_name;
     res_typ = c.result_typ;
     cflags = c.flags;
     comp = fun() -> c0}

let subst_lcomp subst lc =
    {lc with res_typ=SS.subst subst lc.res_typ;
             comp=fun () -> SS.subst_comp subst (lc.comp())}

let is_function t = match (compress t).n with
    | Tm_arrow _ -> true
    | _ -> false

let return_value env t v =
//  if is_function t then failwith (Util.format1 "(%s): Returning a function!" (Range.string_of_range (Env.get_range env)));
  let c = match Env.lookup_effect_abbrev env Const.effect_GTot_lid with 
    | None -> mk_Total t //we're still in prims, not yet having fully defined the primitive effects
    | _ -> 
       let m = must (Env.effect_decl_opt env Const.effect_PURE_lid) in //if Tot isn't fully defined in prims yet, then just return (Total t)
       let a, kwp = Env.wp_signature env Const.effect_PURE_lid in
       let k = SS.subst [NT(a, t)] kwp in
       let wp = N.normalize [N.Beta] env (mk_Tm_app (inst_effect_fun env m m.ret) [S.arg t; S.arg v] (Some k.n) v.pos) in
       let wlp = wp in
       mk_comp m t wp wlp [RETURN] in
  if debug env <| Options.Other "Return"
  then Util.print3 "(%s) returning %s at comp type %s\n" 
                    (Range.string_of_range v.pos)  (P.term_to_string v) (N.comp_to_string env c);
  c

let bind env e1opt (lc1:lcomp) ((b, lc2):lcomp_with_binder) : lcomp =
  if debug env Options.Extreme
  then
    (let bstr = match b with
      | None -> "none"
      | Some x -> Print.bv_to_string x in
    Util.print4 "Before lift: Making bind (e1=%s)@c1=%s\nb=%s\t\tc2=%s\n" 
        (match e1opt with None -> "None" | Some e -> Print.term_to_string e)
        (Print.lcomp_to_string lc1) bstr (Print.lcomp_to_string lc2));
  let bind_it () =
      let c1 = lc1.comp () in
      let c2 = lc2.comp () in
      if debug env Options.Extreme
      then Util.print5 "b=%s,Evaluated %s to %s\n And %s to %s\n"
            (match b with
              | None -> "none"
              | Some x -> Print.bv_to_string x)
            (Print.lcomp_to_string lc1)
            (Print.comp_to_string c1)
            (Print.lcomp_to_string lc2)
            (Print.comp_to_string c2);
      let try_simplify () =
        let aux () =
            if Util.is_trivial_wp c1
            then match b with
                    | None -> Some (c2, "trivial no binder")
                    | Some _ -> 
                        if Util.is_ml_comp c2 //|| not (Util.is_free [Inr x] (Util.freevars_comp c2))
                        then Some (c2, "trivial ml")
                        else None
            else if Util.is_ml_comp c1 && Util.is_ml_comp c2
            then Some (c2, "both ml")
            else None in
        if Util.is_total_comp c1
        && Util.is_total_comp c2
        then Some (c2, "both total")
        else if Util.is_tot_or_gtot_comp c1
             && Util.is_tot_or_gtot_comp c2
        then Some (S.mk_GTotal (Util.comp_result c2), "both gtot")
        else match e1opt, b with
            | Some e, Some x ->
                if Util.is_total_comp c1 && not (Syntax.is_null_bv x)
                then Some (SS.subst_comp [NT(x, e)] c2, "substituted e")
                else aux ()
            | _ -> aux () in
      match try_simplify () with
        | Some (c, reason) ->
          if Env.debug env <| Options.Other "bind"
          then Printf.printf "%s, %A, %A: bind (%s) %s and %s simplified to %s\n"
               reason (Util.comp_flags c1) (Util.comp_flags c2)
              (match b with
                 | None -> "None"
                 | Some x -> Print.bv_to_string x)
            (Print.comp_to_string c1) (Print.comp_to_string c2) (Print.comp_to_string c);
          c
        | None ->
          let (md, a, kwp), (t1, wp1, wlp1), (t2, wp2, wlp2) = lift_and_destruct env c1 c2 in
          let bs = match b with
            | None -> [null_binder t1]
            | Some x -> [S.mk_binder x] in
          let mk_lam wp = U.abs bs wp in
          let wp_args = [S.arg t1; S.arg t2; S.arg wp1; S.arg wlp1; S.arg (mk_lam wp2); S.arg (mk_lam wlp2)] in
          let wlp_args = [S.arg t1; S.arg t2; S.arg wlp1; S.arg (mk_lam wlp2)] in
          let k = SS.subst [NT(a, t2)] kwp in
          let wp = mk_Tm_app  (inst_effect_fun env md md.bind_wp)  wp_args None t2.pos in
          let wlp = mk_Tm_app (inst_effect_fun env md md.bind_wlp) wlp_args None t2.pos in
          let c = mk_comp md t2 wp wlp [] in
          if Env.debug env <| Options.Other "bind"
          then Printf.printf "unsimplified bind %s and %s\n\tproduced %s\n"
              (Print.comp_to_string c1) 
              (Print.comp_to_string c2) 
              (Print.comp_to_string c);
          c in
    {eff_name=join_lcomp env lc1 lc2;
     res_typ=lc2.res_typ;
     cflags=[];
     comp=bind_it}

let lift_formula env t mk_wp mk_wlp f =
  let md_pure = Env.get_effect_decl env Const.effect_PURE_lid in
  let a, kwp = Env.wp_signature env md_pure.mname in
  let k = SS.subst [NT(a, t)] kwp in
  let wp = mk_Tm_app mk_wp   [S.arg t; S.arg f] (Some k.n) f.pos in
  let wlp = mk_Tm_app mk_wlp [S.arg t; S.arg f] (Some k.n) f.pos in
  mk_comp md_pure Recheck.t_unit wp wlp [] 

let unlabel t = mk (Tm_meta(t, Meta_refresh_label (None, t.pos))) None t.pos 

let refresh_comp_label env (b:bool) lc =
    let refresh () =
        let c = lc.comp () in
        if Util.is_ml_comp c then c
        else match c.n with
        | Total _ 
        | GTotal _ -> c
        | Comp ct ->
          if Env.debug env Options.Low
          then (Util.print1 "Refreshing label at %s\n" (Range.string_of_range <| Env.get_range env));
          let c' = Normalize.unfold_effect_abbrev env c in
          if not <| lid_equals ct.effect_name c'.effect_name && Env.debug env Options.Low
          then Util.print2 "To refresh, normalized\n\t%s\nto\n\t%s\n" (Print.comp_to_string c) (Print.comp_to_string <| mk_Comp c');
          let t, wp, wlp = destruct_comp c' in
          let wp = mk (Tm_meta(wp, Meta_refresh_label(Some b, Env.get_range env))) None wp.pos in
          let wlp = mk (Tm_meta(wlp, Meta_refresh_label(Some b, Env.get_range env))) None wlp.pos in
          Syntax.mk_Comp ({c' with effect_args=[S.arg wp; S.arg wlp]; flags=c'.flags}) in
    {lc with comp=refresh}

let label reason r f : term =
    mk (Tm_meta(f, Meta_labeled(reason, r, true))) None f.pos

let label_opt env reason r f = match reason with
    | None -> f
    | Some reason ->
        if not <| Options.should_verify env.curmodule.str
        then f
        else label (reason()) r f

let label_guard reason r g = match g with
    | Trivial -> g
    | NonTrivial f -> NonTrivial (label reason r f)

let weaken_guard g1 g2 = match g1, g2 with
    | NonTrivial f1, NonTrivial f2 ->
      let g = (Util.mk_imp f1 f2) in
      NonTrivial g
    | _ -> g2

let weaken_precondition env lc (f:guard_formula) : lcomp =
  let weaken () =
      let c = lc.comp () in
      match f with
      | Trivial -> c
      | NonTrivial f ->
        if Util.is_ml_comp c
        then c
        else let c = Normalize.unfold_effect_abbrev env c in
             let res_t, wp, wlp = destruct_comp c in
             let md = Env.get_effect_decl env c.effect_name in
             let wp = mk_Tm_app (inst_effect_fun env md md.assume_p)  [S.arg res_t; S.arg f; S.arg wp]  None wp.pos in
             let wlp = mk_Tm_app (inst_effect_fun env md md.assume_p) [S.arg res_t; S.arg f; S.arg wlp] None wlp.pos in
             mk_comp md res_t wp wlp c.flags in
  {lc with comp=weaken}

let strengthen_precondition (reason:option<(unit -> string)>) env (e:term) (lc:lcomp) (g0:guard_t) : lcomp * guard_t =
    if Rel.is_trivial g0 
    then lc, g0
    else
        let _ = if Env.debug env <| Options.Extreme
                then Util.print2 "+++++++++++++Strengthening pre-condition of term %s with guard %s\n" 
                                (N.term_to_string env e)
                                (Rel.guard_to_string env g0) in
        let flags = lc.cflags |> List.collect (function RETURN | PARTIAL_RETURN -> [PARTIAL_RETURN] | _ -> []) in
        let strengthen () =
            let c = lc.comp () in
            let g0 = Rel.simplify_guard env g0 in
            match guard_form g0 with
                | Trivial -> c
                | NonTrivial f ->
                let c =
                    if true 
                    || (Util.is_pure_or_ghost_comp c
                    && not (is_function (Util.comp_result c))
                    && not (Util.is_partial_return c))
                    then let x = S.gen_bv "strengthen_pre_x" None (Util.comp_result c) in
                         let xret = Util.comp_set_flags (return_value env x.sort (S.bv_to_name x)) [PARTIAL_RETURN] in
                         let lc = bind env (Some e) (lcomp_of_comp c) (Some x, lcomp_of_comp xret) in
                         lc.comp()
                    else c in

                if Env.debug env <| Options.Extreme
                then Util.print2 "-------------Strengthening pre-condition of term %s with guard %s\n" 
                                (N.term_to_string env e)
                                (N.term_to_string env f);

                let c = Normalize.unfold_effect_abbrev env c in
                let res_t, wp, wlp = destruct_comp c in
                let md = Env.get_effect_decl env c.effect_name in
                let wp =  mk_Tm_app (inst_effect_fun env md md.assert_p) [S.arg res_t; S.arg <| label_opt env reason (Env.get_range env) f; S.arg wp] None wp.pos in
                let wlp = mk_Tm_app (inst_effect_fun env md md.assume_p) [S.arg res_t; S.arg f; S.arg wlp] None wlp.pos in

                if Env.debug env <| Options.Extreme
                then Util.print1 "-------------Strengthened pre-condition is %s\n"
                                (Print.term_to_string wp);


                let c2 = mk_comp md res_t wp wlp flags in
                c2 in
       {lc with eff_name=norm_eff_name env lc.eff_name;
                cflags=(if Util.is_pure_lcomp lc && not <| Util.is_function_typ lc.res_typ then flags else []);
                comp=strengthen},
       {g0 with guard_f=Trivial}

let add_equality_to_post_condition env (comp:comp) (res_t:typ) =
    let md_pure = Env.get_effect_decl env Const.effect_PURE_lid in
    let x = S.new_bv None res_t in
    let y = S.new_bv None res_t in
    let xexp, yexp = S.bv_to_name x, S.bv_to_name y in
    let yret = mk_Tm_app (inst_effect_fun env md_pure md_pure.ret) [S.arg res_t; S.arg yexp] None res_t.pos in
    let x_eq_y_yret = mk_Tm_app (inst_effect_fun env md_pure md_pure.assume_p) [S.arg res_t; S.arg <| Util.mk_eq res_t res_t xexp yexp; S.arg <| yret] None res_t.pos in
    let forall_y_x_eq_y_yret = mk_Tm_app (inst_effect_fun env md_pure md_pure.close_wp) [S.arg res_t; S.arg res_t; S.arg <| U.abs [mk_binder y] x_eq_y_yret] None res_t.pos in
    let lc2 = mk_comp md_pure res_t forall_y_x_eq_y_yret forall_y_x_eq_y_yret [PARTIAL_RETURN] in
    let lc = bind env None (lcomp_of_comp comp) (Some x, lcomp_of_comp lc2) in
    lc.comp()

let ite env (guard:formula) lcomp_then lcomp_else =
  let comp () =
      let (md, _, _), (res_t, wp_then, wlp_then), (_, wp_else, wlp_else) = lift_and_destruct env (lcomp_then.comp()) (lcomp_else.comp()) in
      let ifthenelse md res_t g wp_t wp_e = mk_Tm_app (inst_effect_fun env md md.if_then_else) [S.arg res_t; S.arg g; S.arg wp_t; S.arg wp_e] None (Range.union_ranges wp_t.pos wp_e.pos) in
      let wp = ifthenelse md res_t guard wp_then wp_else in
      let wlp = ifthenelse md res_t guard wlp_then wlp_else in
      if !Options.split_cases > 0
      then let comp = mk_comp md res_t wp wlp [] in
           add_equality_to_post_condition env comp res_t
      else let wp = mk_Tm_app  (inst_effect_fun env md md.ite_wp)  [S.arg res_t; S.arg wlp; S.arg wp] None wp.pos in
           let wlp = mk_Tm_app (inst_effect_fun env md md.ite_wlp) [S.arg res_t; S.arg wlp] None wlp.pos in
           mk_comp md res_t wp wlp [] in
    {eff_name=join_effects env lcomp_then.eff_name lcomp_else.eff_name;
     res_typ=lcomp_then.res_typ;
     cflags=[];
     comp=comp}

let bind_cases env (res_t:typ) (lcases:list<(formula * lcomp)>) : lcomp =
    let eff = List.fold_left (fun eff (_, lc) -> join_effects env eff lc.eff_name) Const.effect_PURE_lid lcases in
    let bind_cases () =
        let ifthenelse md res_t g wp_t wp_e = 
            mk_Tm_app (inst_effect_fun env md md.if_then_else) [S.arg res_t; S.arg g; S.arg wp_t; S.arg wp_e] None (Range.union_ranges wp_t.pos wp_e.pos) in
        let default_case =
            let post_k = U.arrow [null_binder res_t] (S.mk_Total U.ktype0) in
            let kwp    = U.arrow [null_binder post_k] (S.mk_Total U.ktype0) in
            let post   = S.new_bv None post_k in
            let wp     = U.abs [mk_binder post] (label Errors.exhaustiveness_check (Env.get_range env) <| S.fvar None Const.false_lid (Env.get_range env)) in 
            let wlp    = U.abs [mk_binder post] (S.fvar None Const.true_lid (Env.get_range env)) in
            let md     = Env.get_effect_decl env Const.effect_PURE_lid in
            mk_comp md res_t wp wlp [] in
        let comp = List.fold_right (fun (g, cthen) celse ->
            let (md, _, _), (_, wp_then, wlp_then), (_, wp_else, wlp_else) = lift_and_destruct env (cthen.comp()) celse in
            mk_comp md res_t (ifthenelse md res_t g wp_then wp_else) (ifthenelse md res_t g wlp_then wlp_else) []) lcases default_case in
        if !Options.split_cases > 0
        then add_equality_to_post_condition env comp res_t
        else let comp = U.comp_to_comp_typ comp in
             let md = Env.get_effect_decl env comp.effect_name in
             let _, wp, wlp = destruct_comp comp in
             let wp = mk_Tm_app  (inst_effect_fun env md md.ite_wp)  [S.arg res_t; S.arg wlp; S.arg wp] None wp.pos in
             let wlp = mk_Tm_app (inst_effect_fun env md md.ite_wlp) [S.arg res_t; S.arg wlp] None wlp.pos in
             mk_comp md res_t wp wlp [] in
    {eff_name=eff;
     res_typ=res_t;
     cflags=[];
     comp=bind_cases}

let close_comp env bvs (lc:lcomp) =
  let close () =
      let c = lc.comp() in
      if Util.is_ml_comp c then c
      else
        let close_wp md res_t bvs wp0 =
          List.fold_right (fun x wp -> 
              let bs = [mk_binder x] in
              let wp = U.abs bs wp in
              mk_Tm_app (inst_effect_fun env md md.close_wp) [S.arg res_t; S.arg x.sort; S.arg wp] None wp0.pos)
          bvs wp0 in 
        let c = Normalize.unfold_effect_abbrev env c in
        let t, wp, wlp = destruct_comp c in
        let md = Env.get_effect_decl env c.effect_name in
        let wp = close_wp md c.result_typ bvs wp in
        let wlp = close_wp md c.result_typ bvs wlp in
        mk_comp md c.result_typ wp wlp c.flags in
  {lc with comp=close}

let maybe_assume_result_eq_pure_term env (e:term) (lc:lcomp) : lcomp =
  let refine () =
      let c = lc.comp() in
      if not (is_pure_or_ghost_effect env lc.eff_name)
      then c
      else if Util.is_partial_return c then c
      else if Util.is_tot_or_gtot_comp c && Option.isNone <| Env.lookup_effect_abbrev env Const.effect_GTot_lid
      then failwith (Printf.sprintf "%s: %s\n" (Range.string_of_range e.pos) (Print.term_to_string e))
      else
           let c = Normalize.unfold_effect_abbrev env c in
           let t = c.result_typ in
           let c = mk_Comp c in
           let x = S.new_bv (Some t.pos) t in 
           let xexp = S.bv_to_name x in
           let ret = lcomp_of_comp <| (Util.comp_set_flags (return_value env t xexp) [PARTIAL_RETURN]) in
           let eq = (Util.mk_eq t t xexp e) in
           let eq_ret = weaken_precondition env ret (NonTrivial eq) in

           let c = U.comp_set_flags ((bind env None (lcomp_of_comp c) (Some x, eq_ret)).comp()) (PARTIAL_RETURN::U.comp_flags c) in
           c in

  let flags =
    if not (Util.is_function_typ lc.res_typ)
    && Util.is_pure_or_ghost_lcomp lc
    && not (Util.is_lcomp_partial_return lc)
    then PARTIAL_RETURN::lc.cflags
    else lc.cflags in
  {lc with comp=refine; cflags=flags}

let check_comp env (e:term) (c:comp) (c':comp) : term * comp * guard_t =
  //printfn "Checking sub_comp:\n%s has type %s\n\t<:\n%s\n" (Print.exp_to_string e) (Print.comp_to_string c) (Print.comp_to_string c');
  match Rel.sub_comp env c c' with
    | None -> raise (Error(Errors.computed_computation_type_does_not_match_annotation env e c c', Env.get_range env))
    | Some g -> e, c', g

let maybe_coerce_bool_to_type env (e:term) (lc:lcomp) (t:term) : term * lcomp = 
    match (SS.compress t).n with 
        | Tm_type _ -> 
          begin match (SS.compress lc.res_typ).n with 
            | Tm_fvar(fv, _) when lid_equals fv.v Const.bool_lid -> 
              let _ = Env.lookup_lid env Const.b2t_lid in  //check that we have Prims.b2t in the context
              let b2t = S.fvar None Const.b2t_lid e.pos in
              let lc = bind env (Some e) lc (None, lcomp_of_comp <| S.mk_Total (Util.ktype0)) in
              let e = mk_Tm_app b2t [S.arg e] (Some Util.ktype0.n) e.pos in
              e, lc
            | _ -> e, lc
          end

        | _ -> e, lc

let weaken_result_typ env (e:term) (lc:lcomp) (t:typ) : term * lcomp * guard_t =
  let gopt = if env.use_eq
             then Rel.try_teq env lc.res_typ t, false
             else Rel.try_subtype env lc.res_typ t, true in
  match gopt with
    | None, _ -> subtype_fail env lc.res_typ t
    | Some g, apply_guard ->
      match guard_form g with 
        | Trivial -> 
          let lc = {lc with res_typ = t} in
          (e, lc, g)
        
        | NonTrivial f ->
          let g = {g with guard_f=Trivial} in
          let strengthen () = 
                //try to normalize one more time, since more unification variables may be resolved now
                let f = N.normalize [N.Beta; N.Inline; N.Simplify] env f in
                match (SS.compress f).n with 
                    | Tm_abs(_, {n=Tm_fvar (fv, _)}) when lid_equals fv.v Const.true_lid -> 
                      //it's trivial
                      let lc = {lc with res_typ=t} in
                      lc.comp()
      
                    | _ -> 
                        let c = lc.comp() in
                        if Env.debug env <| Options.Extreme
                        then Util.print4 "Weakened from %s to %s\nStrengthening %s with guard %s\n" 
                                (N.term_to_string env lc.res_typ)
                                (N.term_to_string env t)
                                (N.comp_to_string env c) 
                                (N.term_to_string env f);

                        let ct = Normalize.unfold_effect_abbrev env c in
                        let a, kwp = Env.wp_signature env Const.effect_PURE_lid in
                        let k = SS.subst [NT(a, t)] kwp in
                        let md = Env.get_effect_decl env ct.effect_name in
                        let x = S.new_bv (Some t.pos) t in
                        let xexp = S.bv_to_name x in
                        let wp = mk_Tm_app (inst_effect_fun env md md.ret) [S.arg t; S.arg xexp] (Some k.n) xexp.pos in
                        let cret = lcomp_of_comp <| mk_comp md t wp wp [RETURN] in
                        let guard = if apply_guard then mk_Tm_app f [S.arg xexp] (Some U.ktype0.n) f.pos else f in
                        let eq_ret, _trivial_so_ok_to_discard =
                            strengthen_precondition (Some <| Errors.subtyping_failed env lc.res_typ t) 
                                                    (Env.set_range env e.pos) e cret
                                                    (guard_of_guard_formula <| NonTrivial guard) in
                        let x = {x with sort=lc.res_typ} in
                        let c = bind env (Some e) (lcomp_of_comp <| mk_Comp ct) (Some x, eq_ret) in
                        let c = c.comp () in
                        if Env.debug env <| Options.Extreme
                        then Util.print1 "Strengthened to %s\n" (Normalize.comp_to_string env c);
                        c in
              let flags = lc.cflags |> List.collect (function RETURN | PARTIAL_RETURN -> [PARTIAL_RETURN] | _ -> []) in
          let lc = {lc with res_typ=t; comp=strengthen; cflags=flags; eff_name=norm_eff_name env lc.eff_name} in
          let g = {g with guard_f=Trivial} in
          (e, lc, g)

let pure_or_ghost_pre_and_post env comp =
    let mk_post_type res_t ens =
        let x = S.new_bv None res_t in 
        U.refine x (S.mk_Tm_app ens [S.arg (S.bv_to_tm x)] (Some U.ktype0.n) res_t.pos) in// (Some mk_Kind_type) res_t.pos in
    let norm t = Normalize.normalize [N.Beta;N.Inline;N.Unlabel] env t in
    if Util.is_tot_or_gtot_comp comp
    then None, Util.comp_result comp
    else begin match comp.n with
            | GTotal _
            | Total _ -> failwith "Impossible"
            | Comp ct ->
              if lid_equals ct.effect_name Const.effect_Pure_lid
              || lid_equals ct.effect_name Const.effect_Ghost_lid
              then begin match ct.effect_args with
                      | (req, _)::(ens, _)::_ ->
                         Some (norm req), (norm <| mk_post_type ct.result_typ ens)
                      | _ -> failwith "Impossible"
                   end
              else let ct = Normalize.unfold_effect_abbrev env comp in
                   begin match ct.effect_args with
                            | (wp, _)::(wlp, _)::_ ->
                              let us_r, _ = Env.lookup_lid env Const.as_requires in
                              let us_e, _ = Env.lookup_lid env Const.as_ensures in
                              let r = ct.result_typ.pos in
                              let as_req = S.mk_Tm_uinst (S.fvar None Const.as_requires r) us_r in
                              let as_ens = S.mk_Tm_uinst (S.fvar None Const.as_ensures r) us_e in
                              let req = mk_Tm_app as_req [(ct.result_typ, Some Implicit); S.arg wp] (Some U.ktype0.n) ct.result_typ.pos in
                              let ens = mk_Tm_app as_ens [(ct.result_typ, Some Implicit); S.arg wlp] None ct.result_typ.pos in
                              Some (norm req), norm (mk_post_type ct.result_typ ens)
                            | _ -> failwith "Impossible"
                  end
                   
         end

(*********************************************************************************************)
(* Instantiation and generalization *)
(*********************************************************************************************)
let maybe_instantiate (env:Env.env) e t =
  let torig = SS.compress t in
  if not env.instantiate_imp 
  then e, torig, Rel.trivial_guard 
  else match torig.n with
    | Tm_arrow(bs, c) ->
      let bs, c = SS.open_comp bs c in
      let rec aux subst = function
        | (x, Some Implicit)::rest ->
          let t = SS.subst subst x.sort in
          let v, u, g = new_implicit_var env t in
          let subst = NT(x, v)::subst in
          let args, bs, subst, g' = aux subst rest in
          (v, Some Implicit)::args, bs, subst, Rel.conj_guard g g'
        | bs -> [], bs, subst, Rel.trivial_guard in

     let args, bs, subst, guard = aux [] bs in
     begin match args, bs with 
        | [], _ -> //no implicits were instantiated
          e, torig, guard
        
        | _, [] when not (Util.is_total_comp c) -> 
          //don't instantiate implicitly, if it has an effect
          e, torig, Rel.trivial_guard

        | _ ->

          let t = match bs with 
            | [] -> Util.comp_result c
            | _ -> U.arrow bs c in
          let t = SS.subst subst t in 
          let e = S.mk_Tm_app e args (Some t.n) e.pos in
          e, t, guard
      end
      
  | _ -> e, t, Rel.trivial_guard


(**************************************************************************************)
(* Generalizing types *)
(**************************************************************************************)
let gen_univs env (x:Util.set<universe_uvar>) : list<univ_name> = 
    if Util.set_is_empty x then []
    else let s = Util.set_difference x (Env.univ_vars env) |> Util.set_elements in
         let r = Some (Env.get_range env) in
         let u_names = s |> List.map (fun u -> 
            let u_name = Syntax.new_univ_name r in
            if Env.debug env <| Options.Other "Gen" 
            then Printf.printf "Setting ?%d (%s) to %s\n" (Unionfind.uvar_id u) (Print.univ_to_string (U_unif u)) (Print.univ_to_string (U_name u_name));
            Unionfind.change u (Some (U_name u_name));
            u_name) in
         u_names 

let generalize_universes (env:env) (t:term) : tscheme = 
    if Env.debug env <| Options.Other "Gen" then Printf.printf "Before generalization %s\n" (Print.term_to_string t);
    let t = N.normalize [N.Beta] env t in
    let univs = Free.univs t in 
    if Env.debug env <| Options.Other "Gen" 
    then Printf.printf "univs to gen : %s\n" 
                (Util.set_elements univs 
                |> List.map (fun u -> Unionfind.uvar_id u |> string_of_int) |> String.concat ", ");
    let gen = gen_univs env univs in
    if Env.debug env <| Options.Other "Gen" 
    then Printf.printf "After generalization: %s\n"  (Print.term_to_string t);
    let ts = SS.close_univ_vars gen t in 
    (gen, ts)

let gen env (ecs:list<(term * comp)>) : option<list<(list<univ_name> * term * comp)>> =
  if not <| (Util.for_all (fun (_, c) -> Util.is_pure_or_ghost_comp c) ecs) //No value restriction in F*---generalize the types of pure computations
  then None
  else
     let norm c =
        if debug env Options.Medium 
        then Util.print1 "Normalizing before generalizing:\n\t %s\n" (Print.comp_to_string c);
         let c = if Options.should_verify env.curmodule.str
                 then Normalize.normalize_comp [N.Beta; N.Inline; N.SNComp; N.Eta] env c
                 else Normalize.normalize_comp [N.Beta] env c in
         if debug env Options.Medium then 
            Util.print1 "Normalized to:\n\t %s\n" (Print.comp_to_string c);
         c in
     let env_uvars = Env.uvars_in_env env in
     let gen_uvars uvs = Util.set_difference uvs env_uvars |> Util.set_elements in
     let univs, uvars = ecs |> List.map (fun (e, c) ->
          let t = Util.comp_result c |> SS.compress in
          let c = norm c in
          let ct = U.comp_to_comp_typ c in
          let t = ct.result_typ in
          let univs = Free.univs t in
          let uvt = Free.uvars t in
          let uvs = gen_uvars uvt in
         univs, (uvs, e, c)) |> List.unzip in
  
     let univs = List.fold_left Util.set_union S.no_universe_uvars univs in
     let gen_univs = gen_univs env univs in
     if debug env Options.Medium then gen_univs |> List.iter (fun x -> Util.print1 "Generalizing uvar %s\n" x.idText);

     let ecs = uvars |> List.map (fun (uvs, e, c) ->
          let tvars = uvs |> List.map (fun (u, k) ->
            match Unionfind.find u with
              | Fixed ({n=Tm_name a}, _)
              | Fixed ({n=Tm_abs(_, {n=Tm_name a})}, _) -> a, Some Implicit
              | Fixed _ -> failwith "Unexpected instantiation of mutually recursive uvar"
              | _ ->
                  let bs, kres = Util.arrow_formals k in 
                  let a = S.new_bv (Some <| Env.get_range env) kres in 
                  let t = U.abs bs (S.bv_to_name a) in
                  U.set_uvar u (t,true);//t may have free variables, as indicated by the flag
                  a, Some Implicit) in

          let e, c = match tvars with 
            | [] -> //nothing generalized
              e, c

            | _ -> 
              let t = match (SS.compress (U.comp_result c)).n with 
                    | Tm_arrow(bs, cod) -> 
                      let bs, cod = SS.open_comp bs cod in 
                      U.arrow (tvars@bs) cod

                    | _ -> 
                      U.arrow tvars c in
              let e = U.abs tvars e in
              e, S.mk_Total t in
          (gen_univs, e, c)) in
     Some ecs

let generalize env (lecs:list<(lbname*term*comp)>) : (list<(lbname*univ_names*term*comp)>) =
  if debug env Options.Low 
  then Util.print1 "Generalizing: %s\n"
       (List.map (fun (lb, _, _) -> Print.lbname_to_string lb) lecs |> String.concat ", ");
  match gen env (lecs |> List.map (fun (_, e, c) -> (e, c))) with
    | None -> lecs |> List.map (fun (l,t,c) -> l,[],t,c)
    | Some ecs ->
      List.map2 (fun (l, _, _) (us, e, c) ->
         if debug env Options.Medium 
         then Util.print3 "(%s) Generalized %s to %s\n" 
                    (Range.string_of_range e.pos) 
                    (Print.lbname_to_string l) 
                    (Print.term_to_string (Util.comp_result c));
      (l, us, e, c)) lecs ecs

(************************************************************************)
(* Convertibility *)
(************************************************************************)
//check_and_ascribe env e t1 t2 
//checks is e:t1 is convertible to t2, subject to some guard.
//e is ascribed the type t2 and the guard is returned'
let check_and_ascribe env (e:term) (t1:typ) (t2:typ) : term * guard_t =
  let env = Env.set_range env e.pos in
  let check env t1 t2 =
    if env.use_eq
    then Rel.try_teq env t1 t2
    else match Rel.try_subtype env t1 t2 with
            | None -> None
            | Some f -> Some <| apply_guard f e in
  let is_var e = match (SS.compress e).n with
    | Tm_name _ -> true
    | _ -> false in
  let decorate e t = 
    let e = compress e in
    match e.n with
        | Tm_name x -> mk (Tm_name ({x with sort=t2})) (Some t2.n) e.pos
        | _ -> {e with tk=Util.mk_ref (Some t2.n)} in 
  let env = {env with use_eq=env.use_eq || (env.is_pattern && is_var e)} in
  match check env t1 t2 with
    | None -> raise (Error(Errors.expected_expression_of_type env t2 e t1, Env.get_range env))
    | Some g ->
        if debug env <| Options.Other "Rel"
        then Util.print1 "Applied guard is %s\n" <| guard_to_string env g;
        decorate e t2, g

/////////////////////////////////////////////////////////////////////////////////
let check_top_level env g lc : (bool * comp) =
  let discharge g =
    force_trivial_guard env g;
    Util.is_pure_lcomp lc in
  let g = Rel.solve_deferred_constraints env g in
  if Util.is_total_lcomp lc
  then discharge g, lc.comp()   
  else let c = lc.comp() in
       let steps = [Normalize.Beta; Normalize.SNComp; Normalize.DeltaComp] in
       let c = Normalize.normalize_comp steps env c |> Util.comp_to_comp_typ in
       let md = Env.get_effect_decl env c.effect_name in
       let t, wp, _ = destruct_comp c in
       let vc = mk_Tm_app (inst_effect_fun env md md.trivial) [S.arg t; S.arg wp] (Some U.ktype0.n) (Env.get_range env) in
       if Env.debug env <| Options.Other "Simplification"
       then Util.print1 "top-level VC: %s\n" (Print.term_to_string vc);
       let g = Rel.conj_guard g (Rel.guard_of_guard_formula <| NonTrivial vc) in
       discharge g, mk_Comp c

(* Having already seen_args to head (from right to left),
   compute the guard, if any, for the next argument,
   if head is a short-circuiting operator *)
let short_circuit (head:term) (seen_args:args) : guard_formula =
    let short_bin_op f : args -> guard_formula = function
        | [] -> (* no args seen yet *) Trivial
        | [(fst, _)] -> f fst
        | _ -> failwith "Unexpexted args to binary operator" in

    let op_and_e e = U.b2t e   |> NonTrivial in
    let op_or_e e  = U.mk_neg (U.b2t e) |> NonTrivial in
    let op_and_t t = unlabel t |> NonTrivial in
    let op_or_t t  = unlabel t |> Util.mk_neg |> NonTrivial in
    let op_imp_t t = unlabel t |> NonTrivial in

    let short_op_ite : args -> guard_formula = function
        | [] -> Trivial
        | [(guard, _)] -> NonTrivial guard
        | [_then;(guard, _)] -> Util.mk_neg guard |> NonTrivial
        | _ -> failwith "Unexpected args to ITE" in
    let table =
        [(Const.op_And,  short_bin_op op_and_e);
         (Const.op_Or,   short_bin_op op_or_e);
         (Const.and_lid, short_bin_op op_and_t);
         (Const.or_lid,  short_bin_op op_or_t);
         (Const.imp_lid, short_bin_op op_imp_t);
         (Const.ite_lid, short_op_ite);] in

     match head.n with
        | Tm_fvar (fv, _) ->
          let lid = fv.v in
          begin match Util.find_map table (fun (x, mk) -> if lid_equals x lid then Some (mk seen_args) else None) with
            | None ->   Trivial
            | Some g -> g
          end
        | _ -> Trivial

let short_circuit_head l = 
    match (SS.compress l).n with 
        | Tm_fvar (v, _) ->
           Util.for_some (lid_equals v.v)
                   [Const.op_And;
                    Const.op_Or;
                    Const.and_lid;
                    Const.or_lid;
                    Const.imp_lid;
                    Const.ite_lid]
        | _ -> false


        
(************************************************************************)
(* maybe_add_implicit_binders (env:env) (bs:binders)                    *)
(* Adding implicit binders for ticked variables                         *)
(* in case the expected type is of the form #'a1 -> ... -> #'an -> t    *)
(* and bs does not begin with any implicit binders                      *)
(* add #'a1 ... #'an to bs                                              *)
(************************************************************************)
let maybe_add_implicit_binders (env:env) (bs:binders)  : binders = 
    let pos bs = match bs with 
        | (hd, _)::_ -> S.range_of_bv hd
        | _ -> Env.get_range env in
    match bs with 
        | (_, Some Implicit)::_ -> bs //bs begins with an implicit binder; don't add any
        | _ -> 
          match Env.expected_typ env with 
            | None -> bs
            | Some t -> 
                match (SS.compress t).n with 
                    | Tm_arrow(bs', _) -> 
                      begin match Util.prefix_until (function (_, Some Implicit) -> false | _ -> true) bs' with 
                        | None -> bs
                        | Some ([], _, _) -> bs //no implicits
                        | Some (imps, _,  _) -> 
                          if imps |> Util.for_all (fun (x, _) -> Util.starts_with x.ppname.idText "'")
                          then let r = pos bs in 
                               let imps = imps |> List.map (fun (x, i) -> (S.set_range_of_bv x r, i)) in
                               imps@bs //we have a prefix of ticked variables
                          else bs
                      end

                    | _ -> bs