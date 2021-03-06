
open Prims

let find_deps_if_needed : FStar_Parser_Dep.verify_mode  ->  Prims.string Prims.list  ->  Prims.string Prims.list = (fun verify_mode files -> if (FStar_Options.explicit_deps ()) then begin
files
end else begin
(

let _86_8 = (FStar_Parser_Dep.collect verify_mode files)
in (match (_86_8) with
| (_86_4, deps, _86_7) -> begin
(match (deps) with
| [] -> begin
(

let _86_10 = (FStar_Util.print_error "Dependency analysis failed; reverting to using only the files provided")
in files)
end
| _86_13 -> begin
(

let deps = (FStar_List.rev deps)
in (

let deps = if ((let _177_5 = (FStar_List.hd deps)
in (FStar_Util.basename _177_5)) = "prims.fst") then begin
(FStar_List.tl deps)
end else begin
(

let _86_15 = (FStar_Util.print_error "dependency analysis did not find prims.fst?!")
in (FStar_All.exit 1))
end
in deps))
end)
end))
end)




