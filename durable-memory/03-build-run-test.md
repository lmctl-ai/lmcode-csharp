# Build, run and test

`dotnet build -c Release -warnaserror`, `dotnet test`, and
`dotnet run --project conformance/LmCode.Conformance` are the standard checks.
`scripts/sync-std.sh --verify` confirms the byte-identical 72-signature standard schema and its `std.lock` pin.
The package-free test executable is invoked by the project's VSTest target.
All 51 checked-in core conformance vectors must run; the `SOURCE_COMMIT` files record the C++ source for the core and catalog suites.
Use `scripts/sync-vectors.sh` to refresh without recompiling vectors.

Native publish: `dotnet publish examples/Greeter -r linux-x64 -c Release`.
The Greeter project enables PublishAot. On this development machine clang 19 is in
`~/toolchains/llvm19/bin`; specify `-p:CppCompilerAndLinker=/home/mma/toolchains/llvm19/bin/clang`
and `-p:ObjCopyName=/home/mma/toolchains/llvm19/bin/llvm-objcopy` when needed.
The local native Greeter was executed with the compiled hello.dslb and returned
`{"ok": true, "result": null}`. It dynamically links glibc and libm.
CI has native runner/publish lanes for linux-x64, linux-arm64, osx-arm64, osx-x64,
and win-x64. The schema literal in Greeter must match schema.json byte for byte.

Run the optional string catalog fixtures with
`dotnet run --project conformance/LmCode.Conformance -- conformance/stdlib-vectors`.
`scripts/sync-vectors.sh` refreshes both core and catalog fixtures from the C++
checkout and records their source commit. CI runs both on all five platforms.
