(*--build-config
    options:--admit_fsi FStar.Set --z3timeout 10;
    other-files:ext.fst set.fsi heap.fst st.fst all.fst list.fst  stack.fst listset.fst
    ghost.fst lref.fst stackAndHeap.fst sst.fst sstCombinators.fst constr.fst word.fst seq.fsi seq.fst
  --*)


module SSTArray
open SSTCombinators
open StackAndHeap
open SST
open MachineWord
open FStar.Heap
open Lref  open Located
open FStar.Set
open Stack
open FStar.Seq

(*to make vector opaque, just include vector.fsi*)
(*val testf : vector nat 10 -> nat
let testf v = v 1*)

type sstarray : Type -> Type
open FStar.Ghost

(*making it GTot causes a strange error in the postcondition of readIndex *)
val asRef : #a:Type  -> va:(sstarray a) -> Tot (erased (lref (seq a)))


val length: #a:Type -> x:sstarray a -> PureMem nat
  (requires (fun h -> liveRef (reveal (asRef x)) h))
  (ensures  (fun h y ->  (liveRef (reveal (asRef x)) h /\ y= ((Seq.length) ((loopkupRef) (reveal (asRef x)) h)))))

(*using the 2 definitions below causes a strange error in readIndex amd updIndex*)
(*val arrayExistsInMem : #a:Type -> (sstarray a) -> smem -> GTot bool
let arrayExistsInMem v sm = liveRef (reveal (asRef v)) sm

val lookup : #a:Type  -> va:(sstarray a) -> m:smem{(arrayExistsInMem va m)} -> GTot ((vector a (length va)))
let lookup 'a va m = (admit ())*)

(*loopkupRef (asRef va) m*)

val readIndex :  #a:Type  -> r:(sstarray a)
  -> index:nat
  -> PureMem a
        (requires (fun m ->  (liveRef (reveal (asRef r)) m) /\ index < Seq.length (loopkupRef (reveal (asRef r)) m) ) )
        (ensures (fun m v->
          (liveRef (reveal (asRef r)) m) /\ index < Seq.length (loopkupRef (reveal (asRef r)) m)
          /\ v = Seq.index (loopkupRef (reveal (asRef r)) m) index ))

val writeIndex :  #a:Type -> r:((sstarray a))
  -> index:nat -> newV:a ->
 Mem unit
    (requires (fun m ->  (liveRef (reveal (asRef r)) m) /\ index < Seq.length (loopkupRef (reveal (asRef r)) m) ) )
    (ensures ( fun m0 _ m1 ->
        (liveRef (reveal (asRef r)) m0) /\ index < Seq.length (loopkupRef (reveal (asRef r)) m0) /\
          (m1 = (writeMemAux (reveal (asRef r)) m0 (Seq.upd (loopkupRef (reveal (asRef r)) m0) index newV)))))
      (only (reveal (asRef r)))

(*create an sstarray on stack*)
val screateSeq :  #a:Type -> init:(seq a)
  -> Mem (sstarray a)
        (requires  (fun m -> (isNonEmpty (st m))))
        (ensures (fun m0 vv m1->
            (isNonEmpty (st m0)) /\ (isNonEmpty (st m1))
            /\ allocatedInRegion (reveal (asRef vv)) (topstb m0) (topstb m1) init
            /\ refLoc (reveal (asRef vv)) = InStack (topstid m0) /\ (topstid m0 = topstid m1)
            /\ mtail m0 = mtail m1))
        (hide empty)

(*create an sstarray on the heap*)
//TODO: we should remove this one; currently used only in MD5 for some experiments
val hcreateSeq :  #a:Type -> init:(seq a)
  -> Mem (sstarray a)
        (requires  (fun m -> True))
        (ensures (fun m0 v m1->
            allocatedInRegion (reveal (asRef v)) (hp m0) (hp m1) init
            /\ refLoc (reveal (asRef v)) = InHeap /\ (snd m0 = snd m1)))
        (hide empty)

val screate :  #a:Type -> len:nat -> init:a
  -> Mem (sstarray a)
        (requires  (fun m -> (isNonEmpty (st m))))
        (ensures (fun m0 vv m1->
            (isNonEmpty (st m0)) /\ (isNonEmpty (st m1))
            /\ allocatedInRegion (reveal (asRef vv)) (topstb m0) (topstb m1) (Seq.create len init)
            /\ refLoc (reveal (asRef vv)) = InStack (topstid m0) /\ (topstid m0 = topstid m1)
            /\ mtail m0 = mtail m1))
        (hide empty)

val hcreate :  #a:Type -> len:nat -> init:a
  -> Mem (sstarray a)
        (requires  (fun m -> True))
        (ensures (fun m0 v m1->
            allocatedInRegion (reveal (asRef v)) (hp m0) (hp m1) (Seq.create len init)
            /\ refLoc (reveal (asRef v)) = InHeap /\ (snd m0 = snd m1)))
        (hide empty)


(* This is convenient. It need not be a Primitive, but can be implemented.
   For short arrays, such as the MD5 checksum (4 words), it might not be too inefficient*)
val to_seq :  #a:Type  -> r:(sstarray a)
  -> PureMem (seq a)
        (requires (fun m -> b2t (liveRef (reveal (asRef r)) m)))
        (ensures (fun m v-> (liveRef (reveal (asRef r)) m) /\ v = (loopkupRef (reveal (asRef r)) m)))
