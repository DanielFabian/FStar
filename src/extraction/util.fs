﻿(*
   Copyright 2008-2015 Abhishek Anand, Nikhil Swamy and Microsoft Research

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
module FStar.Extraction.ML.Util
open FStar
open FStar.Util
open FStar.Absyn
open FStar.Absyn.Syntax
open FStar.Extraction.ML.Syntax

let pruneNones (l : list<option<'a>>) : list<'a> =
    List.fold_right (fun  x ll -> match x with
                          | Some xs -> xs::ll
                          | None -> ll) l []


let mlconst_of_const (sctt : sconst) =
  match sctt with
  | Const_unit         -> MLC_Unit
  | Const_char   c     -> MLC_Char  c
  | Const_uint8  c     -> MLC_Byte  c
  | Const_int    c     -> MLC_Int32 (Util.int32_of_int (Util.int_of_string c))
  | Const_int32  i     -> MLC_Int32 i
  | Const_int64  i     -> MLC_Int64 i
  | Const_bool   b     -> MLC_Bool  b
  | Const_float  d     -> MLC_Float d

  | Const_bytearray (bytes, _) ->
      MLC_Bytes bytes

  | Const_string (bytes, _) ->
      MLC_String (string_of_unicode (bytes))

let mlconst_of_const' (p:Range.range) (c:sconst) = 
    try mlconst_of_const c
    with _ -> failwith (Util.format2 "(%s) Failed to translate constant %s " (Range.string_of_range p) (Print.const_to_string c)) 

let rec subst_aux (subst:list<(mlident * mlty)>) (t:mlty)  : mlty =
    match t with
    | MLTY_Var  x -> (match Util.find_opt (fun (y, _) -> y=x) subst with
                     | Some ts -> snd ts
                     | None -> t) // TODO : previously, this case would abort. why? this case was encountered while extracting st3.fst
    | MLTY_Fun (t1, f, t2) -> MLTY_Fun(subst_aux subst t1, f, subst_aux subst t2)
    | MLTY_Named(args, path) -> MLTY_Named(List.map (subst_aux subst) args, path)
    | MLTY_Tuple ts -> MLTY_Tuple(List.map (subst_aux subst) ts)
    | MLTY_App(t1, t2) -> MLTY_App(subst_aux subst t1, subst_aux subst t2)
    | MLTY_Top -> MLTY_Top

let subst ((formals, t):mltyscheme) (args:list<mlty>) : mlty =
    if List.length formals <> List.length args
    then failwith "Substitution must be fully applied"
    else subst_aux (List.zip formals args) t

let delta_unfold g = function
    | MLTY_Named(args, n) ->
      begin match Env.lookup_ty_const g n with
        | Some ts -> Some (subst ts args)
        | _ -> None
      end
    | _ -> None


let rec equiv (g:Env.env) (t:mlty) (t':mlty) : bool =
    match t, t' with
    | MLTY_Var  x, MLTY_Var y ->
      fst x = fst y

    | MLTY_Fun (t1, f, t2), MLTY_Fun (t1', f', t2') ->
      equiv g t1 t1'
      //&& f=f' NS: removing this for now, until effects are properly translated
      && equiv g t2 t2'

    | MLTY_Named(args, path), MLTY_Named(args', path') ->
      if path=path'
      then List.forall2 (equiv g) args args'
      else begin match delta_unfold g t with
                    | Some t -> equiv g t t'
                    | None -> (match delta_unfold g t' with
                                 | None -> false
                                 | Some t' -> equiv g t t')
          end

    | MLTY_Tuple ts, MLTY_Tuple ts' ->
      List.forall2 (equiv g) ts ts'

    | MLTY_Top, MLTY_Top -> true

    | MLTY_Named _, _ ->
      begin match delta_unfold g t with
        | Some t -> equiv g t t'
        | _ ->  false
      end

    | _, MLTY_Named _ ->
      begin match delta_unfold g t' with
        | Some t' -> equiv g t t'
        | _ -> false
      end

    | _ -> false

let unit_binder =
    let x = Util.gen_bvar Tc.Recheck.t_unit in
    v_binder x

let is_type_abstraction = function
    | (Inl _, _)::_ -> true
    | _ -> false

let mkTypFun (bs : Syntax.binders) (c : Syntax.comp) (original : Syntax.typ) : Syntax.typ =
     mk_Typ_fun (bs,c) None original.pos // is this right? if not, also update mkTyp* below

let mkTypApp (typ : Syntax.typ) (arrgs : Syntax.args) (original : Syntax.typ) : Syntax.typ =
      mk_Typ_app (typ,arrgs) None original.pos


(*TODO: Do we need to recurse for c?*)
let tbinder_prefix t = match (Util.compress_typ t).n with
    | Typ_fun(bs, c) ->
      begin match Util.prefix_until (function (Inr _, _) -> true | _ -> false) bs with
        | None -> bs,t
        | Some (bs, b, rest) -> bs, (mkTypFun (b::rest) c t)
      end

    | _ -> [],t


let is_xtuple (ns, n) =
    if ns = ["Prims"]
    then match n with
        | "MkTuple2" -> Some 2
        | "MkTuple3" -> Some 3
        | "MkTuple4" -> Some 4
        | "MkTuple5" -> Some 5
        | "MkTuple6" -> Some 6
        | "MkTuple7" -> Some 7
        | _ -> None
    else None

let resugar_exp e = match e with
    | MLE_CTor(mlp, args) ->
        (match is_xtuple mlp with
        | Some n -> MLE_Tuple args
        | _ -> e)
    | _ -> e

let record_field_path = function
    | f::_ ->
        let ns, _ = Util.prefix f.ns in
        ns |> List.map (fun id -> id.idText)
    | _ -> failwith "impos"

let record_fields fs vs = List.map2 (fun (f:lident) e -> f.ident.idText, e) fs vs

let resugar_pat q p = match p with
    | MLP_CTor(d, pats) ->
      begin match is_xtuple d with
        | Some n -> MLP_Tuple(pats)
        | _ ->
          match q with
            | Some (Record_ctor (_, fns)) ->
              let p = record_field_path fns in
              let fs = record_fields fns pats in
              MLP_Record(p, fs)
            | _ -> p
      end
    | _ -> p


let is_xtuple_ty (ns, n) =
    if ns = ["Prims"]
    then match n with
        | "Tuple2" -> Some 2
        | "Tuple3" -> Some 3
        | "Tuple4" -> Some 4
        | "Tuple5" -> Some 5
        | "Tuple6" -> Some 6
        | "Tuple7" -> Some 7
        | _ -> None
    else None

let resugar_mlty t = match t with
    | MLTY_Named (args, mlp) ->
      begin match is_xtuple_ty mlp with
        | Some n -> MLTY_Tuple args
        | _ -> t
      end
    | _ -> t

let codegen_fsharp () = Option.get (!Options.codegen) = "FSharp"
let flatten_ns ns =
    if codegen_fsharp()
    then String.concat "." ns
    else String.concat "_" ns
let flatten_mlpath (ns, n) =
    if codegen_fsharp()
    then String.concat "." (ns@[n])
    else String.concat "_" (ns@[n])
let mlpath_of_lid (l:lident) = (l.ns |> List.map (fun i -> i.idText),  l.ident.idText)

let rec erasableType (g:Env.env) (t:mlty) :bool =
    //printfn "(* erasability of %A is %A *)\n" t (g.erasableTypes t);
   if Env.erasableTypeNoDelta t
   then true
   else
   ( match delta_unfold g t with
     | Some t -> (erasableType g t)
     | None  -> false
   )


let rec eraseTypeDeep (g:Env.env) (t:mlty) : mlty =
match t with
| MLTY_Fun (tyd, etag, tycd) -> if (etag=E_PURE) then (MLTY_Fun (eraseTypeDeep g tyd, etag, eraseTypeDeep g tycd)) else t
| MLTY_Named (lty, mlp) -> if (erasableType g t) then Env.erasedContent else (MLTY_Named (List.map (eraseTypeDeep g) lty, mlp))  // only some named constants are erased to unit.
| MLTY_Tuple lty ->  MLTY_Tuple (List.map (eraseTypeDeep g) lty)
| MLTY_App  (tyf, tyarg) -> MLTY_App  (eraseTypeDeep g tyf, eraseTypeDeep g  tyarg)
| _ ->  t