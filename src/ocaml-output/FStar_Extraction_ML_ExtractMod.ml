
open Prims

let fail_exp : FStar_Ident.lident  ->  FStar_Absyn_Syntax.typ  ->  FStar_Absyn_Syntax.exp = (fun lid t -> (let _167_16 = (let _167_15 = (FStar_Absyn_Util.fvar None FStar_Absyn_Const.failwith_lid FStar_Absyn_Syntax.dummyRange)
in (let _167_14 = (let _167_13 = (FStar_Absyn_Syntax.targ t)
in (let _167_12 = (let _167_11 = (let _167_10 = (let _167_9 = (let _167_8 = (let _167_7 = (let _167_6 = (let _167_5 = (FStar_Absyn_Print.sli lid)
in (Prims.strcat "Not yet implemented:" _167_5))
in (FStar_Bytes.string_as_unicode_bytes _167_6))
in (_167_7, FStar_Absyn_Syntax.dummyRange))
in FStar_Const.Const_string (_167_8))
in (FStar_Absyn_Syntax.mk_Exp_constant _167_9 None FStar_Absyn_Syntax.dummyRange))
in (FStar_All.pipe_left FStar_Absyn_Syntax.varg _167_10))
in (_167_11)::[])
in (_167_13)::_167_12))
in (_167_15, _167_14)))
in (FStar_Absyn_Syntax.mk_Exp_app _167_16 None FStar_Absyn_Syntax.dummyRange)))


let mangle_projector_lid : FStar_Ident.lident  ->  FStar_Ident.lident = (fun x -> (

let projecteeName = x.FStar_Ident.ident
in (

let _76_11 = (FStar_Util.prefix x.FStar_Ident.ns)
in (match (_76_11) with
| (prefix, constrName) -> begin
(

let mangledName = (FStar_Absyn_Syntax.id_of_text (Prims.strcat (Prims.strcat (Prims.strcat "___" constrName.FStar_Ident.idText) "___") projecteeName.FStar_Ident.idText))
in (FStar_Ident.lid_of_ids (FStar_List.append prefix ((mangledName)::[]))))
end))))


let rec extract_sig : FStar_Extraction_ML_Env.env  ->  FStar_Absyn_Syntax.sigelt  ->  (FStar_Extraction_ML_Env.env * FStar_Extraction_ML_Syntax.mlmodule1 Prims.list) = (fun g se -> (

let _76_16 = (FStar_Extraction_ML_Env.debug g (fun u -> (let _167_25 = (let _167_24 = (FStar_Absyn_Print.sigelt_to_string se)
in (FStar_Util.format1 "now extracting :  %s \n" _167_24))
in (FStar_Util.print_string _167_25))))
in (match (se) with
| (FStar_Absyn_Syntax.Sig_datacon (_)) | (FStar_Absyn_Syntax.Sig_bundle (_)) | (FStar_Absyn_Syntax.Sig_tycon (_)) | (FStar_Absyn_Syntax.Sig_typ_abbrev (_)) -> begin
(

let _76_32 = (FStar_Extraction_ML_ExtractTyp.extractSigElt g se)
in (match (_76_32) with
| (c, tds) -> begin
(c, tds)
end))
end
| FStar_Absyn_Syntax.Sig_let (lbs, r, _76_36, quals) -> begin
(

let elet = (FStar_Absyn_Syntax.mk_Exp_let (lbs, FStar_Absyn_Const.exp_false_bool) None r)
in (

let _76_46 = (FStar_Extraction_ML_ExtractExp.synth_exp g elet)
in (match (_76_46) with
| (ml_let, _76_43, _76_45) -> begin
(match (ml_let.FStar_Extraction_ML_Syntax.expr) with
| FStar_Extraction_ML_Syntax.MLE_Let (ml_lbs, _76_49) -> begin
(

let _76_81 = (FStar_List.fold_left2 (fun _76_54 ml_lb _76_62 -> (match ((_76_54, _76_62)) with
| ((env, ml_lbs), {FStar_Absyn_Syntax.lbname = lbname; FStar_Absyn_Syntax.lbtyp = t; FStar_Absyn_Syntax.lbeff = _76_59; FStar_Absyn_Syntax.lbdef = _76_57}) -> begin
(

let _76_78 = if (FStar_All.pipe_right quals (FStar_Util.for_some (fun _76_1 -> (match (_76_1) with
| FStar_Absyn_Syntax.Projector (_76_65) -> begin
true
end
| _76_68 -> begin
false
end)))) then begin
(

let mname = (let _167_31 = (let _167_30 = (FStar_Util.right lbname)
in (mangle_projector_lid _167_30))
in (FStar_All.pipe_right _167_31 FStar_Extraction_ML_Syntax.mlpath_of_lident))
in (

let env = (let _167_34 = (let _167_32 = (FStar_Util.right lbname)
in (FStar_All.pipe_left FStar_Absyn_Util.fv _167_32))
in (let _167_33 = (FStar_Util.must ml_lb.FStar_Extraction_ML_Syntax.mllb_tysc)
in (FStar_Extraction_ML_Env.extend_fv' env _167_34 mname _167_33 ml_lb.FStar_Extraction_ML_Syntax.mllb_add_unit false)))
in (

let ml_lb = (

let _76_71 = ml_lb
in {FStar_Extraction_ML_Syntax.mllb_name = _76_71.FStar_Extraction_ML_Syntax.mllb_name; FStar_Extraction_ML_Syntax.mllb_tysc = _76_71.FStar_Extraction_ML_Syntax.mllb_tysc; FStar_Extraction_ML_Syntax.mllb_add_unit = _76_71.FStar_Extraction_ML_Syntax.mllb_add_unit; FStar_Extraction_ML_Syntax.mllb_def = _76_71.FStar_Extraction_ML_Syntax.mllb_def; FStar_Extraction_ML_Syntax.print_typ = false})
in (env, (

let _76_74 = ml_lb
in {FStar_Extraction_ML_Syntax.mllb_name = ((Prims.snd mname), 0); FStar_Extraction_ML_Syntax.mllb_tysc = _76_74.FStar_Extraction_ML_Syntax.mllb_tysc; FStar_Extraction_ML_Syntax.mllb_add_unit = _76_74.FStar_Extraction_ML_Syntax.mllb_add_unit; FStar_Extraction_ML_Syntax.mllb_def = _76_74.FStar_Extraction_ML_Syntax.mllb_def; FStar_Extraction_ML_Syntax.print_typ = _76_74.FStar_Extraction_ML_Syntax.print_typ})))))
end else begin
(let _167_37 = (let _167_36 = (let _167_35 = (FStar_Util.must ml_lb.FStar_Extraction_ML_Syntax.mllb_tysc)
in (FStar_Extraction_ML_Env.extend_lb env lbname t _167_35 ml_lb.FStar_Extraction_ML_Syntax.mllb_add_unit false))
in (FStar_All.pipe_left Prims.fst _167_36))
in (_167_37, ml_lb))
end
in (match (_76_78) with
| (g, ml_lb) -> begin
(g, (ml_lb)::ml_lbs)
end))
end)) (g, []) (Prims.snd ml_lbs) (Prims.snd lbs))
in (match (_76_81) with
| (g, ml_lbs') -> begin
(let _167_40 = (let _167_39 = (let _167_38 = (FStar_Extraction_ML_Util.mlloc_of_range r)
in FStar_Extraction_ML_Syntax.MLM_Loc (_167_38))
in (_167_39)::(FStar_Extraction_ML_Syntax.MLM_Let (((Prims.fst ml_lbs), (FStar_List.rev ml_lbs'))))::[])
in (g, _167_40))
end))
end
| _76_83 -> begin
(FStar_All.failwith "impossible")
end)
end)))
end
| FStar_Absyn_Syntax.Sig_val_decl (lid, t, quals, r) -> begin
if (FStar_All.pipe_right quals (FStar_List.contains FStar_Absyn_Syntax.Assumption)) then begin
(

let impl = (match ((FStar_Absyn_Util.function_formals t)) with
| Some (bs, c) -> begin
(let _167_42 = (let _167_41 = (fail_exp lid (FStar_Absyn_Util.comp_result c))
in (bs, _167_41))
in (FStar_Absyn_Syntax.mk_Exp_abs _167_42 None FStar_Absyn_Syntax.dummyRange))
end
| _76_95 -> begin
(fail_exp lid t)
end)
in (

let se = FStar_Absyn_Syntax.Sig_let (((false, ({FStar_Absyn_Syntax.lbname = FStar_Util.Inr (lid); FStar_Absyn_Syntax.lbtyp = t; FStar_Absyn_Syntax.lbeff = FStar_Absyn_Const.effect_ML_lid; FStar_Absyn_Syntax.lbdef = impl})::[]), r, [], quals))
in (

let _76_100 = (extract_sig g se)
in (match (_76_100) with
| (g, mlm) -> begin
(

let is_record = (FStar_Util.for_some (fun _76_2 -> (match (_76_2) with
| FStar_Absyn_Syntax.RecordType (_76_103) -> begin
true
end
| _76_106 -> begin
false
end)) quals)
in (match ((FStar_Util.find_map quals (fun _76_3 -> (match (_76_3) with
| FStar_Absyn_Syntax.Discriminator (l) -> begin
Some (l)
end
| _76_112 -> begin
None
end)))) with
| Some (l) when (not (is_record)) -> begin
(let _167_49 = (let _167_48 = (let _167_45 = (FStar_Extraction_ML_Util.mlloc_of_range r)
in FStar_Extraction_ML_Syntax.MLM_Loc (_167_45))
in (let _167_47 = (let _167_46 = (FStar_Extraction_ML_ExtractExp.ind_discriminator_body g lid l)
in (_167_46)::[])
in (_167_48)::_167_47))
in (g, _167_49))
end
| _76_116 -> begin
(match ((FStar_Util.find_map quals (fun _76_4 -> (match (_76_4) with
| FStar_Absyn_Syntax.Projector (l, _76_120) -> begin
Some (l)
end
| _76_124 -> begin
None
end)))) with
| Some (_76_126) -> begin
(g, [])
end
| _76_129 -> begin
(g, mlm)
end)
end))
end))))
end else begin
(g, [])
end
end
| FStar_Absyn_Syntax.Sig_main (e, r) -> begin
(

let _76_139 = (FStar_Extraction_ML_ExtractExp.synth_exp g e)
in (match (_76_139) with
| (ml_main, _76_136, _76_138) -> begin
(let _167_53 = (let _167_52 = (let _167_51 = (FStar_Extraction_ML_Util.mlloc_of_range r)
in FStar_Extraction_ML_Syntax.MLM_Loc (_167_51))
in (_167_52)::(FStar_Extraction_ML_Syntax.MLM_Top (ml_main))::[])
in (g, _167_53))
end))
end
| (FStar_Absyn_Syntax.Sig_kind_abbrev (_)) | (FStar_Absyn_Syntax.Sig_assume (_)) | (FStar_Absyn_Syntax.Sig_new_effect (_)) | (FStar_Absyn_Syntax.Sig_sub_effect (_)) | (FStar_Absyn_Syntax.Sig_effect_abbrev (_)) | (FStar_Absyn_Syntax.Sig_pragma (_)) -> begin
(g, [])
end)))


let extract_iface : FStar_Extraction_ML_Env.env  ->  FStar_Absyn_Syntax.modul  ->  FStar_Extraction_ML_Env.env = (fun g m -> (let _167_58 = (FStar_Util.fold_map extract_sig g m.FStar_Absyn_Syntax.declarations)
in (FStar_All.pipe_right _167_58 Prims.fst)))


let rec extract : FStar_Extraction_ML_Env.env  ->  FStar_Absyn_Syntax.modul  ->  (FStar_Extraction_ML_Env.env * FStar_Extraction_ML_Syntax.mllib Prims.list) = (fun g m -> (

let _76_162 = (FStar_Absyn_Util.reset_gensym ())
in (

let name = (FStar_Extraction_ML_Syntax.mlpath_of_lident m.FStar_Absyn_Syntax.name)
in (

let g = (

let _76_165 = g
in {FStar_Extraction_ML_Env.tcenv = _76_165.FStar_Extraction_ML_Env.tcenv; FStar_Extraction_ML_Env.gamma = _76_165.FStar_Extraction_ML_Env.gamma; FStar_Extraction_ML_Env.tydefs = _76_165.FStar_Extraction_ML_Env.tydefs; FStar_Extraction_ML_Env.currentModule = name})
in if (((m.FStar_Absyn_Syntax.name.FStar_Ident.str = "Prims") || m.FStar_Absyn_Syntax.is_interface) || (FStar_Options.no_extract m.FStar_Absyn_Syntax.name.FStar_Ident.str)) then begin
(

let g = (extract_iface g m)
in (g, []))
end else begin
(

let _76_171 = (FStar_Util.fold_map extract_sig g m.FStar_Absyn_Syntax.declarations)
in (match (_76_171) with
| (g, sigs) -> begin
(

let mlm = (FStar_List.flatten sigs)
in (let _167_67 = (let _167_66 = (let _167_65 = (let _167_64 = (let _167_63 = (FStar_Extraction_ML_Util.flatten_mlpath name)
in (_167_63, Some (([], mlm)), FStar_Extraction_ML_Syntax.MLLib ([])))
in (_167_64)::[])
in FStar_Extraction_ML_Syntax.MLLib (_167_65))
in (_167_66)::[])
in (g, _167_67)))
end))
end))))




