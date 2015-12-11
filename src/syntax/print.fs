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
module FStar.Syntax.Print

open FStar
open FStar.Syntax
open FStar.Util
open FStar.Syntax.Syntax
open FStar.Syntax.Util
open FStar.Syntax.Subst
open FStar.Ident
open FStar.Const

let lid_to_string (l:lid) = l.str

let fv_to_string fv = lid_to_string (fst fv).v

let bv_to_string bv = bv.ppname.idText ^ string_of_int bv.index
   
let nm_to_string bv = bv.ppname.idText

let db_to_string bv = bv.ppname.idText ^ string_of_int bv.index

let modul_to_string (m:modul) : string = failwith "NYI: modul_to_string"

(* CH: This should later be shared with ocaml-codegen.fs and util.fs (is_primop and destruct_typ_as_formula) *)
let infix_prim_ops = [
    (Const.op_Addition    , "+" );
    (Const.op_Subtraction , "-" );
    (Const.op_Multiply    , "*" );
    (Const.op_Division    , "/" );
    (Const.op_Eq          , "=" );
    (Const.op_ColonEq     , ":=");
    (Const.op_notEq       , "<>");
    (Const.op_And         , "&&");
    (Const.op_Or          , "||");
    (Const.op_LTE         , "<=");
    (Const.op_GTE         , ">=");
    (Const.op_LT          , "<" );
    (Const.op_GT          , ">" );
    (Const.op_Modulus     , "mod");
    (Const.and_lid     , "/\\");
    (Const.or_lid      , "\\/");
    (Const.imp_lid     , "==>");
    (Const.iff_lid     , "<==>");
    (Const.precedes_lid, "<<");
    (Const.eq2_lid     , "==");
    (Const.eqT_lid     , "==");
]

let unary_prim_ops = [
    (Const.op_Negation, "not");
    (Const.op_Minus, "-");
    (Const.not_lid, "~")
]

let is_prim_op ps f = match f.n with
  | Tm_fvar (fv,_) -> ps |> Util.for_some (lid_equals fv.v)
  | _ -> false

let get_lid f = match f.n with
  | Tm_fvar (fv,_) -> fv.v
  | _ -> failwith "get_lid"

let is_infix_prim_op (e:term) = is_prim_op (fst (List.split infix_prim_ops)) e
let is_unary_prim_op (e:term) = is_prim_op (fst (List.split unary_prim_ops)) e

let quants = [
  (Const.forall_lid, "forall");
  (Const.exists_lid, "exists");
  (Const.allTyp_lid, "forall");
  (Const.exTyp_lid , "exists");
]
type exp = term

let is_b2t (t:typ)   = is_prim_op [Const.b2t_lid] t
let is_quant (t:typ) = is_prim_op (fst (List.split quants)) t
let is_ite (t:typ)   = is_prim_op [Const.ite_lid] t

let is_lex_cons (f:exp) = is_prim_op [Const.lexcons_lid] f
let is_lex_top (f:exp) = is_prim_op [Const.lextop_lid] f
let is_inr = function Inl _ -> false | Inr _ -> true
let rec reconstruct_lex (e:exp) =
  match (compress e).n with
  | Tm_app (f, args) ->
      let args = List.filter (fun (a:arg) ->  snd a <> Some Implicit) args in
      let exps = List.map fst args in
      if is_lex_cons f && List.length exps = 2 then
        match reconstruct_lex (List.nth exps 1) with
        | Some xs -> Some (List.nth exps 0 :: xs)
        | None    -> None
      else None
  | _ -> if is_lex_top e then Some [] else None

(* CH: F# List.find has a different type from find in list.fst ... so just a hack for now *)
let rec find  (f:'a -> bool) (l:list<'a>) : 'a = match l with
  | [] -> failwith "blah"
  | hd::tl -> if f hd then hd else find f tl

let find_lid (x:lident) xs : string =
  snd (find (fun p -> lid_equals x (fst p)) xs)

let infix_prim_op_to_string e = find_lid (get_lid e)      infix_prim_ops
let unary_prim_op_to_string e = find_lid (get_lid e)      unary_prim_ops
let quant_to_string t = find_lid (get_lid t) quants

let rec sli (l:lident) : string =
   if !Options.print_real_names
   then l.str
   else l.ident.idText


let filter_imp a = a |> List.filter (function (_, Some Implicit) -> false | _ -> true)
let const_to_string x = match x with
  | Const_effect -> "Effect"
  | Const_unit -> "()"
  | Const_bool b -> if b then "true" else "false"
  | Const_int32 x ->      Util.string_of_int32 x
  | Const_float x ->      Util.string_of_float x
  | Const_char x ->       "'" ^ (Util.string_of_char x) ^ "'"
  | Const_string(bytes, _) -> Util.format1 "\"%s\"" (Util.string_of_bytes bytes)
  | Const_bytearray _  ->  "<bytearray>"
  | Const_int   x -> x
  | Const_int64 _ -> "<int64>"
  | Const_uint8 _ -> "<uint8>"

let lbname_to_string = function
  | Inl l -> bv_to_string l
  | Inr l -> lid_to_string l

let tag_of_term (t:term) = match t.n with
  | Tm_bvar _ -> "Tm_bvar"
  | Tm_name _ -> "Tm_name"
  | Tm_fvar _ -> "Tm_fvar"
  | Tm_uinst _ -> "Tm_uinst"
  | Tm_constant _ -> "Tm_constant"
  | Tm_type _ -> "Tm_type"
  | Tm_abs _ -> "Tm_abs"
  | Tm_arrow _ -> "Tm_arrow"
  | Tm_refine _ -> "Tm_refine"
  | Tm_app _ -> "Tm_app"
  | Tm_match _ -> "Tm_match"
  | Tm_ascribed _ -> "Tm_ascribed"
  | Tm_let _ -> "Tm_let"
  | Tm_uvar _ -> "Tm_uvar" 
  | Tm_delayed(_, m) -> 
    begin match !m with 
        | None -> "Tm_delayed"
        | Some _ -> "Tm_delayed-resolved"
    end
  | Tm_meta _ -> "Tm_meta"
  | Tm_unknown _ -> "Tm_unknown"

let uvar_to_string (u:uvar) = if !Options.hide_uvar_nums then "?" else "?" ^ (Unionfind.uvar_id u |> string_of_int) 
 
(* This function prints the type it gets as argument verbatim.
   For already type-checked types use the typ_norm_to_string
   function in normalize.fs instead, since elaboration
   (higher-order unification) produces types containing lots of
   redexes that should first be reduced. *)
let rec term_to_string x =
  let x = Subst.compress x in
  match x.n with
  | Tm_delayed _ -> failwith "impossible"
  | Tm_meta(t, _) -> term_to_string t
  | Tm_bvar x -> "@"^db_to_string x  
  | Tm_name x -> nm_to_string x
  | Tm_fvar f -> fv_to_string f
  | Tm_uvar (u, _) -> uvar_to_string u
  | Tm_constant c -> const_to_string c
  | Tm_arrow(bs, c) -> Util.format2 "(%s -> %s)"  (binders_to_string " -> " bs) (comp_to_string c)
  | Tm_abs(bs, t2) ->  Util.format2 "(fun %s -> %s)" (binders_to_string " " bs) (term_to_string t2)
  | Tm_refine(xt, f) ->       Util.format3 "%s:%s{%s}" (bv_to_string xt) (xt.sort |> term_to_string) (f |> formula_to_string)
  | Tm_app(_, []) -> failwith "Empty args!"
  | Tm_app(t, args) -> Util.format2 "(%s %s)" (term_to_string t) (args_to_string args)
  | Tm_let(lbs, e) ->  Util.format2 "%s\nin\n%s" (lbs_to_string lbs) (term_to_string e)
  | Tm_match(head, branches) ->
    Util.format2 "(match %s with\n\t| %s)"
      (term_to_string head)
      (Util.concat_l "\n\t|" (branches |> List.map (fun (p,wopt,e) -> 
            Util.format3 "%s %s -> %s"
                        (p |> pat_to_string)
                        (match wopt with | None -> "" | Some w -> Util.format1 "when %s" (w |> term_to_string))
                        (e |> term_to_string))))
  | _ -> tag_of_term x

and  pat_to_string x = match x.v with
    | Pat_cons(l, pats) -> Util.format2 "(%s %s)" (fv_to_string l) (List.map (fun (x, b) -> let p = pat_to_string x in if b then "#"^p else p) pats |> String.concat " ")
    | Pat_dot_term (x, _) -> Util.format1 ".%s" (bv_to_string x)
    | Pat_var x -> bv_to_string x
    | Pat_constant c -> const_to_string c
    | Pat_wild _ -> "_"
    | Pat_disj ps ->  Util.concat_l " | " (List.map pat_to_string ps) 
 

and lbs_to_string lbs =
    Util.format2 "let %s %s"
    (if fst lbs then "rec" else "")
    (Util.concat_l "\n and " (snd lbs |> List.map (fun lb -> Util.format2 "%s = %s" 
                                                            (lbname_to_string lb.lbname) 
                                                            (lb.lbdef |> term_to_string))))

and lcomp_to_string lc =
    Util.format2 "%s %s" (sli lc.eff_name) (term_to_string lc.res_typ)

//and uvar_t_to_string (uv, k) =
//   if false && !Options.print_real_names
//   then
//       Util.format2 "(U%s : %s)"
//       (if !Options.hide_uvar_nums then "?" else Util.string_of_int (Unionfind.uvar_id uv))
//       (kind_to_string k)
//   else Util.format1 "U%s"  (if !Options.hide_uvar_nums then "?" else Util.string_of_int (Unionfind.uvar_id uv))

and imp_to_string s = function
  | Some Implicit -> "#" ^ s
  | Some Equality -> "=" ^ s
  | _ -> s

and binder_to_string' is_arrow b = 
    let (a, imp) = b in
    if is_null_binder b 
    then term_to_string a.sort
    else if not is_arrow && not (!Options.print_implicits) then imp_to_string (nm_to_string a) imp
    else imp_to_string (nm_to_string a ^ ":" ^ term_to_string a.sort) imp
    
and binder_to_string b =  binder_to_string' false b

and arrow_binder_to_string b = binder_to_string' true b

and binders_to_string sep bs =
    let bs = if !Options.print_implicits then bs else filter_imp bs in
    if sep = " -> "
    then bs |> List.map arrow_binder_to_string |> String.concat sep
    else bs |> List.map binder_to_string |> String.concat sep

and arg_to_string = function
   | a, imp -> imp_to_string (term_to_string a) imp
   
and args_to_string args =
    let args = if !Options.print_implicits then args else filter_imp args in
    args |> List.map arg_to_string |> String.concat " "

and comp_to_string c = "<COMP>"
//  match c.n with
//    | Total t -> Util.format1 "Tot %s" (typ_to_string t)
//    | Comp c ->
//      let basic =
//          if c.flags |> Util.for_some (function TOTAL -> true | _ -> false) && not !Options.print_effect_args
//          then Util.format1 "Tot %s" (typ_to_string c.result_typ)
//          else if not !Options.print_effect_args && (lid_equals c.effect_name Const.effect_ML_lid)//  || List.contains MLEFFECT c.flags)
//          then typ_to_string c.result_typ
//          else if not !Options.print_effect_args && c.flags |> Util.for_some (function MLEFFECT -> true | _ -> false)
//          then Util.format1 "ALL %s" (typ_to_string c.result_typ)
//          else if !Options.print_effect_args
//          then Util.format3 "%s (%s) %s" (sli c.effect_name) (typ_to_string c.result_typ) (c.effect_args |> List.map effect_arg_to_string |> String.concat ", ")//match c.effect_args with hd::_ -> effect_arg_to_string hd | _ ->"")
//          else Util.format2 "%s (%s)" (sli c.effect_name) (typ_to_string c.result_typ) in
//      let dec = c.flags |> List.collect (function DECREASES e -> [Util.format1 " (decreases %s)" (exp_to_string e)] | _ -> []) |> String.concat " " in
//      Util.format2 "%s%s" basic dec
//
//and effect_arg_to_string e = match e with
//    | Inr e, _ -> exp_to_string e
//    | Inl wp, _ -> formula_to_string wp
//
(* CH: at this point not even trying to detect if something looks like a formula,
       only locally detecting certain patterns *)
and formula_to_string phi = term_to_string phi


//let subst_to_string subst =
//   Util.format1 "{%s}" <|
//    (List.map (function
//        | Inl (a, t) -> Util.format2 "(%s -> %s)" (strBvd a) (typ_to_string t)
//        | Inr (x, e) -> Util.format2 "(%s -> %s)" (strBvd x) (exp_to_string e)) subst |> String.concat ", ")
//let freevars_to_string (fvs:freevars) =
//    let f (l:set<bvar<'a,'b>>) = l |> Util.set_elements |> List.map (fun t -> strBvd t.v) |> String.concat ", " in
//    Util.format2 "ftvs={%s}, fxvs={%s}" (f fvs.ftvs) (f fvs.fxvs)

let qual_to_string = function
    | Logic -> "logic"
    | Opaque -> "opaque"
    | Discriminator _ -> "discriminator"
    | Projector _ -> "projector"
    | RecordType ids -> Util.format1 "record(%s)" (ids |> List.map (fun lid -> lid.ident.idText) |> String.concat ", ")
    | _ -> "other"
let quals_to_string quals = quals |> List.map qual_to_string |> String.concat " "
//
//let rec sigelt_to_string x = match x with
//  | Sig_pragma(ResetOptions, _) -> "#reset-options"
//  | Sig_pragma(SetOptions s, _) -> Util.format1 "#set-options \"%s\"" s
//  | Sig_tycon(lid, tps, k, _, _, quals, _) -> Util.format4 "%s type %s %s : %s" (quals_to_string quals) lid.str (binders_to_string " " tps) (kind_to_string k)
//  | Sig_typ_abbrev(lid, tps, k, t, _, _) ->  Util.format4 "type %s %s : %s = %s" lid.str (binders_to_string " " tps) (kind_to_string k) (typ_to_string t)
//  | Sig_datacon(lid, t, _, _, _, _) -> Util.format2 "datacon %s : %s" lid.str (typ_to_string t)
//  | Sig_val_decl(lid, t, quals, _) -> Util.format3 "%s val %s : %s" (quals_to_string quals) lid.str (typ_to_string t)
//  | Sig_assume(lid, f, _, _) -> Util.format2 "val %s : %s" lid.str (typ_to_string f)
//  | Sig_let(lbs, _, _, b) -> lbs_to_string lbs
//  | Sig_main(e, _) -> Util.format1 "let _ = %s" (exp_to_string e)
//  | Sig_bundle(ses, _, _, _) -> List.map sigelt_to_string ses |> String.concat "\n"
//  | Sig_new_effect _ -> "new_effect { ... }"
//  | Sig_sub_effect _ -> "sub_effect ..."
//  | Sig_kind_abbrev _ -> "kind ..."
//  | Sig_effect_abbrev(l, tps, c, _, _) -> Util.format3 "effect %s %s = %s" (sli l) (binders_to_string " " tps) (comp_typ_to_string c)
//
//let format_error r msg = format2 "%s: %s\n" (Range.string_of_range r) msg
//
//let rec sigelt_to_string_short x = match x with
//  | Sig_let((_, [{lbname=Inr l; lbtyp=t}]), _, _, _) -> Util.format2 "let %s : %s" l.str (typ_to_string t)
//  | _ -> lids_of_sigelt x |> List.map (fun l -> l.str) |> String.concat ", "
//
//let rec modul_to_string (m:modul) =
//  Util.format2 "module %s\n%s" (sli m.name) (List.map sigelt_to_string m.declarations |> String.concat "\n")