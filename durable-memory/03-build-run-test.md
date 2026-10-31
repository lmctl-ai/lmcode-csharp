# Build, run and test

`dotnet build -c Release -warnaserror`, `dotnet test`, and
`dotnet run --project conformance/LmCode.Conformance` are the standard checks.
`scripts/sync-std.sh --verify` confirms the byte-identical 206-signature standard schema and its `std.lock` pin.
The package-free test executable is invoked by the project's VSTest target.
All 406 checked-in core conformance vectors must run; the `SOURCE_COMMIT` files record the C++ source for the core and catalog suites.
Use `scripts/sync-vectors.sh` to refresh without recompiling vectors.

Native publish: `dotnet publish examples/Greeter -r linux-x64 -c Release`.
The Greeter project enables PublishAot. On this development machine clang 19 is in
`~/toolchains/llvm19/bin`; specify `-p:CppCompilerAndLinker=$HOME/toolchains/llvm19/bin/clang`
and `-p:ObjCopyName=$HOME/toolchains/llvm19/bin/llvm-objcopy` when needed.
The local native Greeter was executed with the compiled hello.dslb and returned a terminal success envelope. It dynamically links glibc and libm.
CI has native runner/publish lanes for linux-x64, linux-arm64, osx-arm64, osx-x64,
and win-x64. The schema literal in Greeter must match schema.json byte for byte.

Run the optional string catalog fixtures with
`dotnet run --project conformance/LmCode.Conformance -- conformance/stdlib-vectors`.
`scripts/sync-vectors.sh` refreshes both core and catalog fixtures from the C++
checkout and records their source commit. CI runs both on all five platforms.

The catalog suite includes 41 seeded property vectors (seeds 7, 42, 20261024),
with independently calculated byte/numeric/container expectations. CI runs these
through the ordinary catalog conformance step. `LMCODE_TRACE=1` emits actual
trace/error records for byte-exact cross-SDK comparisons; the source generator
and larger 16-seed/64-round mode live in lmcode-cpp `scripts/gen-std-corpus.py`
and `scripts/diff-stdlib.py`. The synchronized suite has 268 catalog cases and
406 core cases.

The core and stdlib tree hashes are checked by VectorPinTests against vectors.lock and stdlib-vectors.lock. EntryTests is part of dotnet test; --entry-tests selects the refusal/API controls. No scripts/matrix.sh exists in this SDK. The shared C++ matrix includes C# but must run against independent scratch checkouts when the reference is read-only.

The synchronized invocation corpus adds 14 exact native envelope goldens; the original
nine return vectors now frame controlled failures too. See 09-invocation-results.md.
