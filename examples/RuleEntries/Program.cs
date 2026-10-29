using LmCode;
using System.Reflection;
using VmProgram = LmCode.Program;
if (args.Length != 1) { Console.Error.WriteLine("usage: RuleEntries FILE.dslb"); return 2; }
using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("rule.schema")!;
using var bytes = new MemoryStream(); resource.CopyTo(bytes);
var registry = new Registry();
var program = VmProgram.Load(File.ReadAllBytes(args[0]), bytes.ToArray(), registry);
int index = Enumerable.Range(0, program.Entries.Count).Single(i => program.Entries[i].Annotation == "increment");
if (!program.Entries[index].Params.SequenceEqual(["int"])) throw new Exception("incorrect entry parameters");
var value = program.Call(index, new Context(), registry, [Value.Int(41)]);
if (value.AsInt() != 42) throw new Exception("incorrect entry result");
Console.WriteLine("PASS @increment implements Score: 41 -> 42");
return 0;
