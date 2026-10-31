# Invocation result envelopes (amendment R3)

Public API: InvocationResults.FromCommand(parsedFiles), FromMetadata(raw,files),
Registrations (parser order with canonical indices), Select(index,registry,contract),
Complete(index), Fail(indexOrNull,phase,error), WriteFrame(stream). See README for
lifecycle and exact envelope shapes. Use ResultFrames.FailureFrame for errors before
collector construction. Old sink-only framing and unframed Reports APIs are removed.

One sink per slot, retained across body/entries and arenas. Copies are owned;
completion snapshots/seals. Active selection preserves its immutable contract.
Sealed sinks reject writes/reset/selection/second completion. A completion contract
failure latches its slot association. First failure wins even over invalid later
failure arguments. New work cannot start after failure; active slots may drain.
Collector bookkeeping/shared registry access must be serialized by the host.

Manifest validation is strict: version/mode/keys/contiguous indices, unique absolute
files, exact complete registrations, UTF-8 without BOM, duplicate keys and unsafe
raw numbers refused. Standalone repeated CLI occurrences allow duplicate paths.
No process environment mutation; inherited context is read only for execution.

ResultContract.FromSchema retains schema-2 optional result metadata independently
from invocation metadata. Validate writes atomically and completion; reset null is
unfinished. Json is JSON-wide. Known native collection identity is checked even for
empty collections; int widens only toward float. No general coercion. No contract
means any valid JSON including null. Host metadata outside result is not subjected
to result numeric constraints. No new imports/opcodes/VM types or version change.

One compact terminal envelope per invocation, not per part. Add-order aggregation,
required builder parts, missing completion failure, no partial results on failure.
Known builder slot includes part; single/global omit. Stream flush failure returns
4 and permanently finalizes the collector. No retry, deadline or output limit.
Error.Message (including the existing located diagnostic prefix) matches the C++
canonical bytes; line/column are also explicit failure members.

scripts/sync-vectors.sh reads committed C++ bytes for four suites and rewrites tree
locks. Copy fixture assets Always: git archive may preserve timestamps older than
previous build output, so PreserveNewest could run stale bare-frame expectations.
InvocationTests and InvocationVectorTests run in managed and NativeAOT gates.
check-invocation-controls.py requires compiled exit-1 assertion kills, never build
failures/signals. Existing return ownership/numeric mutants remain enabled.

Mixed protocol warning: do not release or restore compatibility reporters. R7
owns the external C# sample's full multi-part adapter adoption; do not merely repin
an old adapter against removed APIs. SDK examples are migrated with the SDK.
