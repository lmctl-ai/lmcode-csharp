namespace LmCode.Stdlib;

/// <summary>Optional host functions; merge the matching fragment into the host schema.</summary>
public static class StringCatalog
{
    /// <summary>Byte length, including NULs; UTF-8 is never decoded.</summary>
    public static long StrLen(VmString s) => s.Length;

    /// <summary>Register once per registry: str_len(string) -> int.</summary>
    public static void RegisterString(Registry r) =>
        r.Function("str_len", ["string"], "int", static (_, args) => Value.Int(StrLen(args[0].AsStr())));
}
