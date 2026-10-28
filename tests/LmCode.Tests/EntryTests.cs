using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LmCode;

static class EntryTests
{
    internal static byte[] Image(int version=6,int name=0,int nparams=1,int nlocals=1,int type=3,int hidden=0,string iface="interface Step(int) -> int",byte[]? code=null)
    {
        code ??= [4,0,0,17];var b=new List<byte>("DSLB"u8.ToArray());U16(b,version);b.AddRange(SHA256.HashData([]));
        string[] pool=["increment","pair",iface,"int","string"];U32(b,pool.Length);
        foreach(string s in pool){byte[] bytes=Encoding.UTF8.GetBytes(s);b.Add(1);U32(b,bytes.Length);b.AddRange(bytes);}
        U16(b,0);U16(b,0);U16(b,0);U16(b,0);U32(b,1);b.Add(17);U32(b,0);
        if(version==5)return b.ToArray();
        U16(b,1);U32(b,name);U32(b,1);U32(b,2);U16(b,nparams);U16(b,nlocals);
        for(int i=0;i<nlocals;i++){U32(b,type);b.Add((byte)hidden);}U16(b,2);U32(b,code.Length);b.AddRange(code);
        U32(b,1);U32(b,0);U32(b,7);U16(b,3);return b.ToArray();
    }
    static void U16(List<byte>b,int n){Span<byte>x=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(x,(ushort)n);b.AddRange(x.ToArray());}
    static void U32(List<byte>b,int n){Span<byte>x=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(x,(uint)n);b.AddRange(x.ToArray());}
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static int Run()
    {
        var cases=new (string Name,byte[] Bytes,string Text)[]{
            ("v5",Image(version:5),"unsupported bytecode version 5 (this runtime reads version 6)"),
            ("constant",Image(name:99),"constant index 99"),
            ("interface",Image(iface:"fn Step(int) -> int"),"entry 'increment': interface"),
            ("params count",Image(nparams:0),"entry 'increment' declares 0 parameter(s); interface Step(int) -> int has 1"),
            ("locals count",Image(nlocals:0,code:[17]),"nparams 1 exceeds nlocals 0"),
            ("param type",Image(type:4),"entry 'increment' parameter 1 has type string; the interface expects int"),
            ("hidden param",Image(hidden:1),"entry 'increment' parameter 1 is hidden"),
            ("hidden flag",Image(hidden:2),"hidden flag must be 0 or 1"),
            ("HALT value",Image(nparams:0,nlocals:0,iface:"interface Step() -> int",code:[17]),"entry 'increment': HALT leaves 0 values"),
            ("HALT void",Image(nparams:0,nlocals:0,iface:"interface Step() -> void",code:[3,1,17]),"entry 'increment': HALT leaves 1 values"),
            ("jump",Image(nparams:0,nlocals:0,iface:"interface Step() -> void",code:[12,0,0,0,20,17]),"entry 'increment': jump at offset"),
            ("underflow",Image(nparams:0,nlocals:0,iface:"interface Step() -> void",code:[16,17]),"entry 'increment': operand stack underflow"),
            ("truncated",Image()[..^1],"truncated"),
            ("trailing",Image().Concat(new byte[]{0}).ToArray(),"trailing byte")
        };
        int failed=0;
        foreach(var (name,bytes,text) in cases){try{Program.Load(bytes,[],new Registry(false));Console.WriteLine("FAIL "+name+": accepted");failed++;}catch(Error e){bool ok=e.Code=="LOAD_BAD_BYTECODE"&&e.Message.Contains(text,StringComparison.Ordinal);Console.WriteLine((ok?"PASS ":"FAIL ")+name+": "+e.Message);if(!ok)failed++;}}
        Require(failed==0,$"{failed} entry loader controls failed");
        var registry=new Registry(false);var program=Program.Load(Image(),[],registry);var context=new Context();
        Require(program.Entries.Count==1 && program.Entries[0].Name=="increment" && program.Entries[0].Params.SequenceEqual(["int"]),"entry metadata");
        Require(program.Call(0,context,registry,[Value.Int(41)]).AsInt()==41,"call before body");program.Run(context,registry);
        Require(program.Call(0,context,registry,[Value.Int(9)]).AsInt()==9,"fresh locals");
        Require(program.Call(0,context,registry,[Value.Str(VmString.FromString("untyped"))]).AsStr().ToString()=="untyped","untyped arguments/result");
        try{program.Call(0,context,registry,[]);throw new Exception("arity accepted");}catch(Error e){Require(e.Code=="RUN_HOST" && e.Message=="call entry 'increment' expects 1 argument(s); found 0","arity message");}
        try{program.Call(-1,context,registry,[]);throw new Exception("index accepted");}catch(Error e){Require(e.Code=="RUN_HOST","index refusal");}
        try{((IList<Entry>)program.Entries).Clear();throw new Exception("mutable entries");}catch(NotSupportedException){}
        try{((IList<string>)program.Entries[0].Params).Clear();throw new Exception("mutable params");}catch(NotSupportedException){}
        var empty=Program.Load(Image(nparams:0,nlocals:0,iface:"interface Step() -> void",code:[17]),[],registry);
        Require(empty.Call(0,context,registry,[]).Kind==ValueKind.None,"void return");
        var bad=Program.Load(Image(nparams:0,nlocals:0,iface:"interface Step() -> int",code:[1,0,0,0,0,0,0,0,1,1,0,0,0,0,0,0,0,0,22,17]),[],registry);
        try{bad.Call(0,context,registry,[]);throw new Exception("division accepted");}catch(Error e){Require(e.Code=="RUN_ERROR"&&e.Line==7&&e.Column==3,"entry location");}
        Console.WriteLine("PASS entry API");return cases.Length+10;
    }
}
