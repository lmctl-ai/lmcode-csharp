# lmcode-csharp durable memory

`lmcode-csharp` is a zero-dependency .NET 8 / NativeAOT runtime and verifier for schema-bound lmscript bytecode v5.
Repository: https://github.com/lmctl-ai/lmcode-csharp.

## 60-second takeover

```sh
dotnet build -c Release -warnaserror                   # Build solution with warnings as errors
dotnet test                                            # Run unit tests
dotnet run --project conformance/LmCode.Conformance   # Run 51 core conformance vectors
dotnet publish examples/Greeter -r linux-x64 -c Release # NativeAOT standalone publish
```

## Document map

| File | Contents |
| --- | --- |
| [01-purpose-and-place.md](01-purpose-and-place.md) | C# runtime role, NativeAOT capability, zero external dependencies |
| [02-decisions.md](02-decisions.md) | Architectural choices, memory management, span semantics, type mapping |
| [03-build-run-test.md](03-build-run-test.md) | Build targets, test execution, NativeAOT compiler flags, vector sync |
| [04-gotchas.md](04-gotchas.md) | Common traps, AOT trimming, schema byte parity rules |
| [05-open-items.md](05-open-items.md) | Future improvements and roadmap |
| [06-stdlib-catalog.md](06-stdlib-catalog.md) | Catalog v2 semantics, ownership, compiler evidence and validation |

## Conformance vector sync

Core and stdlib vectors are synced from `lmcode-cpp`:
```sh
scripts/sync-vectors.sh               # Sync vectors from ../lmcode-cpp
scripts/sync-std.sh --verify          # Verify standard library schema parity
```

## Adding an example or host function

### Adding an example
1. Create an example project under `examples/<Name>/` referencing `src/LmCode`.
2. Provide an `.lms` script and compiled `.dslb` bytecode v5.
3. Configure `PublishAot` in the `.csproj` if testing NativeAOT standalone publishing.
4. Add verification run in `test.sh` or CI workflow.

### Adding a host function or method
1. Implement the method handler adhering to `HostDelegate` signature in `HostRegistry`.
2. Register the typed method symbol in the runtime registry.
3. Add a unit test in `tests/LmCode.Tests` verifying parameter extraction and return values.

## Releases and CI

Continuous Integration runs on GitHub Actions across 5 platforms (Linux x64/arm64, macOS arm64/x64, Windows x64), verifying Release builds, unit tests, 51 core vectors, stdlib vectors, and NativeAOT publishing.
- [update-sample-suite.md](update-sample-suite.md): canonical sample-suite update and all-port verification rule.
Releases/tags track SDK format versions and compatibility milestones.
