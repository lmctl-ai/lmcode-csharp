namespace LmCode.Stdlib;

/// <summary>The built-in lmcode standard catalog (v1).</summary>
public static class StandardCatalog
{
    private static readonly Lazy<HashSet<string>> Canonical = new(() =>
    {
        var registry = new Registry(false);
        StringCatalog.RegisterString(registry);
        MathCatalog.RegisterMath(registry);
        CollectionsCatalog.RegisterCollections(registry);
        TestingCatalog.RegisterTesting(registry, new MemoryStream());
        return registry.Signatures().ToHashSet(StringComparer.Ordinal);
    });

    public static void RegisterStd(Registry registry)
    {
        registry.BeginBuiltinRegistration();
        try
        {
            StringCatalog.RegisterString(registry);
            MathCatalog.RegisterMath(registry);
            CollectionsCatalog.RegisterCollections(registry);
            TestingCatalog.RegisterTesting(registry, null);
        }
        finally { registry.EndBuiltinRegistration(); }
    }

    internal static bool IsStandard(string signature) => Canonical.Value.Contains(signature);
}
