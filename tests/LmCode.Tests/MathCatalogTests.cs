using System.Text.Json;
using System.Text.Json.Nodes;
using LmCode;
using LmCode.Stdlib;

static class MathCatalogTests
{
    public static int Run()
    {
        var registry = new Registry(false); MathCatalog.RegisterMath(registry);
        Require(registry.Signatures().Length == 16, "math signature count");
        CheckParity("math", registry, "math.schema-fragment.json");
        var collections = new Registry(false); CollectionsCatalog.RegisterCollections(collections); CheckParity("collections", collections, "collections.schema-fragment.json");
        var testing = new Registry(false); TestingCatalog.RegisterTesting(testing, new MemoryStream()); CheckParity("testing", testing, "testing.schema-fragment.json");
        Require(MathCatalog.MathAbs(long.MinValue) == long.MinValue && MathCatalog.MathPow(2, 63) == long.MinValue && MathCatalog.MathPow(2, 64) == 0, "integer wrapping");
        Require(MathCatalog.MathSqrt(9_223_372_030_926_249_000L) == 3_037_000_498, "exact integer sqrt");
        Require(MathCatalog.MathRound(0.5) == 1 && MathCatalog.MathRound(-0.5) == -1, "ties away from zero");
        Require(double.IsNegative(MathCatalog.MathRound(-0.1)), "negative zero rounding");
        Require(double.IsNaN(MathCatalog.MathMin(double.NaN, 1)) && double.IsNaN(MathCatalog.MathMax(1, double.NaN)), "NaN propagation");
        Require(double.IsNegative(MathCatalog.MathMin(-0.0, 0.0)) && !double.IsNegative(MathCatalog.MathMin(0.0, -0.0)), "min signed zero");
        Require(MathCatalog.MathPow(double.NaN, 0) == 1 && MathCatalog.MathPow(1, double.NaN) == 1, "power exceptions");
        HostError(() => MathCatalog.MathPow(2, -1), "math_pow: integer exponent must be nonnegative");
        HostError(() => MathCatalog.MathSqrt(-1), "math_sqrt: integer argument must be nonnegative");
        Console.WriteLine("PASS math catalog tests"); return 10;
    }
    private static void HostError(Action action, string message)
    {
        try { action(); } catch (Error e) when (e.Code == "RUN_HOST" && e.Message == message) { return; }
        throw new Exception("math host error mismatch");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void CheckParity(string name, Registry registry, string fragmentName)
    {
        string fragmentPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fragmentName);
        using var fragment = JsonDocument.Parse(File.ReadAllBytes(fragmentPath));
        var expected = new List<string>();
        if (fragment.RootElement.TryGetProperty("types", out var types))
            foreach (var type in types.EnumerateObject())
                foreach (var method in type.Value.GetProperty("methods").EnumerateObject())
                    foreach (var overload in method.Value.EnumerateArray()) expected.Add($"method {type.Name}.{method.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        foreach (var function in fragment.RootElement.GetProperty("functions").EnumerateObject())
            foreach (var overload in function.Value.EnumerateArray()) expected.Add($"fn {function.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        expected.Sort(StringComparer.Ordinal);
        Require(registry.Signatures().SequenceEqual(expected), name + " fragment/registry parity");
        var fragmentNode = JsonNode.Parse(File.ReadAllText(fragmentPath))!.AsObject();
        var schema = new JsonObject { ["dsl.schema"] = 2, ["name"] = name, ["types"] = fragmentNode["types"]?.DeepClone() ?? new JsonObject(), ["functions"] = fragmentNode["functions"]?.DeepClone() ?? new JsonObject(), ["context"] = new JsonObject() };
        string schemaPath = Path.Combine(Path.GetTempPath(), $"lmcode-{name}-{Guid.NewGuid():N}.json"); File.WriteAllText(schemaPath, schema.ToJsonString());
        try
        {
            StringCatalogTests.CompilerParity(registry.Signatures(), schemaPath, name);
        }
        finally { File.Delete(schemaPath); }
    }
}
