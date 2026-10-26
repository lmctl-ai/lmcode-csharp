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
