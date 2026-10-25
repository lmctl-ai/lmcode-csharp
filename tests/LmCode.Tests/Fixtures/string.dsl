program = _lmcode_ schema="string-trace.schema.json";
    capture(str_len(""));
    capture(str_len("abc"));
    capture(str_len("é"));
    capture(str_len("日本"));
end _lmcode_
savedsl(program, "string.dslb");
