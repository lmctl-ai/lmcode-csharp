using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LmCode;
using LmCode.Stdlib;

static class StringCatalogTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static Registry Catalog() { var r = new Registry(); StringCatalog.RegisterString(r); return r; }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static int Run()
    {
        foreach (var (value, expected) in new (VmString, long)[] {
            (VmString.FromString("a\0b"), 3), (VmString.FromString("é"), 2),
            (VmString.FromString("日本"), 6), (VmString.FromString(""), 0), (new VmString([0xff, 0xfe]), 2) })
            Require(StringCatalog.StrLen(value) == expected, "str_len must count bytes without decoding UTF-8");

        var signatures = Catalog().Signatures();
        Require(signatures.SequenceEqual(["fn str_len(string) -> int"]), "unexpected catalog functions");
        Require(signatures.SequenceEqual(SchemaSignatures(Fixture("string.schema-fragment.json"))), "fragment differs from registry");
        Require(signatures.SequenceEqual(SchemaSignatures(Fixture("string.schema.json"))), "fixture differs from registry");
        CompilerParity(signatures);

        var trace = new List<long>();
        var registry = Catalog().Function("capture", ["int"], "void", (_, args) => { trace.Add(args[0].AsInt()); return Value.None; });
        Program.Load(File.ReadAllBytes(Fixture("string.dslb")), File.ReadAllBytes(Fixture("string-trace.schema.json")), registry)
            .Run(new Context(), registry);
        Require(trace.SequenceEqual(new long[] { 0, 3, 2, 6 }), "compiled str_len trace differs");
        Console.WriteLine("PASS string catalog: byte lengths, schema parity, compiled trace");
        return 9;
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

    private static void CompilerParity(string[] signatures)
    {
        string? requested = Environment.GetEnvironmentVariable("LMSCRIPT_BIN");
        string? compiler = FindCompiler(string.IsNullOrEmpty(requested) ? "lmscript" : requested);
        if (compiler is null)
        {
            if (!string.IsNullOrEmpty(requested)) throw new Exception($"LMSCRIPT_BIN not found: {requested}");
            Console.WriteLine("SKIP string compiler parity: lmscript is absent from PATH");
            return;
        }
        var start = new ProcessStartInfo(compiler) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (OperatingSystem.IsWindows() && (compiler.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || compiler.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            start.FileName = "cmd.exe";
            start.Arguments = $"/d /s /c \"\"{compiler}\" --dsl-signatures \"{Fixture("string.schema.json")}\"\"";
        }
        else
        {
            start.ArgumentList.Add("--dsl-signatures");
            start.ArgumentList.Add(Fixture("string.schema.json"));
        }
        using var process = Process.Start(start) ?? throw new Exception("cannot start lmscript");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Require(process.ExitCode == 0, "lmscript --dsl-signatures failed: " + stderr.GetAwaiter().GetResult());
        string expected = string.Join('\n', signatures) + "\n";
        Require(stdout.GetAwaiter().GetResult().Replace("\r\n", "\n", StringComparison.Ordinal) == expected, "compiler signatures differ from registry");
        Console.WriteLine("PASS string compiler/registry signature parity");
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
