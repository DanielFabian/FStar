VERFILES=StlcCbvNamed.fst DbSubst.fst HereditarySubst.fst
FSTAR_HOME=../..
include ../Makefile.include
all: .all.ver uall LambdaOmega FOmega MicroFStar HoareWeakestPrecondition

uall: ParSubst.univ.ver StlcCbvDbPntSubstNoLists.univ.ver StlcStrongDbParSubst.univ.ver StlcCbvDbParSubst.univ.ver

_STDLIB_=FStar.Constructive.fst FStar.Classical.fst FStar.FunctionalExtensionality.fst FStar.Set.fst FStar.Heap.fst FStar.ST.fst FStar.All.fst 

%.univ.ver: %.fst
	$(FSTAR) $^

LambdaOmega:
	$(FSTAR) LambdaOmega.fst --max_fuel 1 --max_ifuel 1 --initial_fuel 1

FOmega:
	$(SFSTAR) FOmega.fst

MicroFStar:
	$(SFSTAR) MicroFStar.fst

HoareWeakestPrecondition:
	$(SFSTAR) $(_STDLIB_) HoareWeakestPrecondition.fst --max_fuel 1 --max_ifuel 1

# This takes forever to verify
StlcCbvDbPntSubstLists:
	$(FSTAR) StlcCbvDbPntSubstLists.fst --z3timeout 20
