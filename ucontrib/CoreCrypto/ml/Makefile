FSTAR_HOME = ../../..
CONTRIB=ucontrib
PLATFORM = $(FSTAR_HOME)/$(CONTRIB)/Platform/ml
DB=db
INCLUDE=-package batteries -I $(PLATFORM) -I $(DB)

OCAMLC = ocamlfind c $(INCLUDE) -g -annot
OCAMLOPT = ocamlfind opt $(INCLUDE) -g -annot
OCAMLMKLIB = ocamlfind mklib $(INCLUDE)
OCAMLDEP = ocamlfind dep

CCOPTS = $(addprefix -ccopt ,-Wall -std=c11 -D__USE_MINGW_ANSI_STDIO)

ifeq ($(OS),Windows_NT)
    # On cygwin + cygwinports, DLLs are searched in the PATH, which is not
    # altered to include by default the mingw64 native DLLs. We also need to
    # find dllcorecrypto.dll; it is in the current directory, which Windows
    # always uses to search for DLLs.
    EXTRA_PATH = PATH="/usr/x86_64-w64-mingw32/sys-root/mingw/bin/:$(PATH)"
    ARCH = win32
else
    # On Unix-like systems, the library search path is LD_LIBRARY_PATH, which is
    # correctly setup to find libssleay.so and the like, but never includes the
    # current directory, which is where dllcorecrypto.so is located.
    EXTRA_PATH = LD_LIBRARY_PATH=.
    UNAME_S := $(shell uname -s)
    ifeq ($(UNAME_S),Darwin)
        EXTRA_OPTS = -ccopt -L. -I /usr/local/opt/openssl/include -I /opt/local/include/openssl
        EXTRA_LIBS = -L /usr/local/opt/openssl/lib -L /opt/local/lib
        ARCH = osx
    else
        EXTRA_OPTS = -ccopt -fPIC -ccopt -L.
        ARCH = x86_64
    endif
endif

.PHONY: test dep

all: CoreCrypto.cmxa

%.o: %.c
	$(OCAMLOPT) $(CCOPTS) $(EXTRA_OPTS) -c $<

%.cmi: %.mli
	$(OCAMLC) -c $<

%.cmo: %.ml
	$(OCAMLC) -c $<

%.cmx: %.ml
	$(OCAMLOPT) -c $<

$(PLATFORM)/platform.cmx: $(PLATFORM)/platform.ml
	$(MAKE) -C $(PLATFORM)

$(DB)/DB.cmx: $(DB)/DB.ml
	$(MAKE) -C $(DB)

DLL_OBJ = $(PLATFORM)/platform.cmx CoreCrypto.cmx openssl_stub.o $(DB)/DB.cmx DHDB.cmx
CoreCrypto.cmxa: $(DLL_OBJ)
	$(OCAMLMKLIB) -lcrypto  $(EXTRA_LIBS) -o CoreCrypto $^

DLL_BYTE = $(PLATFORM)/platform.cmo CoreCrypto.cmo openssl_stub.o DHDB.cmo $(DB)/DB.cmo
CoreCrypto.cma: $(DLL_BYTE)
	$(OCAMLMKLIB) -lcrypto  $(EXTRA_LIBS) -o CoreCrypto $^

TEST_CMX = Tests.cmx
Tests.exe: CoreCrypto.cmxa $(TEST_CMX)
	$(OCAMLOPT) $(EXTRA_OPTS) -I $(PLATFORM) -package batteries -linkpkg -o $@ \
	CoreCrypto.cmxa $(TEST_CMX)

test: Tests.exe
	@$(EXTRA_PATH) ./Tests.exe

clean:
	$(MAKE) -C $(DB) clean
	$(MAKE) -C $(PLATFORM) clean
	rm -f Tests.exe *.[oa] *.so *.cm[ixoa] *.cmxa *.exe *.dll

depend:
	$(OCAMLDEP) *.ml *.mli > .depend

include .depend

valgrind: Tests$(EXE)
	valgrind --leak-check=yes --suppressions=suppressions ./Tests$(EXE)
