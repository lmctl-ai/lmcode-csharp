namespace LmCode;
internal sealed record Binding(string Name, string? TypeName, string[] Parameters, string Returns,
    Func<Context, Value[], Value>? Function, Func<Context, ObjRef, Value[], Value>? Method);
public sealed class Registry
{
    private readonly Dictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> contexts = new(StringComparer.Ordinal);
    private Registry Add(string name, string? type, string[] parameters, string returns,
        Func<Context, Value[], Value>? function, Func<Context, ObjRef, Value[], Value>? method)
    {
        string label = type is null ? "function" : "method";
        string fullName = type is null ? name : type + "." + name;
        string sig = $"({string.Join(", ", parameters)}) -> {returns}";
        foreach (var entry in bindings.Values)
            if (entry.Name == name && entry.TypeName == type && entry.Parameters.AsSpan().SequenceEqual(parameters))
                throw new Error("LOAD_UNBOUND", $"registry: {label} '{fullName}{sig}' is registered twice");
        bindings.Add($"{(type is null ? "fn" : "method")} {fullName}{sig}", new(name, type, (string[])parameters.Clone(), returns, function, method));
        return this;
    }
    public Registry Function(string name, string[] parameters, string returns, Func<Context, Value[], Value> function) => Add(name, null, parameters, returns, function, null);
    public Registry Method(string type, string name, string[] parameters, string returns, Func<Context, ObjRef, Value[], Value> method) => Add(name, type, parameters, returns, null, method);
    public Registry Context(string name, string type)
    {
        if (!contexts.TryAdd(name, type)) throw new Error("LOAD_UNBOUND", $"registry: context '{name}' is declared twice");
        return this;
    }
    public string? ContextType(string name) => contexts.GetValueOrDefault(name);
    public string[] Signatures()
    {
        var result = new List<string>(bindings.Keys);
        foreach (var (name, type) in contexts) result.Add($"context {name}: {type}");
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
    internal Binding? Resolve(string canonical) => bindings.GetValueOrDefault(canonical);
    internal string DeclaredFor(string canonical)
    {
        int par = canonical.IndexOf('('); if (par < 0) return "";
        string prefix = canonical[..par] + "(";
        var matches = new List<string>();
        foreach (string key in bindings.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) matches.Add(key);
        matches.Sort(StringComparer.Ordinal); return string.Join(", ", matches);
    }
}
