using System.Security.Cryptography;
using LmCode;
using LmCode.Stdlib;

static class FileCatalogTests
{
    public static int Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lmcode-file-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var seen = new List<VmString>(); var ints = new List<long>();
            var r = new Registry(false); CollectionsCatalog.RegisterCollections(r); FileCatalog.RegisterFile(r);
            r.Function("capture", ["string"], "void", (_, a) => { seen.Add(a[0].AsStr()); return Value.None; });
            r.Function("capture_i", ["int"], "void", (_, a) => { ints.Add(a[0].AsInt()); return Value.None; });
            string path = Path.Combine(dir, "data.bin");
            var constants = new[] { "fn file_write(string, string) -> void", "fn file_read(string) -> string", "fn file_append(string, string) -> void", "fn file_lines(string) -> StrList", "method StrList.len() -> int", "fn capture(string) -> void", "fn capture_i(int) -> void", path, "a\0\xff" };
            var code = new List<byte>(); PushString(code, 7); PushString(code, 8); Call(code, 0, 2); PushString(code, 7); Call(code, 1, 1); Call(code, 5, 1); PushString(code, 7); PushString(code, 8); Call(code, 2, 2); PushString(code, 7); Call(code, 1, 1); Call(code, 5, 1); PushString(code, 7); Call(code, 3, 1); CallMethod(code, 4, 0); Call(code, 6, 1); code.Add(0x11);
            Program.Load(Image(constants, code.ToArray(), 7), [], r).Run(new Context(), r);
            Require(seen.Count == 2 && seen[0].Span.SequenceEqual(new byte[] { 97, 0, 195, 191 }) && seen[1].Span.SequenceEqual(new byte[] { 97, 0, 195, 191, 97, 0, 195, 191 }), "raw file bytes");
            Require(ints.SequenceEqual([1]), "file line count");
            Console.WriteLine("PASS file catalog tests"); return 3;
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
    private static void PushString(List<byte> code, int index) { code.Add(2); U32(code, (uint)index); }
    private static void Call(List<byte> code, int index, int argc) { code.Add(7); U16(code, (ushort)index); code.Add((byte)argc); }
    private static void CallMethod(List<byte> code, int index, int argc) { code.Add(8); U16(code, (ushort)index); code.Add((byte)argc); }
    private static byte[] Image(string[] constants, byte[] code, int imports)
    {
        var b = new List<byte>("DSLB"u8.ToArray()); U16(b, 5); b.AddRange(SHA256.HashData([])); U32(b, (uint)constants.Length);
        foreach (string value in constants) { b.Add(1); byte[] raw = System.Text.Encoding.UTF8.GetBytes(value); U32(b, (uint)raw.Length); b.AddRange(raw); }
        U16(b, (ushort)imports); for (ushort i = 0; i < imports; ++i) U32(b, i); U16(b, 0); U16(b, 0); U16(b, 6); U32(b, (uint)code.Length); b.AddRange(code); U32(b, 0); return b.ToArray();
    }
    private static void U16(List<byte> b, ushort value) { b.Add((byte)(value >> 8)); b.Add((byte)value); }
    private static void U32(List<byte> b, uint value) { b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16)); b.Add((byte)(value >> 8)); b.Add((byte)value); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
