using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LmCode;
using LmCode.Rest;

/// <summary>Tests of the opt-in `rest` std module (stdio only): router, HTTP subset, registration, lifecycle, exchange rules and the shared suite.</summary>
static class RestTests
{
    const string Handler = "interface RestHandler(RestExchange) -> void";
    static int passed;
    static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-suite");
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Equal<T>(T expected, T actual, string what) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}"); }
    static void Case(string name, Action body)
    {
        try { body(); }
        catch (Exception e) when (e is not OutOfMemoryException) { Console.WriteLine("FAIL rest: " + name); throw new Exception($"rest test '{name}' failed: {e.Message}", e); }
        Console.WriteLine("PASS rest: " + name); passed++;
    }
    static void Throws(Action action, string code, string text, string what)
    {
        try { action(); }
        catch (Error e) { Require(e.Code == code && e.Message.Contains(text, StringComparison.Ordinal), $"{what}: wrong error {e.Code}: {e.Message}"); return; }
        throw new Exception(what + ": no error");
    }
    static string L(byte[] b) => Encoding.Latin1.GetString(b);
    static byte[] B(string s) => Encoding.Latin1.GetBytes(s);
    static byte[] Get(string target) => B($"GET {target} HTTP/1.1\r\nHost: x\r\n\r\n");

    // ---- a minimal bytecode image with chosen entries (name, annotation, interface), each a HALT body taking one RestExchange ----
    static byte[] Image(params (string Name, string Annotation, string Iface)[] entries)
    {
        var pool = new List<string> { "RestExchange" };
        int Add(string s) { int i = pool.IndexOf(s); if (i < 0) { pool.Add(s); i = pool.Count - 1; } return i; }
        var refs = entries.Select(e => (Add(e.Name), Add(e.Annotation), Add(e.Iface))).ToArray();
        var b = new List<byte>("DSLB"u8.ToArray());
        void U16(int n) { Span<byte> x = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(x, (ushort)n); b.AddRange(x.ToArray()); }
        void U32(int n) { Span<byte> x = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(x, (uint)n); b.AddRange(x.ToArray()); }
        U16(6); b.AddRange(SHA256.HashData([]));
        U32(pool.Count);
        foreach (string s in pool) { byte[] bytes = Encoding.UTF8.GetBytes(s); b.Add(1); U32(bytes.Length); b.AddRange(bytes); }
        U16(0); U16(0); U16(0); U16(0); U32(1); b.Add(17); U32(0);
        U16(entries.Length);
        foreach (var (n, a, i) in refs)
        {
            U32(n); U32(a); U32(i); U16(1); U16(1); U32(0); b.Add(0); U16(2); U32(1); b.Add(17); U32(0);
        }
        return b.ToArray();
    }
    static Program Fake(params (string Name, string Annotation, string Iface)[] entries) => Program.Load(Image(entries), [], new Registry());
    static (RestController Controller, Program Program, Registry Registry) Bound(RestOptions? options, params (string, string, string)[] entries)
    {
        var registry = new Registry(); var rest = RestCatalog.RegisterRest(registry, options);
        var program = Program.Load(Image(entries), [], registry);
        rest.Attach(program, new Context());
        return (rest, program, registry);
    }
    static (string, string, string) H(string annotation, string? name = null) => (name ?? annotation, annotation, Handler);

    // ---- integration helpers ----------------------------------------------------------------------------------
    sealed record Reply(int Status, Dictionary<string, string> Headers, byte[] Body, int Length);
    static List<Reply> ParseResponses(byte[] data, IReadOnlyList<string> methods)
    {
        var list = new List<Reply>(); int pos = 0;
        while (pos < data.Length)
        {
            int end = data.AsSpan(pos).IndexOf("\r\n\r\n"u8);
            if (end < 0) throw new Exception("incomplete response head: " + L(data[pos..]));
            string[] lines = L(data[pos..(pos + end)]).Split("\r\n");
            Require(lines[0].StartsWith("HTTP/1.1 ", StringComparison.Ordinal), "status line: " + lines[0]);
            int status = int.Parse(lines[0].Substring(9, 3), System.Globalization.CultureInfo.InvariantCulture);
            var headers = new Dictionary<string, string>();
            foreach (string line in lines.Skip(1)) { int c = line.IndexOf(':'); headers[line[..c].ToLowerInvariant()] = line[(c + 1)..].Trim(); }
            int length = int.Parse(headers["content-length"], System.Globalization.CultureInfo.InvariantCulture);
            bool head = list.Count < methods.Count && methods[list.Count] == "HEAD";
            int start = pos + end + 4, take = head ? 0 : length;
            Require(start + take <= data.Length, "truncated response body");
            list.Add(new(status, headers, data[start..(start + take)], length));
            pos = start + take;
        }
        return list;
    }
    static byte[] ComposeRequest(JsonElement s)
    {
        if (s.TryGetProperty("raw", out var raw)) return B(raw.GetString()!);
        byte[] body = [];
        if (s.TryGetProperty("body_repeat", out var rep)) body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(rep.GetProperty("text").GetString()!, rep.GetProperty("count").GetInt32())));
        else if (s.TryGetProperty("body", out var text)) body = Encoding.UTF8.GetBytes(text.GetString()!);
        string method = s.GetProperty("method").GetString()!;
        var lines = new List<string> { $"{method} {s.GetProperty("target").GetString()} HTTP/1.1", "Host: localhost" };
        if (s.TryGetProperty("headers", out var hs)) foreach (var h in hs.EnumerateObject()) lines.Add($"{h.Name}: {h.Value.GetString()}");
        if (body.Length > 0 || method is "POST" or "PUT" or "PATCH") lines.Add($"Content-Length: {body.Length}");
        return [.. B(string.Join("\r\n", lines) + "\r\n\r\n"), .. body];
    }
    static void CheckReply(string label, Reply r, JsonElement expect)
    {
        Equal(expect.GetProperty("status").GetInt32(), r.Status, label + ": status");
        if (expect.TryGetProperty("headers", out var hs))
            foreach (var h in hs.EnumerateObject())
            {
                if (h.Value.ValueKind == JsonValueKind.Null) Require(!r.Headers.ContainsKey(h.Name), $"{label}: header {h.Name} must be absent");
                else Equal(h.Value.GetString(), r.Headers.GetValueOrDefault(h.Name), $"{label}: header {h.Name}");
            }
        Require(!r.Headers.ContainsKey("connection"), label + ": no Connection header");
        if (expect.TryGetProperty("body", out var body)) Require(r.Body.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(body.GetString()!)), $"{label}: body {L(r.Body)}");
    }
    static RestController Suite(Stream input, Stream output, out Program program, Action<string, string>? onError = null, int max = 1024)
    {
        var registry = new Registry();
        var rest = RestCatalog.RegisterRest(registry, new RestOptions { Input = input, Output = output, MaxBodyBytes = max, OnHandlerError = onError ?? ((_, _) => { }) });
        program = Program.Load(File.ReadAllBytes(Path.Combine(Fixtures, "app.dslb")), File.ReadAllBytes(Path.Combine(Fixtures, "host.schema.json")), registry);
        return rest;
    }
    static byte[] ServeMemory(byte[] input, Action<string, string>? onError = null, int max = 1024, Stream? inStream = null)
    {
        var output = new MemoryStream();
        var rest = Suite(inStream ?? new MemoryStream(input), output, out var program, onError, max);
        rest.Run(program);
        return output.ToArray();
    }
    static byte[] ServeProcess(byte[] input)
    {
        string executable = Environment.ProcessPath ?? throw new Exception("missing executable path");
        var start = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        foreach (string a in new[] { "--rest-serve", Path.Combine(Fixtures, "app.dslb"), Path.Combine(Fixtures, "host.schema.json"), "--max-body-bytes", "1024" }) start.ArgumentList.Add(a);
        using var child = Process.Start(start) ?? throw new Exception("child failed to start");
        using var bytes = new MemoryStream();
        var drain = child.StandardOutput.BaseStream.CopyToAsync(bytes); var diagnostic = child.StandardError.ReadToEndAsync();
        try { child.StandardInput.BaseStream.Write(input); child.StandardInput.BaseStream.Flush(); child.StandardInput.Close(); }
        catch (IOException) { } // the server may stop reading (rest_stop, framing errors) before all input is written
        Task.WhenAll(drain, diagnostic).GetAwaiter().GetResult(); child.WaitForExit();
        Equal(0, child.ExitCode, "serve host exit code (" + diagnostic.Result + ")");
        return bytes.ToArray();
    }
    /// <summary>Delivers its bytes in tiny pieces, like a slow pipe.</summary>
    sealed class Trickle(byte[] data, int piece) : Stream
    {
        int pos;
        public override int Read(byte[] buffer, int offset, int count) { int n = Math.Min(Math.Min(count, piece), data.Length - pos); Array.Copy(data, pos, buffer, offset, n); pos += n; return n; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    sealed class Forbidden : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new Exception("input was read");
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>Replays stream, fatal and silent groups; returns the number of checks (32 + 4 + 3).</summary>
    static int Replay(Func<byte[], byte[]> serve)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "scenarios.json")));
        var root = doc.RootElement; int count = 0;
        var stream = root.GetProperty("stream").EnumerateArray().ToArray();
        var answered = stream.Where(s => !s.TryGetProperty("unanswered", out var u) || !u.GetBoolean()).ToArray();
        byte[] all = stream.SelectMany(ComposeRequest).ToArray();
        var replies = ParseResponses(serve(all), answered.Select(s => s.GetProperty("method").GetString()!).ToArray());
        Equal(answered.Length, replies.Count, "stream: exactly one response per answered request, none after rest_stop()");
        for (int i = 0; i < answered.Length; i++)
        {
            var s = answered[i]; CheckReply($"{s.GetProperty("name").GetString()} ({s.GetProperty("method").GetString()})", replies[i], s.GetProperty("expect")); count++;
            if (s.GetProperty("method").GetString() != "HEAD") Equal(replies[i].Body.Length, replies[i].Length, "Content-Length equals body length");
        }
        byte[] good = B("GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n");
        foreach (var s in root.GetProperty("fatal").EnumerateArray())
        {
            var r = ParseResponses(serve([.. ComposeRequest(s), .. good]), [s.GetProperty("method").GetString()!]);
            Equal(1, r.Count, s.GetProperty("name").GetString() + ": one error response, then the end");
            CheckReply(s.GetProperty("name").GetString()!, r[0], s.GetProperty("expect")); count++;
        }
        foreach (var s in root.GetProperty("silent").EnumerateArray())
        {
            Equal(0, serve(ComposeRequest(s)).Length, s.GetProperty("name").GetString() + ": no output"); count++;
        }
        return count;
    }

    public static int Run()
    {
        passed = 0;
        // ---- unit: routes ------------------------------------------------------------------------------------
        Case("route templates: valid shapes parse into literal, param and rest segments", () =>
        {
            var r = Router.ParseRoute("GET", "/users/{id}/files/{path...}");
            Require(r.Segments.Select(s => s.Kind).SequenceEqual([SegmentKind.Literal, SegmentKind.Param, SegmentKind.Literal, SegmentKind.Rest]), "kinds");
            Equal(1, Router.ParseRoute("GET", "/").Segments.Count, "root");
            Equal("", Router.ParseRoute("GET", "/files/").Segments[^1].Value, "empty last segment");
        });
        Case("route templates: invalid shapes are RUN_HOST errors with pinned text", () =>
        {
            void Bad(string m, string p, string text) => Throws(() => Router.ParseRoute(m, p), "RUN_HOST", text, m + " " + p);
            Bad("get", "/x", "rest: invalid route get /x: method must be one of GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS");
            Bad("GET", "x", "path must start with '/'");
            Bad("GET", "/a b", "path must not contain whitespace, control characters, ? or #");
            Bad("GET", "/a\u007f", "whitespace");
            Router.ParseRoute("GET", "/cafÃ "); // UTF-8 bytes such as C3 A0 are ordinary
            Router.ParseRoute("GET", "/a b");
            Bad("GET", "/a?b", "whitespace, control characters, ? or #");
            Bad("GET", "/a#b", "whitespace, control characters, ? or #");
            Bad("GET", "/a//b", "empty segment ('//')");
            Bad("GET", "/{1x}", "invalid parameter name '1x'");
            Bad("GET", "/{}", "invalid parameter name ''");
            Bad("GET", "/{a}/{a}", "duplicate parameter 'a'");
            Bad("GET", "/{a...}/x", "'{a...}' must be the last segment");
            Bad("GET", "/a{b}", "malformed placeholder in segment 'a{b}'");
            Bad("GET", "/{b", "malformed placeholder");
        });
        Case("route table: duplicates are detected by shape, parameter names do not matter", () =>
        {
            var t = new RouteTable(); t.Add("GET", "/a/{x}", "one"); t.Add("POST", "/a/{x}", "two");
            Throws(() => t.Add("GET", "/a/{y}", "three"), "RUN_HOST", "rest: duplicate route GET /a/{y}", "duplicate");
            t.Add("GET", "/a/b", "literal");
        });
        Case("route table: specificity is literal > {param} > {rest...}, first difference wins", () =>
        {
            var t = new RouteTable();
            t.Add("GET", "/x/{rest...}", "rest"); t.Add("GET", "/x/{p}/z", "param-z"); t.Add("GET", "/x/{p}/{q}", "param-param");
            t.Add("GET", "/x/y/{q}", "literal-param"); t.Add("GET", "/x/y/z", "literal-literal");
            string? Pick(string p) => t.Match("GET", p).Route?.Annotation;
            Equal("literal-literal", Pick("/x/y/z"), "y/z"); Equal("literal-param", Pick("/x/y/w"), "y/w"); Equal("param-z", Pick("/x/a/z"), "a/z");
            Equal("param-param", Pick("/x/a/b"), "a/b"); Equal("rest", Pick("/x/a/b/c"), "a/b/c"); Equal("rest", Pick("/x/"), "/x/");
            Equal(404, t.Match("GET", "/x").Status, "/x");
        });
        Case("route table: 404 vs 405, Allow header, HEAD fallback", () =>
        {
            var t = new RouteTable(); t.Add("GET", "/r", "g"); t.Add("DELETE", "/r", "d"); t.Add("PUT", "/only-put", "p");
            var m = t.Match("POST", "/r"); Equal(405, m.Status, "405"); Equal("DELETE, GET, HEAD", m.Allow, "allow");
            Equal("PUT", t.Match("GET", "/only-put").Allow, "allow put");
            Equal(404, t.Match("GET", "/missing").Status, "404");
            Equal("GET", t.Match("HEAD", "/r").Route!.Method, "HEAD falls back to GET");
            Equal(405, t.Match("HEAD", "/only-put").Status, "HEAD without GET");
        });
        Case("route table: encoded slashes stay inside one segment; invalid escapes never match", () =>
        {
            var t = new RouteTable(); t.Add("GET", "/f/{name}", "f");
            Equal("a/b", t.Match("GET", "/f/a%2Fb").Params!["name"], "decoded");
            Equal(404, t.Match("GET", "/f/a%2Fb/c").Status, "extra segment");
            Equal(404, t.Match("GET", "/f/%zz").Status, "bad escape");
            Equal(404, t.Match("GET", "/f/").Status, "empty segment is not a {param}");
            Equal("Ã¶", Router.PercentDecode("%C3%B6"), "decoding yields bytes");
            Equal("a b", Router.PercentDecode("a+b", true), "plus in queries");
            Equal("a+b", Router.PercentDecode("a+b"), "plus in paths");
            Require(Router.PercentDecode("%4") is null && Router.PercentDecode("%") is null && Router.PercentDecode("%g0") is null, "invalid escapes");
        });
        // ---- unit: HTTP subset ---------------------------------------------------------------------------------
        Case("http: request heads (ASCII whitespace only, values trimmed of spaces and tabs)", () =>
        {
            var h = Http.ParseHead("GET /a?b=1 HTTP/1.1\r\nHost: x\r\nX-A: \t v \t ");
            Equal(0, h.Error, "ok"); Equal("GET", h.Method, "method"); Equal("/a?b=1", h.Target, "target");
            Require(h.Headers.Select(p => p.Key + "=" + p.Value).SequenceEqual(["Host=x", "X-A=v"]), "headers");
            Equal(0, Http.ParseHead("GET /cafÃ  HTTP/1.0").Error, "0xA0 inside the target is an ordinary byte");
            Equal(" v ", Http.ParseHead("GET /a HTTP/1.1\r\nX:  v ").Headers[0].Value, "0xA0 is not trimmed from values");
            foreach (string bad in new[] { "garbage", "GET /a HTTP/2.0", "GET a HTTP/1.1", "GET /a#f HTTP/1.1", "get /a HTTP/1.1", "GET  /a HTTP/1.1", "GET /a HTTP/1.1\r\nNoColon", "GET /a HTTP/1.1\r\n: v", "GET /a HTTP/1.1\r\nBad Name: v", "GET /a HTTP/1.1\r\nX: a\u0001b", "GET /a HTTP/1.1\r\nX: a\rb" })
                Equal(400, Http.ParseHead(bad).Error, bad);
        });
        Case("http: content-length rules", () =>
        {
            KeyValuePair<string, string> P(string n, string v) => new(n, v);
            Equal((0L, 0), Http.BodyLength([]), "none"); Equal((12L, 0), Http.BodyLength([P("content-length", "12")]), "12");
            Equal((1L, 0), Http.BodyLength([P("Content-Length", "1"), P("content-length", "1")]), "same twice");
            Equal(400, Http.BodyLength([P("Content-Length", "1"), P("content-length", "2")]).Error, "conflicting");
            foreach (string v in new[] { "-1", "1.5", "abc", "", new string('1', 16), "٣" }) Equal(400, Http.BodyLength([P("Content-Length", v)]).Error, v);
            Equal(501, Http.BodyLength([P("Transfer-Encoding", "chunked")]).Error, "transfer-encoding");
        });
        Case("http: response encoding has Content-Length and no Connection header", () =>
        {
            var r = new RestResponse(200, [new("A", "b")], B("hi"));
            Equal("HTTP/1.1 200 OK\r\nA: b\r\nContent-Length: 2\r\n\r\nhi", L(Http.EncodeResponse(r)), "full");
            Equal("HTTP/1.1 299 OK\r\nContent-Length: 2\r\n\r\n", L(Http.EncodeResponse(new(299, [], B("hi")), true)), "head");
            Equal("HTTP/1.1 404 Not Found", L(Http.EncodeResponse(Http.ErrorResponse(404)))[..22], "reason");
        });
        Case("http: TakeRequest frames incrementally, reports fatal errors and keeps pipelined remainders", () =>
        {
            byte[] full = B("POST /a HTTP/1.1\r\nContent-Length: 3\r\n\r\nabcGET /b HTTP/1.1\r\n\r\n");
            var one = Http.TakeRequest(full, 1024);
            Equal("abc", L(one.Request!.Body), "body"); var two = Http.TakeRequest(full.AsSpan(one.Consumed), 1024);
            Equal("/b", two.Request!.Target, "second");
            Equal(Http.TakeKind.Need, Http.TakeRequest(B("GET /a HTT"), 1024).Kind, "partial head");
            Equal(Http.TakeKind.Need, Http.TakeRequest(B("POST /a HTTP/1.1\r\nContent-Length: 5\r\n\r\nab"), 1024).Kind, "partial body");
            Equal(413, Http.TakeRequest(B("POST /a HTTP/1.1\r\nContent-Length: 2000\r\n\r\n"), 1024).Status, "413 from the header alone");
            Equal(431, Http.TakeRequest(new byte[20000].Select(_ => (byte)'a').ToArray(), 1024).Status, "431 before the head ends");
            Equal(400, Http.TakeRequest(B("nonsense\r\n\r\n"), 1024).Status, "400");
            Equal(501, Http.TakeRequest(B("POST /a HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n"), 1024).Status, "501");
        });
        // ---- unit: registration, validation, lifecycle ----------------------------------------------------------
        Case("registration: exactly the 18 declared signatures; the default registry is unchanged", () =>
        {
            using var schema = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Fixtures, "host.schema.json")));
            var declared = new List<string>();
            foreach (var t in schema.RootElement.GetProperty("types").EnumerateObject())
                foreach (var m in t.Value.GetProperty("methods").EnumerateObject())
                    foreach (var o in m.Value.EnumerateArray()) declared.Add($"method {t.Name}.{m.Name}({string.Join(", ", o.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {o.GetProperty("returns").GetString()}");
            foreach (var f in schema.RootElement.GetProperty("functions").EnumerateObject())
                foreach (var o in f.Value.EnumerateArray()) declared.Add($"fn {f.Name}({string.Join(", ", o.GetProperty("params").EnumerateArray().Select(x => x.GetString()))}) -> {o.GetProperty("returns").GetString()}");
            declared.Sort(StringComparer.Ordinal);
            Equal(18, declared.Count, "declared");
            var plain = new Registry();
            Equal(206, plain.Signatures().Length, "default catalog"); Equal(0, plain.HostSignatures().Length, "no host signatures");
            Require(!plain.Signatures().Any(s => s.Contains("rest_") || s.Contains("RestExchange")), "no rest signature by default");
            RestCatalog.RegisterRest(plain);
            Require(plain.HostSignatures().SequenceEqual(declared), "exactly the declared 18");
            Equal(224, plain.Signatures().Length, "206 + 18");
            Require(!plain.Signatures().Any(s => s.Contains("rest_listen") || s.Contains("rest_port")), "no network functions");
        });
        Case("rest_serve validation happens before any byte is read, in the pinned order", () =>
        {
            var none = Bound(null, H("a")).Controller;
            Throws(none.Serve, "RUN_HOST", "rest: no routes registered", "no routes");
            var missing = Bound(null).Controller; missing.Route("GET", "/a", "nope");
            Throws(missing.Serve, "RUN_HOST", "rest: route GET /a references missing annotation @nope", "missing");
            var wrong = Bound(null, ("check", "a", "interface Rule(RestExchange) -> void")).Controller; wrong.Route("GET", "/a", "a");
            Throws(wrong.Serve, "RUN_HOST", "rest: entry check must implement RestHandler(RestExchange) -> void", "wrong interface");
            var dup = Bound(null, H("a", "one"), H("a", "two")).Controller; dup.Route("GET", "/a", "a");
            Throws(dup.Serve, "RUN_HOST", "rest: duplicate annotation @a", "duplicate annotation");
            var unbound = RestCatalog.RegisterRest(new Registry()); unbound.Route("GET", "/a", "a");
            Throws(unbound.Serve, "RUN_HOST", "rest: no program is bound (run the program through the controller)", "unbound");
            foreach (var c in new[] { none, missing, wrong, dup, unbound }) Require(!c.Serving, "must not be serving after a failed validation");
            var ignored = Bound(null, H("a"), ("other", "a", "interface Rule(RestExchange) -> void")).Controller; // other interfaces are ignored for uniqueness
            ignored.Route("GET", "/a", "a"); ignored.Serve(); Require(ignored.Serving, "serving");
        });
        Case("lifecycle: routes freeze after rest_serve, a second rest_serve fails", () =>
        {
            var c = Bound(null, H("a")).Controller; c.Route("GET", "/a", "a"); c.Serve();
            Throws(() => c.Route("GET", "/b", "a"), "RUN_HOST", "rest: routes are frozen once rest_serve has been called", "frozen");
            Throws(c.Serve, "RUN_HOST", "rest: rest_serve already called", "twice");
        });
        Case("dispatch: invalid escapes are 400 before routing, 404/405/HEAD fallback and handler defaults", () =>
        {
            var (c, _, _) = Bound(null, H("a")); c.Route("GET", "/a", "a"); c.Serve();
            RestResponse D(string method, string target) => c.Dispatch(new(method, target, [], []));
            Equal(400, D("GET", "/nope?x=%zz").Status, "query escape"); Equal(400, D("GET", "/a/%zz").Status, "path escape");
            Equal(404, D("GET", "/nope").Status, "404");
            var m = D("POST", "/a"); Equal(405, m.Status, "405"); Equal("Allow", m.Headers.Last().Key, "Allow"); Equal("GET, HEAD", m.Headers.Last().Value, "Allow value");
            var ok = D("HEAD", "/a"); Equal(200, ok.Status, "HEAD uses GET"); Equal(0, ok.Body.Length, "empty body");
            Equal(0, ok.Headers.Count, "no default headers for an empty body");
        });
        Case("exchange rules: status range, header validation, reserved headers, counters vs strings", () =>
        {
            var registry = new Registry(); RestCatalog.RegisterRest(registry);
            var ctx = new Context();
            var x = new Exchange(new("GET", "/", [new("X-Dup", "1"), new("x-dup", "2")], []), new Dictionary<string, string>(), new(), "/", new RestState());
            var handle = ctx.Insert("RestExchange", x);
            Value Call(string sig, params Value[] args) => registry.Resolve("method RestExchange." + sig)!.Method!(ctx, handle, args);
            Value S(string s) => Value.Str(VmString.FromString(s));
            Throws(() => Call("status(int) -> void", Value.Int(199)), "RUN_HOST", "rest: status 199 out of range 200..599", "199");
            Throws(() => Call("status(int) -> void", Value.Int(600)), "RUN_HOST", "out of range", "600");
            Call("status(int) -> void", Value.Int(599)); Equal(599, x.Status, "status");
            foreach (string name in new[] { "", "a b", "a:b", "a\tb", "a\u007f" })
                Throws(() => Call("set_header(string, string) -> void", S(name), S("v")), "RUN_HOST", "invalid response header", "name " + name);
            Throws(() => Call("set_header(string, string) -> void", S("X"), S("a\r\nInjected: 1")), "RUN_HOST", "invalid response header", "CRLF in value");
            foreach (string name in new[] { "Content-Length", "connection", "TRANSFER-ENCODING" })
                Throws(() => Call("set_header(string, string) -> void", S(name), S("1")), "RUN_HOST", "managed by the server", name);
            Call("set_header(string, string) -> void", S("X-A"), S("1")); Call("set_header(string, string) -> void", S("x-a"), S("2"));
            Require(x.Headers.Count == 1 && x.Headers[0].Name == "x-a" && x.Headers[0].Value == "2", "same name replaces, case-insensitively");
            Equal(1L, Call("increment(string) -> int", S("k")).AsInt(), "first"); Equal(2L, Call("increment(string) -> int", S("k")).AsInt(), "second");
            Equal("", Call("get(string) -> string", S("k")).AsStr().ToString(), "counters and strings are separate maps");
            x.State.Counts["w"] = long.MaxValue; Equal(long.MinValue, Call("increment(string) -> int", S("w")).AsInt(), "increment wraps as int64");
            Equal("1, 2", Call("header(string) -> string", S("X-DUP")).AsStr().ToString(), "repeated request headers are joined");
            Require(!Call("has_header(string) -> bool", S("nope")).AsBool(), "has_header");
            Call("json(string) -> void", S("{}")); Equal("application/json", x.Headers.Single(h => h.Key == "content-type").Value, "json sets Content-Type");
        });
        Case("a body that never calls rest_serve reads nothing", () =>
        {
            var registry = new Registry(); var rest = RestCatalog.RegisterRest(registry, new RestOptions { Input = new Forbidden(), Output = new MemoryStream() });
            rest.Run(Program.Load(Image(), [], registry));
        });
        // ---- integration: stdio -------------------------------------------------------------------------------------
        Case("conformance suite through in-memory streams (stream, fatal, silent: 39 checks)", () => Equal(39, Replay(i => ServeMemory(i)), "checks"));
        Case("conformance suite through a real child process: stdin to stdout, exit code 0 (39 checks)", () => Equal(39, Replay(ServeProcess), "checks"));
        Case("stdio: a request trickled in 1-byte pieces (mid-header, mid-body) is reassembled", () =>
        {
            byte[] request = B("POST /sum HTTP/1.1\r\nHost: x\r\nContent-Length: 21\r\n\r\n{\"values\":[10,20,12]}");
            var r = ParseResponses(ServeMemory(request, inStream: new Trickle(request, 1)), ["POST"]);
            Equal(201, r[0].Status, "status"); Equal("{\"sum\":42}", L(r[0].Body), "body");
        });
        Case("stdio: pipelined requests in one chunk are answered in order", () =>
        {
            var out_ = ServeMemory([.. Get("/hello/a"), .. Get("/hello/b"), .. Get("/nope"), .. Get("/hello/c")]);
            Require(ParseResponses(out_, []).Select(r => r.Status).SequenceEqual([200, 200, 404, 200]), "statuses");
        });
        Case("stdio: handler errors reach onHandlerError with the entry name, never the client; serving continues", () =>
        {
            var seen = new List<(string, string)>();
            var r = ParseResponses(ServeMemory([.. Get("/boom"), .. Get("/health")], (e, m) => seen.Add((e, m))), []);
            Equal(500, r[0].Status, "500"); Equal("{\"error\":\"internal error\"}", L(r[0].Body), "fixed body"); Equal(200, r[1].Status, "keeps serving");
            Equal(1, seen.Count, "callback count"); Equal("boom", seen[0].Item1, "entry name"); Require(seen[0].Item2.Contains("zero", StringComparison.OrdinalIgnoreCase), "VM message: " + seen[0].Item2);
        });
        Case("stdio: invalid percent escapes in the path or query are 400 and do not end serving", () =>
        {
            var r = ParseResponses(ServeMemory([.. Get("/nope?x=%zz"), .. Get("/hello/%zz"), .. Get("/health")]), []);
            Require(r.Select(x => x.Status).SequenceEqual([400, 400, 200]), "statuses");
        });
        Case("stdio: a body larger than one read chunk and bytes after a fatal error are handled", () =>
        {
            byte[] big = Enumerable.Repeat((byte)'x', 1000).ToArray();
            var r = ParseResponses(ServeMemory([.. B("POST /sum HTTP/1.1\r\nContent-Length: 1000\r\n\r\n"), .. big, .. Get("/health")]), ["POST"]);
            Equal(500, r[0].Status, "not JSON: handler failure"); Equal(200, r[1].Status, "next request still served");
        });
        Console.WriteLine($"PASS rest tests ({passed} cases)");
        return passed;
    }
}
