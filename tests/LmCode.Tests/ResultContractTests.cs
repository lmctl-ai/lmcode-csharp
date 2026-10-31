using LmCode;
using LmCode.Stdlib;
static class ResultContractTests
{
    public static int Run() {
        int checks=0;var context=new Context();var registry=new Registry();
        static ResultContract Contract(string type,bool nullable=false)=>ResultContract.FromSchema("{\"dsl.schema\":2,\"metadata\":9223372036854775807,\"result\":{\"type\":\""+type+"\""+(nullable?",\"nullable\":true":"")+"}}");
        static void Reject(Action action,string message){try{action();}catch(Error e)when(e.Message.Contains("result type mismatch",StringComparison.Ordinal)){return;}throw new Exception(message);}
        Value Json(string raw) => registry.Resolve("fn json_parse(string) -> Json")!.Function!(context,[Value.Str(VmString.FromString(raw))]);
        var shapes=new Dictionary<string,string>{{"null","null"},{"bool","true"},{"int","2"},{"float","0.25"},{"string","\"s\""},{"Json","{\"nested\":[null,1]}"},{"StrList","[\"s\"]"},{"IntList","[2]"},{"FloatList","[0.25]"},{"StrMap","{\"x\":\"s\"}"},{"IntMap","{\"x\":2}"},{"FloatMap","{\"x\":0.25}"}};
        registry.EnterRun(context);
        try {
            foreach(var (type,raw) in shapes) {
                var sink=new ResultSink(Contract(type));sink.Set(context,Json(raw));sink.Seal();checks++;
                if(type!="null") {
                    var nullable=new ResultSink(Contract(type,true));nullable.SetNull();nullable.Seal();checks++;
                    var unfinished=new ResultSink(Contract(type));Reject(()=>unfinished.Seal(),"unset nonnullable result accepted: "+type);checks++;
                }
            }
            foreach(string type in new[]{"StrList","IntList","FloatList","StrMap","IntMap","FloatMap"}) {
                string raw=type.EndsWith("List",StringComparison.Ordinal)?"[null]":"{\"x\":null}";
                var sink=new ResultSink(Contract(type,true));Reject(()=>sink.Set(context,Json(raw)),"nullable element accepted: "+type);checks++;
            }
            // Empty collections still have native identity even with no elements to inspect.
            var integerList=Value.Object("IntList",context.Insert("IntList",new IntList([])));
            var floatList=Value.Object("FloatList",context.Insert("FloatList",new FloatList([])));
            new ResultSink(Contract("FloatList")).Set(context,integerList);checks++;
            Reject(()=>new ResultSink(Contract("IntList")).Set(context,floatList),"narrowed empty FloatList");checks++;
            Reject(()=>new ResultSink(Contract("StrList")).Set(context,integerList),"empty collection type erased");checks++;
            foreach(string type in new[]{"int","IntList","IntMap"}) {
                string raw=type=="int"?"2.0":type=="IntList"?"[2.0]":"{\"x\":2.0}";
                new ResultSink(Contract(type)).Set(context,Json(raw));checks++;
            }
            foreach(var value in new[]{Value.Int(2),Value.Float(.5),Value.Bool(true),Value.Str(VmString.FromString("s")),integerList}) {
                new ResultSink(Contract("Json")).Set(context,value);checks++;
            }
            var any=new ResultSink();any.Seal();checks++;
        }finally{registry.ExitRun(context);}
        Console.WriteLine($"PASS result contract matrix ({checks} checks)");return checks;
    }
}
