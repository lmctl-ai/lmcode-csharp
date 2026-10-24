using System.Globalization;
using System.Text;
using System.Text.Json;
using LmCode;
using LmCode.Stdlib;

sealed class Thing { public long Id; public Thing(long id) => Id = id; }
sealed class IntList { public readonly long[] Values = [0, 1, 2, 3]; }

sealed class Stub
{
    public readonly List<string> Trace = [];
    public readonly Registry Registry;
    public readonly Context Context = new();
    public readonly ObjRef TraceRef;
    public readonly ObjRef RootRef;
    public readonly ObjRef VarRef;
    public readonly ObjRef XsRef;
    public readonly MemoryStream Output = new();
    private readonly bool collections;

    public Stub(bool collectionCatalog)
    {
        collections = collectionCatalog;
        Registry = new Registry(collectionCatalog);
        TraceRef = Context.Insert("Trace", Trace);
        RootRef = Context.Insert("Thing", new Thing(0));
        VarRef = Context.Insert("Var", new Thing(0));
        XsRef = Context.Insert("IntList", new IntList());
        {
            StringCatalog.RegisterString(Registry);
            MathCatalog.RegisterMath(Registry);
            Registry.Function("bytes", ["string"], "string", (_, args) => Value.Str(DecodeHex(args[0].AsStr())));
            Registry.Method("Trace", "hex", ["string"], "void", (_, _, a) => { Trace.Add("hex " + Convert.ToHexString(a[0].AsStr().Span).ToLowerInvariant()); return Value.None; });
            if (collections)
            {
                CollectionsCatalog.RegisterCollections(Registry);
                TestingCatalog.RegisterTesting(Registry, Output);
                Registry.Function("printed", [], "string", (_, _) => Value.Str(new VmString(Output.ToArray())));
            }
        }
        Registry.Context("t", "Trace");
        if (!collections) Registry.Context("root", "Thing").Context("v", "Var").Context("xs", "IntList");
        if (!collections)
        {
            Registry.Method("IntList", "len", [], "int", (ctx, self, _) => Value.Int(ctx.Get<IntList>(self).Values.Length));
            Registry.Method("IntList", "at", ["int"], "int", (ctx, self, a) => Value.Int(ctx.Get<IntList>(self).Values[checked((int)a[0].AsInt())]));
        }
        Registry.Method("Trace", "int", ["int"], "void", (_, _, a) => { Trace.Add("int " + a[0].AsInt()); return Value.None; });
        Registry.Method("Trace", "float", ["float"], "void", (_, _, a) => { Trace.Add("float " + Formatting.FormatG15(a[0].AsFloat())); return Value.None; });
        Registry.Method("Trace", "str", ["string"], "void", (_, _, a) => { Trace.Add("str " + a[0].AsStr()); return Value.None; });
        Registry.Method("Trace", "bool", ["bool"], "void", (_, _, a) => { Trace.Add("bool " + (a[0].AsBool() ? "true" : "false")); return Value.None; });
        Registry.Method("Trace", "thing", ["Thing"], "void", (ctx, _, a) => { Trace.Add("thing " + ctx.Get<Thing>(a[0].AsObj()).Id); return Value.None; });
        Registry.Method("Thing", "len", [], "int", (ctx, self, _) => { var n = ctx.Get<Thing>(self).Id; Trace.Add("len " + n); return Value.Int(n); });
        Registry.Method("Thing", "at", ["int"], "Thing", (ctx, _, a) => { var n = a[0].AsInt(); Trace.Add("at " + n); return Value.Object("Thing", ctx.Insert("Thing", new Thing(n))); });
        Registry.Method("Thing", "id", [], "int", (ctx, self, _) => Value.Int(ctx.Get<Thing>(self).Id));
        Registry.Method("Thing", "next", [], "Thing", (ctx, self, _) => Value.Object("Thing", ctx.Insert("Thing", new Thing(unchecked(ctx.Get<Thing>(self).Id + 1)))));
        Registry.Method("Thing", "name", [], "string", (ctx, self, _) => Value.Str(VmString.FromString("thing-" + ctx.Get<Thing>(self).Id)));
        Registry.Method("Thing", "scaled", ["float"], "float", (ctx, self, a) => Value.Float(ctx.Get<Thing>(self).Id * a[0].AsFloat()));
        Registry.Method("Trace", "int", ["float"], "void", (_, _, a) => { Trace.Add("float " + Formatting.FormatG15(a[0].AsFloat())); return Value.None; });
        Registry.Method("Trace", "status", [], "Status", (_, _, _) => Value.Str(VmString.FromString("OPTIMAL")));
        Registry.Method("Thing", "plus", ["Thing"], "Thing", (ctx, self, a) => ThingValue(unchecked(ctx.Get<Thing>(self).Id + ctx.Get<Thing>(a[0].AsObj()).Id)));
        Registry.Method("Thing", "minus", ["Thing"], "Thing", (ctx, self, a) => ThingValue(unchecked(ctx.Get<Thing>(self).Id - ctx.Get<Thing>(a[0].AsObj()).Id)));
        Registry.Method("Thing", "times", ["int"], "Thing", (ctx, self, a) => ThingValue(unchecked(ctx.Get<Thing>(self).Id * a[0].AsInt())));
        Registry.Method("Thing", "neg", [], "Thing", (ctx, self, _) => ThingValue(unchecked(-ctx.Get<Thing>(self).Id)));
        Registry.Method("Thing", "rminus", ["int"], "Thing", (ctx, self, a) => ThingValue(unchecked(a[0].AsInt() - ctx.Get<Thing>(self).Id)));
        Registry.Method("Thing", "set", ["int"], "Thing", (ctx, self, a) => { ctx.Get<Thing>(self).Id = a[0].AsInt(); return Value.Object("Thing", self); });
        Registry.Method("Var", "id", [], "int", (ctx, self, _) => Value.Int(ctx.Get<Thing>(self).Id));
        Registry.Method("Var", "expr", [], "Thing", (ctx, self, _) => { var n = ctx.Get<Thing>(self).Id; Trace.Add("expr " + n); return ThingValue(n); });
        Registry.Function("var", ["int"], "Var", (_, a) => { var n = a[0].AsInt(); Trace.Add("var " + n); return Value.Object("Var", Context.Insert("Var", new Thing(n))); });
        Registry.Function("twice", ["float"], "float", (_, a) => Value.Float(a[0].AsFloat() * 2));
        Registry.Function("thing", ["int"], "Thing", (_, a) => ThingValue(a[0].AsInt()));
        Registry.Function("twice", ["int"], "int", (_, a) => Value.Int(unchecked(a[0].AsInt() * 2)));
        Registry.Function("bound", ["int"], "int", (_, a) => { Trace.Add("bound " + a[0].AsInt()); return Value.Int(a[0].AsInt()); });
        Registry.Function("half", ["float"], "float", (_, a) => Value.Float(a[0].AsFloat() / 2));
        Registry.Function("yes", [], "bool", (_, _) => { Trace.Add("yes"); return Value.Bool(true); });
        Registry.Function("bad_int", [], "int", (_, _) => Value.Str(VmString.FromString("x")));
        Registry.Function("fail", ["string"], "void", (_, a) => throw new Error("RUN_HOST", a[0].AsStr().ToString()));
    }

    private Value ThingValue(long id) => Value.Object("Thing", Context.Insert("Thing", new Thing(id)));
    private static VmString DecodeHex(VmString encoded)
    {
        var text = encoded.Span;
        if ((text.Length & 1) != 0) throw new Error("RUN_HOST", "bytes: expected lowercase hexadecimal pairs");
        var bytes = new byte[text.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var hi = HexDigit(text[2 * i]);
            var lo = HexDigit(text[2 * i + 1]);
            bytes[i] = (byte)((hi << 4) | lo);
        }
        return new VmString(bytes);
    }
    private static int HexDigit(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        _ => throw new Error("RUN_HOST", "bytes: expected lowercase hexadecimal pairs")
    };
    public void Bind() { Context.Set("t", TraceRef); if (!collections) { Context.Set("root", RootRef); Context.Set("v", VarRef); Context.Set("xs", XsRef); } }
}

static class Runner
{
    static string Unescape(string s) => s.Replace("\\n", "\n");
    static bool Match(string actual, string expected)
    {
        if (!expected.StartsWith("~ ", StringComparison.Ordinal)) return actual == expected;
        var body = expected[2..]; var split = body.LastIndexOf(' '); if (split < 0) return false;
        var prefix = body[..(split + 1)]; if (!actual.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (!double.TryParse(body[(split + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var want) || !double.TryParse(actual[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var got)) return false;
        return double.IsFinite(want) && double.IsFinite(got) && Math.Abs(got - want) <= 1e-12 * Math.Max(Math.Abs(want), 1e-300);
    }
    public static int Main(string[] args)
    {
        var dir = args.Length == 1 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "vectors"));
        var schemaByName = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "manifest.json"))))
            foreach (var vector in manifest.RootElement.GetProperty("vectors").EnumerateArray())
                schemaByName[vector.GetProperty("name").GetString()!] = vector.GetProperty("schema").GetString()!;
        var names = Directory.EnumerateFiles(dir, "*.expect").Select(Path.GetFileNameWithoutExtension).Where(x => x is not null).Cast<string>().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (names.Length == 0) { Console.Error.WriteLine("no vectors in " + dir); return 2; }
        var failed = 0;
        foreach (var name in names)
        {
            var exp = File.ReadAllLines(Path.Combine(dir, name + ".expect")); var kind = exp[0]; var lines = exp.Skip(1).Select(Unescape).ToArray(); var schemaName = schemaByName.TryGetValue(name, out var selectedSchema) ? selectedSchema : "schema.json"; var schemaBytes = File.ReadAllBytes(Path.Combine(dir, schemaName));
            using var schemaDocument = JsonDocument.Parse(schemaBytes);
            var collectionCatalog = schemaDocument.RootElement.TryGetProperty("functions", out var functions) && functions.TryGetProperty("str_list", out _);
            var stub = new Stub(collectionCatalog); stub.Bind(); var loaded = false; var ran = false; var error = ""; var code = "";
            Error? caught = null;
            try { var p = Program.Load(File.ReadAllBytes(Path.Combine(dir, name + ".dslb")), schemaBytes, stub.Registry); loaded = true; p.Run(stub.Context, stub.Registry); ran = true; }
            catch (Error e) { caught = e; error = e.Message; code = e.Code; }
            catch (Exception e) { error = e.Message; }
            var locationOk = false;
            if (lines.Length >= 3 && uint.TryParse(lines[1], out var expectedLine) && ushort.TryParse(lines[2], out var expectedColumn))
                locationOk = (expectedLine == 0 ? caught is null || !caught.Line.HasValue : caught?.Line == expectedLine) && (expectedColumn == 0 ? caught is null || !caught.Column.HasValue : caught?.Column == expectedColumn);
            var exactMessage = error;
            if (caught?.Line is uint line)
            {
                var prefix = $"dsl line {line}{(caught.Column is ushort column ? $":{column}" : "")}: ";
                if (exactMessage.StartsWith(prefix, StringComparison.Ordinal)) exactMessage = exactMessage[prefix.Length..];
            }
            var ok = kind == "trace" ? ran && stub.Trace.Count == lines.Length && stub.Trace.Zip(lines).All(x => Match(x.First, x.Second)) : kind == "runtime_error" ? loaded && !ran && error.Contains(lines[0], StringComparison.Ordinal) : kind == "runtime_error_exact" ? loaded && !ran && lines.Length == 2 && code == lines[0] && exactMessage == lines[1] : kind == "runtime_error_at" ? loaded && !ran && lines.Length >= 4 && code == lines[0] && locationOk && error.Contains(lines[3], StringComparison.Ordinal) : kind == "load_error" && !loaded && error.Contains(lines[0], StringComparison.Ordinal);
            Console.WriteLine((ok ? "ok - " : "FAIL - ") + name + " (" + kind + ")"); if (!ok) { failed++; Console.WriteLine("    error: " + error); }
        }
        Console.WriteLine($"{(failed == 0 ? "PASS" : "FAIL")} {names.Length - failed}/{names.Length} vectors"); return failed == 0 ? 0 : 1;
    }
}
