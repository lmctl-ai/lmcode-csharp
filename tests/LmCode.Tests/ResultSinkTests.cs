using System.Globalization;
using System.Text;
using LmCode;
using LmCode.Stdlib;

static class ResultSinkTests
{
    static VmString S(string value) => VmString.FromString(value);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Equal(string expected, string actual) => Require(expected == actual, $"expected {expected}, got {actual}");
    static Value Call(Registry r, Context c, string sig, params Value[] args) =>
        (r.Resolve(sig)?.Function ?? throw new Exception("missing return callback " + sig))(c, args);
    static void Trap(Action action, string text)
    {
        try { action(); } catch (Error e) when (e.Code == "RUN_HOST" && e.Message == text) { return; }
        throw new Exception("missing exact return error: " + text);
    }
    private sealed class DeferredFailureStream : MemoryStream
    {
        public override void Flush() => throw new IOException("deferred fixture failure");
    }
    static byte[] UnitFrame(ResultSink sink)=>Encoding.UTF8.GetBytes("LMCODE_RETURN_BEGIN\n{\"ok\":true,\"result\":"+sink.Json()+"}\nLMCODE_RETURN_END\n");
    public static int Run()
    {
        var c = new Context(); var r = new Registry(); var sink = new ResultSink(); r.SetResultSink(sink);
        Equal("null", sink.Json());
        Equal("LMCODE_RETURN_BEGIN\n{\"ok\":true,\"result\":null}\nLMCODE_RETURN_END\n", Encoding.UTF8.GetString(UnitFrame(sink)));
        Call(r,c,"fn lmcode_return_put(string, int) -> void",Value.Str(S("a")),Value.Int(1));
        Call(r,c,"fn lmcode_return_put(string, bool) -> void",Value.Str(S("b")),Value.Bool(false));
        Call(r,c,"fn lmcode_return_put(string, string) -> void",Value.Str(S("a")),Value.Str(S("last")));
        Call(r,c,"fn lmcode_return_put_null(string) -> void",Value.Str(S("nil")));
        Equal("{\"a\":\"last\",\"b\":false,\"nil\":null}",sink.Json());
        sink.Set(c,Value.Int(5));
        Trap(()=>sink.Put(c,S("x"),Value.Int(1)),"lmcode return: field assignment requires object or null");
        Equal("5",sink.Json());
        sink.Reset();sink.Put(c,S("keep"),Value.Int(3));
        Trap(()=>sink.Set(c,Value.Float(double.PositiveInfinity)),"lmcode return: invalid JSON value at : nonfinite");
        Trap(()=>sink.Put(c,S("bad/key~"),Value.Int(9007199254740992)),"lmcode return: invalid JSON value at /bad~1key~0: unsafe_integer");
        Equal("{\"keep\":3}",sink.Json());
        var unbound = new Registry();
        Trap(()=>Call(unbound,c,"fn lmcode_return_set(int) -> void",Value.Int(1)),"lmcode return: no result sink");
        sink.Reset();r.EnterRun(c);
        Value list = Call(r,c,"fn int_list() -> IntList");
        r.Resolve("method IntList.add(int) -> IntList")!.Method!(c,list.AsObj(),[Value.Int(7)]);
        sink.Set(c,list);
        r.Resolve("method IntList.set(int, int) -> IntList")!.Method!(c,list.AsObj(),[Value.Int(0),Value.Int(99)]);
        Equal("[7]",sink.Json());
        r.Resolve("method IntList.set(int, int) -> IntList")!.Method!(c,list.AsObj(),[Value.Int(0),Value.Int(7)]);
        sink.Reset();sink.Put(c,S("saved"),list);
        r.Resolve("method IntList.set(int, int) -> IntList")!.Method!(c,list.AsObj(),[Value.Int(0),Value.Int(99)]);
        Equal("{\"saved\":[7]}",sink.Json()); // source mutation must not alias
        r.ExitRun(c);Equal("{\"saved\":[7]}",sink.Json()); // arena teardown must not alias
        r.EnterRun(c);Call(r,c,"fn lmcode_return_put(string, bool) -> void",Value.Str(S("entry")),Value.Bool(true));r.ExitRun(c);
        Equal("{\"saved\":[7],\"entry\":true}",sink.Json()); // no reset between body/calls
        r.EnterRun(c);
        var parsed=Call(r,c,"fn json_parse(string) -> Json",Value.Str(S("{\"nested\":[1,true,null]}")));
        sink.Set(c,parsed);
        JsonCatalog.ResultNode(c,parsed.AsObj()).Object[0].Value.Array.Clear();
        r.ExitRun(c);Equal("{\"nested\":[1,true,null]}",sink.Json());
        r.EnterRun(c);
        parsed=Call(r,c,"fn json_parse(string) -> Json",Value.Str(S("{\"x\":[4,5]}")));
        var child=r.Resolve("method Json.get(string) -> Json")!.Method!(c,parsed.AsObj(),[Value.Str(S("x"))]);
        sink.Set(c,child);r.ExitRun(c);Equal("[4,5]",sink.Json());
        sink.Set(c,Value.Float(0.1+0.2));
        Equal("LMCODE_RETURN_BEGIN\n{\"ok\":true,\"result\":0.30000000000000004}\nLMCODE_RETURN_END\n",Encoding.UTF8.GetString(UnitFrame(sink)));
        Require(Formatting.FormatG15(0.1+0.2) != "0.30000000000000004","legacy-format control does not differ");
        var culture=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR"); sink.Set(c,Value.Float(0.1));Equal("0.1",sink.Json()); }
        finally { CultureInfo.CurrentCulture=culture; }
        sink.Reset();
        Trap(()=>sink.Put(c,new VmString([255]),Value.Int(1)),"lmcode return: invalid JSON value at : utf8");
        Equal("null",sink.Json());
        Trap(()=>sink.Set(c,Value.Str(new VmString([255]))),"lmcode return: invalid JSON value at : utf8");
        Trap(()=>sink.Set(c,Value.Object("Widget",c.Insert("Widget",new object()))),"lmcode return: invalid JSON value at : handle");
        sink.Reset();sink.Put(c,S("keep"),Value.Int(3));
        var cycle=new JsonCatalog.Node {Kind="object"};cycle.Object.Add((S("self"),"",cycle));
        Trap(()=>sink.Set(c,Value.Object("Json",c.Insert("Json",cycle))),"lmcode return: invalid JSON value at /self: cycle");
        Equal("{\"keep\":3}",sink.Json());
        var retained=new JsonCatalog.Node {Kind="int",Integer=1,Raw="9007199254740993"};
        Trap(()=>sink.Set(c,Value.Object("Json",c.Insert("Json",retained))),"lmcode return: invalid JSON value at : unsafe_integer");
        var duplicate=new JsonCatalog.Node {Kind="object"};duplicate.Object.Add((S("x"),"",new()));duplicate.Object.Add((S("x"),"",new()));
        Trap(()=>sink.Set(c,Value.Object("Json",c.Insert("Json",duplicate))),"lmcode return: invalid JSON value at /x: handle");
        var ownedJson=new JsonCatalog.Node {Kind="array"};ownedJson.Array.Add(new() {Kind="int",Integer=2,Raw="2"});
        sink.Set(c,Value.Object("Json",c.Insert("Json",ownedJson)));ownedJson.Array.Clear();Equal("[2]",sink.Json());
        sink.Reset();
        var output=new MemoryStream();output.Write(UnitFrame(sink));
        Equal("LMCODE_RETURN_BEGIN\n{\"ok\":true,\"result\":null}\nLMCODE_RETURN_END\n",Encoding.UTF8.GetString(output.ToArray()));
        using(var deferred = new DeferredFailureStream())
            Require(ResultFrames.FailureFrame(deferred,"app",new Error("APP_FAIL","test"))==4,"flush failure ignored");
        sink.Put(c,S("café"),Value.Str(S("水\nLMCODE_RETURN_END")));
        string utf8=Encoding.UTF8.GetString(UnitFrame(sink));
        Require(utf8.Count(ch=>ch=='\n')==3 && utf8.Contains("水\\nLMCODE_RETURN_END",StringComparison.Ordinal),"escaped marker data broke frame");
        sink.Reset();
        Parallel.For(0,20,i=>{
            var ownRegistry=new Registry();var ownSink=new ResultSink();var ownContext=new Context();ownRegistry.SetResultSink(ownSink);
            ownRegistry.EnterRun(ownContext);
            try { Call(ownRegistry,ownContext,"fn lmcode_return_set(int) -> void",Value.Int(i));Equal(i.ToString(CultureInfo.InvariantCulture),ownSink.Json()); }
            finally { ownRegistry.ExitRun(ownContext); }
        });
        Console.WriteLine("PASS result sink ownership, atomic writes, callbacks, concurrency and shortest frames");
        return 34;
    }
}
