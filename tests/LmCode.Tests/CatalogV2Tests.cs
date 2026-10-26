using LmCode;
using LmCode.Stdlib;

static class CatalogV2Tests
{
    public static int Run()
    {
        JsonOwnershipAndBudget();
        PrioritySequenceBoundary();
        Console.WriteLine("PASS v2 JSON ownership, borrowed identity, shared quota and cleanup");
        return 8;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Trap(Action action, string message)
    {
        try { action(); } catch (Error e) when (e.Code == "RUN_HOST" && e.Message == message) { return; }
        throw new Exception("missing exact trap: " + message);
    }
    private static void PrioritySequenceBoundary()
    {
        var registry = new Registry(); var context = new Context(); registry.EnterRun(context);
        try
        {
            Value queue = registry.Resolve("fn float_pq() -> FloatPQ")!.Function!(context, []);
            var native = context.Get<ContainersCatalog.Pq<double>>(queue.AsObj());
            Value Push(double item, double priority) => registry.Resolve("method FloatPQ.push(float, float) -> FloatPQ")!.Method!(context, queue.AsObj(), [Value.Float(item), Value.Float(priority)]);
            native.Next = long.MaxValue;
            Trap(() => Push(1, double.NaN), "FloatPQ.push: priority must be finite");
            Trap(() => Push(1, 0), "FloatPQ.push: insertion sequence exhausted");
            Require(native.Items.Count == 0 && native.Next == long.MaxValue, "failed push must not mutate PQ");
            registry.Resolve("method FloatPQ.clear() -> FloatPQ")!.Method!(context, queue.AsObj(), []);
            long bits = unchecked((long)0x7ff8000000000042UL);
            Push(BitConverter.Int64BitsToDouble(bits), double.NegativeZero);
            Push(2, 0);
            Value Pop() => registry.Resolve("method FloatPQ.pop() -> float")!.Method!(context, queue.AsObj(), []);
            Require(BitConverter.DoubleToInt64Bits(Pop().AsFloat()) == bits && Pop().AsFloat() == 2, "PQ ties must preserve insertion order and NaN payload bits");
        }
        finally { registry.ExitRun(context); }
    }
    private static void JsonOwnershipAndBudget()
    {
        var registry = new Registry(); var context = new Context();
        Value Call(string signature, params Value[] args) => registry.Resolve(signature)!.Function!(context, args);
        Value Method(string signature, Value self, params Value[] args) => registry.Resolve(signature)!.Method!(context, self.AsObj(), args);
        Value Parse(string json) => Call("fn json_parse(string) -> Json", Value.Str(VmString.FromString(json)));
        registry.EnterRun(context);
        ObjRef root, child, keys;
        try
        {
            var json = Parse("{\"a\":[1]}"); root = json.AsObj();
            var key = Value.Str(VmString.FromString("a"));
            child = Method("method Json.get(string) -> Json", json, key).AsObj();
            Require(child == Method("method Json.get(string) -> Json", json, key).AsObj(), "navigation must retain node identity");
            keys = Method("method Json.keys() -> StrList", json).AsObj();
            Require(context.Get<StrList>(keys).Values.Single().Equals(key.AsStr()), "keys snapshot content");
            for (int i = 0; i < 3; i++) Trap(() => Parse("{"), "json_parse: object key must be a string at byte 1");
            long[] native = [7, 9];
            var ints = CollectionsCatalog.CreateIntList(context, native);
            native[0] = 100;
            Require(context.Get<IntList>(ints.AsObj()).Values[0] == 7, "host adapters snapshot values");
            _ = CollectionsCatalog.CreateFloatList(context, [double.NegativeZero]);
            _ = CollectionsCatalog.CreateStrList(context, [new VmString(new byte[] { 0, 255 })]);
            _ = CollectionsCatalog.CreateStrMap(context, [(key.AsStr(), key.AsStr())]);
            // One root, one keys list and four adapter copies count; the borrowed child and failed
            // parses do not. All catalog families spend this same budget.
            for (int i = 0; i < 65530; i++) Call("fn int_queue() -> IntQueue");
            Trap(() => Parse("null"), "Json object limit exceeded (65536)");
            Trap(() => Method("method Json.keys() -> StrList", json), "StrList object limit exceeded (65536)");
            Require(child == Method("method Json.get(string) -> Json", json, key).AsObj(), "navigation works when root quota is full");
        }
        finally { registry.ExitRun(context); }
        foreach (ObjRef reference in new[] { root, child, keys })
        {
            try { _ = context.Get<object>(reference); throw new Exception("std object survived run end"); }
            catch (Error) { }
        }
        int slots = context.Count;
        for (int i = 0; i < 20; i++)
        {
            registry.EnterRun(context);
            try { _ = Parse("null"); }
            finally { registry.ExitRun(context); }
        }
        Require(context.Count == slots, "all borrowed/root slots must be reusable between runs");
    }
}
