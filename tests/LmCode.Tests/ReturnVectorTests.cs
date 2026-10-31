using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LmCode;

// Host composition is explicit: the VM never frames before entries/app checks.
static class ReturnVectorTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory,"Fixtures","return-vectors");
    private static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    internal static (int Exit,byte[] Output,string Error) Execute(string name,int repeat=1,bool reset=false,bool early=false,bool appFailure=false)
    {
        var registry=new Registry();var sink=new ResultSink();registry.SetResultSink(sink);
        registry.Function("entry_fail",[],"void",(_,_)=>throw Error.Host("entry failure"));
        registry.Function("expect_result",["string"],"void",(_,args)=>{
            Require(sink.Json()==args[0].AsStr().ToString(),"return overload payload mismatch");return Value.None;
        });
        using var output=new MemoryStream();
        try
        {
            var program=LmCode.Program.Load(File.ReadAllBytes(Path.Combine(Folder,name+".dslb")),File.ReadAllBytes(Path.Combine(Folder,"schema.json")),registry);
            var context=new Context();sink.Reset();program.Run(context,registry);
            if(early)ResultFrames.Write(output,sink);
            for(int n=0;n<repeat;n++)for(int i=0;i<program.Entries.Count;i++){
                if(reset)sink.Reset();
                Require(program.Call(i,context,registry,[]).AsInt()==7,"entry HALT result changed");
            }
            if(appFailure)return (4,output.ToArray(),"");
            ResultFrames.Write(output,sink);
            return (0,output.ToArray(),"");
        }
        catch(Error error){return (3,output.ToArray(),error.Message);}
    }
    public static int Run()
    {
        foreach(string line in File.ReadAllLines(Path.Combine(Folder,"vectors.lock"))){
            string[] pieces=line.Split("  ",2,StringSplitOptions.None);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Folder,pieces[1])))).Equals(pieces[0],StringComparison.OrdinalIgnoreCase),"return fixture digest: "+pieces[1]);
        }
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder,"manifest.json")));
        int checks=0;
        foreach(var item in manifest.RootElement.GetProperty("vectors").EnumerateArray()){
            string name=item.GetProperty("name").GetString()!;
            var actual=Execute(name,item.GetProperty("repeat").GetInt32());
            byte[] expected=File.ReadAllBytes(Path.Combine(Folder,item.GetProperty("expect").GetProperty("file").GetString()!));
            Require(actual.Exit==item.GetProperty("expect").GetProperty("exit").GetInt32() && actual.Output.SequenceEqual(expected),"canonical return vector differs: "+name+"; "+actual.Error);
            if(actual.Exit!=0)Require(actual.Output.Length==0,"frame after failed invocation: "+name);
            if(name=="entry-error")Require(actual.Error.Contains("dsl line ",StringComparison.Ordinal)&&actual.Error.Contains("entry failure",StringComparison.Ordinal),"entry diagnostic location");
            if(name=="unsafe-json-integer")Require(actual.Error.Contains("/amount: unsafe_integer",StringComparison.Ordinal),"unsafe JSON pointer/reason");
            if(name=="scalar-field")Require(actual.Error.Contains("field assignment requires object or null",StringComparison.Ordinal),"field diagnostic");
            if(name=="invalid-utf8")Require(actual.Error.Contains(": utf8",StringComparison.Ordinal),"UTF8 diagnostic");
            Console.WriteLine("PASS canonical return fixture "+name);checks++;
        }
        var repeated=Execute("repeated-calls",2,reset:true);
        Require(repeated.Exit==0 && !repeated.Output.SequenceEqual(File.ReadAllBytes(Path.Combine(Folder,"repeated-calls.expect"))),"uncaught reset-between-calls framing control");
        Console.WriteLine("CAUGHT reset-between-calls host control");
        var premature=Execute("entry-error",early:true);
        Require(premature.Exit==3 && Encoding.UTF8.GetString(premature.Output).Contains("LMCODE_RETURN_BEGIN\n",StringComparison.Ordinal),"uncaught early-frame control");
        Console.WriteLine("CAUGHT premature frame before entry error");
        var app=Execute("entry-sink",appFailure:true);
        Require(app.Exit==4 && app.Output.Length==0,"application failure emitted frame");
        Console.WriteLine("PASS application failure is frame-silent");
        return checks+3;
    }
}
