using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using LmCode;
static class InvocationVectorTests
{
    static void Require(bool v,string m){if(!v)throw new Exception(m);}
    public static int Run(){
        string folder=Path.Combine(AppContext.BaseDirectory,"Fixtures","invocation-vectors");
        foreach(string line in File.ReadAllLines(Path.Combine(folder,"vectors.lock"))){var p=line.Split("  ",2,StringSplitOptions.None);Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,p[1])))).Equals(p[0],StringComparison.OrdinalIgnoreCase),"invocation fixture hash "+p[1]);}
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"manifest.json")));int count=0;
        foreach(var test in manifest.RootElement.GetProperty("cases").EnumerateArray()){
            string name=test.GetProperty("name").GetString()!;
            string temp=Path.Combine(Path.GetTempPath(),"lmcode-invocation-"+Guid.NewGuid());Directory.CreateDirectory(temp);
            try{
                var names=test.GetProperty("programs").EnumerateArray().Select(n=>n.GetString()!).ToArray();
                var files=names.Select((n,i)=>Path.Combine(temp,i+" 水 "+n+".dslb")).ToArray();for(int i=0;i<files.Length;i++)File.Copy(Path.Combine(folder,names[i]+".dslb"),files[i]);
                string? mode=test.GetProperty("mode").GetString();
                if(mode is null&&names.Distinct().Count()==1)Array.Fill(files,files[0]);
                var parsed=files.Reverse().ToArray();InvocationResults results;
                if(mode is null)results=InvocationResults.FromCommand(files,null);
                else {string path=Path.Combine(temp,"invocation 水.json");File.WriteAllText(path,JsonSerializer.Serialize(new{version=1,mode,parts=files.Select((f,i)=>new{index=i,file=f})}),new UTF8Encoding(false));results=InvocationResults.FromCommand(parsed,path);}
                var registry=new Registry();var context=new Context();long counter=0;
                registry.Function("next_counter",[],"int",(_,_)=>Value.Int(++counter));registry.Function("entry_fail",[],"void",(_,_)=>throw Error.Host("native entry failure"));
                byte[] schema=File.ReadAllBytes(Path.Combine(folder,"schema.json"));var contract=ResultContract.FromSchema(Encoding.UTF8.GetString(schema));
                using var config=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,name+".json")));
                foreach(var position in config.RootElement.GetProperty("execution").EnumerateArray()){
                    int index=position.GetInt32();string phase="load";
                    try{var registration=results.Registrations.Single(r=>r.Index==index);var program=LmCode.Program.Load(File.ReadAllBytes(registration.File),schema,registry);results.Select(index,registry,contract);phase="run";program.Run(context,registry);for(int i=0;i<program.Entries.Count;i++)Require(program.Call(i,context,registry,[]).AsInt()==7,"entry HALT");results.Complete(index);}
                    catch(Error e){results.Fail(index,phase,e);break;}
                }
                if(config.RootElement.TryGetProperty("app_fail",out var app)&&app.GetBoolean())results.Fail(null,"app",new Error("APP_FAIL","native validation failed"));
                using var output=new MemoryStream();int status=results.WriteFrame(output);
                Require(status==test.GetProperty("exit").GetInt32()&&output.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(folder,name+".expect"))),"canonical invocation differs: "+name+"; actual="+Encoding.UTF8.GetString(output.ToArray()));
                Console.WriteLine("PASS canonical invocation "+name);count++;
            }finally{Directory.Delete(temp,true);}
        }
        return count;
    }
}
