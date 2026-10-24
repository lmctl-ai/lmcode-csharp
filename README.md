# lmcode-csharp

A dependency-free .NET 8 SDK for embedding lmscript bytecode v5 in C# hosts. Namespace
`LmCode` provides a typed registry, schema-bound loader, structural verifier, VM,
per-run object context, and source-located errors. NativeAOT is supported without
reflection or generated dynamic code.

Experimental: bytecode compatibility is not promised. The SDK reads version 5 only;
compiler, SDK and host must move together when the format version changes. Pin an
SDK commit and matching compiler version.

## Embed

Reference `src/LmCode/LmCode.csproj` from your host (`dotnet add reference PATH`), or
vendor that directory. No NuGet package is published and the library declares no
package dependencies. The .NET SDK restores its own AOT/analyzer tooling.

The complete small host is [examples/Greeter/Program.cs](examples/Greeter/Program.cs):

```csharp
using LmCode;
using VmProgram = LmCode.Program;

var schema = """{"dsl.schema":2,"name":"greeter","types":{},"functions":{"greet":[{"params":["string"],"returns":"void"}]},"context":{}}"""u8.ToArray();
var registry = new Registry();
registry.Function("greet", ["string"], "void", (_, values) =>
{
    Console.Error.WriteLine("Hello, " + values[0].AsStr());
    return Value.None;
});
if (args is ["--dsl-schema", "--json"]) { Console.OpenStandardOutput().Write(schema); return 0; }
if (args is ["--dsl-signatures"]) { Console.WriteLine(string.Join('\n', registry.Signatures())); return 0; }
if (args is not ["--dsl", var path]) return 2;
VmProgram program;
try { program = VmProgram.Load(File.ReadAllBytes(path), schema, registry); }
catch (Error error) { return Reports.ReportError("load", error); }
try { program.Run(new Context(), registry); return Reports.ReportOk("null"); }
catch (Error error) { return Reports.ReportError("run", error); }
```

`Registry.Context(name, type)` declares an object name; `Context.SetObject(name,
 type, object)` binds it per run. `Registry.Method(type, name, parameters, returns,
 callback)` receives the context, receiver `ObjRef`, and ordered `Value[]` arguments.
Use `context.Get<T>(receiver)` to access a host object. Objects and handles belong to
one context; do not move handles between contexts. Register overloads with distinct
parameter tuples. `Signatures()` returns sorted canonical schema signatures.

VM strings are immutable **bytes**, including NUL and invalid UTF-8. `Value.Str`
accepts `VmString`, and `AsStr()` returns it. Construct from `ReadOnlySpan<byte>`
and use `.Span` for lossless access; `FromString`/`ToString` explicitly encode/decode
UTF-8 for hosts that want text. Do not round-trip arbitrary VM bytes through C# strings.
Integers wrap as signed 64-bit values, including `long.MinValue / -1`; division and
remainder by zero raise `RUN_ERROR`. Floats are binary64.

## Build and test

```sh
dotnet build -c Release -warnaserror
dotnet test
dotnet run --project conformance/LmCode.Conformance
dotnet run --project examples/Greeter -- --dsl examples/Greeter/hello.dslb
dotnet publish examples/Greeter -r linux-x64 -c Release -p:PublishAot=true
```

`dotnet test` runs the package-free console regression suite through MSBuild; no
xUnit or other test framework is used. The conformance command runs every checked-in
vector and exits nonzero on failure. `scripts/sync-vectors.sh` copies vectors from
`../lmcode-cpp` and records its source commit; it never regenerates them.

To rebuild the example bytecode with a compatible `lmscript` compiler:

```sh
cd examples/Greeter
lmscript hello.lm
```

CI builds, tests, runs all vectors, publishes NativeAOT, and executes the native
Greeter on Linux x64/arm64, macOS x64/arm64, and Windows x64. Linux NativeAOT requires
clang and zlib development headers. AOT removes the .NET runtime installation
requirement; the Linux executable still dynamically links system libc and libm.

## Standard library catalog

`LmCode.Stdlib.StringCatalog` provides optional ordinary host functions, without
special compiler or VM behavior. Merge the `functions` object from
`stdlib/string.schema-fragment.json` into your host schema, then register once:

```csharp
var registry = new Registry();
LmCode.Stdlib.StringCatalog.RegisterString(registry);
```

All 16 C++ catalog functions are available: `str_len`, `str_starts_with`,
`str_ends_with`, `str_contains`, `str_find`, `str_substr`, `str_lower`, `str_upper`,
`str_trim`, `str_repeat`, `str_replace`, `str_match`, `str_is_int`, `str_to_int`,
`str_is_float`, and `str_to_float`. Operations use byte spans, including NUL and
invalid UTF-8; case conversion is ASCII-only. The corresponding PascalCase
`StringCatalog` methods accept `VmString` directly. Repeat and replace cap output
at 64 MiB; glob matching caps work at 2^27 units, raising `RUN_HOST`. Numeric
parsing is locale-independent decimal with signed int64/binary64 range checks.
The shared fixtures in `conformance/stdlib-vectors` verify all 16 functions.

`dotnet test` checks byte lengths, fragment/registry parity, and a committed
compiler-generated bytecode trace. Compiler signature parity runs when `lmscript`
is on PATH; set `LMSCRIPT_BIN` to select a compiler executable. An absent PATH
compiler is explicitly skipped; a configured or installed compiler that fails is
an error. CI installs the compiler and also runs the catalog tests under NativeAOT.
For just the catalog checks, run `dotnet run --project tests/LmCode.Tests -- --string-catalog`.
Regenerate the fixture from `tests/LmCode.Tests/Fixtures` with `lmscript string.dsl`.

## Loader guarantees and errors

`Program.Load` checks magic/version, the SHA-256 of the exact schema bytes,
truncation/trailing data, constant tags and indexes, imports and context declarations,
instruction decoding, call kind/arity, boolean and math operands, jump boundaries,
paired nested range/while loops, read-only range slots, final HALT, and line-table
ordering/bounds. Loop-bearing programs additionally receive operand stack depth
propagation and join validation. As in the references, loop-free stack underflow is
reported at runtime. This is structural verification, not full type inference.

Programs retain immutable bound callbacks and copied bytecode and can be reused.
The host owns callback thread safety. There are no VM time or memory budgets;
`max_stack` is only a hint. This runtime is not a sandbox.

`Error` exposes `Code`, `Message`, nullable `Line` and `Column`. Host callbacks throw
`Error.Host(message)` for `RUN_HOST`; source positions are attached once at the
faulting instruction. Other codes are `LOAD_BAD_BYTECODE`, `LOAD_SCHEMA_MISMATCH`,
`LOAD_UNBOUND`, `RUN_ERROR`, and `APP_FAIL`. `Reports` emits one JSON result line;
load/run failures return exit 3 and application failure returns 4. Hosts remain
responsible for their own I/O exceptions.

MIT, Copyright (c) 2026 Lattice Systems LLC. See [LICENSE](LICENSE).
