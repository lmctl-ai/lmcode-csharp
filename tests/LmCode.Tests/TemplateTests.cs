using LmCode;
using System.Text.Json;
static class TemplateTests {
 public static void Run(){
  var r=new Registry();var c=new Context();r.EnterRun(c);
  Value Call(string sig,params Value[] a)=>r.Resolve(sig)!.Function!(c,a);
  try{
   using var cases=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","template-cases.json")));
   foreach(var item in cases.RootElement.EnumerateArray()){
    var context=Call("fn json_parse(string) -> Json",Value.Str(VmString.FromString(item.GetProperty("context").GetRawText())));
    bool fails=item.TryGetProperty("error",out var expectedError);
    try {
     var got=Call("fn template_render(string, Json) -> string",Value.Str(VmString.FromString(item.GetProperty("template").GetString()!)),context);
     if(fails || !got.AsStr().Equals(VmString.FromString(item.GetProperty("result").GetString()!)))throw new Exception("template mismatch: "+item.GetProperty("name"));
    }catch(Error e){if(!fails || e.Message!=expectedError.GetString())throw;}
   }
   var empty=Call("fn json_parse(string) -> Json",Value.Str(VmString.FromString("{}")));
   var bytes=new VmString(new byte[]{255,0,128});
   if(!Call("fn template_render(string, Json) -> string",Value.Str(bytes),empty).AsStr().Equals(bytes))throw new Exception("template bytes changed");
   Console.WriteLine("PASS template vectors and raw bytes");
  }finally{r.ExitRun(c);}
 }
}
