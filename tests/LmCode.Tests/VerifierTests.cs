using System.Buffers.Binary;
using System.Security.Cryptography;
using LmCode;

public static class VerifierTests
{
    public static int Run()
    {
        Reject("inconsistent stack join", new byte[] { 0x34, 0x03, 1, 0x35, 0, 0, 0, 7, 0x03, 1, 0x36, 0xff, 0xff, 0xff, 0xf1, 0x11 }, "inconsistent operand stack depth");
        Reject("backward ordinary jump", new byte[] { 0x0c, 0xff, 0xff, 0xff, 0xfb }, "goes backwards");
        RejectRange("range slot write", RangeWithStore());
        Reject("unmatched loop", new byte[] { 0x34, 0x11 }, "HALT");
        Reject("jump skips while test", new byte[] { 0x34, 0x0c, 0, 0, 0, 5, 0x35, 0, 0, 0, 5, 0x36, 0xff, 0xff, 0xff, 0xf0, 0x11 }, "skips its WHILE_TEST");
        Reject("out of range constant", new byte[] { 0x02, 0, 0, 0, 0, 0x11 }, "constant index");
        Reject("line beyond code", new byte[] { 0x11 }, "line table entry points", true);
        RejectTruncated();
        return 8;
    }

    private static void Reject(string name, byte[] code, string expected, bool badLine = false)
    {
        try { Program.Load(Image(code, badLine: badLine), Array.Empty<byte>(), new Registry()); }
        catch (Error e) when (e.Code == "LOAD_BAD_BYTECODE" && e.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"verifier case failed: {name}");
    }

    private static void RejectRange(string name, byte[] code)
    {
        try { Program.Load(Image(code, locals: ["int", "int"]), Array.Empty<byte>(), new Registry()); }
        catch (Error e) when (e.Code == "LOAD_BAD_BYTECODE" && e.Message.Contains("read-only range slot", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"verifier case failed: {name}");
    }

    private static void RejectTruncated()
    {
        try { Program.Load(new byte[] { (byte)'D', (byte)'S', (byte)'L', (byte)'B', 0, 6 }, Array.Empty<byte>(), new Registry()); }
        catch (Error e) when (e.Code == "LOAD_BAD_BYTECODE" && e.Message.Contains("truncated", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("verifier case failed: truncation");
    }

    private static byte[] RangeWithStore()
    {
        var code = new List<byte>();
        PushInt(code, 0); PushInt(code, 1);
        code.Add(0x2f); U16(code, 0); U16(code, 1); U32(code, 19);
        PushInt(code, 2); code.Add(0x05); U16(code, 0);
        code.Add(0x31); U16(code, 1); U32(code, unchecked((uint)-19)); code.Add(0x11);
        return code.ToArray();
    }

    private static byte[] Image(byte[] code, string[]? locals = null, bool badLine = false)
    {
        locals ??= Array.Empty<string>();
        var b = new List<byte>(); b.AddRange("DSLB"u8.ToArray()); U16(b, 6); b.AddRange(SHA256.HashData(Array.Empty<byte>()));
        U32(b, locals.Length == 0 ? 0 : 1); if (locals.Length != 0) { b.Add(1); U32(b, 3); b.Add((byte)'i'); b.Add((byte)'n'); b.Add((byte)'t'); }
        U16(b, 0); U16(b, 0); U16(b, (ushort)locals.Length);
        foreach (string local in locals) { U32(b, 0); b.Add(1); }
        U16(b, 8); U32(b, (uint)code.Length); b.AddRange(code); U32(b, (uint)(badLine ? 1 : 0));
        if (badLine) { U32(b, 99); U32(b, 1); U16(b, 1); }
        U16(b, 0); return b.ToArray();
    }

    private static void PushInt(List<byte> b, long value) { b.Add(1); Span<byte> x = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(x, value); b.AddRange(x.ToArray()); }
    private static void U16(List<byte> b, int value) { Span<byte> x = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(x, (ushort)value); b.AddRange(x.ToArray()); }
    private static void U32(List<byte> b, long value) { Span<byte> x = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(x, unchecked((uint)value)); b.AddRange(x.ToArray()); }
}
