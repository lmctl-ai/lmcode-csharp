using System.Security.Cryptography;
using System.Text;

static class VectorPinTests
{
    static string Root()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"LmCode.sln")))directory=directory.Parent;
        return directory?.FullName ?? throw new Exception("run vector pin checks inside the SDK checkout");
    }
    static string Digest(string directory)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(string file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).OrderBy(p=>Path.GetRelativePath(directory,p).Replace('\\','/'),StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory,file).Replace('\\','/')));hash.AppendData(new byte[]{0});
            hash.AppendData(File.ReadAllBytes(file));hash.AppendData(new byte[]{0});
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public static void Write(string commit)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(commit,@"\A[0-9a-f]{40}\z"))throw new Exception("expected immutable commit SHA");
        foreach(string suite in new[]{"vectors","stdlib-vectors","return-vectors"})File.WriteAllText(Path.Combine(Root(),suite+".lock"),"repo=https://github.com/lmctl-ai/lmcode-cpp\ncommit="+commit+"\nsha256="+Digest(Path.Combine(Root(),"conformance",suite))+"\n");
    }
    public static int Run()
    {
        foreach(string suite in new[]{"vectors","stdlib-vectors","return-vectors"})
        {
            string expected=File.ReadAllLines(Path.Combine(Root(),suite+".lock")).Single(s=>s.StartsWith("sha256=",StringComparison.Ordinal))[7..];
            if(Digest(Path.Combine(Root(),"conformance",suite))!=expected)throw new Exception("modified pinned "+suite);
            Console.WriteLine("PASS pinned "+suite);
        }
        return 3;
    }
}
