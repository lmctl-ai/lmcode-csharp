using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LmCode;
using LmCode.Stdlib;

static class StringCatalogTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static Registry Catalog() { var r = new Registry(false); StringCatalog.RegisterString(r); return r; }
    private static int checks;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); ++checks; }

    public static int Run()
    {
        checks = 0;
        CatalogEdges();
        foreach (var (value, expected) in new (VmString, long)[] {
            (VmString.FromString("a\0b"), 3), (VmString.FromString("é"), 2),
            (VmString.FromString("日本"), 6), (VmString.FromString(""), 0), (new VmString([0xff, 0xfe]), 2) })
            Require(StringCatalog.StrLen(value) == expected, "str_len must count bytes without decoding UTF-8");

        var signatures = Catalog().Signatures();
        Require(signatures.Length == 16, "expected all 16 catalog functions");
        Require(signatures.SequenceEqual(SchemaSignatures(Fixture("string.schema-fragment.json"))), "fragment differs from registry");
        Require(signatures.SequenceEqual(SchemaSignatures(Fixture("string.schema.json"))), "fixture differs from registry");
        CompilerParity(signatures);

        var trace = new List<long>();
        var registry = Catalog().Function("capture", ["int"], "void", (_, args) => { trace.Add(args[0].AsInt()); return Value.None; });
        Program.Load(File.ReadAllBytes(Fixture("string.dslb")), File.ReadAllBytes(Fixture("string-trace.schema.json")), registry)
            .Run(new Context(), registry);
        Require(trace.SequenceEqual(new long[] { 0, 3, 2, 6 }), "compiled str_len trace differs");
        Console.WriteLine($"PASS string catalog ({checks} checks): byte semantics, schema/compiler parity, compiled trace");
        return checks;
    }

    private static VmString S(string text) => VmString.FromString(text);
    private static void Bytes(VmString actual, byte[] expected) => Require(actual.Span.SequenceEqual(expected), "byte result differs");
    private static void HostError(Action action, string message)
    {
        try { action(); }
        catch (Error e) { Require(e.Code == "RUN_HOST" && e.Message == message, "host policy error differs"); return; }
        throw new Exception("expected host policy failure");
    }

    private static void CatalogEdges()
    {
        var raw = new VmString([0xff, 0, 0x41, 0xc3, 0xa9]);
        Require(StringCatalog.StrStartsWith(raw, new VmString([0xff, 0])), "binary prefix");
        Require(StringCatalog.StrEndsWith(raw, S("é")), "multibyte suffix");
        Require(StringCatalog.StrContains(raw, new VmString([0, 0x41])), "NUL search");
        Require(StringCatalog.StrStartsWith(raw, S("")) && StringCatalog.StrEndsWith(raw, S("")) && StringCatalog.StrContains(raw, S("")), "empty needles");
        Require(!StringCatalog.StrStartsWith(S("a"), S("ab")) && !StringCatalog.StrEndsWith(S("a"), S("ba")), "oversized needles");
        Require(StringCatalog.StrFind(raw, S("é")) == 3 && StringCatalog.StrFind(raw, S("z")) == -1 && StringCatalog.StrFind(raw, S("")) == 0, "byte offsets");
        Bytes(StringCatalog.StrSubstr(S("é"), 1, long.MaxValue), [0xa9]);
        Bytes(StringCatalog.StrSubstr(raw, long.MinValue, 2), [0xff, 0]);
        Bytes(StringCatalog.StrSubstr(raw, long.MaxValue, 1), []);
        Bytes(StringCatalog.StrSubstr(raw, 0, long.MinValue), []);
        Bytes(StringCatalog.StrLower(raw), [0xff, 0, 0x61, 0xc3, 0xa9]);
        Bytes(StringCatalog.StrUpper(new VmString([0xff, 0, 0x61, 0xc3, 0xa9])), [0xff, 0, 0x41, 0xc3, 0xa9]);
        Bytes(StringCatalog.StrTrim(new VmString([9, 10, 11, 12, 13, 32, 0, 32, 9])), [0]);
        Bytes(StringCatalog.StrTrim(S(" \t\r\n")), []);
        Bytes(StringCatalog.StrTrim(S("é")), [0xc3, 0xa9]);
        Bytes(StringCatalog.StrReplace(S("aaaaa"), S("aa"), S("b")), [0x62, 0x62, 0x61]);
        Bytes(StringCatalog.StrReplace(raw, S(""), S("z")), raw.Span.ToArray());
        Bytes(StringCatalog.StrReplace(new VmString([0xff, 0, 0xff]), new VmString([0xff]), S("é")), [0xc3, 0xa9, 0, 0xc3, 0xa9]);
        Bytes(StringCatalog.StrRepeat(new VmString([0xff, 0]), 2), [0xff, 0, 0xff, 0]);
        Bytes(StringCatalog.StrRepeat(S(""), long.MaxValue), []);
        Bytes(StringCatalog.StrRepeat(raw, 0), []);
        const string repeatError = "str_repeat: count must be nonnegative and result at most 67108864 bytes";
        HostError(() => StringCatalog.StrRepeat(S(""), -1), repeatError);
        HostError(() => StringCatalog.StrRepeat(S("x"), 67108865), repeatError);
        HostError(() => StringCatalog.StrRepeat(raw, long.MaxValue), repeatError);
        var large = new VmString(new byte[67108865]);
        const string replaceError = "str_replace: result must be at most 67108864 bytes";
        HostError(() => StringCatalog.StrReplace(large, S(""), S("")), replaceError);
        HostError(() => StringCatalog.StrReplace(large, S("x"), S("")), replaceError);
        foreach (var (token, value) in new (string, long)[] { ("+12",12), ("-9223372036854775808",long.MinValue), ("9223372036854775807",long.MaxValue), ("-0",0), ("0001",1) })
            Require(StringCatalog.StrIsInt(S(token)) && StringCatalog.StrToInt(S(token)) == value, "integer parsing " + token);
        foreach (string token in new[] { "", "+", "-", "+-1", "++1", "--1", "-+1", " 1", "1 ", "0x10", "1,5", "1\0", "9223372036854775808", "-9223372036854775809" })
            Require(!StringCatalog.StrIsInt(S(token)) && StringCatalog.StrToInt(S(token)) == 0, "invalid integer " + token);
        foreach (string token in new[] { "", "+", "-", ".", "+-1", "++1", " 1", "1 ", "0x1p2", "nan", "inf", "1e", "1e+", "1,5", "1\0", "1e9999", "1e-9999" })
            Require(!StringCatalog.StrIsFloat(S(token)) && StringCatalog.StrToFloat(S(token)) == 0, "invalid float " + token);
        foreach (var (token, value) in new (string, double)[] { (".5",.5), ("1.",1), ("+1.25e2",125), ("4.9406564584124654e-324",double.Epsilon), ("0e9999",0), ("0e-9999",0) })
            Require(StringCatalog.StrIsFloat(S(token)) && StringCatalog.StrToFloat(S(token)) == value, "float parsing " + token);
        Require(BitConverter.DoubleToInt64Bits(StringCatalog.StrToFloat(S("-0"))) == long.MinValue, "negative zero");
        Require(!StringCatalog.StrIsInt(raw) && !StringCatalog.StrIsFloat(raw), "invalid UTF8 is not numeric");
        foreach (var (text, pattern, expected) in new (string, string, bool)[] {
            ("a/b", "a*b", true), ("", "*", true), ("", "?", false), ("é", "?", false), ("é", "??", true),
            ("b", "[!a]", true), ("a", "[z-a]", false), ("]", "[]]", true), ("[", "[", true),
            ("*", "\\*", true), ("\\", "\\", true), ("-", "[\\-]", true), ("abc", "a*d", false) })
            Require(StringCatalog.StrMatch(S(text), S(pattern)) == expected, "glob " + pattern);
        Require(StringCatalog.StrMatch(new VmString([0xff, 0]), S("??")), "glob raw bytes");
        HostError(() => StringCatalog.StrMatch(S(new string('a', 32768)), S("*" + new string('a', 16384) + "b")), "str_match: pattern exceeds the 2^27 work limit");
    }

    private static string[] SchemaSignatures(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var signatures = new List<string>();
        foreach (var function in document.RootElement.GetProperty("functions").EnumerateObject())
            foreach (var overload in function.Value.EnumerateArray())
            {
                var parameters = overload.GetProperty("params").EnumerateArray().Select(p => p.GetString());
                signatures.Add($"fn {function.Name}({string.Join(", ", parameters)}) -> {overload.GetProperty("returns").GetString()}");
            }
        signatures.Sort(StringComparer.Ordinal);
        return signatures.ToArray();
    }

    internal static void CompilerParity(string[] signatures, string? schemaPath = null, string catalog = "string")
    {
        schemaPath ??= Fixture("string.schema.json");
        string? requested = Environment.GetEnvironmentVariable("LMSCRIPT_BIN");
        string? compiler = FindCompiler(string.IsNullOrEmpty(requested) ? "lmscript" : requested);
        if (compiler is null)
        {
            if (!string.IsNullOrEmpty(requested)) throw new Exception($"LMSCRIPT_BIN not found: {requested}");
            Console.WriteLine($"SKIP {catalog} compiler parity: lmscript is absent from PATH");
            return;
        }
        // lmscript 0.2.420 builds std into the compiler: --dsl-signatures emits
        // the full std set merged with the schema's functions, while
        // --without-std emits only what the schema adds on top of std.
        string[] all = CompilerSignatures(compiler, schemaPath, false);
        string[] hostOnly = CompilerSignatures(compiler, schemaPath, true);
        var stdRegistry = new Registry(false);
        StandardCatalog.RegisterStd(stdRegistry);
        var std = stdRegistry.Signatures();
        var stdSet = std.ToHashSet(StringComparer.Ordinal);
        var registrySet = signatures.ToHashSet(StringComparer.Ordinal);
        Require(hostOnly.All(registrySet.Contains), $"{catalog} compiler declares functions the registry lacks: {string.Join(", ", hostOnly.Where(sig => !registrySet.Contains(sig)).Order(StringComparer.Ordinal))}");
        string[] registryForeign = registrySet.Except(hostOnly).Where(sig => !stdSet.Contains(sig)).Order(StringComparer.Ordinal).ToArray();
        Require(registryForeign.Length == 0, $"{catalog} registry signatures missing from compiler std: {string.Join(", ", registryForeign)}");
        var expected = std.Concat(hostOnly).Order(StringComparer.Ordinal).ToArray();
        Require(all.SequenceEqual(expected), $"{catalog} compiler signatures differ from std registry: missing [{string.Join(", ", expected.Except(all).Order(StringComparer.Ordinal))}], extra [{string.Join(", ", all.Except(expected).Order(StringComparer.Ordinal))}]");
        Console.WriteLine($"PASS {catalog} compiler/registry signature parity");
    }

    private static string[] CompilerSignatures(string compiler, string schemaPath, bool withoutStd)
    {
        var start = new ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (OperatingSystem.IsWindows() && (compiler.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || compiler.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            start.FileName = "cmd.exe";
            start.Arguments = $"/d /s /c \"\"{compiler}\" --dsl-signatures \"{schemaPath}\"{(withoutStd ? " --without-std" : "")}\"\"";
        }
        else
        {
            start.ArgumentList.Add("--dsl-signatures");
            start.ArgumentList.Add(schemaPath);
            if (withoutStd) start.ArgumentList.Add("--without-std");
        }
        using var process = Process.Start(start) ?? throw new Exception("cannot start lmscript");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Require(process.ExitCode == 0, "lmscript --dsl-signatures failed: " + stderr.GetAwaiter().GetResult());
        return stdout.GetAwaiter().GetResult().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? FindCompiler(string name)
    {
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        string[] directories = Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar)
            ? [""] : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var directory in directories)
            foreach (var extension in extensions)
            {
                string path = Path.GetFullPath(Path.Combine(directory, name + extension));
                if (File.Exists(path)) return path;
            }
        return null;
    }
}
