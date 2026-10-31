using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using LmCode;
static class InvocationVectorTests
{
    static void Require(bool v,string m){if(!v)throw new Exception(m);}
    static int Execute(InvocationResults results,string schemaPath,string configPath,Stream output) {
                var registry=new Registry();var context=new Context();long counter=0;
                registry.Function("next_counter",[],"int",(_,_)=>Value.Int(++counter));registry.Function("entry_fail",[],"void",(_,_)=>throw Error.Host("native entry failure"));
                byte[] schema=File.ReadAllBytes(schemaPath);var contract=ResultContract.FromSchema(Encoding.UTF8.GetString(schema));
                using var config=JsonDocument.Parse(File.ReadAllBytes(configPath));
                foreach(var position in config.RootElement.GetProperty("execution").EnumerateArray()){
                    int index=position.GetInt32();string phase="load";
                    try{var registration=results.Registrations.Single(r=>r.Index==index);var program=LmCode.Program.Load(File.ReadAllBytes(registration.File),schema,registry);results.Select(index,registry,contract);phase="run";program.Run(context,registry);for(int i=0;i<program.Entries.Count;i++)Require(program.Call(i,context,registry,[]).AsInt()==7,"entry HALT");results.Complete(index);}
                    catch(Error e){results.Fail(index,phase,e);break;}
                }
                if(config.RootElement.TryGetProperty("app_fail",out var app)&&app.GetBoolean())results.Fail(null,"app",new Error("APP_FAIL","native validation failed"));
        return results.WriteFrame(output);
    }
    internal static int Child(string[] args) {
        try{return Execute(InvocationResults.FromCommand(args[2..]),args[0],args[1],Console.OpenStandardOutput());}
        catch(Error e){return ResultFrames.FailureFrame(Console.OpenStandardOutput(),"cli",e);}
    }
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
                else {string path=Path.Combine(temp,"invocation 水.json");File.WriteAllText(path,"{\"version\":1,\"mode\":"+ResultJson.Quote(mode)+",\"parts\":["+string.Join(',',files.Select((f,i)=>"{\"index\":"+i+",\"file\":"+ResultJson.Quote(f)+"}"))+"]}",new UTF8Encoding(false));results=InvocationResults.FromCommand(parsed,path);}
                using var output=new MemoryStream();int status=Execute(results,Path.Combine(folder,"schema.json"),Path.Combine(folder,name+".json"),output);
                Require(status==test.GetProperty("exit").GetInt32()&&output.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(folder,name+".expect"))),"canonical invocation differs: "+name+"; actual="+Encoding.UTF8.GetString(output.ToArray()));
                // A separate real process exercises inherited environment reading without mutating this process.
                string executable=Environment.ProcessPath ?? throw new Exception("missing executable path");
                var start=new System.Diagnostics.ProcessStartInfo(executable){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
                if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
                foreach(string arg in new[]{"--invocation-fixture",Path.Combine(folder,"schema.json"),Path.Combine(folder,name+".json")})start.ArgumentList.Add(arg);
                foreach(string file in mode is null?files:parsed)start.ArgumentList.Add(file);
                if(mode is null)start.Environment.Remove("LMCODE_INVOCATION_FILE");else start.Environment["LMCODE_INVOCATION_FILE"]=Path.Combine(temp,"invocation 水.json");
                using var child=System.Diagnostics.Process.Start(start) ?? throw new Exception("child failed to start");
                using var bytes=new MemoryStream();var drain=child.StandardOutput.BaseStream.CopyToAsync(bytes);var diagnostic=child.StandardError.ReadToEndAsync();
                System.Threading.Tasks.Task.WhenAll(drain,diagnostic).GetAwaiter().GetResult();child.WaitForExit();
                Require(child.ExitCode==status&&bytes.ToArray().SequenceEqual(output.ToArray()),"environment/argv child fixture differs: "+name+"; "+diagnostic.Result);
                Console.WriteLine("PASS canonical invocation "+name+" (embedded + child env)");count++;
            }finally{Directory.Delete(temp,true);}
        }
        return count;
    }
}
