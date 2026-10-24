using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LmCode.Stdlib;

/// <summary>Optional raw-byte regular-file helpers.</summary>
public static class FileCatalog
{
    public static void RegisterFile(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
            r.Function("file_read", ["string"], "string", static (_, a) => Value.Str(new VmString(Read("file_read", a[0].AsStr()))));
            r.Function("file_write", ["string", "string"], "void", static (_, a) => { Write("file_write", a[0].AsStr(), a[1].AsStr().Span, append: false); return Value.None; });
            r.Function("file_append", ["string", "string"], "void", static (_, a) => { Write("file_append", a[0].AsStr(), a[1].AsStr().Span, append: true); return Value.None; });
            r.Function("file_exists", ["string"], "bool", static (_, a) => Value.Bool(Exists("file_exists", a[0].AsStr())));
            r.Function("file_lines", ["string"], "StrList", static (c, a) =>
            {
                byte[] bytes = Read("file_lines", a[0].AsStr()); var list = new StrList(); int start = 0;
                for (int i = 0; i < bytes.Length; ++i) if (bytes[i] == (byte)'\n') { list.Items.Add(new VmString(bytes.AsSpan(start, i - start))); start = i + 1; }
                list.Items.Add(new VmString(bytes.AsSpan(start))); return Value.Object("StrList", CollectionsCatalog.AllocateStrList(c, list));
            });
        }
        finally { r.EndCompatibilityRegistration(); }
    }

    private static byte[] Read(string function, VmString path)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            try { EnsureRegular(function, text!); return File.ReadAllBytes(text!); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw Failure(function, path, e.Message); }
        }
        return UnixRead(function, path.Span);
    }
    private static void Write(string function, VmString path, ReadOnlySpan<byte> bytes, bool append)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            try
            {
                if (OperatingSystem.IsWindows() && IsWindowsDevice(text!)) throw Failure(function, path, "invalid argument");
                if (File.Exists(text) || Directory.Exists(text)) EnsureRegular(function, text);
                using var stream = new FileStream(text!, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.Write(bytes); stream.Flush(true); return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw Failure(function, path, e.Message); }
        }
        UnixWrite(function, path.Span, bytes, append);
    }
    private static bool Exists(string function, VmString path)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            if (text!.Length == 0) return false;
            if (OperatingSystem.IsWindows() && IsWindowsDevice(text)) return false;
            try
            {
                var attributes = File.GetAttributes(text!);
                return (attributes & FileAttributes.Directory) == 0;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw Failure(function, path, e.Message); }
        }
        byte[] native = NativePath(path.Span); byte[] stat = new byte[512];
        if (UnixStat(native, stat) != 0) { int error = Marshal.GetLastPInvokeError(); return error is 2 or 20 ? false : throw Failure(function, path, Errno(error)); }
        return StatRegular(stat);
    }
    private static void EnsureRegular(string function, string path)
    {
        if (OperatingSystem.IsWindows() && IsWindowsDevice(path)) throw Failure(function, VmString.FromString(path), "invalid argument");
        if (Directory.Exists(path)) throw Failure(function, VmString.FromString(path), "is a directory");
        if (OperatingSystem.IsWindows() && File.Exists(path))
        {
            try
            {
                using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (GetFileType(handle.DangerousGetHandle()) != 1) throw Failure(function, VmString.FromString(path), "invalid argument");
            }
            catch (Error) { throw; }
        }
    }
    private static bool IsWindowsDevice(string path) => path.StartsWith(@"\\.\", StringComparison.Ordinal) || string.Equals(path, "NUL", StringComparison.OrdinalIgnoreCase);
    private static void ValidatePath(string function, VmString path)
    {
        if (path.Span.IndexOf((byte)0) >= 0) throw Failure(function, path, "invalid argument");
        if (OperatingSystem.IsWindows())
        {
            try { _ = new UTF8Encoding(false, true).GetString(path.Span); }
            catch (DecoderFallbackException e) { throw Failure(function, path, e.Message); }
        }
    }
    private static bool TryTextPath(VmString path, out string? text)
    {
        try { text = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? new UTF8Encoding(false, true).GetString(path.Span) : null; return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(); }
        catch (DecoderFallbackException) { text = null; return false; }
    }
    private static Error Failure(string function, VmString path, string reason) => Error.Host($"{function}: {Display(path)}: {(string.IsNullOrEmpty(reason) ? "file operation failed" : reason)}");
    private static string Display(VmString path)
    {
        try { return new UTF8Encoding(false, true).GetString(path.Span); }
        catch (DecoderFallbackException) { return "bytes:" + Convert.ToHexString(path.Span).ToLowerInvariant(); }
    }
    private static string Errno(int error) => new Win32Exception(error).Message;

    [DllImport("libc", SetLastError = true, EntryPoint = "open")] private static extern int UnixOpen(byte[] path, int flags, int mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "read")] private static extern nint UnixRead(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true, EntryPoint = "write")] private static extern nint UnixWriteRaw(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true, EntryPoint = "fsync")] private static extern int UnixFsync(int fd);
    [DllImport("libc", SetLastError = true, EntryPoint = "close")] private static extern int UnixClose(int fd);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(IntPtr handle);
    private static byte[] UnixRead(string function, ReadOnlySpan<byte> path)
    {
        byte[] stat = new byte[512];
        if (UnixStat(NativePath(path), stat) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        if (!StatRegular(stat)) throw Failure(function, new VmString(path), "invalid argument");
        int fd = UnixOpen(NativePath(path), 0, 0); if (fd < 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        try { var result = new List<byte>(); var buffer = new byte[65536]; while (true) { nint n = UnixRead(fd, buffer, (nuint)buffer.Length); int error = Marshal.GetLastPInvokeError(); if (n == 0) break; if (n < 0 && error == 4) continue; if (n < 0) throw Failure(function, new VmString(path), Errno(error)); result.AddRange(buffer.AsSpan(0, (int)n).ToArray()); } return result.ToArray(); }
        finally { if (UnixClose(fd) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
    }
    private static void UnixWrite(string function, ReadOnlySpan<byte> path, ReadOnlySpan<byte> bytes, bool append)
    {
        byte[] native = NativePath(path); byte[] stat = new byte[512]; int statResult = UnixStat(native, stat); if (statResult == 0 && !StatRegular(stat)) throw Failure(function, new VmString(path), "invalid argument"); if (statResult != 0 && Marshal.GetLastPInvokeError() is not (2 or 20)) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        int flags = 1 | (OperatingSystem.IsMacOS() ? 0x200 : 64) | (append ? (OperatingSystem.IsMacOS() ? 0x8 : 1024) : (OperatingSystem.IsMacOS() ? 0x400 : 512)); int fd = UnixOpen(native, flags, 438); if (fd < 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        try { byte[] data = bytes.ToArray(); int at = 0; while (at < data.Length) { nint n = UnixWriteRaw(fd, data.AsSpan(at).ToArray(), (nuint)(data.Length - at)); int error = Marshal.GetLastPInvokeError(); if (n < 0 && error == 4) continue; if (n <= 0) throw Failure(function, new VmString(path), Errno(error)); at += (int)n; } int sync; do { sync = UnixFsync(fd); } while (sync != 0 && Marshal.GetLastPInvokeError() == 4); if (sync != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
        finally { if (UnixClose(fd) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
    }
    private static byte[] NativePath(ReadOnlySpan<byte> path) { byte[] result = new byte[path.Length + 1]; path.CopyTo(result); return result; }
    private static bool StatRegular(byte[] stat)
    {
        int offset = OperatingSystem.IsMacOS() ? 4 : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16 : 24); uint mode = BitConverter.ToUInt32(stat, offset); return (mode & 0xF000) == 0x8000;
    }
    private static int UnixStat(byte[] path, byte[] buffer) => OperatingSystem.IsMacOS() ? (RuntimeInformation.ProcessArchitecture == Architecture.X64 ? MacStat64(path, buffer) : MacStatNative(path, buffer)) : LinuxStat(path, buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")] private static extern int LinuxStat(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat$INODE64")] private static extern int MacStat64(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")] private static extern int MacStatNative(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "fstat")] private static extern int Fstat(int fd, byte[] buffer);
}
