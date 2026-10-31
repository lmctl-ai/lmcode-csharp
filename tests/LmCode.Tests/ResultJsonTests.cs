using LmCode;

static class ResultJsonTests
{
    public static int Run()
    {
        int checks = 0;
        static void Equal(string expected, string actual) { if (expected != actual) throw new Exception($"expected {expected}, got {actual}"); }
        Equal("0.30000000000000004", ResultJson.FormatNumber(0.1 + 0.2)); checks++;
        Equal("0.1", ResultJson.FormatNumber(0.1)); checks++;
        Equal("5E-324", ResultJson.FormatNumber(5e-324)); checks++;
        Equal("0", ResultJson.FormatNumber(-0.0)); checks++;
        Equal("0", ResultJson.ValidateNumberToken("-0")); checks++;
        Equal("0.1", ResultJson.ValidateNumberToken("1e-1")); checks++;
        Equal("9007199254740991", ResultJson.ValidateNumberToken("9007199254740991")); checks++;
        Equal("9007199254740991", ResultJson.ValidateNumberToken("9007199254740991.0")); checks++;
        Equal("9007199254740991", ResultJson.ValidateNumberToken("90071992547409910e-1")); checks++;
        Equal("1E-50", ResultJson.ValidateNumberToken("1e-50")); checks++;
        foreach (string zero in new[] {"0e-999999999999999999999", "-0.0e999999999999999999999"}) { Equal("0",ResultJson.ValidateNumberToken(zero));checks++; }
        foreach (string token in new[] {"90071992547409930000e-4", "-9007199254740993", "1e999999999999999999999"}) {
            try {ResultJson.ValidateNumberToken(token,"/raw");throw new Exception("unsafe raw accepted");}
            catch(Error e) when(e.Message=="lmcode return: invalid JSON value at /raw: unsafe_integer") {checks++;}
        }
        foreach (double bad in new[] {double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
            try {ResultJson.FormatNumber(bad,"/f");throw new Exception("nonfinite float accepted");}
            catch(Error e) when(e.Message=="lmcode return: invalid JSON value at /f: nonfinite") {checks++;}
        }
        try {ResultJson.Quote("\ud800","/key");throw new Exception("unpaired surrogate accepted");}
        catch(Error e) when(e.Message=="lmcode return: invalid JSON value at /key: utf8") {checks++;}
        foreach (var token in new[] { "9007199254740992", "9007199254740993", "9.007199254740992e15", "9007199254740991.9", "1e999999" })
        {
            try { _ = ResultJson.ValidateNumberToken(token, "/n"); throw new Exception("unsafe control survived: " + token); }
            catch (Error e) when (e.Message == "lmcode return: invalid JSON value at /n: unsafe_integer") { checks++; }
        }
        try { _ = ResultJson.ValidateNumberToken("1e-999999", "/n"); throw new Exception("underflow control survived"); }
        catch (Error e) when (e.Message == "lmcode return: invalid JSON value at /n: underflow") { checks++; }
        foreach (var token in new[] { "+1", "01", "1.", "1e", "1e+", "x" })
        {
            try { _ = ResultJson.ValidateNumberToken(token, "/bad"); throw new Exception("malformed control survived: " + token); }
            catch (Error e) when (e.Message == "lmcode return: invalid JSON value at /bad: handle") { checks++; }
        }
        Equal("\"héllo\\nworld\"", ResultJson.Quote("héllo\nworld")); checks++;
        Equal("\"😀\"", ResultJson.Quote("😀")); checks++;
        Equal("\"ok\"", ResultJson.Quote(new VmString(System.Text.Encoding.UTF8.GetBytes("ok")))); checks++;
        try { _ = ResultJson.Quote(new VmString(new byte[] { 0xff }), "/s"); throw new Exception("utf8 control survived"); }
        catch (Error e) when (e.Message.Contains("at /s: utf8")) { checks++; }
        Console.WriteLine($"PASS return numeric and UTF-8 boundary ({checks} exact checks)");
        return checks;
    }
}
