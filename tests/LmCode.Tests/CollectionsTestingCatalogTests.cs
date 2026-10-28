using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Linq;
using LmCode;
using LmCode.Stdlib;

static class CollectionsTestingCatalogTests
{
    public static int Run()
    {
        CollectionSignatures();
        ListAndMapCallbacks();
        TestingCallbacks();
        LiveMapEntries();
        Console.WriteLine("PASS collections/testing catalog tests");
        return 4;
    }

    private static void CollectionSignatures()
    {
        var r = new Registry(false); CollectionsCatalog.RegisterCollections(r);
        Require(r.Signatures().Length == 37, "collection signature count");
        Require(r.Signatures().Contains("fn str_split(string, string) -> StrList"), "split signature");
        Require(r.Signatures().Contains("method FloatList.contains(float) -> bool"), "float signature");
    }

    private static void ListAndMapCallbacks()
    {
        VmString? joined = null; VmString? key = null;
        var r = new Registry(false); CollectionsCatalog.RegisterCollections(r);
        r.Function("capture_str", ["string"], "void", (_, a) => { if (joined is null) joined = a[0].AsStr(); else key = a[0].AsStr(); return Value.None; });
        var list = Run(r, [
            "fn str_list() -> StrList", "method StrList.add(string) -> StrList", "method StrList.join(string) -> string",
            "fn capture_str(string) -> void", "a", "b", ","], [
            7, 0, 0, 0, 2, 0, 0, 0, 4, 8, 0, 1, 1, 2, 0, 0, 0, 5, 8, 0, 1, 1, 2, 0, 0, 0, 6, 8, 0, 2, 1, 7, 0, 3, 1, 0x11], 4);
        Require(list && joined is not null && joined.Span.SequenceEqual("a,b"u8), "list callbacks");

        var map = new Registry(false); CollectionsCatalog.RegisterCollections(map);
        map.Function("capture_str", ["string"], "void", (_, a) => { key = a[0].AsStr(); return Value.None; });
        Run(map, ["fn str_map() -> StrMap", "method StrMap.set(string, string) -> StrMap", "method StrMap.key_at(int) -> string", "fn capture_str(string) -> void", "a", "one", "b", "two"], [
            7, 0, 0, 0, 2, 0, 0, 0, 4, 2, 0, 0, 0, 5, 8, 0, 1, 2, 2, 0, 0, 0, 6, 2, 0, 0, 0, 7, 8, 0, 1, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 2, 1, 7, 0, 3, 1, 0x11], 4);
        Require(key is not null && key.Span.SequenceEqual("a"u8), "map insertion order");
    }

    private static void LiveMapEntries()
    {
        var registry = new Registry(false); CollectionsCatalog.RegisterCollections(registry);
        var context = new Context(); ObjRef saved = default;
        IReadOnlyList<(VmString Key, VmString Value)>? view = null;
        string[][] expected = [["a"], ["a", "b"], ["b", "c"]]; int observations = 0;
        registry.Function("remember", ["StrMap"], "void", (c, args) =>
        {
            saved = args[0].AsObj(); view ??= c.Get<StrMap>(saved).Entries;
            Require(view is not IList<(VmString, VmString)>, "map view cannot be cast to a mutable list");
            Require(view.Select(item => item.Key.ToString()).SequenceEqual(expected[observations]), "held map view observes live insertion order");
            Require(view[view.Count - 1].Key.ToString() == expected[observations][^1], "held map view rank lookup");
            ++observations; return Value.None;
        });
        registry.Function("saved", [], "StrMap", (_, _) => Value.Object("StrMap", saved));
        registry.Function("discard", ["bool"], "void", (_, args) => { Require(args[0].AsBool(), "remove from native view map"); return Value.None; });
        string[] constants = ["fn str_map() -> StrMap", "method StrMap.set(string, string) -> StrMap", "method StrMap.remove(string) -> bool", "fn remember(StrMap) -> void", "fn saved() -> StrMap", "fn discard(bool) -> void", "a", "one", "b", "two", "c", "three"];
        void Execute(byte[] code) => Program.Load(Image(constants, code, 6), [], registry).Run(context, registry);
        Execute([7,0,0,0, 2,0,0,0,6, 2,0,0,0,7, 8,0,1,2, 7,0,3,1, 0x11]);
        Execute([7,0,4,0, 2,0,0,0,8, 2,0,0,0,9, 8,0,1,2, 7,0,3,1, 0x11]);
        Execute([7,0,4,0, 2,0,0,0,6, 8,0,2,1, 7,0,5,1, 7,0,4,0, 2,0,0,0,10, 2,0,0,0,11, 8,0,1,2, 7,0,3,1, 0x11]);
        Require(observations == 3, "all native view transitions observed");
    }

    private static void TestingCallbacks()
    {
        using var output = new MemoryStream(); var r = new Registry(false); TestingCatalog.RegisterTesting(r, output);
        Run(r, ["fn print(string) -> void", "hello"], [2, 0, 0, 0, 1, 7, 0, 0, 1, 0x11], 1);
        Require(output.ToArray().AsSpan().SequenceEqual("hello\n"u8), "print writes raw bytes and LF");
        var close = new List<byte>(); for (int i = 0; i < 3; ++i) close.AddRange([18, 0, 0, 0, 0, 0, 0, 0, 0]);
        close.AddRange([7, 0, 0, 3, 0x11]);
        Run(r, ["fn assert_close(float, float, float) -> void"], close.ToArray(), 1);
    }

    private static bool Run(Registry registry, string[] constants, byte[] code, int imports)
    {
        var bytes = Image(constants, code, imports); Program.Load(bytes, [], registry).Run(new Context(), registry); return true;
    }

    private static byte[] Image(string[] constants, byte[] code, int imports)
    {
        var b = new List<byte>("DSLB"u8.ToArray()); U16(b, 6); b.AddRange(SHA256.HashData([]));
        U32(b, (uint)constants.Length);
        foreach (string value in constants) { b.Add(1); byte[] raw = System.Text.Encoding.UTF8.GetBytes(value); U32(b, (uint)raw.Length); b.AddRange(raw); }
        U16(b, (ushort)imports); for (ushort i = 0; i < imports; ++i) U32(b, i); U16(b, 0); U16(b, 0); U16(b, 8); U32(b, (uint)code.Length); b.AddRange(code); U32(b, 0); U16(b, 0); return b.ToArray();
    }
    private static void U16(List<byte> b, ushort n) { b.Add((byte)(n >> 8)); b.Add((byte)n); }
    private static void U32(List<byte> b, uint n) { b.Add((byte)(n >> 24)); b.Add((byte)(n >> 16)); b.Add((byte)(n >> 8)); b.Add((byte)n); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
