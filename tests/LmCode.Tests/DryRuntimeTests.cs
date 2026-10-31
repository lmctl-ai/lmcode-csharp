using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LmCode;

static class DryRuntimeTests
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void U16(List<byte> bytes,int value){Span<byte> b=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,(ushort)value);bytes.AddRange(b.ToArray());}
    static void U32(List<byte> bytes,int value){Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)value);bytes.AddRange(b.ToArray());}
    static byte[] Image(params string[] imports)
    {
        var bytes=new List<byte>("DSLB"u8.ToArray());U16(bytes,6);bytes.AddRange(SHA256.HashData([]));
        U32(bytes,imports.Length);foreach(string name in imports){byte[] text=Encoding.UTF8.GetBytes(name);bytes.Add(1);U32(bytes,text.Length);bytes.AddRange(text);}
        U16(bytes,imports.Length);for(int i=0;i<imports.Length;i++)U32(bytes,i);
        U16(bytes,0);U16(bytes,0);U16(bytes,1);byte[] code;
        if(imports.Length==1)code=[7,0,0,0,17];else code=[7,0,0,0,8,0,1,0,17];
        U32(bytes,code.Length);bytes.AddRange(code);U32(bytes,0);U16(bytes,0);return bytes.ToArray();
    }
    static void Run(Registry registry,params string[] imports){var context=new Context();context.SetDry(true);Program.Load(Image(imports),[],registry).Run(context,registry);}
    public static int RunAll()
    {
        string path=Path.Combine(Path.GetTempPath(),"lmcode-dry-sentinel-"+Guid.NewGuid());
        try {
            var registry=new Registry(false);int real=0,preview=0;
            registry.Function("effect",[],"int",(_,_)=>{real++;File.WriteAllText(path,"effect");return Value.Int(41);});
            Run(registry,"fn effect() -> int");
            Require(real==0&&!File.Exists(path),"dry default invoked effectful real callback");
            registry.Function("preview",[],"int",(_,_)=>{real++;return Value.Int(7);},Registry.Dry((_,_)=>{preview++;return Value.Int(8);}));
            Run(registry,"fn preview() -> int");Require(preview==1&&real==0,"dry override did not run exactly once / real ran");
            var badOverride=new Registry(false);badOverride.Function("bad",[],"int",(_,_)=>Value.Int(1),Registry.Dry((_,_)=>Value.Str(VmString.FromString("bad"))));
            try{Run(badOverride,"fn bad() -> int");throw new Exception("invalid dry override result accepted");}catch(Error e){Require(e.Code=="RUN_HOST"&&e.Message.Contains("dry callback for int returned string",StringComparison.Ordinal),"dry override return validation");}
            File.WriteAllText(path,"fixture");int read=0;Func<Context,Value[],Value> realRead=(_,_)=>{read++;return Value.Str(VmString.FromString(File.ReadAllText(path)));};
            registry.Function("fixture_read",[],"string",realRead,Registry.Dry(realRead));Run(registry,"fn fixture_read() -> string");Require(read==1,"Registry.Dry(real) did not opt into one real read");File.Delete(path);
            var realContext=new Context();Program.Load(Image("fn preview() -> int"),[],registry).Run(realContext,registry);
            Require(preview==1&&real==1,"real invocation did not preserve the original callback");
            var dummyContext=new Context();Value first=registry.DryDummy(dummyContext,"IntList"),second=registry.DryDummy(dummyContext,"IntList");
            Require(first.Kind==ValueKind.Obj&&second.Kind==ValueKind.Obj&&first.AsObj()!=second.AsObj(),"dry collection dummy was shared");
            Value firstMap=registry.DryDummy(dummyContext,"IntMap"),secondMap=registry.DryDummy(dummyContext,"IntMap");
            Require(firstMap.AsObj()!=secondMap.AsObj()&&registry.DryDummy(dummyContext,"void").Kind==ValueKind.None&&registry.DryDummy(dummyContext,"null").IsDryNull&&registry.DryDummy(dummyContext,"Opaque").IsDryNull,"typed object/map/void dummies");
            Require(registry.DryDummy(new Context(),"bool").AsBool()&&registry.DryDummy(new Context(),"int").AsInt()==0&&registry.DryDummy(new Context(),"float").AsFloat()==0&&registry.DryDummy(new Context(),"string").AsStr().Length==0,"typed scalar dummies");
            var standards=new Registry();
            foreach(string signature in standards.StdSignatures()){
                Binding binding=standards.Resolve(signature)!;bool method=signature.StartsWith("method ",StringComparison.Ordinal),write=signature.StartsWith("fn file_write(",StringComparison.Ordinal)||signature.StartsWith("fn file_append(",StringComparison.Ordinal);
                Require(method?binding.DryMethod is not null&&binding.DryFunction is null:(binding.DryFunction is null)==write&&binding.DryMethod is null,"stdlib dry policy: "+signature);
            }
            standards.BeginCompatibilityRegistration();try{standards.Function("math_abs",["int"],"int",(_,_)=>Value.Int(1));}finally{standards.EndCompatibilityRegistration();}
            Require(standards.Resolve("fn math_abs(int) -> int")!.DryFunction is null,"replacement inherited builtin dry policy by name");
            using var diagnostics=new MemoryStream();var testing=new Registry(false);LmCode.Stdlib.TestingCatalog.RegisterTesting(testing,diagnostics);
            Binding print=testing.Resolve("fn print(string) -> void")!;Require(print.DryFunction is not null,"captured print has no dry policy");print.DryFunction!(new Context(),[Value.Str(VmString.FromString("diagnostic"))]);
            Require(Encoding.UTF8.GetString(diagnostics.ToArray())=="diagnostic\n","dry diagnostic was not captured exactly once");
            var uncapturedTesting=new Registry(false);LmCode.Stdlib.TestingCatalog.RegisterTesting(uncapturedTesting);Require(uncapturedTesting.Resolve("fn print(string) -> void")!.DryFunction is null,"uncaptured print writes during dry mode");
            var sink=new ResultSink();standards.SetResultSink(sink);Binding resultBinding=standards.Resolve("fn lmcode_return_set(int) -> void")!;
            resultBinding.DryFunction!(new Context(),[Value.Int(7)]);Require(sink.Json()=="7","dry result sink was suppressed");
            var nullableSink=new ResultSink(ResultContract.FromSchema("{\"dsl.schema\":2,\"result\":{\"type\":\"int\",\"nullable\":true}}"));nullableSink.Set(new Context(),Value.DryNull("Opaque"));Require(nullableSink.Json()=="null","dry null result did not preserve null");
            try{new ResultSink(ResultContract.FromSchema("{\"dsl.schema\":2,\"result\":{\"type\":\"int\"}}" )).Set(new Context(),Value.DryNull("Opaque"));throw new Exception("nonnullable dry null accepted");}catch(Error e){Require(e.Message.Contains("expected int; got null",StringComparison.Ordinal),"dry null result contract message");}
            int methods=0;var methodRegistry=new Registry(false);
            methodRegistry.Function("make",[],"Thing",(_,_)=>Value.Object("Thing",new ObjRef(0)));
            methodRegistry.Method("Thing","read",[],"int",(_,_,_)=>{methods++;return Value.Int(9);});
            Run(methodRegistry,"fn make() -> Thing","method Thing.read() -> int");
            Require(methods==0,"dry null receiver reached the real method callback");
            int previews=0;var guardedMethod=new Registry(false);guardedMethod.Function("make",[],"Thing",(_,_)=>Value.Object("Thing",new ObjRef(0)));
            guardedMethod.Method("Thing","read",[],"int",(_,_,_)=>{methods++;return Value.Int(9);},Registry.Dry((_,_,_)=>{previews++;return Value.Int(10);}));
            try{Run(guardedMethod,"fn make() -> Thing","method Thing.read() -> int");throw new Exception("dry null receiver reached method override");}catch(Error e){Require(e.Code=="RUN_HOST"&&previews==0,"dry null receiver override guard");}
        }
        finally {if(File.Exists(path))File.Delete(path);}
        Console.WriteLine("PASS dry callback defaults, overrides, real mode, typed fresh dummies, and null receivers");return 10;
    }
}
