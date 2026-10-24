using LmCode;

static class Tests
{
    static int Main(string[] args)
    {
        if (args is ["--string-catalog"]) { StringCatalogTests.Run(); return 0; }
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
        };
        foreach (var (name, check) in checks)
            if (!check()) return Fail(name);
        if (!RuntimeTests.Run()) return Fail("runtime tests");
        int verified = VerifierTests.Run();
        int catalog = StringCatalogTests.Run();
        int collections = CollectionsTestingCatalogTests.Run();
        int math = MathCatalogTests.Run();
        Console.WriteLine($"PASS unit tests ({checks.Length + 6 + verified + catalog + collections + math})"); return 0;
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
        var r = new Registry().Context("root", "Thing").Function("f", ["int"], "int", (_, a) => Value.Int(a[0].AsInt()));
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
        var json = Reports.ErrorJson("run", e);
        return json.Contains("\"code\": \"RUN_ERROR\"", StringComparison.Ordinal) && json.Contains("\"line\": 7", StringComparison.Ordinal) && json.Contains("\"column\": 3", StringComparison.Ordinal);
    }
    static int Fail(string message) { Console.Error.WriteLine("FAIL " + message); return 1; }
}
