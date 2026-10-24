using LmCode;
using LmCode.Stdlib;
using System.Text.Json;

static class StandardRegistryTests
{
    public static int Run()
    {
        var standard = new Registry();
        Require(standard.Signatures().Length == 69 && standard.HostSignatures().Length == 0, "default standard surface");
        standard.CheckSchemaSignatures(CanonicalSignatures());
        var optout = new Registry(false);
        Require(optout.Signatures().Length == 0, "opt-out registry");
        StandardCatalog.RegisterStd(optout); StandardCatalog.RegisterStd(optout);
        Require(optout.Signatures().Length == 69, "idempotent standard binder");
        var overrideRegistry = new Registry();
        overrideRegistry.Function("str_len", ["string"], "int", (_, _) => Value.Int(99));
        Require(overrideRegistry.HostSignatures().Length == 0, "standard override remains standard");
        var host = new Registry(false).Function("host", [], "int", (_, _) => Value.Int(1));
        host.CheckSchemaSignatures(["fn host() -> int"]);
        Require(host.HostSignatures().SequenceEqual(["fn host() -> int"]), "host-only signatures");
        Console.WriteLine("PASS standard registry tests"); return 5;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string[] CanonicalSignatures()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lmcode-std.schema.json")));
        var result = new List<string>();
        foreach (var type in doc.RootElement.GetProperty("types").EnumerateObject())
            foreach (var method in type.Value.GetProperty("methods").EnumerateObject())
                foreach (var overload in method.Value.EnumerateArray()) result.Add($"method {type.Name}.{method.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        foreach (var function in doc.RootElement.GetProperty("functions").EnumerateObject())
            foreach (var overload in function.Value.EnumerateArray()) result.Add($"fn {function.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
}
