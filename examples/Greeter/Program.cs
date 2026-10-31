using LmCode;
using VmProgram = LmCode.Program;

var schema = """{"dsl.schema":2,"name":"greeter","types":{},"functions":{"greet":[{"params":["string"],"returns":"void"}]},"context":{}}"""u8.ToArray();
var registry = new Registry();
registry.Function("greet", ["string"], "void", (_, values) =>
{
    Console.Error.WriteLine("Hello, " + values[0].AsStr());
    return Value.None;
});
if (args is ["--lmcode-schema", "--json"]) { Console.OpenStandardOutput().Write(schema); return 0; }
if (args is ["--lmcode-signatures"]) { Console.WriteLine(string.Join('\n', registry.Signatures())); return 0; }
if (args is not ["--lmcode", var path]) { Console.Error.WriteLine("Usage: Greeter --lmcode FILE | --lmcode-schema --json | --lmcode-signatures"); return 2; }
VmProgram program;
try { program = VmProgram.Load(File.ReadAllBytes(path), schema, registry); }
catch (Error error) { return ResultFrames.FailureFrame(Console.OpenStandardOutput(),"load", error); }
catch (IOException error) { return ResultFrames.FailureFrame(Console.OpenStandardOutput(),"load", new Error("LOAD_BAD_BYTECODE", error.Message)); }
try { var results=InvocationResults.FromCommand([path]);results.Select(0,registry,ResultContract.FromSchema(System.Text.Encoding.UTF8.GetString(schema)));program.Run(new Context(), registry);results.Complete(0);return results.WriteFrame(Console.OpenStandardOutput()); }
catch (Error error) { return ResultFrames.FailureFrame(Console.OpenStandardOutput(),"run", error); }
