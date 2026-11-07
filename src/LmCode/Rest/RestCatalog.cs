namespace LmCode.Rest;

/// <summary>Opt-in `rest` std module (lmcode_std_rest_v1): 18 signatures, never registered by the default registry.</summary>
public static class RestCatalog
{
    private static readonly string[] Reserved = ["content-length", "connection", "transfer-encoding"];
    private static Exchange X(Context c, ObjRef r) => c.Get<Exchange>(r);

    public static RestController RegisterRest(Registry registry, RestOptions? options = null)
    {
        var rc = new RestController(registry, options);
        registry.Function("rest_route", ["string", "string", "string"], "void", (_, a) => { rc.Route(a[0].AsStr(), a[1].AsStr(), a[2].AsStr()); return Value.None; });
        registry.Function("rest_serve", [], "void", (_, _) => { rc.Serve(); return Value.None; });
        registry.Function("rest_stop", [], "void", (_, _) => { rc.Stop(); return Value.None; });
        void M(string name, string[] ps, string ret, Func<Exchange, Value[], Value> f) =>
            registry.Method("RestExchange", name, ps, ret, (c, r, a) => f(X(c, r), a));
        static Value S(string s) => Value.Str(RestController.V(s));
        M("method", [], "string", (x, _) => S(x.Request.Method));
        M("path", [], "string", (x, _) => S(x.Path));
        M("query", ["string"], "string", (x, a) => S(x.Query.GetValueOrDefault(RestController.L(a[0].AsStr()), "")));
        M("has_query", ["string"], "bool", (x, a) => Value.Bool(x.Query.ContainsKey(RestController.L(a[0].AsStr()))));
        M("param", ["string"], "string", (x, a) => S(x.Params.GetValueOrDefault(RestController.L(a[0].AsStr()), "")));
        M("header", ["string"], "string", (x, a) => S(string.Join(", ", x.RequestHeader(RestController.L(a[0].AsStr())))));
        M("has_header", ["string"], "bool", (x, a) => Value.Bool(x.RequestHeader(RestController.L(a[0].AsStr())).Length > 0));
        M("body", [], "string", (x, _) => Value.Str(VmString.FromOwned((byte[])x.Request.Body.Clone())));
        M("status", ["int"], "void", (x, a) =>
        {
            long code = a[0].AsInt();
            if (code < 200 || code > 599) throw Error.Host($"rest: status {code} out of range 200..599");
            x.Status = (int)code; return Value.None;
        });
        M("set_header", ["string", "string"], "void", (x, a) =>
        {
            string name = RestController.L(a[0].AsStr()), value = RestController.L(a[1].AsStr());
            if (name.Length == 0 || name.Any(ch => Router.IsSpace(ch) || ch == ':') || value.AsSpan().IndexOfAny('\r', '\n') >= 0) throw Error.Host("rest: invalid response header");
            if (Array.IndexOf(Reserved, name.ToLowerInvariant()) >= 0) throw Error.Host($"rest: response header {name} is managed by the server");
            x.SetHeader(name, value); return Value.None;
        });
        M("send", ["string"], "void", (x, a) => { x.Body = a[0].AsStr().Span.ToArray(); return Value.None; });
        M("json", ["string"], "void", (x, a) => { x.Body = a[0].AsStr().Span.ToArray(); x.SetHeader("Content-Type", "application/json"); return Value.None; });
        M("get", ["string"], "string", (x, a) => Value.Str(x.State.Values.GetValueOrDefault(RestController.L(a[0].AsStr())) ?? new VmString([])));
        M("set", ["string", "string"], "void", (x, a) => { x.State.Values[RestController.L(a[0].AsStr())] = a[1].AsStr(); return Value.None; });
        M("increment", ["string"], "int", (x, a) =>
        {
            string k = RestController.L(a[0].AsStr()); long next = unchecked(x.State.Counts.GetValueOrDefault(k) + 1); x.State.Counts[k] = next; return Value.Int(next);
        });
        return rc;
    }
}
