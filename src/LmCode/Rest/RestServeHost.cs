namespace LmCode.Rest;

/// <summary>A tiny serve host: PROGRAM.dslb HOST.schema.json [--max-body-bytes N]. Exit 0 after serving, 2 on usage, 3 on error.</summary>
public static class RestServeHost
{
    public static int Run(string[] args, Stream? input = null, Stream? output = null, TextWriter? error = null)
    {
        error ??= Console.Error;
        if (args.Length < 2 || args.Length != 2 && !(args.Length == 4 && args[2] == "--max-body-bytes"))
        { error.WriteLine("usage: RestServe PROGRAM.dslb HOST.schema.json [--max-body-bytes N]"); return 2; }
        try
        {
            var registry = new Registry();
            var options = new RestOptions
            {
                Input = input, Output = output,
                MaxBodyBytes = args.Length == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1024 * 1024,
                OnHandlerError = (entry, message) => error.WriteLine($"rest: handler {entry} failed: {message}"),
            };
            var rest = RestCatalog.RegisterRest(registry, options);
            var program = Program.Load(File.ReadAllBytes(args[0]), File.ReadAllBytes(args[1]), registry);
            rest.Run(program);
            return 0;
        }
        catch (Exception e) when (e is Error or IOException or FormatException or OverflowException)
        {
            error.WriteLine("rest-serve: " + e.Message); return 3;
        }
    }
}
