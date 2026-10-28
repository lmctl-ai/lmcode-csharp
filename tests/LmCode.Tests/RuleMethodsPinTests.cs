using LmCode;
using System.Text.Json;

static class RuleMethodsPinTests
{
    // Regression pin for compiler rule methods (R47/R48): a dsl.schema: 2 schema may carry a
    // top-level "interfaces" map of SAM signatures, and bytecode from an annotated `implements`
    // fn preserves body behavior and provides a callable rule entry. Fixtures under Fixtures/rule-methods were compiled with an
    // R47/R48 lmscript (LMSCRIPT_BIN); plain.lms keeps a blank line where annotated.lms has the
    // @PRICE_RULE annotation so source-map line numbers align.
    public static int Run()
    {
        byte[] schema = File.ReadAllBytes(Fixture("schema.json"));
        byte[] withoutInterfaces = File.ReadAllBytes(Fixture("schema-no-interfaces.json"));
        byte[] annotated = File.ReadAllBytes(Fixture("annotated.dslb"));
        byte[] plain = File.ReadAllBytes(Fixture("plain.dslb"));

        Require(SchemaSignatures(schema).SequenceEqual(["fn note(string) -> void"]),
            "interfaces must be tolerated without becoming signatures");

        var output = new List<string>();
        var registry = new Registry(false);
        registry.Function("note", ["string"], "void", (_, values) => { output.Add(values[0].AsStr().ToString()); return Value.None; });

        Program.Load(annotated, schema, registry).Run(new Context(), registry);
        Require(output.SequenceEqual(["adjust 41"]), "annotated rule-method program output: " + string.Join(", ", output));
        output.Clear();
        Program.Load(plain, schema, registry).Run(new Context(), registry);
        Require(output.SequenceEqual(["adjust 41"]), "unannotated program output: " + string.Join(", ", output));

        var program = Program.Load(annotated, schema, registry);
        Require(program.Entries.Count == 1 && program.Entries[0].Annotation == "PRICE_RULE", "rule metadata");
        Require(Program.Load(plain, schema, registry).Entries.Count == 0, "plain function is not an entry");
        output.Clear();program.Call(0,new Context(),registry,[Value.Int(12)]);
        Require(output.SequenceEqual(["adjust 12"]), "entry call parameter");

        bool mismatch = false;
        try { Program.Load(annotated, withoutInterfaces, registry); }
        catch (Error error) { mismatch = error.Code == "LOAD_SCHEMA_MISMATCH"; }
        Require(mismatch, "interfaces join the schema sha256, so a schema without them must not load this bytecode");
        Console.WriteLine("PASS rule-methods pin"); return 7;
    }

    private static string[] SchemaSignatures(byte[] schema)
    {
        using var document = JsonDocument.Parse(schema);
        Require(document.RootElement.GetProperty("dsl.schema").GetInt32() == 2, "rule-methods pin schema version");
        var signatures = new List<string>();
        foreach (var function in document.RootElement.GetProperty("functions").EnumerateObject())
            foreach (var overload in function.Value.EnumerateArray())
            {
                var parameters = overload.GetProperty("params").EnumerateArray().Select(p => p.GetString());
                signatures.Add($"fn {function.Name}({string.Join(", ", parameters)}) -> {overload.GetProperty("returns").GetString()}");
            }
        signatures.Sort(StringComparer.Ordinal);
        return signatures.ToArray();
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rule-methods", name);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
