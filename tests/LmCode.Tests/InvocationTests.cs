using System.Text;
using System.Text.Json;
using LmCode;
static class InvocationTests
{
    static void Require(bool v,string m){if(!v)throw new Exception(m);}
    static void Reject(Action a,string text=""){try{a();}catch(Error e){if(e.Message.Contains(text,StringComparison.Ordinal))return;throw;}throw new Exception("missing refusal: "+text);}
    static string Output(InvocationResults r,int expected=0){using var s=new MemoryStream();Require(r.WriteFrame(s)==expected,"exit status");return Encoding.UTF8.GetString(s.ToArray());}
    static string Frame(string json)=>"LMCODE_RETURN_BEGIN\n"+json+"\nLMCODE_RETURN_END\n";
    sealed class FlushFailure:MemoryStream {public override void Flush()=>throw new IOException("deferred");}
    static ResultContract Contract(string type,bool nullable=false)=>ResultContract.FromSchema("{\"dsl.schema\":2,\"result\":{\"type\":\""+type+"\""+(nullable?",\"nullable\":true":"")+"}}");
    public static int Run(){
        var r=new Registry();var c=new Context();var parts=InvocationResults.FromCommand(["a","b"],null);
        var b=parts.Select(1,r);b.Put(c,VmString.FromString("b"),Value.Int(2));
        var a=parts.Select(0,r);Require(!ReferenceEquals(a,b),"independent slot sinks");a.Put(c,VmString.FromString("a"),Value.Int(1));
        Require(ReferenceEquals(b,parts.Select(1,r)),"reused selection reset sink");b.Put(c,VmString.FromString("entry"),Value.Bool(true));
        parts.Complete(1);parts.Complete(0);
        Reject(()=>a.Reset(),"sealed");Reject(()=>a.Set(c,Value.Int(3)),"sealed");Reject(()=>parts.Select(0,r),"sealed");Reject(()=>r.SetResultSink(a),"sealed");Reject(()=>parts.Complete(0),"sealed");
        // Complete twice latches a failure: check the already authored successes separately.
        var ordered=InvocationResults.FromCommand(["a","b"],null);ordered.Select(1,r).Set(c,Value.Int(2));ordered.Complete(1);ordered.Select(0,r).Set(c,Value.Int(1));ordered.Complete(0);
        Require(Output(ordered)==Frame("{\"ok\":true,\"result\":[1,2],\"parts\":2}"),"ordered independent sinks");
        Require(ordered.WriteFrame(new MemoryStream())==4,"second frame");
        var missing=InvocationResults.FromCommand(["a","b"],null);missing.Select(1,r).SetNull();missing.Complete(1);
        Require(Output(missing,3).Contains("\"part\":0}",StringComparison.Ordinal),"missing completion association");
        var failed=InvocationResults.FromCommand(["a","b"],null);failed.Select(0,r).Set(c,Value.Int(1));failed.Complete(0);
        var error=Error.Host("first");error.Locate(4,2);failed.Fail(1,"run",error);failed.Fail(999,"bad",Error.Host("second"));
        Require(Output(failed,3)==Frame("{\"ok\":false,\"phase\":\"run\",\"code\":\"RUN_HOST\",\"message\":\"dsl line 4:2: first\",\"line\":4,\"column\":2,\"part\":1}"),"first failure part and no partial result");
        var snapshot=InvocationResults.FromCommand(["a"],null);var mutable=Error.Host("observed");snapshot.Fail(0,"run",mutable);mutable.Locate(9,2);Require(Output(snapshot,3)==Frame("{\"ok\":false,\"phase\":\"run\",\"code\":\"RUN_HOST\",\"message\":\"observed\"}"),"failure snapshot aliased caller");
        var single=InvocationResults.FromCommand(["a"],null);single.Select(0,r).Set(c,Value.Float(.1+.2));single.Complete(0);
        Require(Output(single)==Frame("{\"ok\":true,\"result\":0.30000000000000004}"),"shortest envelope");
        using(var bad=new FlushFailure()){var one=InvocationResults.FromCommand(["a"],null);one.Select(0,r);one.Complete(0);Require(one.WriteFrame(bad)==4,"flush failure ignored");long length=bad.Length;Require(one.WriteFrame(bad)==4&&bad.Length==length,"retry after output failure");}
        var typed=new ResultSink(Contract("IntMap"));typed.Put(c,VmString.FromString("ok"),Value.Int(1));Reject(()=>typed.PutNull(VmString.FromString("bad")),"/bad");Require(typed.Json()=="{\"ok\":1}","non-atomic contract");typed.Reset();Reject(()=>typed.Seal(),"expected IntMap; got null");typed.Put(c,VmString.FromString("x"),Value.Int(2));typed.Seal();
        var ints=new ResultSink(Contract("int"));Reject(()=>ints.Set(c,Value.Float(2)),"expected int; got float");ints.Set(c,Value.Int(2));ints.Seal();
        var nullable=new ResultSink(Contract("string",true));nullable.SetNull();nullable.Seal();
        foreach(string bad in new[]{"{\"dsl.schema\":2,\"result\":{\"type\":\"null\",\"nullable\":false}}","{\"dsl.schema\":2,\"result\":{\"type\":\"handle\"}}","{\"dsl.schema\":2,\"result\":{\"type\":\"int\",\"extra\":1}}"})Reject(()=>ResultContract.FromSchema(bad),"lmcode result contract:");
        var typedParts=InvocationResults.FromCommand(["a","b"],null);typedParts.Select(1,r,Contract("string")).Set(c,Value.Str(VmString.FromString("s")));typedParts.Complete(1);typedParts.Select(0,r,Contract("int")).Set(c,Value.Int(1));typedParts.Complete(0);Require(Output(typedParts)==Frame("{\"ok\":true,\"result\":[1,\"s\"],\"parts\":2}"),"per-slot contracts");
        var incomplete=InvocationResults.FromCommand(["a","b"],null);incomplete.Select(1,r,Contract("int"));Reject(()=>incomplete.Complete(1),"expected int; got null");Require(Output(incomplete,3).Contains("\"part\":1",StringComparison.Ordinal),"completion contract association");
        string f=Path.GetFullPath("水 a.dslb"),g=Path.GetFullPath("b.dslb");
        string metadata="{\"version\":1,\"mode\":\"builder\",\"parts\":[{\"index\":0,\"file\":"+ResultJson.Quote(f)+"},{\"index\":1,\"file\":"+ResultJson.Quote(g)+"}]}";
        var meta=InvocationResults.FromMetadata(metadata,[g,f]);Require(meta.Registrations.Select(x=>x.Index).SequenceEqual([1,0]),"canonical registration mapping");
        Reject(()=>InvocationResults.FromMetadata(metadata,[f,f]),"duplicate registration");Reject(()=>InvocationResults.FromMetadata(metadata,[f]),"count");
        foreach(string bad in new[]{metadata.Replace("\"version\":1","\"version\":2"),metadata.Replace("\"version\":1","\"version\":1,\"version\":1"),metadata.Replace("\"index\":1","\"index\":0"),metadata.Replace("\"builder\"","\"single\""),"\uFEFF"+metadata,metadata.Replace("\"version\":1","\"version\":1,\"extra\":0"),metadata.Replace("\"version\":1","\"version\":1e-9999")})Reject(()=>InvocationResults.FromMetadata(bad,[g,f]));
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+" 水.json");try{File.WriteAllText(path,metadata,new UTF8Encoding(false));Require(InvocationResults.FromCommand([g,f],path).Registrations[0].Index==1,"manifest file");}finally{File.Delete(path);}
        Reject(()=>InvocationResults.FromCommand(["a"],""));Reject(()=>InvocationResults.FromCommand(["a"],"relative"));Reject(()=>InvocationResults.FromCommand(["a"],Path.GetFullPath("bad")+"\ud800"));
        Console.WriteLine("PASS invocation slots, sealing, contracts, metadata, first failure, flush and exact envelopes");return 40;
    }
}
