using System.Text;
namespace LmCode.Rest;

public sealed record RestRequest(string Method, string Target, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);
public sealed record RestResponse(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);
public sealed class RestOptions
{
    public int MaxBodyBytes { get; init; } = 1024 * 1024;
    /// <summary>Request stream (default: process stdin).</summary>
    public Stream? Input { get; init; }
    /// <summary>Response stream (default: process stdout).</summary>
    public Stream? Output { get; init; }
    public Action<string, string>? OnHandlerError { get; init; }
}

internal sealed class Exchange(RestRequest request, IReadOnlyDictionary<string, string> parameters, Dictionary<string, string> query, string path, RestState state)
{
    public readonly RestRequest Request = request; public readonly IReadOnlyDictionary<string, string> Params = parameters;
    public readonly Dictionary<string, string> Query = query; public readonly string Path = path; public readonly RestState State = state;
    public int Status = 200; public byte[] Body = [];
    /// <summary>Insertion-ordered; replacing a name keeps its position (like a JS Map).</summary>
    public readonly List<(string Key, string Name, string Value)> Headers = [];
    public void SetHeader(string name, string value)
    {
        string key = name.ToLowerInvariant(); int i = Headers.FindIndex(h => h.Key == key);
        if (i < 0) Headers.Add((key, name, value)); else Headers[i] = (key, name, value);
    }
    public string[] RequestHeader(string name)
    {
        string wanted = name.ToLowerInvariant();
        return Request.Headers.Where(h => h.Key.ToLowerInvariant() == wanted).Select(h => h.Value).ToArray();
    }
}
internal sealed class RestState { public readonly Dictionary<string, VmString> Values = new(StringComparer.Ordinal); public readonly Dictionary<string, long> Counts = new(StringComparer.Ordinal); }

public sealed class RestController
{
    public const string HandlerInterface = "interface RestHandler(RestExchange) -> void";
    private readonly Registry registry;
    internal readonly RestState State = new();
    public RestOptions Options { get; }
    public RouteTable Table { get; } = new();
    private Program? program; private Context context = new();
    private readonly Dictionary<string, (int Index, string Name)> entryIndex = new(StringComparer.Ordinal);
    private readonly Action<string, string> onHandlerError;
    public RestController(Registry registry, RestOptions? options = null)
    {
        this.registry = registry; Options = options ?? new RestOptions();
        onHandlerError = Options.OnHandlerError ?? ((entry, message) => Console.Error.WriteLine($"rest: handler {entry} failed: {message}"));
    }
    private bool serving, stopped;
    public bool Serving => serving;
    /// <summary>Binds the program, runs its body, then (if rest_serve was called and not rest_stop) serves Input to Output
    /// until EOF, rest_stop() or a framing error.</summary>
    public void Run(Program program, Context? context = null)
    {
        this.program = program; this.context = context ?? new Context(); stopped = false;
        program.Run(this.context, registry);
        if (serving && !stopped) Pump();
    }
    internal void Route(VmString method, VmString path, VmString annotation)
    {
        if (serving) throw Host("rest: routes are frozen once rest_serve has been called");
        Table.Add(L(method), L(path), L(annotation));
    }
    public void Route(string method, string path, string annotation)
    {
        if (serving) throw Host("rest: routes are frozen once rest_serve has been called");
        Table.Add(method, path, annotation);
    }
    /// <summary>rest_serve: validate, then record that serving was requested (does not block).</summary>
    public void Serve()
    {
        if (serving) throw Host("rest: rest_serve already called");
        if (program is null) throw Host("rest: no program is bound (run the program through the controller)");
        Bind();
        serving = true;
    }
    /// <summary>Ends serving once the current response is written.</summary>
    public void Stop() => stopped = true;

    /// <summary>Binds a program without running its body (tests and hosts that drive Dispatch directly).</summary>
    internal void Attach(Program p, Context c) { program = p; context = c; }

    private void Pump()
    {
        Stream input = Options.Input ?? Console.OpenStandardInput(), output = Options.Output ?? Console.OpenStandardOutput();
        byte[] buf = new byte[8192]; int len = 0;
        var chunk = new byte[8192];
        while (true)
        {
            int n = input.Read(chunk, 0, chunk.Length);
            if (n <= 0) return;
            if (len + n > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + n));
            Buffer.BlockCopy(chunk, 0, buf, len, n); len += n;
            while (true)
            {
                var taken = Http.TakeRequest(buf.AsSpan(0, len), Options.MaxBodyBytes);
                if (taken.Kind == Http.TakeKind.Need) break;
                if (taken.Kind == Http.TakeKind.Fatal) { Write(output, Http.EncodeResponse(Error_(taken.Status))); return; }
                Buffer.BlockCopy(buf, taken.Consumed, buf, 0, len - taken.Consumed); len -= taken.Consumed;
                var request = taken.Request!;
                Write(output, Http.EncodeResponse(Dispatch(request), request.Method == "HEAD"));
                if (stopped) return;
            }
        }
    }
    private static void Write(Stream output, byte[] bytes) { output.Write(bytes, 0, bytes.Length); output.Flush(); }

    internal static Error Host(string message) => Error.Host(message);
    internal static string L(VmString s) => Encoding.Latin1.GetString(s.Span);
    internal static VmString V(string s) => VmString.FromOwned(Encoding.Latin1.GetBytes(s));

    /// <summary>Validation performed by rest_listen before any socket is opened.</summary>
    public void Bind()
    {
        if (Table.Size == 0) throw Host("rest: no routes registered");
        entryIndex.Clear();
        var entries = program!.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Iface != HandlerInterface) continue;
            if (!entryIndex.TryAdd(entries[i].Annotation, (i, entries[i].Name))) throw Host($"rest: duplicate annotation @{entries[i].Annotation}");
        }
        foreach (var route in Table.Routes)
        {
            if (entryIndex.ContainsKey(route.Annotation)) continue;
            var wrong = entries.FirstOrDefault(e => e.Annotation == route.Annotation);
            if (wrong is not null) throw Host($"rest: entry {wrong.Name} must implement RestHandler(RestExchange) -> void");
            throw Host($"rest: route {route.Method} {route.Path} references missing annotation @{route.Annotation}");
        }
    }

    private static RestResponse Error_(int status, params KeyValuePair<string, string>[] extra) => Http.ErrorResponse(status, extra);

    /// <summary>The transport-independent core. Not synchronised: the transport serialises calls.</summary>
    public RestResponse Dispatch(RestRequest request)
    {
        int mark = request.Target.IndexOf('?');
        string rawPath = mark < 0 ? request.Target : request.Target[..mark], queryText = mark < 0 ? "" : request.Target[(mark + 1)..];
        string? path = Router.PercentDecode(rawPath);
        if (path is null) return Error_(400);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (queryText.Length > 0)
            foreach (string pair in queryText.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string? key = Router.PercentDecode(eq < 0 ? pair : pair[..eq], true), value = Router.PercentDecode(eq < 0 ? "" : pair[(eq + 1)..], true);
                if (key is null || value is null) return Error_(400);
                query.TryAdd(key, value);
            }
        var match = Table.Match(request.Method, rawPath);
        if (match.Status == 404) return Error_(404);
        if (match.Status == 405) return Error_(405, new KeyValuePair<string, string>("Allow", match.Allow!));
        var (index, name) = entryIndex[match.Route!.Annotation];
        var x = new Exchange(request, match.Params!, query, path, State);
        var handle = context.InsertCollection("RestExchange", x);
        try { program!.Call(index, context, registry, [Value.Object("RestExchange", handle)]); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            onHandlerError(name, e.Message);
            return Error_(500);
        }
        finally { context.ReleaseCollection(handle); }
        var headers = x.Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).ToList();
        byte[] body = x.Body;
        if (x.Status is 204 or 304) body = [];
        else if (body.Length > 0 && !x.Headers.Any(h => h.Key == "content-type")) headers.Insert(0, new("Content-Type", "text/plain; charset=utf-8"));
        return new(x.Status, headers, body);
    }
}
