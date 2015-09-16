(*--build-config
    options:--admit_fsi FStar.Set;
    other-files:ext.fst list.fst
  --*)

module ListSet
open FStar.List

(*this is impossible to define unless equality on a is decidable.
  the definition of memT does not seem to need a proof of decidability
*)
val lsubset :#a:Type -> list a -> list a -> Tot bool
let rec lsubset la lb =
match la with
| [] -> true
| h :: tl ->  ((mem h lb) && (lsubset tl lb))

val notIn : #a:Type -> a-> list a  -> Tot bool
let notIn id l = not (mem id l)

val noRepeats :#a:Type -> list a  -> Tot bool
let rec noRepeats la =
match la with
| [] -> true
| h :: tl ->  ((notIn h tl) && (noRepeats tl))
