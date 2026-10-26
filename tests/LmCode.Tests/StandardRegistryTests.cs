using LmCode;
using LmCode.Stdlib;
using System.Text.Json;
using System.Security.Cryptography;

static class StandardRegistryTests
{
    public static int Run()
    {
        var standard = new Registry();
        Require(standard.Signatures().Length == 171 && standard.HostSignatures().Length == 0, "default standard surface");
        Require(standard.Signatures().SequenceEqual(CanonicalSignatures()), "canonical std exact surface");
        standard.CheckSchemaSignatures(CanonicalSignatures());
        var optout = new Registry(includeStd: false);
        Require(optout.Signatures().Length == 0, "opt-out registry");
        StandardCatalog.RegisterStd(optout); StandardCatalog.RegisterStd(optout);
        Require(optout.Signatures().Length == 171, "idempotent standard binder");
        Require(optout.StdSignatures().Length == 171 && Std.Signatures.Count == 171, "installed standard signatures");
        var overrideRegistry = new Registry();
        try { overrideRegistry.Function("str_len", ["string"], "int", (_, _) => Value.Int(99)); throw new Exception("builtin override accepted as ordinary duplicate"); }
        catch (Error e) when (e.Code == "LOAD_UNBOUND") { }
        StringCatalog.RegisterString(overrideRegistry); StringCatalog.RegisterString(overrideRegistry);
        Require(overrideRegistry.HostSignatures().Length == 0, "standard compatibility binder remains standard");
        var host = new Registry(false).Context("ctx", "Thing").Function("host", [], "int", (_, _) => Value.Int(1));
        host.CheckSchemaSignatures(["context ctx: Thing", "fn host() -> int"]);
        Require(host.MatchesSchemaSignatures(["context ctx: Thing", "fn host() -> int"]), "schema match alias");
        Require(host.HostSignatures().SequenceEqual(["context ctx: Thing", "fn host() -> int"]), "host-only signatures");
        var testing = new Registry(false); TestingCatalog.RegisterTesting(testing, new MemoryStream());
        Require(testing.StdSignatures().Length == 0 && testing.HostSignatures().SequenceEqual(["fn assert_close(float, float, float) -> void", "fn print(string) -> void"]), "optional testing host signatures");
        Require(!testing.MatchesSchemaSignatures([]) && testing.MatchesSchemaSignatures(["fn assert_close(float, float, float) -> void", "fn print(string) -> void"]), "optional testing schema check");
        OptOutStandardNames();
        RunStorageReuse();
        LifecycleGuards();
        Console.WriteLine("PASS standard registry tests"); return 7;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string[] CanonicalSignatures()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lmcode-std.schema.json"));
        string expectedHash = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "std.lock")).Single(line => line.StartsWith("sha256=", StringComparison.Ordinal))[7..];
        Require(Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase), "canonical std digest");
        using var doc = JsonDocument.Parse(bytes);
        Require(doc.RootElement.GetProperty("name").GetString() == "lmcode_std_v2", "canonical std version");
        var result = new List<string>();
        foreach (var type in doc.RootElement.GetProperty("types").EnumerateObject())
            foreach (var method in type.Value.GetProperty("methods").EnumerateObject())
                foreach (var overload in method.Value.EnumerateArray()) result.Add($"method {type.Name}.{method.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        foreach (var function in doc.RootElement.GetProperty("functions").EnumerateObject())
            foreach (var overload in function.Value.EnumerateArray()) result.Add($"fn {function.Name}({string.Join(", ", overload.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {overload.GetProperty("returns").GetString()}");
        result.Sort(StringComparer.Ordinal); return result.ToArray();
    }
    private static void OptOutStandardNames()
    {
        var registry = new Registry(false).Function("str_len", ["string"], "int", (_, _) => Value.Int(7));
        Require(registry.StdSignatures().Length == 0, "opt-out does not classify host declarations as std");
        Require(registry.HostSignatures().SequenceEqual(["fn str_len(string) -> int"]), "opt-out canonical name remains host-owned");
        Require(registry.MatchesSchemaSignatures(registry.HostSignatures()), "opt-out host self-check");
        try { StandardCatalog.RegisterStd(registry); throw new Exception("std silently adopted a conflicting host callback"); }
        catch (Error e) when (e.Code == "LOAD_UNBOUND") { }
    }
    private static void RunStorageReuse()
    {
        var context = new Context(); var host = new object(); ObjRef hostRef = context.Insert("Host", host);
        var registry = new Registry(); ObjRef last = default; bool fail = false;
        registry.Function("capture", ["StrList"], "void", (c, a) => {
            last = a[0].AsObj(); _ = c.Get<StrList>(last);
            if (fail) throw new InvalidOperationException("host failure");
            return Value.None;
        });
        var program = Program.Load(Image(["fn str_list() -> StrList", "fn capture(StrList) -> void"], [7,0,0,0, 7,0,1,1, 0x11]), [], registry);
        for (int i = 0; i < 100; ++i) program.Run(context, new Registry(false));
        Require(context.Count == 2 && ReferenceEquals(context.Get<object>(hostRef), host), "completed runs reuse collection slots and preserve host objects");
        try { _ = context.Get<StrList>(last); throw new Exception("std handle survived run exit"); } catch (Error) { }
        fail = true;
        try { program.Run(context, registry); throw new Exception("host failure was swallowed"); } catch (InvalidOperationException) { }
        try { _ = context.Get<StrList>(last); throw new Exception("std handle survived exceptional exit"); } catch (Error) { }
        fail = false; program.Run(context, registry);
        Require(context.Count == 2, "exception cleanup restores the run state and quota");
    }
    private static void LifecycleGuards()
    {
        var registry = new Registry(); var program = Program.Load(Image("fn str_list() -> StrList", [7, 0, 0, 0, 0x11]), [], registry); var context = new Context();
        program.Run(context, registry); program.Run(context, registry);
        bool nestedRejected = false; Program? nested = null;
        var guarded = new Registry(); guarded.Function("reenter", [], "void", (ctx, _) => { try { nested!.Run(ctx, new Registry(false)); } catch (Error e) when (e.Message == "registry run is already active") { nestedRejected = true; } return Value.None; });
        nested = Program.Load(Image("fn reenter() -> void", [7, 0, 0, 0, 0x11]), [], guarded); nested.Run(new Context(), guarded);
        Require(nestedRejected, "nested run guard");
        var bare = new Registry(false); Program? leaf = null; bool entered = false;
        bare.Function("leaf", [], "void", (ctx, _) => { leaf!.Run(ctx, bare); entered = true; return Value.None; });
        leaf = Program.Load(Image("fn leaf() -> void", [0x11]), [], bare);
        Program.Load(Image("fn leaf() -> void", [7,0,0,0,0x11]), [], bare).Run(new Context(), bare);
        Require(entered, "opt-out preserves legacy nested-run behavior");
    }
    private static byte[] Image(string import, byte[] code) => Image([import], code);
    private static byte[] Image(string[] imports, byte[] code)
    {
        var b = new List<byte>("DSLB"u8.ToArray()); U16(b, 5); b.AddRange(SHA256.HashData([])); U32(b, (uint)imports.Length);
        foreach (string import in imports) { b.Add(1); byte[] text = System.Text.Encoding.UTF8.GetBytes(import); U32(b, (uint)text.Length); b.AddRange(text); }
        U16(b, (ushort)imports.Length); for (uint i = 0; i < imports.Length; ++i) U32(b, i);
        U16(b, 0); U16(b, 0); U16(b, 4); U32(b, (uint)code.Length); b.AddRange(code); U32(b, 0); return b.ToArray();
    }
    private static void U16(List<byte> b, ushort value) { b.Add((byte)(value >> 8)); b.Add((byte)value); }
    private static void U32(List<byte> b, uint value) { b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16)); b.Add((byte)(value >> 8)); b.Add((byte)value); }
}
