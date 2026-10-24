using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
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
            int checks = 3;
            string largePath = Path.Combine(dir, "large.bin"); string large = new('x', 100 * 1024 * 1024); var perf = System.Diagnostics.Stopwatch.StartNew();
            RunFile("file_write", largePath, large); var writeTime = perf.Elapsed; perf.Restart(); var largeValue = RunFile("file_read", largePath, ""); var readTime = perf.Elapsed;
            Require(largeValue.AsStr().Length == large.Length, "100MB file round trip"); Console.WriteLine($"file perf: write={writeTime} read={readTime}"); ++checks;
            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode owner = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                Require((File.GetUnixFileMode(path) & owner) == owner, "created file owner permissions"); ++checks;
                string fifo = Path.Combine(dir, "fifo");
                Require(MkFifo(Encoding.UTF8.GetBytes(fifo + "\0"), 384) == 0, "create FIFO fixture");
                checks += RejectSpecial(fifo) + RejectSpecial("/dev/null");
                string broken = Path.Combine(dir, "broken"); File.CreateSymbolicLink(broken, Path.Combine(dir, "absent"));
                Require(!RunFile("file_exists", broken).AsBool(), "broken symlink does not exist"); ++checks;
            }
            else
            {
                checks += RejectSpecial("NUL") + RejectSpecial("NUL.txt") + RejectSpecial(Path.Combine(dir, "NUL"));
            }
            Console.WriteLine($"PASS file catalog tests ({checks})"); return checks;
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
    [DllImport("libc", SetLastError = true, EntryPoint = "mkfifo")]
    private static extern int MkFifo(byte[] path, uint mode);
    private static int RejectSpecial(string path)
    {
        Require(!RunFile("file_exists", path).AsBool(), "nonregular path does not exist as a file");
        foreach (string function in new[] { "file_read", "file_write", "file_append", "file_lines" })
        {
            bool trapped = false;
            try { RunFile(function, path); }
            catch (Error e) { trapped = e.Code == "RUN_HOST" && e.Message.Contains(function + ": " + path + ": ", StringComparison.Ordinal); }
            Require(trapped, function + " rejects nonregular path");
        }
        return 5;
    }
    private static Value RunFile(string function, string path, string payload = "payload")
    {
        bool writes = function is "file_write" or "file_append";
        string returns = writes ? "void" : function == "file_exists" ? "bool" : function == "file_lines" ? "StrList" : "string";
        string signature = $"fn {function}(string{(writes ? ", string" : "")}) -> {returns}";
        var registry = new Registry(); Value result = Value.None;
        registry.Function("capture_file", [returns], "void", (_, args) => { result = args[0]; return Value.None; });
        string[] constants = [signature, $"fn capture_file({returns}) -> void", path, payload];
        var code = new List<byte>(); PushString(code, 2); if (writes) PushString(code, 3);
        Call(code, 0, writes ? 2 : 1); if (!writes) Call(code, 1, 1); code.Add(0x11);
        Program.Load(Image(constants, code.ToArray(), writes ? 1 : 2), [], registry).Run(new Context(), registry);
        return result;
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
