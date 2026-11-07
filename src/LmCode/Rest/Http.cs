using System.Text;
namespace LmCode.Rest;

/// <summary>The HTTP/1.1 subset of spec/std-rest.md: request framing and response encoding. Heads are Latin-1 text.</summary>
public static class Http
{
    public const int MaxHeaderBytes = 16 * 1024;
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [200] = "OK", [201] = "Created", [202] = "Accepted", [204] = "No Content", [301] = "Moved Permanently", [302] = "Found", [304] = "Not Modified",
        [400] = "Bad Request", [401] = "Unauthorized", [403] = "Forbidden", [404] = "Not Found", [405] = "Method Not Allowed", [408] = "Request Timeout",
        [409] = "Conflict", [413] = "Payload Too Large", [415] = "Unsupported Media Type", [422] = "Unprocessable Entity", [429] = "Too Many Requests",
        [431] = "Request Header Fields Too Large", [500] = "Internal Server Error", [501] = "Not Implemented", [502] = "Bad Gateway", [503] = "Service Unavailable",
    };
    public static string ReasonPhrase(int status) => Reasons.TryGetValue(status, out var r) ? r : status < 300 ? "OK" : status < 400 ? "Redirect" : status < 500 ? "Client Error" : "Server Error";

    public static readonly IReadOnlyDictionary<int, string> Errors = new Dictionary<int, string>
    {
        [400] = "{\"error\":\"bad request\"}", [404] = "{\"error\":\"not found\"}", [405] = "{\"error\":\"method not allowed\"}",
        [413] = "{\"error\":\"payload too large\"}", [431] = "{\"error\":\"request header too large\"}",
        [500] = "{\"error\":\"internal error\"}", [501] = "{\"error\":\"not implemented\"}",
    };
    public static RestResponse ErrorResponse(int status, params KeyValuePair<string, string>[] extra) =>
        new(status, new[] { new KeyValuePair<string, string>("Content-Type", "application/json") }.Concat(extra).ToList(), Encoding.Latin1.GetBytes(Errors[status]));

    public readonly record struct Head(string Method, string Target, IReadOnlyList<KeyValuePair<string, string>> Headers, int Error);

    private static bool IsTrim(char c) => c == ' ' || c == '\t';
    /// <summary>Parse the header section (text without the terminating blank line). Error is 0 or an HTTP status.</summary>
    public static Head ParseHead(string text)
    {
        static Head Bad() => new("", "", [], 400);
        string[] lines = text.Split("\r\n");
        string first = lines[0];
        int sp = first.IndexOf(' ');
        if (sp <= 0) return Bad();
        for (int i = 0; i < sp; i++) if (first[i] is < 'A' or > 'Z') return Bad();
        string rest = first[(sp + 1)..];
        if (!(rest.EndsWith(" HTTP/1.0", StringComparison.Ordinal) || rest.EndsWith(" HTTP/1.1", StringComparison.Ordinal))) return Bad();
        string target = rest[..^9];
        if (target.Length == 0 || target.Any(Router.IsSpace)) return Bad();
        if (target[0] != '/' || target.Contains('#')) return Bad();
        var headers = new List<KeyValuePair<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i]; int colon = line.IndexOf(':');
            if (colon <= 0) return Bad();
            for (int j = 0; j < colon; j++) if (Router.IsSpace(line[j])) return Bad();
            foreach (char c in line) if (c <= '\b' || c is >= '\n' and <= '\u001f' || c == '\u007f') return Bad();
            int a = colon + 1, b = line.Length;
            while (a < b && IsTrim(line[a])) a++;
            while (b > a && IsTrim(line[b - 1])) b--;
            headers.Add(new(line[..colon], line[a..b]));
        }
        return new(first[..sp], target, headers, 0);
    }

    /// <summary>Content-Length (or 0); Error is 400/501. Transfer-Encoding is unsupported.</summary>
    public static (long Length, int Error) BodyLength(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (headers.Any(h => h.Key.ToLowerInvariant() == "transfer-encoding")) return (0, 501);
        var values = headers.Where(h => h.Key.ToLowerInvariant() == "content-length").Select(h => h.Value).ToList();
        if (values.Count == 0) return (0, 0);
        if (values.Count > 1 && values.Distinct(StringComparer.Ordinal).Count() > 1) return (0, 400);
        string v = values[0];
        if (v.Length is < 1 or > 15 || v.Any(c => c is < '0' or > '9')) return (0, 400);
        return (long.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 0);
    }

    public static byte[] EncodeResponse(RestResponse r, bool head = false)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(r.Status).Append(' ').Append(ReasonPhrase(r.Status)).Append("\r\n");
        foreach (var h in r.Headers) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        sb.Append("Content-Length: ").Append(r.Body.Length).Append("\r\n\r\n");
        byte[] headBytes = Encoding.Latin1.GetBytes(sb.ToString());
        if (head) return headBytes;
        byte[] all = new byte[headBytes.Length + r.Body.Length];
        headBytes.CopyTo(all, 0); r.Body.CopyTo(all, headBytes.Length); return all;
    }

    public enum TakeKind { Need, Fatal, Request }
    public readonly record struct Taken(TakeKind Kind, int Status, RestRequest? Request, int Consumed);

    /// <summary>Take one request off the front of <paramref name="buffer"/>. A fatal framing error ends serving.</summary>
    public static Taken TakeRequest(ReadOnlySpan<byte> buffer, long maxBodyBytes)
    {
        int end = buffer.IndexOf("\r\n\r\n"u8);
        if (end < 0) return buffer.Length > MaxHeaderBytes ? new(TakeKind.Fatal, 431, null, 0) : new(TakeKind.Need, 0, null, 0);
        if (end > MaxHeaderBytes) return new(TakeKind.Fatal, 431, null, 0);
        var head = ParseHead(Encoding.Latin1.GetString(buffer[..end]));
        if (head.Error != 0) return new(TakeKind.Fatal, head.Error, null, 0);
        var (length, error) = BodyLength(head.Headers);
        if (error != 0) return new(TakeKind.Fatal, error, null, 0);
        if (length > maxBodyBytes) return new(TakeKind.Fatal, 413, null, 0);
        int start = end + 4;
        if (buffer.Length - start < length) return new(TakeKind.Need, 0, null, 0);
        int len = (int)length;
        return new(TakeKind.Request, 0, new RestRequest(head.Method, head.Target, head.Headers, buffer.Slice(start, len).ToArray()), start + len);
    }
}
