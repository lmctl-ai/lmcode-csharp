# Standard catalog v2

Canonical declarations are byte-for-byte `lmcode-cpp/std/lmcode-std.schema.json`,
171 signatures, pinned by `std/std.lock`. `scripts/sync-std.sh --verify` checks the
local digest without requiring a sibling checkout. `sync-vectors.sh` reads only
committed snapshots; the current catalog/core corpora contain 229/51 cases.

`StandardCatalog` registers strings, math, lists/maps/files, typed containers and
Json by default. Opt-out, idempotent compatibility binders, host-only signatures,
raw schema hashing and duplicate rejection retain the v1 contract.

`ContainersCatalog` uses insertion-ordered maps with byte-key dictionary indexes,
stack lists, queue head indexes and .NET PriorityQueue with `(priority, sequence)`
ordering. Signed zeros tie, nonfinite priorities trap before mutation, int64
sequence cannot wrap, and clear resets it. Payload floats/bytes are not converted.

`JsonCatalog` parses validated UTF-8 bytes with exact byte diagnostics. It preserves
raw scalar/key tokens for deterministic compact serialization, validates decoded
key uniqueness and surrogate pairs eagerly, permits finite float underflow, and
caps array/object depth at 256. Parsing completes before allocating the root.

All factories spend the existing shared 65,536-object arena budget. Json roots
count once; child navigation caches handles by reference identity without quota;
keys snapshots count as StrList objects. Every borrowed/root slot is released and
reused after successful or exceptional runs. Unit coverage fills the shared quota,
checks failed parses, borrowed identity, key allocation, cleanup and repeated runs.

## Compiler evidence

Published npm latest/staging 0.2.426 still embeds the 72-signature v1 bundle.
Public CI therefore explicitly merges canonical v2 declarations for parity and
runs the compiled v2 vectors; the mode is named in test output and is not reported
as implicit bundle support. Private-source checkout/token dependencies are absent.

A local scratch Git archive of `lmctlhq/lmctl-mono` main commit
`0ce49614a3236b88495652c7c892bba82bb3551d` built the lmscript workspace without
changing the shared checkout. Its compiler reports 171 implicit signatures and
passes the SDK's full compiler/registry parity tests with explicit mode disabled.
No compiler source or binaries are vendored or published by this SDK.

Acceptance: Release warnings-as-errors build; all unit tests; 51 core and 229
catalog vectors; actual NativeAOT conformance on five native CI targets. Sample
host pins and its full current corpus are updated after SDK verification.


## V3 byte encodings and map snapshots

Canonical source: lmcode-cpp `38bbd918b7fde81db15d1d7427955cc5465d6f35`.
The byte-identical canonical v3 fragment has 181 signatures; the shared corpus
contains 267 catalog and 51 core cases. The original v2 notes above record that
rollout; the current standard includes ten additional signatures.

`EncodingCatalog` implements byte-oriented hex and canonical padded RFC 4648
Base64. Whitespace, URL-safe alphabet, misplaced padding, nonzero pad bits and
non-hex bytes fail with exact RUN_HOST messages. Encoding sizes are checked
against .NET byte-array bounds before allocating; no text decoding or newline
conversion occurs. The four-function compatibility binder is idempotent after
default registration; no new dependency or opcode is used.

Map keys/values snapshots reuse the existing typed list value-copy adapters.
StrMap enumerates its live entries, excluding tombstones. Numeric maps enumerate
insertion order and use the corresponding typed list constructor. Every call
spends one shared 65,536-object arena slot, including empty snapshots; data copies
preserve float bits and independent mutation in both directions.

Compiler main `fa04890cbf6379abfd70803c6283a974efea5554` was built from a scratch
Git archive and checked using an empty schema: 181 implicit signatures, zero with
`--without-std`. SDK unit parity also runs against that build. Published npm
0.2.426 remains v1; public CI uses explicit canonical v3 declarations, as in v2.
The canonical sample runner executes all 90 pinned cases; its reproduction policy
explicitly skips five literal source compilations only for a verified legacy
compiler. The local main compiler reproduces all 90 cases without those skips.
