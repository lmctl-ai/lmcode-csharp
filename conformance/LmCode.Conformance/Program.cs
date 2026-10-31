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
    public readonly string TempDir;
    private readonly bool standard;

    public Stub(bool standardCatalog)
    {
        standard = standardCatalog;
        TempDir = standard ? Path.Combine(Path.GetTempPath(), "lmcode-" + Guid.NewGuid().ToString("N")) : "";
        if (standard) Directory.CreateDirectory(TempDir);
        Registry = new Registry(standardCatalog);
        if (standardCatalog) Registry.SetResultSink(new ResultSink());
        TraceRef = Context.Insert("Trace", Trace);
        RootRef = Context.Insert("Thing", new Thing(0));
        VarRef = Context.Insert("Var", new Thing(0));
        XsRef = Context.Insert("IntList", new IntList());
        {
            Registry.Function("bytes", ["string"], "string", (_, args) => Value.Str(DecodeHex(args[0].AsStr())));
            Registry.Method("Trace", "hex", ["string"], "void", (_, _, a) => { Trace.Add("hex " + Convert.ToHexString(a[0].AsStr().Span).ToLowerInvariant()); return Value.None; });
            if (standard)
            {
                TestingCatalog.RegisterTesting(Registry, Output);
                Registry.Function("printed", [], "string", (_, _) => Value.Str(new VmString(Output.ToArray())));
                Registry.Function("test_temp_dir", [], "string", (_, _) => Value.Str(VmString.FromString(TempDir)));
                Registry.Function("test_long_path", [], "string", (_, _) =>
                {
                    string parent = Path.Combine(TempDir, new string('x', 120), new string('y', 120)); Directory.CreateDirectory(parent);
                    return Value.Str(VmString.FromString(Path.Combine(parent, "file.bin")));
                });
            }
        }
        Registry.Context("t", "Trace");
        if (!standard) Registry.Context("root", "Thing").Context("v", "Var").Context("xs", "IntList");
        if (!standard)
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
    public void Bind() { Context.Set("t", TraceRef); if (!standard) { Context.Set("root", RootRef); Context.Set("v", VarRef); Context.Set("xs", XsRef); } }
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
    static Value[] ParseArgs(string json, Entry entry, Stub stub)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new Exception("expected an array");
            var values = document.RootElement.EnumerateArray().ToArray();
            if (values.Length != entry.Params.Count) return new Value[values.Length];
            var result = new Value[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i]; var type = entry.Params[i];
                if (value.ValueKind == JsonValueKind.Object)
                {
                    if (!value.TryGetProperty("type", out var typeValue) || !value.TryGetProperty("id", out var idValue) || typeValue.GetString() != type || type != "Thing")
                        throw new Exception("object argument does not match the parameter type");
                    result[i] = Value.Object("Thing", stub.Context.Insert("Thing", new Thing(idValue.GetInt64())));
                }
                else if (value.ValueKind == JsonValueKind.String) { if (type is "int" or "float" or "bool" or "Thing") throw new Exception($"string argument does not match parameter type {type}"); result[i] = Value.Str(VmString.FromString(value.GetString()!)); }
                else if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) { if (type != "bool") throw new Exception($"bool argument does not match parameter type {type}"); result[i] = Value.Bool(value.GetBoolean()); }
                else if (value.ValueKind == JsonValueKind.Number) { if (type != "int" && type != "float") throw new Exception($"number argument does not match parameter type {type}"); result[i] = type == "float" ? Value.Float(value.GetDouble()) : Value.Int(value.GetInt64()); }
                else throw new Exception("expected a scalar or Thing object");
            }
            return result;
        }
        catch (JsonException e) { throw Error.Host("conformance: bad call args: " + e.Message); }
        catch (Exception e) when (e is not Error) { throw Error.Host("conformance: bad call args: " + e.Message + " in " + json); }
    }
    static bool ResultMatches(string line, Value value, Context context)
    {
        if (!line.StartsWith("result ", StringComparison.Ordinal)) return false;
        var spec = line[7..]; if (spec == "void") return value.Kind == ValueKind.None;
        var split = spec.IndexOf(' '); if (split < 0) return false;
        var type = spec[..split]; var text = spec[(split + 1)..];
        return type switch
        {
            "int" => value.Kind == ValueKind.Int && value.AsInt().ToString(CultureInfo.InvariantCulture) == text,
            "float" => value.Kind == ValueKind.Float && Formatting.FormatG15(value.AsFloat()) == text,
            "string" => value.Kind == ValueKind.Str && value.AsStr().ToString() == text,
            "bool" => value.Kind == ValueKind.Bool && (value.AsBool() ? "true" : "false") == text,
            "thing" => value.Kind == ValueKind.Obj && value.TypeName == "Thing" && context.Get<Thing>(value.AsObj()).Id.ToString(CultureInfo.InvariantCulture) == text,
            _ => false
        };
    }
    static string BareMessage(Error? caught, string message)
    {
        if (caught?.Line is not uint line) return message;
        var prefix = $"dsl line {line}{(caught.Column is ushort column ? $":{column}" : "")}: ";
        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }
    static bool LocationMatches(Error? caught, uint line, ushort column) =>
        (line == 0 ? caught?.Line is null : caught?.Line == line) &&
        (column == 0 ? caught?.Column is null : caught?.Column == column);
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
            var standardCatalog = name.StartsWith("return-", StringComparison.Ordinal) || (schemaDocument.RootElement.TryGetProperty("functions", out var functions) &&
                new[] { "str_len", "math_abs", "str_list", "print", "file_read" }.Any(name => functions.TryGetProperty(name, out _)));
            var stub = new Stub(standardCatalog); stub.Bind(); var loaded = false; var ran = false; var called = false; var error = ""; var code = ""; Value callResult = Value.None; Program? program = null;
            Error? caught = null;
            try
            {
                program = Program.Load(File.ReadAllBytes(Path.Combine(dir, name + ".dslb")), schemaBytes, stub.Registry); loaded = true;
                var isCall = kind is "call" or "call_error" or "call_error_at";
                if (kind != "entries") { program.Run(stub.Context, stub.Registry); ran = true; }
                if (isCall)
                {
                    var split = exp[1].IndexOf(' '); var entryName = split < 0 ? exp[1] : exp[1][..split];
                    var index = -1; for (var i = 0; i < program.Entries.Count; i++) if (program.Entries[i].Name == entryName) { index = i; break; }
                    if (index < 0) throw Error.Host($"conformance: entry '{entryName}' not found");
                    var json = split < 0 ? "[]" : exp[1][(split + 1)..]; callResult = program.Call(index, stub.Context, stub.Registry, ParseArgs(json, program.Entries[index], stub)); called = true;
                }
            }
            catch (Error e) { caught = e; error = e.Message; code = e.Code; }
            catch (Exception e) { error = e.Message; }
            var locationOk = false;
            if (lines.Length >= 3 && uint.TryParse(lines[1], out var expectedLine) && ushort.TryParse(lines[2], out var expectedColumn))
                locationOk = (expectedLine == 0 ? caught is null || !caught.Line.HasValue : caught?.Line == expectedLine) && (expectedColumn == 0 ? caught is null || !caught.Column.HasValue : caught?.Column == expectedColumn);
            var exactMessage = BareMessage(caught, error);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LMCODE_TRACE")))
            {
                foreach (string value in stub.Trace) Console.WriteLine($"TRACE\t{name}\t{value}");
                if (code.Length != 0) Console.WriteLine($"ERROR\t{name}\t{code}\t{exactMessage}");
            }
            var callAt = false;
            if (kind == "call_error_at" && lines.Length >= 5 && uint.TryParse(lines[2], out var callLine) && ushort.TryParse(lines[3], out var callColumn))
                callAt = LocationMatches(caught, callLine, callColumn);
            var ok = kind == "trace" ? ran && stub.Trace.Count == lines.Length && stub.Trace.Zip(lines).All(x => Match(x.First, x.Second)) : kind == "entries" ? loaded && program!.Entries.Select(e => $"{e.Name} {e.Annotation} {e.Iface}").SequenceEqual(lines) : kind == "call" ? loaded && ran && called && lines.Length >= 2 && ResultMatches(lines[1], callResult, stub.Context) && stub.Trace.Count == lines.Length - 2 && stub.Trace.Zip(lines.Skip(2)).All(x => Match(x.First, x.Second)) : kind == "call_error" ? loaded && !called && lines.Length == 3 && code == lines[1] && error.Contains(lines[2], StringComparison.Ordinal) : kind == "call_error_at" ? loaded && !called && lines.Length >= 5 && code == lines[1] && callAt && error.Contains(lines[4], StringComparison.Ordinal) : kind == "runtime_error" ? loaded && !ran && error.Contains(lines[0], StringComparison.Ordinal) : kind == "runtime_error_exact" ? loaded && !ran && lines.Length == 2 && code == lines[0] && exactMessage == lines[1] : kind == "runtime_error_file" ? loaded && !ran && lines.Length >= 3 && code == lines[0] && exactMessage.StartsWith(lines[1] + ": ", StringComparison.Ordinal) && exactMessage.Contains(lines[2], StringComparison.Ordinal) : kind == "runtime_error_at" ? loaded && !ran && lines.Length >= 4 && code == lines[0] && locationOk && error.Contains(lines[3], StringComparison.Ordinal) : kind == "load_error" && !loaded && error.Contains(lines[0], StringComparison.Ordinal);
            Console.WriteLine((ok ? "ok - " : "FAIL - ") + name + " (" + kind + ")"); if (!ok) { failed++; Console.WriteLine("    error: " + error); }
            if (stub.TempDir.Length != 0) try { Directory.Delete(stub.TempDir, true); } catch { }
        }
        Console.WriteLine($"{(failed == 0 ? "PASS" : "FAIL")} {names.Length - failed}/{names.Length} vectors"); return failed == 0 ? 0 : 1;
    }
}
