# DSLB v6 entry table

Only v6 is accepted. The entry table follows body source lines and shares constants, imports and context declarations. Each entry has independent local metadata, code, loop map, stack hint and line map. Parameter locals must match the canonical interface count/types and be visible. The existing verifier runs per region; stack verification is unconditional for entries and enforces void/value HALT depth. No opcode or registry change.

Entries is immutable metadata. Call initializes local slots 0..nparams-1 and invokes the same private Execute used by Run. Run remains body-only; Call requires no prior Run. No argument/result kind checks are added. Registry/context lifetimes and error location behavior match Run. Incorrect arity is RUN_HOST with R49's exact "expects N argument(s); found M" text.

Fourteen refusal tests were run red against the old runtime before implementation: version, constant/interface metadata, parameter count/local count/type/hidden flag, HALT depths, jump/underflow, truncation and trailing data. API checks cover immutable metadata, calls before/after body, fresh locals, untyped values, void, arity/index and entry source locations. Vector pin verification was demonstrated red by changing one copied expectation byte, then green after restoration.

The canonical d608ba2 C++ snapshot contains 70 core and 267 catalog vectors. It preserves the entries tail in runtime-empty-lines-fallback; unmodified mono 0e621804's generator truncates that one vector and must not be used to weaken the loader. Mono and C++ source checkouts were read-only throughout. External sample SDK locks and source remain unchanged.
