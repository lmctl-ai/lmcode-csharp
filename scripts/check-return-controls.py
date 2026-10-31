#!/usr/bin/env python3
"""Compile genuine runtime faults in an isolated copy; crashes are not test kills."""
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='s2-csharp-controls-') as temporary:
    work=Path(temporary)
    for directory in ['src/LmCode','tests/LmCode.Tests','std','stdlib','conformance/return-vectors','conformance/invocation-vectors']:
        shutil.copytree(ROOT/directory, work/directory, ignore=shutil.ignore_patterns('bin','obj'))
    files=['src/LmCode/Registry.cs','src/LmCode/ResultSink.cs','src/LmCode/ResultJson.cs']
    original={name:(work/name).read_text() for name in files}
    project=work/'tests/LmCode.Tests/LmCode.Tests.csproj'
    env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1'}
    def execute(mode='--return-tests'):
        build=subprocess.run(['dotnet','build',str(project),'-c','Release','-warnaserror'],capture_output=True,env=env)
        assert build.returncode==0,build.stdout.decode(errors='replace')+build.stderr.decode(errors='replace')
        return subprocess.run(['dotnet',str(project.parent/'bin/Release/net8.0/LmCode.Tests.dll'),mode],capture_output=True,env=env)
    for mode in ['--return-tests','--return-vector-tests']:
        baseline=execute(mode)
        assert baseline.returncode==0,(baseline.stdout,baseline.stderr)
    print('PASS return mutation baselines',flush=True)
    mutants=[
        ('arena teardown','src/LmCode/Registry.cs','if (defaultCollections) Stdlib.CollectionsCatalog.EndRun(context);','resultSink?.Reset();\n        if (defaultCollections) Stdlib.CollectionsCatalog.EndRun(context);','--return-tests','expected {"saved"'),
        ('reset-between-calls','src/LmCode/Registry.cs','if (defaultCollections) Stdlib.CollectionsCatalog.BeginRun(context);','resultSink?.Reset();\n        if (defaultCollections) Stdlib.CollectionsCatalog.BeginRun(context);','--return-tests','expected {"saved"'),
        ('missing configured sink','src/LmCode/Registry.cs','resultSink ?? throw Error.Host("lmcode return: no result sink")','resultSink ?? new ResultSink()','--return-tests','missing exact return error: lmcode return: no result sink'),
        ('bad retained numeric token','src/LmCode/ResultSink.cs','if (node.Raw.Length!=0) _=ResultJson.ValidateNumberToken(node.Raw,path);','// mutation: ignore retained integer token','--return-tests','missing exact return error: lmcode return: invalid JSON value at : unsafe_integer'),
        ('source-mutation alias','src/LmCode/ResultSink.cs','public void Set(Context context, Value value) {Writable();Contract.ValidateSource(value);var next=CopyValue(context,value,"");Contract.Validate(Encode(next));root=next;}','public void Set(Context context, Value value) {Writable();Contract.ValidateSource(value);var next=CopyValue(context,value,"");Contract.Validate(Encode(next));root=next;borrowedContext=context;borrowed=value;}','--return-tests','expected [7], got [99]'),
        ('G15 framing formatter','src/LmCode/ResultJson.cs','return value.ToString(CultureInfo.InvariantCulture);','return Formatting.FormatG15(value);','--return-vector-tests','canonical return vector differs: float-roundtrip'),
    ]
    for name,file,anchor,replacement,mode,expected in mutants:
        for target,text in original.items():(work/target).write_text(text)
        source=original[file]
        assert source.count(anchor)==1,('mutation site changed',name)
        source=source.replace(anchor,replacement)
        if name=='source-mutation alias':
            source=source.replace('private JsonCatalog.Node root = new();','private Context? borrowedContext; private Value borrowed;\n    private JsonCatalog.Node root = new();')
            source=source.replace('public void Reset() {Writable();root=new();}','public void Reset() {Writable();borrowedContext=null;root=new();}')
            source=source.replace('public string Json() => Encode(root);','public string Json() => borrowedContext is null ? Encode(root) : Encode(CopyValue(borrowedContext,borrowed,""));')
        (work/file).write_text(source)
        result=execute(mode)
        assert result.returncode==1 and b'FAIL return' in result.stderr and expected.encode() in result.stderr,(name,result.returncode,result.stdout,result.stderr)
        print('CAUGHT '+name,flush=True)
