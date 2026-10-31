# Rule methods pin (compiler R47/R48), 2026-10-27

The compiler gained rule methods: `@NAME` before a top-level `fn` plus
`implements NAME` bound to a schema SAM interface (R47), and an optional
top-level `"interfaces"` key in `dsl.schema: 2` schemas that joins the schema
SHA-256 (R48). Bytecode is lmcode bytecode (DSLB v6) since R49: annotated rules also emit entries (see the note at the end of this file).

No runtime change was needed, mirroring lmcode-cpp and lmcode-java:
`Program.Load` binds the schema by the SHA-256 of its exact bytes
(`src/LmCode/Program.cs`) and nothing in `src/LmCode` parses schema JSON, so
there is no top-level key whitelist to extend. The only JSON readers live in
tests and use `JsonDocument`, which tolerates unknown keys. Vendored vectors
were verified byte-identical with lmcode-cpp HEAD `aec9b09` (the feature
commit touched only the sample suite, not `conformance/` or `std/`; the
`SOURCE_COMMIT` pins still name `38bbd91`, whose conformance content equals
HEAD).

`RuleMethodsPinTests` (in the `dotnet test` console suite) pins the contract
with fixtures in `tests/LmCode.Tests/Fixtures/rule-methods/`:

- `schema.json` carries `"interfaces": {"Adjuster": int -> void}`; reading
  signatures from it must tolerate the key and yield no extra signatures.
- `annotated.dslb` (from `@PRICE_RULE fn adjust ... implements Adjuster`) and
  `plain.dslb` (same body, no annotation) must be byte-identical and both must
  load and run, reporting `adjust 41` through the `note` host fn.
- Loading the same bytecode against `schema-no-interfaces.json` must fail
  with `LOAD_SCHEMA_MISMATCH` (interfaces are part of the hash).

Regenerate fixtures with an R47/R48 compiler, e.g.
`LMSCRIPT_BIN=<lmscript> $LMSCRIPT_BIN --no-preflight annotated.lms annotated.dslb`
from the fixture directory. `plain.lms` keeps a blank line where
`annotated.lms` has the annotation so source-map line numbers align; without
it the files differ only in line-table entries.

## R49 supersession (2026-10-28)

The earlier byte-equivalence statement is historical: annotated rules now emit separate v6 entries. Plain functions remain inline-only. The pin checks both body output and direct rule invocation. Core and stdlib now pin C++ d608ba2; see 07-v6-entry-table.md.
