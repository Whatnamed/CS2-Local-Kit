"""Frozen d796572 + schema-confirmed PBot fix, explicit Head policy and bounded Hook evidence."""
import argparse,difflib,os,subprocess,sys,zipfile
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'bot-experiment'))
import bot_experiment as tx

def build(archive,out,dotnet):
    archive=Path(archive);out=Path(out);tx.require(not out.exists(),'Fresh build output required')
    out.mkdir(parents=True);source=out/'source'
    with zipfile.ZipFile(archive) as z:
        for entry in z.infolist():
            if entry.is_dir():continue
            p=tx.under(source,entry.filename);p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(z.read(entry))
    main=source/'BotAimImprover.cs';old=main.read_text(encoding='utf-8-sig');text=old
    def replace(a,b):
        nonlocal text
        tx.require(text.count(a)==1,'Aim patch anchor mismatch: '+a[:80]);text=text.replace(a,b)
    replace('public class BotAimImprover : BasePlugin','public partial class BotAimImprover : BasePlugin')
    replace('ts: 0x5994, en: 0x5A00, vis: 0x5A04, pbot: 0x14F8,','ts: 0x5994, en: 0x5A00, vis: 0x5A04, pbot: 0x1500,')
    replace('        AddCommand("bot_aim",','        RegisterLocalKitDiagnostics();\n        AddCommand("bot_aim",')
    replace('            Server.PrintToConsole(reply);','            if (arg is "head" or "body" or "mixed") SetLocalKitMode(arg);\n            info.ReplyToCommand(reply);\n            Server.PrintToConsole(reply);')
    replace('AimMode.HEAD => wpn == "weapon_awp" ? _priorityTrunk : _priorityHead,','AimMode.HEAD => _priorityHead,')
    replace('            IntPtr pCCSBot = hook.GetParam<IntPtr>(0);','            _hookCalls++;\n            IntPtr pCCSBot = hook.GetParam<IntPtr>(0);')
    replace('            if (ReadByte(pCCSBot + _off.IsVisible) == 0)\n                return HookResult.Continue;','            if (ReadByte(pCCSBot + _off.IsVisible) == 0)\n            { _visibilityMisses++; return HookResult.Continue; }')
    replace('            if (botController == null || !TryGetBotEyePosition(botController, out var botEye))\n                return HookResult.Continue;','            if (botController == null || !TryGetBotEyePosition(botController, out var botEye))\n            { _controllerMisses++; return HookResult.Continue; }')
    replace('            // One-time confirmation that the override path actually runs end-to-end.','            RecordLocalKitWrite(pCCSBot,chosenIdx,wpn,botController.Slot,botController.UserId,enemyIdx,rx,ry,rz);\n            // One-time confirmation that the override path actually runs end-to-end.')
    main.write_text(text,encoding='utf-8',newline='\n')
    extra=tx.REPO/'experiments/aim-14192/LocalKitDiagnostics.cs'
    (source/extra.name).write_bytes(extra.read_bytes())
    diff=list(difflib.unified_diff(old.splitlines(True),text.splitlines(True),fromfile='a/BotAimImprover.cs',tofile='b/BotAimImprover.cs'))
    diff+=list(difflib.unified_diff([],extra.read_text().splitlines(True),fromfile='/dev/null',tofile='b/'+extra.name))
    (out/'aim-integration.patch').write_text(''.join(diff),encoding='utf-8',newline='\n')
    env=os.environ.copy();env.update(DOTNET_CLI_HOME=str(out/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1')
    for i,args in enumerate([
        ['restore',str(source/'BotAimImprover.csproj'),'--source',str(Path.home()/'.nuget/packages'),'-p:NuGetAudit=false','-p:RestoreFallbackFolders='],
        ['build',str(source/'BotAimImprover.csproj'),'-c','Release','--no-restore','-p:SourceRevisionId=d796572+14192-integration']]):
        p=subprocess.run([dotnet,*args],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (out/f'build-{i}.log').write_bytes(p.stdout);tx.require(p.returncode==0,'Aim build failed: '+str(out/f'build-{i}.log'))
    with zipfile.ZipFile(out/'BotAimImprover-integrated-source.zip','x',zipfile.ZIP_DEFLATED) as z:
        for p in source.rglob('*'):
            if p.is_file() and not {'bin','obj'} & set(p.relative_to(source).parts):z.write(p,p.relative_to(source))
    result={'originalArchiveSha256':tx.sha(archive),'dll':str(source/'bin/Release/net10.0/BotAimImprover.dll'),'dllSha256':tx.sha(source/'bin/Release/net10.0/BotAimImprover.dll'),'sourceSha256':tx.sha(out/'BotAimImprover-integrated-source.zip'),'gameplay':'pending'}
    (out/'build-result.json').write_bytes(tx.json_bytes(result));print(tx.json_bytes(result).decode())

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['archive','output','dotnet']:p.add_argument('--'+name,required=True)
    a=p.parse_args();build(a.archive,a.output,a.dotnet)
