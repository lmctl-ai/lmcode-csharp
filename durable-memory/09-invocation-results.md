# Invocation-owned results (slice 2, C#)

The default standard registry has 206 signatures, including 24 return imports.
`Registry.SetResultSink(ResultSink?)` binds an external invocation-owned sink;
callbacks read that registry's current sink, with no process-global fallback.
Result storage never enters `CollectionsCatalog` or resets on Run/Call boundaries.
Hosts reset once before each logical program, keep one sink across body and selected
entries, then write a frame only after application validation. Entry HALT values
are unchanged. Failures emit no frame in the host-composition fixture runner.

`ResultSink.Set(Context,Value)`, `Put(Context,VmString,Value)`, `SetNull`, `PutNull`,
`Reset` and `Json` provide conversion/ownership helpers. Accepted values are the
consensus scalar, Json and typed list/map types. Json AST access remains internal;
no new general Json mutation/builder API was introduced. Copies validate full
subtrees before committing a write. Keys are strict UTF-8; overwrite preserves
insertion order, and null promotes to an object only after successful validation.

Number conversion inspects retained raw decimal tokens, including exponent-form
mathematical integers, rather than trusting already-rounded double values. Finite
fractional binary64 is allowed; unsafe integral values, nonfinite data and nonzero
underflow are RUN_HOST with JSON Pointer paths and fixed reason codes. Floats are
serialized by default invariant .NET double formatting, never Formatting.FormatG15
or G17. The exact .1+.2 frame is 0.30000000000000004; -0 becomes 0. Exponent casing
follows the .NET formatter and remains valid shortest-round-trip JSON.

`ResultFrames.Encode` returns strict UTF-8 bytes and `Write(Stream,ResultSink)`
writes those bytes with LF/no BOM and flushes before success. Deferred write/flush
I/O errors map to RUN_HOST `lmcode return: cannot write result frame`. The SDK does not print implicitly. Hosts own
application exit policy and stream I/O failure handling. Reports.ReportOk is still
ordinary diagnostics, not an inferred return channel. Samples adopt reporters in
separate slices; old example protocols were not silently changed by this SDK port.

Canonical schema/docs are copied from committed lmcode-cpp text and digest-pinned
in std/std.lock. Core, standard and return corpora have separate tree pins; the
return corpus also retains its upstream per-file vectors.lock. Sync helpers read
only committed source bytes; no local regeneration stands in for canonical data.

`dotnet test` runs numeric/sink tests and all nine return fixtures. The isolated
Python source-mutation gate proves arena teardown, reset-between-calls, source
mutation alias, missing sink, bad retained numeric token and G15 formatter bugs
are rejected by actual assertions, not compile failures. NativeAOT runs return
unit and framing suites through test.lms. Existing registry limits/concurrency
rules and std Json parsing behavior outside the result boundary are preserved.
