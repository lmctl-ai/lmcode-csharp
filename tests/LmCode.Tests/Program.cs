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
            ("Public collection constructors", CollectionConstructors),
        };
        foreach (var (name, check) in checks)
            if (!check()) return Fail(name);
        if (!RuntimeTests.Run()) return Fail("runtime tests");
        int verified = VerifierTests.Run();
        int catalog = StringCatalogTests.Run();
        int collections = CollectionsTestingCatalogTests.Run();
        int math = MathCatalogTests.Run();
        int standard = StandardRegistryTests.Run();
        int files = FileCatalogTests.Run();
        int v2 = CatalogV2Tests.Run();
        Console.WriteLine($"PASS unit tests ({checks.Length + 6 + verified + catalog + collections + math + standard + files + v2})"); return 0;
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
        var json = Reports.ErrorJson("run", e);
        return json.Contains("\"code\": \"RUN_ERROR\"", StringComparison.Ordinal) && json.Contains("\"line\": 7", StringComparison.Ordinal) && json.Contains("\"column\": 3", StringComparison.Ordinal);
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
