using System.Text;
namespace LmCode.Rest;

/// <summary>Route templates and matching (spec/std-rest.md). Strings are lmcode byte strings carried as Latin-1 text (one char per byte).</summary>
public enum SegmentKind { Literal = 0, Param = 1, Rest = 2 }
public sealed record Segment(SegmentKind Kind, string Value);
public sealed class Route
{
    public string Method { get; }
    public string Path { get; }
    public IReadOnlyList<Segment> Segments { get; }
    public string Shape { get; }
    public string Annotation { get; internal set; } = "";
    internal Route(string method, string path, Segment[] segments, string shape) { Method = method; Path = path; Segments = segments; Shape = shape; }
}
public readonly record struct RouteMatch(int Status, Route? Route, IReadOnlyDictionary<string, string>? Params, string? Allow);

public static class Router
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
    private static Error Fail(string message) => Error.Host(message);
    /// <summary>Whitespace is ASCII only (0x00-0x20, 0x7f); 0xA0 is an ordinary byte.</summary>
    internal static bool IsSpace(char c) => c <= ' ' || c == '\u007f';
    private static bool IsName(string s)
    {
        if (s.Length == 0 || !(s[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_')) return false;
        foreach (char c in s) if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    /// <summary>Percent-decode; null on an invalid escape. <paramref name="plus"/> turns '+' into a space (queries).</summary>
    public static string? PercentDecode(string text, bool plus = false)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%')
            {
                int hi = i + 1 < text.Length ? Hex(text[i + 1]) : -1, lo = i + 2 < text.Length ? Hex(text[i + 2]) : -1;
                if (hi < 0 || lo < 0) return null;
                sb.Append((char)(hi * 16 + lo)); i += 2;
            }
            else if (plus && c == '+') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
    private static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;

    public static Route ParseRoute(string method, string path)
    {
        string label = method + " " + path;
        Error Invalid(string reason) => Fail($"rest: invalid route {label}: {reason}");
        if (Array.IndexOf(Methods, method) < 0) throw Invalid("method must be one of " + string.Join(", ", Methods));
        if (!path.StartsWith('/')) throw Invalid("path must start with '/'");
        foreach (char c in path) if (c <= ' ' || c == '\u007f' || c == '?' || c == '#') throw Invalid("path must not contain whitespace, control characters, ? or #");
        string[] raw = path[1..].Split('/');
        var names = new HashSet<string>(StringComparer.Ordinal);
        var segments = new Segment[raw.Length];
        for (int index = 0; index < raw.Length; index++)
        {
            string text = raw[index]; bool last = index == raw.Length - 1;
            if (text.Length == 0 && !last) throw Invalid("empty segment ('//')");
            if (text.Length >= 2 && text[0] == '{' && text[^1] == '}')
            {
                string inner = text[1..^1]; bool rest = inner.EndsWith("...", StringComparison.Ordinal);
                string name = rest ? inner[..^3] : inner;
                if (!IsName(name)) throw Invalid($"invalid parameter name '{name}'");
                if (!names.Add(name)) throw Invalid($"duplicate parameter '{name}'");
                if (rest && !last) throw Invalid($"'{{{name}...}}' must be the last segment");
                segments[index] = new(rest ? SegmentKind.Rest : SegmentKind.Param, name);
            }
            else
            {
                if (text.AsSpan().IndexOfAny('{', '}') >= 0) throw Invalid($"malformed placeholder in segment '{text}'");
                segments[index] = new(SegmentKind.Literal, text);
            }
        }
        string shape = string.Join("/", segments.Select(s => s.Kind == SegmentKind.Literal ? "L" + s.Value : s.Kind == SegmentKind.Param ? "P" : "R"));
        return new Route(method, path, segments, shape);
    }

    internal static int Compare(Route a, Route b)
    {
        int n = Math.Min(a.Segments.Count, b.Segments.Count);
        for (int i = 0; i < n; i++) { int d = (int)a.Segments[i].Kind - (int)b.Segments[i].Kind; if (d != 0) return d; }
        return a.Segments.Count - b.Segments.Count;
    }

    internal static Dictionary<string, string>? MatchSegments(Route route, string[] raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < route.Segments.Count; i++)
        {
            var s = route.Segments[i];
            if (s.Kind == SegmentKind.Rest)
            {
                if (raw.Length <= i) return null;
                var parts = new List<string>();
                for (int j = i; j < raw.Length; j++) { var d = PercentDecode(raw[j]); if (d is null) return null; parts.Add(d); }
                result[s.Value] = string.Join("/", parts);
                return result;
            }
            if (i >= raw.Length) return null;
            var decoded = PercentDecode(raw[i]);
            if (decoded is null) return null;
            if (s.Kind == SegmentKind.Literal) { if (!string.Equals(decoded, s.Value, StringComparison.Ordinal)) return null; }
            else { if (decoded.Length == 0) return null; result[s.Value] = decoded; }
        }
        return raw.Length == route.Segments.Count ? result : null;
    }
}

public sealed class RouteTable
{
    private readonly List<Route> routes = [];
    public IReadOnlyList<Route> Routes => routes;
    public int Size => routes.Count;
    public Route Add(string method, string path, string annotation)
    {
        var route = Router.ParseRoute(method, path);
        foreach (var r in routes) if (r.Method == method && r.Shape == route.Shape) throw Error.Host($"rest: duplicate route {method} {path}");
        route.Annotation = annotation; routes.Add(route); return route;
    }

    /// <summary>404, 405 (with Allow) or a hit. <paramref name="rawPath"/> is the undecoded path without the query.</summary>
    public RouteMatch Match(string method, string rawPath)
    {
        string[] raw = (rawPath.Length > 0 ? rawPath[1..] : "").Split('/');
        var hits = new List<(Route Route, Dictionary<string, string> Params)>();
        foreach (var route in routes) { var p = Router.MatchSegments(route, raw); if (p is not null) hits.Add((route, p)); }
        if (hits.Count == 0) return new(404, null, null, null);
        (Route, Dictionary<string, string>)? Best(string verb)
        {
            (Route, Dictionary<string, string>)? best = null;
            foreach (var h in hits) if (h.Route.Method == verb && (best is null || Router.Compare(h.Route, best.Value.Item1) < 0)) best = h;
            return best;
        }
        var direct = Best(method) ?? (method == "HEAD" ? Best("GET") : null);
        if (direct is { } d) return new(200, d.Item1, d.Item2, null);
        var allow = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var h in hits) allow.Add(h.Route.Method);
        if (allow.Contains("GET")) allow.Add("HEAD");
        return new(405, null, null, string.Join(", ", allow));
    }
}
