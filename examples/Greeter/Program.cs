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
if (args is not ["--dsl", var path]) { Console.Error.WriteLine("Usage: Greeter --dsl FILE | --dsl-schema --json | --dsl-signatures"); return 2; }
VmProgram program;
try { program = VmProgram.Load(File.ReadAllBytes(path), schema, registry); }
catch (Error error) { return Reports.ReportError("load", error); }
catch (IOException error) { return Reports.ReportError("load", new Error("LOAD_BAD_BYTECODE", error.Message)); }
try { program.Run(new Context(), registry); return Reports.ReportOk("null"); }
catch (Error error) { return Reports.ReportError("run", error); }
