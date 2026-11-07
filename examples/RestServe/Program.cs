// Serves a compiled program's REST routes over stdin/stdout (the opt-in `rest` std module; no network).
//   dotnet run --project examples/RestServe -- PROGRAM.dslb HOST.schema.json [--max-body-bytes N]
return LmCode.Rest.RestServeHost.Run(args);
