﻿(*
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

module FStar.TypeChecker.Normalize
open FStar
open FStar.Util
open FStar.Syntax
open FStar.Syntax.Syntax
open FStar.Syntax.Subst
open FStar.Syntax.Util
open FStar.TypeChecker.Env

let debug_flag = ref false
let debug () = debug_flag := true

(**********************************************************************************************
 * Reduction of types via the Krivine Abstract Machine (KN), with lazy
 * reduction and strong reduction (under binders), as described in:
 *
 * Strongly reducing variants of the Krivine abstract machine
 * Pierre Crégut
 * Higher-Order Symb Comput (2007) 20: 209–230
 **********************************************************************************************)

type step =
  | WHNF
  | Eta             //remove?
  | EtaArgs         //remove?
  | Delta
  | DeltaHard
  | Beta            //remove? Always do beta
  | DeltaComp
  | Simplify        //remove?
  | SNComp
  | Unmeta
  | Unlabel
and steps = list<step>


type closure = 
  | Clos of env * term * memo<env * term> //memo for lazy evaluation
  | Dummy 
and env = list<closure>

let closure_to_string = function 
    | Clos (_, t, _) -> Print.term_to_string t
    | _ -> "dummy"

type cfg = {
    steps: steps;
    tcenv: Env.env
}

type branches = list<(pat * option<term> * term)> 

type subst_t = list<subst_elt>

type stack_elt = 
 | Arg      of closure * aqual * Range.range
 | MemoLazy of memo<(env * term)>
 | Match    of env * branches * Range.range
 | Abs      of env * binders  * Range.range
 | App      of term * aqual * Range.range

type stack = list<stack_elt>

let mk t r = mk t None r
let set_memo r t =
  match !r with 
    | Some _ -> failwith "Unexpected set_memo: thunk already evaluated"
    | None -> r := Some t

let env_to_string env =
    List.map closure_to_string env |> String.concat "; "

let stack_elt_to_string = function 
    | Arg (c, _, _) -> closure_to_string c
    | MemoLazy _ -> "MemoLazy"
    | Abs (_, bs, _) -> Printf.sprintf "Abs %d" (List.length bs)
    | _ -> "Match"

let stack_to_string s = 
    List.map stack_elt_to_string s |> String.concat "; "

let log f = 
    if !debug_flag
    then f()
    else ()

let is_empty = function
    | [] -> true
    | _ -> false

let lookup_bvar env x = 
    try List.nth env x.index
    with _ -> failwith (Printf.sprintf "Failed to find %s\n" (Print.bv_to_string x))

(* t is a closure with environment env *)
(* closure_as_term env t is closed term with all its free variables subsituted with their closures in env (recursively closed) *)
let rec closure_as_term env t =
    match env with
        | [] -> t
        | _ -> 
        let t = compress t in 
        match t.n with 
            | Tm_delayed _ -> 
              failwith "Impossible"

            | Tm_unknown _
            | Tm_uvar _ 
            | Tm_constant _
            | Tm_name _
            | Tm_fvar _
            | Tm_type _  (* universe terms are never closures *)
            | Tm_uinst _ (* head symbol must be an fvar *) -> 
              t
         
            | Tm_bvar x -> 
              begin match lookup_bvar env x with
                    | Dummy _ -> t
                    | Clos(env, t0, r) -> closure_as_term env t0
              end

           | Tm_app(head, args) -> 
             let head = closure_as_term_delayed env head in 
             let args = closures_as_args_delayed env args in
             mk (Tm_app(head, args)) t.pos
        
           | Tm_abs(bs, body) -> 
             let bs, env = closures_as_binders_delayed env bs in 
             let body = closure_as_term_delayed env body in
              mk (Tm_abs(List.rev bs, body)) t.pos

           | Tm_arrow(bs, c) -> 
             let bs, env = closures_as_binders_delayed env bs in 
             let c = close_comp env c in 
             mk (Tm_arrow(bs, c)) t.pos

           | Tm_refine(x, phi) -> 
             let x, env = closures_as_binders_delayed env [mk_binder x] in 
             let phi = closure_as_term_delayed env phi in 
             mk (Tm_refine(List.hd x |> fst, phi)) t.pos

           | Tm_ascribed(t1, t2, lopt) -> 
             mk (Tm_ascribed(closure_as_term_delayed env t1, closure_as_term_delayed env t2, lopt)) t.pos

           | Tm_meta(t', m) -> 
             mk (Tm_meta(closure_as_term_delayed env t', m)) t.pos
       
           | Tm_match  _ -> failwith "NYI"
           | Tm_let _    -> failwith "NYI"
       
and closure_as_term_delayed env t = 
    match env with 
        | [] -> t
        | _ -> mk_Tm_delayed (Inr (fun () -> closure_as_term env t)) t.pos  
 
and closures_as_args_delayed env args =
    match env with 
        | [] -> args
        | _ ->  List.map (fun (x, imp) -> closure_as_term_delayed env x, imp) args 

and closures_as_binders_delayed env bs = 
    let env, bs = bs |> List.fold_left (fun (env, out) (b, imp) -> 
            let b = {b with sort = closure_as_term_delayed env b.sort} in
            let env = Dummy::env in
            env, ((b,imp)::out)) (env, []) in
    List.rev bs, env

and close_comp env c = 
    match env with
        | [] -> c
        | _ -> 
        match c.n with 
            | Total t -> mk_Total (closure_as_term_delayed env t)
            | Comp c -> 
              let rt = closure_as_term_delayed env c.result_typ in
              let args = closures_as_args_delayed env c.effect_args in 
              let flags = c.flags |> List.map (function 
                | DECREASES t -> DECREASES (closure_as_term_delayed env t)
                | f -> f) in
              mk_Comp ({c with result_typ=rt;
                               effect_args=args;
                               flags=flags})  

let rec norm : cfg -> env -> stack -> term -> term = 
    fun cfg env stack t -> 
        log (fun () -> Printf.printf ">>> %s\n" (Print.tag_of_term t));
        let t = compress t in
//        log (fun () -> Printf.printf "Norm %s\n\t\tEnv=%s\n\t\tStack=%s\n" (Print.term_to_string t) (env_to_string env) (stack_to_string stack));
        log (fun () -> Printf.printf "Norm %s\n" (Print.term_to_string t));
        match t.n with 
          | Tm_delayed _ -> 
            failwith "Impossible"

          | Tm_unknown _
          | Tm_uvar _ 
          | Tm_constant _
          | Tm_fvar(_, Some Data_ctor)
          | Tm_fvar(_, Some (Record_ctor _)) -> //these last three are just constructors; no delta steps can apply
            rebuild cfg env stack t
     
          | Tm_type u -> 
            let u = norm_universe cfg env u in
            rebuild cfg env stack (mk (Tm_type u) t.pos)
         
          | Tm_uinst _ -> failwith "NYI"
     
          | Tm_name x -> 
            rebuild cfg env stack t
           
          | Tm_fvar (f, _) -> 
            if List.contains DeltaHard cfg.steps
            || (List.contains Delta cfg.steps && not (is_empty stack)) //delta only if reduction is blocked
            then match Env.lookup_definition cfg.tcenv f.v with 
                    | None -> rebuild cfg env stack t
                    | Some t -> norm cfg env stack t 
            else rebuild cfg env stack t     

          | Tm_bvar x -> 
            begin match lookup_bvar env x with 
                | Dummy _ -> failwith "Term variable not found"
                | Clos(env, t0, r) ->  
                   begin match !r with 
                        | Some (env, t') -> 
                            log (fun () -> Printf.printf "Lazy hit: %s cached to %s\n" (Print.term_to_string t) (Print.term_to_string t'));
                            begin match (compress t').n with
                                | Tm_abs _  ->  
                                    norm cfg env stack t'
                                | _ -> 
                                    rebuild cfg env stack t'
                            end
                        | None -> norm cfg env (MemoLazy r::stack) t0
                   end
            end
            
          | Tm_abs(bs, body) -> 
            begin match stack with 
                | Match _::_ -> 
                  failwith "Ill-typed term: cannot pattern match an abstraction"

                | Arg(c, _, _)::stack -> 
                  let body = match bs with 
                    | [] -> failwith "Impossible"
                    | [_] -> body
                    | _::tl -> mk (Tm_abs(tl, body)) t.pos in
                  log (fun () -> Printf.printf "\tShifted %s\n" (closure_to_string c));
                  norm cfg (c :: env) stack body 

                | MemoLazy r :: stack -> 
                  set_memo r (env, t); //TODO: fix! this doesn't always memoize the strong normal form
                  log (fun () -> Printf.printf "\tSet memo\n");
                  norm cfg env stack t

//                | App _ :: _ -> 
//                  rebuild cfg env stack t
                | App _ :: _ 
                | Abs _ :: _
                | [] -> 
                  if List.contains WHNF cfg.steps //don't descend beneath a lambda if we're just doing WHNF   
                  then rebuild cfg env stack (closure_as_term env t) //But, if the environment is non-empty, we need to substitute within the term
                  else let bs, body = open_term bs body in 
                       let env' = bs |> List.fold_left (fun env _ -> Dummy::env) env in
                       log (fun () -> Printf.printf "\tShifted %d dummies\n" (List.length bs));
                       norm cfg env' (Abs(env, bs, t.pos)::stack) body
            end

          | Tm_app(head, args) -> 
            let stack = stack |> List.fold_right (fun (a, aq) stack -> Arg (Clos(env, a, Util.mk_ref None),aq,t.pos)::stack) args in
            log (fun () -> Printf.printf "\tPushed %d arguments\n" (List.length args));
            norm cfg env stack head
                            
          | Tm_refine(x, f) -> //non tail-recursive; the alternative is to keep marks on the stack to rebuild the term ... but that's very heavy
            if List.contains WHNF cfg.steps
            then rebuild cfg env stack (closure_as_term env t)
            else let t_x = norm cfg env [] x.sort in 
                 let closing, f = open_term [(x, None)] f in
                 let f = norm cfg env [] f in 
                 let t = mk (Tm_refine({x with sort=t_x}, close closing f)) t.pos in 
                 rebuild cfg env stack t 

          | Tm_arrow(bs, c) -> 
            if List.contains WHNF cfg.steps
            then rebuild cfg env stack (closure_as_term env t)
            else let bs, c = open_comp bs c in 
                 let c = norm_comp cfg env c in
                 let t = arrow (norm_binders cfg env bs) c in
                 rebuild cfg env stack t
          
          | Tm_ascribed(t1, t2, l) -> 
            let t1 = norm cfg env [] t1 in 
            let t2 = norm cfg env [] t2 in
            rebuild cfg env stack (mk (Tm_ascribed(t1, t2, l)) t.pos)

          | Tm_match(head, branches) -> 
            let stack = Match(env, branches, t.pos)::stack in 
            norm cfg env stack head

          | Tm_let((false, [lb]), body) ->
            let env = Clos(env, lb.lbdef, Util.mk_ref None)::env in 
            norm cfg env stack body

          | Tm_let((_, {lbname=Inr _}::_), _) -> //this is a top-level let binding; nothing to normalize
            rebuild cfg env stack t

          | Tm_let(lbs, body) -> 
            //let rec: The basic idea is to reduce the body in an environment that includes recursive bindings for the lbs
            //Consider reducing (let rec f x = f x in f 0) in initial environment env
            //We build two environments, rec_env and body_env and reduce (f 0) in body_env
            //rec_env = Clos(env, let rec f x = f x in f, memo)::env
            //body_env = Clos(rec_env, \x. f x, _)::env
            //i.e., in body, the bound variable is bound to definition, \x. f x
            //Within the definition \x.f x, f is bound to the recursive binding (let rec f x = f x in f), aka, fix f. \x. f x
            //Finally, we add one optimization for laziness by tying a knot in rec_env
            //i.e., we set memo := Some (rec_env, \x. f x)
            
            let rec_env, memos, _ = List.fold_right (fun lb (rec_env, memos, i) -> 
                    let f_i = Syntax.bv_to_tm ({left lb.lbname with index=i}) in
                    let fix_f_i = mk (Tm_let(lbs, f_i)) t.pos in 
                    let memo = Util.mk_ref None in 
                    let rec_env = Clos(env, fix_f_i, memo)::rec_env in
                    rec_env, memo::memos, i + 1) (snd lbs) (env, [], 0) in
            let _ = List.map2 (fun lb memo -> memo := Some (rec_env, lb.lbdef)) (snd lbs) memos in //tying the knot
            let body_env = List.fold_right (fun lb env -> Clos(rec_env, lb.lbdef, Util.mk_ref None)::env)
                               (snd lbs) env in
            norm cfg body_env stack body

          | Tm_meta (head, m) -> 
            let head = norm cfg env [] head in
            let m = match m with 
                | Meta_pattern args -> 
                  let args = args |> List.map (List.map (fun (a, imp) -> norm cfg env [] a, imp)) in 
                  Meta_pattern args 
                | _ -> m in
            let t = mk (Tm_meta(head, m)) t.pos in
            rebuild cfg env stack t

and norm_comp : cfg -> env -> comp -> comp = 
    fun cfg env comp -> 
        match comp.n with 
            | Total t -> 
              {comp with n=Total (norm cfg env [] t)}

            | Comp ct -> 
              let norm_args args = args |> List.map (fun (a, i) -> (norm cfg env [] a, i)) in
              {comp with n=Comp {ct with result_typ=norm cfg env [] ct.result_typ;
                                         effect_args=norm_args ct.effect_args}}
    
and norm_binder : cfg -> env -> binder -> binder = 
    fun cfg env (x, imp) -> {x with sort=norm cfg env [] x.sort}, imp

and norm_binders : cfg -> env -> binders -> binders = 
    fun cfg env bs -> 
        let nbs, _ = List.fold_left (fun (nbs', env) b -> 
            let b = norm_binder cfg env b in
            (b::nbs', Dummy::env) (* crossing a binder, so shift environment *)) 
            ([], env)
            bs in
        List.rev nbs

and norm_universe cfg env u = failwith "NYI"

and rebuild : cfg -> env -> stack -> term -> term = 
    fun cfg env stack t ->
    (* Pre-condition: t is in strong normal form w.r.t env;
                      It has no free de Bruijn indices *)
        match stack with 
            | [] -> t

            | MemoLazy r::stack -> 
              set_memo r (env, t);
              rebuild cfg env stack t

            | Abs (env', bs, r)::stack ->
              let bs = norm_binders cfg env' bs in
              rebuild cfg env stack ({abs bs t with pos=r})

            | Arg (Dummy, _, _)::_ -> failwith "Impossible"

            | Arg (Clos(env, tm, m), aq, r) :: stack ->
              log (fun () -> Printf.printf "Rebuilding with arg %s\n" (Print.term_to_string tm));
              //this needs to be tail recursive for reducing large terms
              begin match !m with 
                | None -> 
                  if List.contains WHNF cfg.steps
                  then let arg = closure_as_term env tm in 
                       let t = extend_app t (arg, aq) None r in 
                       rebuild cfg env stack t
                  else let stack = MemoLazy m::App(t, aq, r)::stack in 
                       norm cfg env stack tm

                | Some (_, a) -> 
                  let t = mk (Tm_app(t, [(a,aq)])) r in 
                  rebuild cfg env stack t
              end

            | App(head, aq, r)::stack -> 
              let t = mk (Tm_app(head, [(t, aq)])) r in
              rebuild cfg env stack t

            | Match(env, branches, r) :: stack -> 
              let rebuild () = rebuild cfg env stack (mk (Tm_match(t, branches)) r) in

              let guard_when_clause wopt b rest = 
                  match wopt with 
                   | None -> b
                   | Some w -> 
                     let then_branch = b in
                     let else_branch = mk (Tm_match(t, rest)) r in 
                     Util.if_then_else w then_branch else_branch in
                
              let rec matches_pat (t:term) (p:pat) :  option<list<term>> = 
                    let t = compress t in 
                    match p.v with 
                    | Pat_disj ps -> FStar.Util.find_map ps (matches_pat t)
                    | Pat_var _ -> Some [t]
                    | Pat_wild _
                    | Pat_dot_term _ -> Some []
                    | Pat_constant s -> 
                      begin match t.n with 
                        | Tm_constant s' when s=s' -> Some []
                        | _ -> None
                      end
                    | Pat_cons(fv, arg_pats) -> 
                      let head, args = Util.head_and_args t in 
                      match head.n with 
                        | Tm_fvar fv' when fv_eq fv fv' -> 
                          matches_args [] args arg_pats
                        | _ -> None

              and matches_args out (a:args) (p:list<(pat * bool)>) = match a, p with 
                | [], [] -> Some out
                | (t, _)::rest_a, (p, _)::rest_p -> 
                    begin match matches_pat t p with 
                    | None -> None
                    | Some x -> matches_args (out@x) rest_a rest_p 
                    end 
                | _ -> None in
            
              let rec matches t p = match p with 
                | [] -> rebuild ()
                | (p, wopt, b)::rest -> 
                   match matches_pat t p with
                    | None -> matches t rest 
                    | Some s ->
                      let env = List.fold_right (fun t env -> Clos([], t, Util.mk_ref (Some ([], t)))::env) s env in //the sub-terms should be fully normalized; so their environment is []
                      norm cfg env stack (guard_when_clause wopt b rest) in

              matches t branches

let config s e = {tcenv=e; steps=s}
let normalize s e t = norm (config s e) [] [] t
let normalize_comp s e t = norm_comp (config s e) [] t

let term_to_string env t = Print.term_to_string (normalize [] env t)
let comp_to_string env c = Print.comp_to_string (norm_comp (config [] env) [] c)

let whnf (env:Env.env) (t:term) : term = normalize [Beta; WHNF] env t

let normalize_refinement steps env t0 =
   let t = normalize (steps@[Beta; WHNF; DeltaHard]) env t0 in
   let rec aux t =
    let t = compress t in
    match t.n with
       | Tm_refine(x, phi) ->
         let t0 = aux x.sort in
         begin match t0.n with
            | Tm_refine(y, phi1) ->
              mk (Tm_refine(y, Util.mk_conj phi1 phi)) t0.pos
            | _ -> t
         end
       | _ -> t in
   aux t

let weak_norm_comp (_:Env.env) (_:comp) : comp_typ = failwith "NYI: weak_norm_comp"
let normalize_sigelt (_:steps) (_:Env.env) (_:sigelt) : sigelt = failwith "NYI: normalize_sigelt"
let eta_expand (_:Env.env) (_:typ) : typ = failwith "NYI: eta_expand"