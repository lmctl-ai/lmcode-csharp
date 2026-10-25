using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LmCode;

static class RuntimeTests
{
    public static bool Run()
    {
        RawVmStringCallback();
        OwnedVmStringHelpers();
        IntegerEdges();
        DuplicateBinding();
        SchemaMismatch();
        OrdinaryVmTypeError();
        LocatedHostError();
        return true;
    }
    private static void Require(bool condition) { if (!condition) throw new Exception("runtime regression"); }
    private static byte[] Image(byte[] code, byte[][] constants, uint[] imports, bool location = false)
    {
        var b = new List<byte>("DSLB"u8.ToArray());
        U16(b, 5); b.AddRange(SHA256.HashData([])); U32(b, (uint)constants.Length);
        foreach (var c in constants) { b.Add(1); U32(b, (uint)c.Length); b.AddRange(c); }
        U16(b, (ushort)imports.Length); foreach (var i in imports) U32(b, i);
        U16(b, 0); U16(b, 0); U16(b, 8); U32(b, (uint)code.Length); b.AddRange(code);
        U32(b, location ? 1u : 0u); if (location) { U32(b, 0); U32(b, 12); U16(b, 8); }
        return b.ToArray();
    }
    private static void U16(List<byte> b, ushort n) { b.Add((byte)(n >> 8)); b.Add((byte)n); }
    private static void U32(List<byte> b, uint n) { b.Add((byte)(n >> 24)); b.Add((byte)(n >> 16)); b.Add((byte)(n >> 8)); b.Add((byte)n); }
    private static void Push(List<byte> code, long n) { code.Add(1); byte[] bits = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bits, n); code.AddRange(bits); }
    private static void RawVmStringCallback()
    {
        byte[] raw = [0, 255, 192, 128, 42];
        VmString? seen = null;
        var r = new Registry().Function("capture", ["string"], "void", (_, a) => { seen = a[0].AsStr(); return Value.None; });
        byte[] code = [2, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0x26, 7, 0, 0, 1, 0x11];
        Program.Load(Image(code, [raw, " tail"u8.ToArray(), "fn capture(string) -> void"u8.ToArray()], [2]), [], r).Run(new Context(), r);
        Require(seen is not null && seen.Span.SequenceEqual(new byte[] { 0, 255, 192, 128, 42, 32, 116, 97, 105, 108 }));
        // The constructor must own a copy, including invalid UTF-8 sequences.
        var immutable = new VmString(raw); raw[0] = 99; Require(immutable.Span[0] == 0);
    }
    private static void OwnedVmStringHelpers()
    {
        var left = VmString.FromString("left"); var right = VmString.FromString("right"); var joined = VmString.Concat(left, right);
        Require(joined.Span.SequenceEqual("leftright"u8));
        Require(VmString.FromString("é").Span.SequenceEqual(new byte[] { 0xc3, 0xa9 }));
    }
    private static void IntegerEdges()
    {
        foreach (var (a, b, op, want) in new (long, long, byte, long)[] { (long.MaxValue, 1, 0x13, long.MinValue), (long.MinValue, 1, 0x14, long.MaxValue), (long.MinValue, -1, 0x15, long.MinValue), (long.MinValue, -1, 0x16, long.MinValue), (long.MinValue, -1, 0x33, 0) })
        {
            long? seen = null; var r = new Registry().Function("capture", ["int"], "void", (_, v) => { seen = v[0].AsInt(); return Value.None; });
            var code = new List<byte>(); Push(code, a); Push(code, b); code.AddRange([op, 7, 0, 0, 1, 0x11]);
            Program.Load(Image(code.ToArray(), ["fn capture(int) -> void"u8.ToArray()], [0]), [], r).Run(new Context(), r);
            Require(seen == want);
        }
    }
    private static void DuplicateBinding()
    {
        var r = new Registry().Function("f", ["int"], "int", (_, _) => Value.Int(1));
        r.Function("f", ["float"], "float", (_, _) => Value.Float(1));
        try { r.Function("f", ["int"], "float", (_, _) => Value.Float(2)); }
        catch (Error e) when (e.Code == "LOAD_UNBOUND" && e.Message.Contains("registered twice", StringComparison.Ordinal)) { return; }
        throw new Exception("duplicate parameter tuple accepted");
    }
    private static void SchemaMismatch()
    {
        try { Program.Load(Image([0x11], [], []), [1], new Registry()); }
        catch (Error e) when (e.Code == "LOAD_SCHEMA_MISMATCH" && e.Message.Contains("schema mismatch", StringComparison.Ordinal)) { return; }
        throw new Exception("schema mismatch accepted");
    }
    private static void OrdinaryVmTypeError()
    {
        var p = Program.Load(Image([3, 1, 0x1b, 0x11], [], []), [], new Registry());
        try { p.Run(new Context(), new Registry()); }
        catch (Error e) when (e.Code == "RUN_ERROR" && e.Message == "int negation applied to bool") { return; }
        throw new Exception("ordinary VM type error lost its RUN_ERROR code");
    }
    private static void LocatedHostError()
    {
        var r = new Registry().Function("fail", [], "void", (_, _) => throw Error.Host("host failure"));
        var p = Program.Load(Image([7, 0, 0, 0, 0x11], ["fn fail() -> void"u8.ToArray()], [0], true), [], r);
        try { p.Run(new Context(), r); }
        catch (Error e) when (e.Code == "RUN_HOST" && e.Line == 12 && e.Column == 8 && e.Message == "dsl line 12:8: host failure") { return; }
        throw new Exception("host error lost its source location");
    }
}
