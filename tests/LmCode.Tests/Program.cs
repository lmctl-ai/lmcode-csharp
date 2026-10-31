using LmCode;

static class Tests
{
    static int Main(string[] args)
    {
        if(args.Length>=3 && args[0]=="--invocation-fixture")return InvocationVectorTests.Child(args[1..]);
        if (args is ["--write-vector-locks", var commit]) { VectorPinTests.Write(commit); return 0; }
        if (args is ["--vector-pins"]) { VectorPinTests.Run(); return 0; }
        if (args is ["--entry-tests"]) { EntryTests.Run(); return 0; }
        if (args is ["--string-catalog"]) { StringCatalogTests.Run(); return 0; }
        if(args is ["--invocation-tests"]) {try{InvocationTests.Run();ResultContractTests.Run();InvocationVectorTests.Run();return 0;}catch(Exception e){Console.Error.WriteLine("FAIL invocation tests: "+e.Message);return 1;}}
        if (args is ["--return-tests"]) {
            try {ResultJsonTests.Run();ResultSinkTests.Run();return 0;}
            catch(Exception error) {Console.Error.WriteLine("FAIL return tests: "+error.Message);return 1;}
        }
        if(args is ["--return-vector-tests"]) {
            try {ReturnVectorTests.Run();return 0;}
            catch(Exception error) {Console.Error.WriteLine("FAIL return vectors: "+error.Message);return 1;}
        }
        var checks = new (string Name, Func<bool> Check)[]
        {
            ("VmString preserves raw bytes", RawBytes),
            ("G15 special values", G15),
            ("Value accessors", Values),
            ("Registry signatures", RegistrySignatures),
            ("Context object lifecycle", ContextObjects),
            ("Loader rejects truncated input", LoaderRejectsTruncated),
            ("Loader rejects bad magic", LoaderRejectsBadMagic),
            ("Report JSON includes location", ReportJsonLocation),
            ("Public collection constructors", CollectionConstructors),
        };
        foreach (var (name, check) in checks)
            if (!check()) return Fail(name);
        TemplateTests.Run();
        int results=ResultJsonTests.Run()+ResultSinkTests.Run()+ReturnVectorTests.Run()+InvocationTests.Run()+ResultContractTests.Run()+InvocationVectorTests.Run();
        if (!RuntimeTests.Run()) return Fail("runtime tests");
        int verified = VerifierTests.Run();
        int catalog = StringCatalogTests.Run();
        int collections = CollectionsTestingCatalogTests.Run();
        int math = MathCatalogTests.Run();
        int standard = StandardRegistryTests.Run();
        int files = FileCatalogTests.Run();
        int v2 = CatalogV2Tests.Run();
        int encoding = EncodingCatalogTests.Run();
        int ruleMethods = RuleMethodsPinTests.Run();
        int entries = EntryTests.Run();
        int pins = VectorPinTests.Run();
        Console.WriteLine($"PASS unit tests ({checks.Length + 6 + results + verified + catalog + collections + math + standard + files + v2 + encoding + ruleMethods + entries + pins})"); return 0;
    }

    static bool RawBytes()
    {
        var raw = new byte[] { 0, 255, 1, 0, 42 };
        var s = new VmString(raw);
        return s.Span.SequenceEqual(raw) && VmString.Concat(s, VmString.FromString("x")).Span.SequenceEqual(new byte[] { 0, 255, 1, 0, 42, 120 });
    }
    static bool G15() => Formatting.FormatG15(double.NegativeZero) == "-0" && Formatting.FormatG15(double.PositiveInfinity) == "inf" && Formatting.FormatG15(double.NaN) == "nan";
    static bool Values() => Value.Int(4).AsInt() == 4 && Value.Bool(true).AsBool() && Value.Str(VmString.FromString("x")).AsStr().ToString() == "x";
    static bool RegistrySignatures()
    {
        var r = new Registry(false).Context("root", "Thing").Function("f", ["int"], "int", (_, a) => Value.Int(a[0].AsInt()));
        return r.Signatures().SequenceEqual(["context root: Thing", "fn f(int) -> int"]);
    }
    static bool ContextObjects()
    {
        var c = new Context(); var reference = c.SetObject("x", "Thing", "value");
        return c.Lookup("x") == reference && c.Get<string>(reference) == "value" && c.Take<string>(reference) == "value";
    }
    static bool LoaderRejectsTruncated()
    {
        try { _ = Program.Load([0], [], new Registry()); return false; }
        catch (Error e) { return e.Code == "LOAD_BAD_BYTECODE"; }
    }
    static string Vector(string name) => Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")), "conformance", "vectors", name);
    static bool LoaderRejectsBadMagic()
    {
        try { _ = Program.Load(File.ReadAllBytes(Vector("load-bad-magic.dslb")), File.ReadAllBytes(Vector("schema.json")), new Registry()); return false; }
        catch (Error e) { return e.Code == "LOAD_BAD_BYTECODE" && e.Message.Contains("bad magic", StringComparison.Ordinal); }
    }
    static bool ReportJsonLocation()
    {
        var e = new Error("RUN_ERROR", "bad"); e.Locate(7, 3);
        using var output=new MemoryStream();ResultFrames.FailureFrame(output,"run",e);var json=System.Text.Encoding.UTF8.GetString(output.ToArray());
        return json.Contains("\"code\":\"RUN_ERROR\"", StringComparison.Ordinal) && json.Contains("\"line\":7", StringComparison.Ordinal) && json.Contains("\"column\":3", StringComparison.Ordinal);
    }
    static bool CollectionConstructors()
    {
        var raw = new VmString(new byte[] { 0, 255 });
        var strings = new LmCode.Stdlib.StrList([raw]);
        var ints = new LmCode.Stdlib.IntList([1, long.MaxValue]);
        var floats = new LmCode.Stdlib.FloatList([1.5, -0.0]);
        var map = new LmCode.Stdlib.StrMap([
            (VmString.FromString("a"), VmString.FromString("one")),
            (VmString.FromString("b"), VmString.FromString("two")),
            (VmString.FromString("a"), VmString.FromString("updated"))]);
        return strings.Values[0].Span.SequenceEqual(raw.Span) && ints.Values.SequenceEqual([1, long.MaxValue]) &&
            floats.Values.Count == 2 && BitConverter.DoubleToInt64Bits(floats.Values[1]) == BitConverter.DoubleToInt64Bits(-0.0) &&
            map.Entries.Select(x => x.Key.ToString()).SequenceEqual(["a", "b"]) && map.Entries[0].Value.ToString() == "updated";
    }
    static int Fail(string message) { Console.Error.WriteLine("FAIL " + message); return 1; }
}
