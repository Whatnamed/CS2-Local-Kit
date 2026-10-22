"""Full actual-payload roundtrip through the public PowerShell entry, in a synthetic tree."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

import bot_experiment as tx


def write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(data if isinstance(data,bytes) else data.encode())


def run(candidate,manifest_sha):
    candidate,m=tx.load_candidate(candidate,manifest_sha)
    temp=tx.REPO/'temp/bot-experiment-tests';temp.mkdir(parents=True,exist_ok=True)
    home=Path(tempfile.mkdtemp(prefix='actual-payload-',dir=temp))
    root=home/'temp/steamapps/common/cs2';release=home/'synthetic-candidate';release.mkdir()
    # Verify the complete delivered bundle first. Duplicate its actual runtime payload;
    # source archives stay at the verified original release, no private baseline bytes are copied.
    for rel in m['bundleFiles']:
        if rel.endswith('.zip') and rel.startswith('sources/'):continue
        write(release/rel,(candidate/rel).read_bytes())
    for rel in m['baselineFiles']:
        write(root/rel,'synthetic accepted preimage: '+rel)
    for rel,items in m['moduleInventory'].items():
        if rel=='valveServerPluginDeclarations':continue
        for path,value in items.items():
            if value['kind']=='directory':(root/rel/path).mkdir(parents=True,exist_ok=True)
    write(root/'.bot-experiment-fake-root','synthetic-test-only')
    write(root/'game/csgo/addons/metamod/metaplugins.ini','; synthetic baseline, no active modules\n')
    write(root/'game/csgo/gameinfo.gi','GameInfo\n{\n FileSystem\n {\n  SearchPaths\n  {\n   Game csgo/addons/metamod\n   Game csgo\n  }\n }\n}\n')
    write(root/'game/csgo/steam.inf',''.join(k+'='+v+'\n' for k,v in m['targetBuild'].items() if k!='buildId'))
    write(root.parent.parent/'appmanifest_730.acf','"AppState" { "StateFlags" "4" "buildid" "'+m['targetBuild']['buildId']+'" }')
    write(root/'game/csgo/addons/counterstrikesharp/plugins/UnknownPlugin/User.dll','unknown plugin sentinel')
    write(root/'game/csgo/addons/metamod/UnknownModule.vdf','"Metamod Plugin" { "file" "addons/User/bin/User" }')
    write(home/'private-presets/personal.json','private preset sentinel')
    m['testFixture']=True
    m['baselineFiles']={rel:tx.sha(root/rel) for rel in m['baselineFiles']}
    for rel in ['game/csgo/addons/counterstrikesharp/plugins/UnknownPlugin/User.dll','game/csgo/addons/metamod/UnknownModule.vdf']:m['baselineFiles'][rel]=tx.sha(root/rel)
    m['gameBinaries']={rel:tx.sha(root/rel) for rel in m['gameBinaries']}
    m['moduleInventory']=tx.module_inventory(root)
    joint=m.get('humanPolicy')=='preserve'
    if joint:
        m['coexistence']['humanFiles']={p:h for p,h in m['baselineFiles'].items() if tx.human_path(p)}
        # Synthetic unknown-module sentinels have explicit synthetic ownership.
        # The actual candidate was verified above; no real manifest is modified.
        isolated=set(m['coexistence']['isolatedModules'])|{'game/csgo/addons/counterstrikesharp/plugins/UnknownPlugin','game/csgo/addons/metamod/UnknownModule.vdf'}
        m['coexistence']['isolatedModules']={p:tx.identity(root/p) for p in isolated}
    m['bundleFiles']={rel:v['sha256'] for rel,v in tx.inventory(release).items() if v['kind']=='file'}
    write(release/'experiment-manifest.json',tx.json_bytes(m));synthetic_sha=tx.sha(release/'experiment-manifest.json')
    backups=home/'backups';tool=release/'tools/Invoke-BotExperiment.ps1'
    def command(action,apply=False,install_id=None):
        args=['pwsh','-NoProfile','-File',str(tool),'-Action',action,'-BackupRoot',str(backups),'-AllowFakeTree']
        if action in {'Preview','Install'}:args+=['-ReleaseDir',str(release),'-ManifestSha256',synthetic_sha,'-Cs2Root',str(root),'-PreserveHumanCosmetics' if joint else '-IsolateHumanCosmetics']
        else:args+=['-InstallId',install_id]
        if apply:args+=['-Apply']
        p=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace')
        tx.require(p.returncode==0,p.stdout+p.stderr)
        return json.loads(p.stdout)
    before=tx.inventory(root);private=tx.inventory(home/'private-presets');home_before=tx.inventory(home)
    preview=command('Preview');tx.require(tx.inventory(home)==home_before,'Preview wrote files')
    installed=command('Install',True);backup=Path(installed['backupDir'])
    if joint:
        tx.require(all(tx.sha(root/p)==h for p,h in m['coexistence']['humanFiles'].items()),'Human changed during joint installation')
    else:
        tx.require(not (root/'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator').exists(),'Human was not isolated')
    tx.require(all(tx.sha(root/e['target'])==e['sha256'] for e in m['files']),'Actual payload hash mismatch')
    home_before=tx.inventory(home);command('Restore',install_id=installed['installId'])
    tx.require(tx.inventory(home)==home_before,'Restore preview wrote files')
    command('Restore',True,installed['installId'])
    tx.require(tx.inventory(root)==before,'Not byte-for-byte equivalent (paths/types/content)')
    tx.require(tx.inventory(home/'private-presets')==private,'Private preset changed')
    from collections import Counter
    result={'status':'PASS','candidate':str(candidate),'manifestSha256':manifest_sha,'actualPayloadFiles':len(m['files']),'publicEntry':'candidate/tools/Invoke-BotExperiment.ps1','actions':dict(Counter(e['action'] for e in preview['entries'])),'syntheticBaselineFiles':sum(v['kind']=='file' for v in before.values()),'installId':installed['installId'],'backupDir':str(backup),'byteForByteRestored':True,'previewWrites':0,'restorePreviewWrites':0,'realTargetApplied':False}
    result['profile']=m.get('profile')
    result['humanPolicy']=m.get('humanPolicy','isolate')
    write(tx.REPO/'temp/actual-candidate-test.json',tx.json_bytes(result))
    write(tx.REPO/f"temp/actual-candidate-{m.get('defaultDifficulty','Medium')}-test.json",tx.json_bytes(result))
    print(json.dumps(result,indent=2))


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--candidate',required=True);p.add_argument('--manifest-sha256',required=True)
    a=p.parse_args();run(a.candidate,a.manifest_sha256)
