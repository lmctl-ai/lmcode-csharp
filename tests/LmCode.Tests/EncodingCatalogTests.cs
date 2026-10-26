using LmCode;
using LmCode.Stdlib;

static class EncodingCatalogTests
{
    public static int Run()
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
        var value = new VmString(bytes);
        Require(EncodingCatalog.Base64Decode(EncodingCatalog.Base64Encode(value)).Span.SequenceEqual(bytes), "base64 all bytes");
        Require(EncodingCatalog.HexDecode(EncodingCatalog.HexEncode(value)).Span.SequenceEqual(bytes), "hex all bytes");
        Require(EncodingCatalog.Base64Encode(VmString.FromString("Man")).ToString() == "TWFu", "RFC base64");
        Require(EncodingCatalog.Base64Encode(VmString.FromString("Ma")).ToString() == "TWE=", "RFC base64 padding");
        Require(EncodingCatalog.Base64Encode(VmString.FromString("M")).ToString() == "TQ==", "RFC base64 final padding");
        foreach (string invalid in new[] { " TQ==", "TQ== ", "TQ==\n", "TQ?=", "TQ===", "TR==", "TWF=", "TQ" })
            Trap(() => EncodingCatalog.Base64Decode(VmString.FromString(invalid)), "str_base64_decode: invalid encoding");
        foreach (string invalid in new[] { "f", "0x00", "aa bb", "gg", "aa\n" })
            Trap(() => EncodingCatalog.HexDecode(VmString.FromString(invalid)), "str_hex_decode: invalid encoding");
        Trap(() => EncodingCatalog.CheckedBase64Groups(int.MaxValue), "str_base64_encode: size overflow");
        Trap(() => EncodingCatalog.CheckedHexSize(int.MaxValue), "str_hex_encode: size overflow");
        Console.WriteLine("PASS encoding catalog tests");
        return 8;
    }

    private static void Trap(Action action, string message)
    {
        try { action(); }
        catch (Error error) when (error.Code == "RUN_HOST" && error.Message == message) { return; }
        throw new Exception("missing exact encoding error: " + message);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
