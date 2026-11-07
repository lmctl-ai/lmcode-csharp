# REST server over stdio (`rest` std module)

Opt-in module `lmcode_std_rest_v1` of [spec/std-rest.md](https://github.com/lmctl-ai/lmcode/blob/main/spec/std-rest.md).
There is **no network listener**: the host process reads HTTP/1.1 requests from stdin and writes responses to stdout.
`new Registry()` still has exactly the 206 standard signatures; the module adds 18 only when you register it.

```csharp
using LmCode;
using LmCode.Rest;

var registry = new Registry();
var rest = RestCatalog.RegisterRest(registry, new RestOptions
{
    MaxBodyBytes = 1 << 20,                                   // default 1 MiB
    Input = Console.OpenStandardInput(),                      // defaults: process stdin / stdout
    Output = Console.OpenStandardOutput(),
    OnHandlerError = (entry, message) => Console.Error.WriteLine($"{entry}: {message}"),
});
var program = Program.Load(bytecode, hostSchemaBytes, registry);   // host schema includes lmcode-rest.schema.json
rest.Run(program);   // runs the body (rest_route(...), rest_serve()), then serves until EOF, rest_stop() or a framing error
```

* `rest.Dispatch(new RestRequest(method, target, headers, body))` is the transport-independent core used by the unit tests.
* While serving, stdout belongs to the protocol: send `print` and diagnostics to stderr.
* Handlers run strictly one at a time, never nested in the body's VM run. Strings are bytes and stay byte-faithful.
* `examples/RestServe` is a tiny serve host (NativeAOT-compatible):
  `printf 'GET /health HTTP/1.1\r\n\r\n' | dotnet run --project examples/RestServe -- conformance/rest-suite/app.dslb conformance/rest-suite/host.schema.json`
  (exit 0 after serving, 2 on usage, 3 on a load/run error).

## Tests and pins

* `dotnet run -c Release --project tests/LmCode.Tests -- --rest-tests` runs the 23 rest cases (unit tests plus the 39 checks of the shared
  suite through in-memory streams and through a real child process); the full `lmscript test.lms` also runs them on the whole OS matrix, natively too.
* `scripts/sync-rest.sh` copies the shared suite from `$LMCODE_SPEC_DIR` (default `../lmcode`) into `conformance/rest-suite` and writes `rest-suite.lock`;
  `scripts/sync-rest.sh --verify` checks the copy (CI runs it). The fixtures are marked `-text` in `.gitattributes`.
