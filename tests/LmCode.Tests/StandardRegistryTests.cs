using LmCode;
using LmCode.Stdlib;
using System.Text.Json;
using System.Security.Cryptography;

static class StandardRegistryTests
{
    public static int Run()
    {
        var standard = new Registry();
        Require(standard.Signatures().Length == 69 && standard.HostSignatures().Length == 0, "default standard surface");
        standard.CheckSchemaSignatures(CanonicalSignatures());
        var optout = new Registry(includeStd: false);
        Require(optout.Signatures().Length == 0, "opt-out registry");
        StandardCatalog.RegisterStd(optout); StandardCatalog.RegisterStd(optout);
        Require(optout.Signatures().Length == 69, "idempotent standard binder");
        Require(optout.StdSignatures().Length == 69 && Std.Signatures.Count == 69, "installed standard signatures");
        var overrideRegistry = new Registry();
        try { overrideRegistry.Function("str_len", ["string"], "int", (_, _) => Value.Int(99)); throw new Exception("builtin override accepted as ordinary duplicate"); }
        catch (Error e) when (e.Code == "LOAD_UNBOUND") { }
        StringCatalog.RegisterString(overrideRegistry); StringCatalog.RegisterString(overrideRegistry);
        Require(overrideRegistry.HostSignatures().Length == 0, "standard compatibility binder remains standard");
        var host = new Registry(false).Context("ctx", "Thing").Function("host", [], "int", (_, _) => Value.Int(1));
        host.CheckSchemaSignatures(["context ctx: Thing", "fn host() -> int"]);
        Require(host.MatchesSchemaSignatures(["context ctx: Thing", "fn host() -> int"]), "schema match alias");
        Require(host.HostSignatures().SequenceEqual(["context ctx: Thing", "fn host() -> int"]), "host-only signatures");
        LifecycleGuards();
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
    private static void LifecycleGuards()
    {
        var registry = new Registry(); var program = Program.Load(Image("fn str_list() -> StrList", [7, 0, 0, 0, 0x11]), [], registry); var context = new Context();
        program.Run(context, registry); program.Run(context, registry);
        bool nestedRejected = false; Program? nested = null;
        var guarded = new Registry(); guarded.Function("reenter", [], "void", (ctx, _) => { try { nested!.Run(ctx, new Registry(false)); } catch (Error e) when (e.Message == "registry run is already active") { nestedRejected = true; } return Value.None; });
        nested = Program.Load(Image("fn reenter() -> void", [7, 0, 0, 0, 0x11]), [], guarded); nested.Run(new Context(), guarded);
        Require(nestedRejected, "nested run guard");
    }
    private static byte[] Image(string import, byte[] code)
    {
        var b = new List<byte>("DSLB"u8.ToArray()); U16(b, 5); b.AddRange(SHA256.HashData([])); U32(b, 1); b.Add(1); byte[] text = System.Text.Encoding.UTF8.GetBytes(import); U32(b, (uint)text.Length); b.AddRange(text); U16(b, 1); U32(b, 0); U16(b, 0); U16(b, 0); U16(b, 4); U32(b, (uint)code.Length); b.AddRange(code); U32(b, 0); return b.ToArray();
    }
    private static void U16(List<byte> b, ushort value) { b.Add((byte)(value >> 8)); b.Add((byte)value); }
    private static void U32(List<byte> b, uint value) { b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16)); b.Add((byte)(value >> 8)); b.Add((byte)value); }
}
