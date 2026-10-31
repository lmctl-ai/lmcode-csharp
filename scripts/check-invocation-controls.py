#!/usr/bin/env python3
"""Real SDK faults; build errors, crashes and unrelated exceptions are not catches."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='r3-csharp-invocation-') as temporary:
    work=Path(temporary)
    for name in ['src/LmCode','tests/LmCode.Tests','std','stdlib','conformance/return-vectors','conformance/invocation-vectors']:
        shutil.copytree(ROOT/name,work/name,ignore=shutil.ignore_patterns('bin','obj'))
    project=work/'tests/LmCode.Tests/LmCode.Tests.csproj'
    files=['src/LmCode/ResultSink.cs','src/LmCode/InvocationResults.cs','src/LmCode/ResultContract.cs']
    original={name:(work/name).read_text() for name in files}
    def execute():
        build=subprocess.run(['dotnet','build',str(project),'-c','Release','-warnaserror'],capture_output=True)
        assert build.returncode==0,(build.stdout,build.stderr)
        return subprocess.run(['dotnet',str(project.parent/'bin/Release/net8.0/LmCode.Tests.dll'),'--invocation-tests'],capture_output=True)
    baseline=execute();assert baseline.returncode==0,(baseline.stdout,baseline.stderr)
    print('PASS invocation mutation baseline',flush=True)
    mutants=[
        ('flush before write','src/LmCode/InvocationResults.cs','output.Write(bytes);output.Flush();','output.Flush();output.Write(bytes);','flush failure ignored'),
        ('failure snapshot','src/LmCode/InvocationResults.cs','error.Snapshot()','error','failure snapshot aliased caller'),
        ('missing environment','src/LmCode/InvocationResults.cs','FromCommand(parsedFiles,Environment.GetEnvironmentVariable("LMCODE_INVOCATION_FILE"))','FromCommand(parsedFiles,null)','environment/argv child fixture differs'),
        ('missing seal','src/LmCode/ResultSink.cs','IsSealed=true;return Json();','IsSealed=false;return Json();','missing refusal: sealed'),
        ('reused sink','src/LmCode/InvocationResults.cs','slot.Sink=new ResultSink(contract);','slot.Sink=slots.FirstOrDefault(s=>s.Sink is not null)?.Sink ?? new ResultSink(contract);','independent slot sinks'),
        ('wrong part','src/LmCode/InvocationResults.cs','builder?index:null','builder?0:null','first failure part'),
        ('missing flush','src/LmCode/InvocationResults.cs','output.Write(bytes);output.Flush();','output.Write(bytes);','flush failure ignored'),
        ('missing completion','src/LmCode/InvocationResults.cs','Fail(i,"run",Error.Host("lmcode invocation: slot not completed"));','slots[i].Result="null";','exit status'),
        ('wrong result order','src/LmCode/InvocationResults.cs','slots.Select(s=>s.Result)','slots.Reverse().Select(s=>s.Result)','ordered independent sinks'),
        ('dropped contract','src/LmCode/ResultContract.cs','internal void Validate(string json) {using var doc=InvocationJson.Parse(json);Validate(doc.RootElement,Type,"");}','internal void Validate(string json) {}','missing refusal: /bad'),
    ]
    for name,file,anchor,replacement,diagnostic in mutants:
        for path,source in original.items():(work/path).write_text(source)
        source=original[file];assert source.count(anchor)==1,('changed anchor',name)
        (work/file).write_text(source.replace(anchor,replacement))
        result=execute()
        assert result.returncode==1 and b'FAIL invocation tests:' in result.stderr and diagnostic.encode() in result.stderr,(name,result.returncode,result.stdout,result.stderr)
        print('CAUGHT '+name,flush=True)
