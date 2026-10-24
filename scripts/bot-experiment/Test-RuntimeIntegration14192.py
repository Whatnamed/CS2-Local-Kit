"""Actual delivered payload through the public entry, synthetic protected baseline only."""
import argparse,copy,json,shutil,subprocess,sys,tempfile
from pathlib import Path
sys.dont_write_bytecode=True
import bot_experiment as tx

def write(path,value):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(value if isinstance(value,bytes) else value.encode())

def fixture(candidate,digest):
    candidate,original=tx.load_candidate(candidate,digest)
    home=Path(tempfile.mkdtemp(dir=tx.REPO/'temp/integration',prefix='roundtrip-'))
    root=home/'temp/steamapps/common/cs2';package=home/'package';backups=home/'backups/bot-experiment'
    for rel in original['bundleFiles']:
        if rel.endswith('.zip'):continue
        write(package/rel,(candidate/rel).read_bytes())
    for rel in original['baselineFiles']:write(root/rel,'synthetic protected '+rel)
    for folder,rows in original['moduleInventory'].items():
        if folder=='valveServerPluginDeclarations':continue
        for path,value in rows.items():
            if value['kind']=='directory':(root/folder/path).mkdir(parents=True,exist_ok=True)
    write(root/'.bot-experiment-fake-root','synthetic-test-only')
    write(root/'game/csgo/addons/metamod/metaplugins.ini','; synthetic empty\n')
    write(root/'game/csgo/gameinfo.gi','SearchPaths\n{\n Game csgo/overrides/botprofile.vpk\n Game csgo/addons/metamod\n Game csgo\n}\n')
    write(root/'game/csgo/steam.inf','PatchVersion=1.41.9.2\nClientVersion=2000935\n')
    write(root.parent.parent/'appmanifest_730.acf','"appid" "730" "buildid" "26144732" "StateFlags" "4" "installdir" "cs2" "LastPlayed" "1"')
    m=copy.deepcopy(original);m['testFixture']=True;c=m['runtimeIntegration'];c['cs2Root']=str(root)
    for name,p in c['parents'].items():
        folder=(backups if name=='bot' else home/'backups'/name)/p['installId']
        write(folder/p['manifestName'],b'synthetic parent manifest')
        pd=tx.sha(folder/p['manifestName'])
        record={'state':'installed','installId':p['installId'],'cs2Root':str(root),'manifestSha256':pd,'humanPolicy':'preserve'}
        write(folder/p['recordName'],tx.json_bytes(record));write(folder/'preimages/sentinel','parent backup '+name)
        write(root/p['marker'],tx.json_bytes({'installId':p['installId'],'manifestSha256':pd,'backupRoot':str(backups),'humanPolicy':'preserve'}))
        p.update(backupDir=str(folder),inventory=tx.inventory(folder),markerIdentity=tx.identity(root/p['marker']))
    m['baselineFiles']={p:tx.sha(root/p) for p in m['baselineFiles']}
    m['gameBinaries']={p:m['baselineFiles'][p] for p in tx.GAME_BINARIES}
    m['moduleInventory']=tx.module_inventory(root);m['coexistence']['humanFiles']={p:h for p,h in m['baselineFiles'].items() if tx.human_path(p)}
    m['bundleFiles']={p:v['sha256'] for p,v in tx.inventory(package).items() if v['kind']=='file'}
    write(package/'experiment-manifest.json',tx.json_bytes(m));expected=tx.sha(package/'experiment-manifest.json')
    return home,root,package,backups,m,expected

def run(candidate,digest,faults=False):
    tx.MARKER=tx.INTEGRATION_MARKER
    home,root,package,backups,m,expected=fixture(candidate,digest)
    args=(package,expected,root,backups);checks=[]
    before=tx.inventory(root);parents={p['backupDir']:tx.inventory(p['backupDir']) for p in m['runtimeIntegration']['parents'].values()}
    def public(action,apply=False,id=None):
        command=['pwsh','-NoProfile','-File',str(package/'tools/Invoke-BotExperiment.ps1'),'-Action',action,'-BackupRoot',str(backups),'-AllowFakeTree']
        if id:command+=['-InstallId',id]
        else:command+=['-ReleaseDir',str(package),'-ManifestSha256',expected,'-Cs2Root',str(root),'-PreserveHumanCosmetics']
        if apply:command+=['-Apply']
        p=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace')
        tx.require(p.returncode==0,p.stdout+p.stderr);return json.loads(p.stdout)
    tree=tx.inventory(home);public('Preview');tx.require(tx.inventory(home)==tree,'Preview wrote files');checks.append('public preview zero writes')
    installed=public('Install',True);id=installed['installId'];backup=backups/id
    tx.require(all(tx.sha(root/e['target'])==e['sha256'] for e in m['files']),'Payload identity failed');checks.append('actual full payload installed')
    tree=tx.inventory(home);public('Restore',id=id);tx.require(tx.inventory(home)==tree,'Restore preview wrote files');checks.append('restore preview zero writes')
    public('Diagnostics',id=id);checks.append('exact diagnostics')
    # LastPlayed must be harmless between operations, while in-flight raw-byte changes remain blocked.
    acf=root.parent.parent/'appmanifest_730.acf';acf.write_text(acf.read_text().replace('"1"','"2"'))
    public('Restore',id=id);checks.append('noncritical Steam metadata drift accepted')
    public('Restore',True,id);tx.require(tx.inventory(root)==before,'Roundtrip did not restore paths/types/hashes');checks.append('exact playable baseline restored')
    for p,rows in parents.items():tx.require(tx.inventory(p)==rows,'Parent backup changed')
    checks.append('three parent records/backups unchanged')
    # All guards fail before writes. Each case is reverted only in this synthetic tree.
    def reject(label,path,value):
        old=path.read_bytes() if path.exists() else None;write(path,value);state=tx.inventory(home)
        try:tx.install(*args,fake=True,preserve_human=True)
        except (ValueError,FileNotFoundError):pass
        else:raise AssertionError('Guard accepted '+label)
        tx.require(tx.inventory(home)==state,'Rejected preview wrote data')
        if old is None:path.unlink()
        else:path.write_bytes(old)
        checks.append('reject '+label)
    reject('game binary drift',root/'game/bin/win64/cs2.exe',b'changed')
    reject('unknown plugin',root/'game/csgo/addons/counterstrikesharp/plugins/Alien.dll',b'unknown')
    reject('parent record drift',Path(m['runtimeIntegration']['parents']['compat']['backupDir'])/'recovery-record.json',b'{}')
    reject('parent backup drift',Path(m['runtimeIntegration']['parents']['nades']['backupDir'])/'preimages/sentinel',b'bad')
    reject('Human drift',root/tx.HUMAN_FIXTURE,b'unknown private edit')
    reject('Steam build drift',acf,acf.read_text().replace('26144732','26144733'))
    reject('Steam busy',acf,acf.read_text().replace('"4"','"6"'))
    preview=tx.install(*args,fake=True,preserve_human=True)
    changing=[i+1 for i,e in enumerate(preview['entries']) if e['action']!='retained']
    if faults:
        for index in [0,*changing]:
            try:tx.install(*args,apply=True,fake=True,preserve_human=True,fault='write-'+str(index))
            except RuntimeError:pass
            else:raise AssertionError('Failure hook not reached')
            tx.require(tx.inventory(root)==before,'Failure rollback mismatch');checks.append('rollback write '+str(index))
            try:tx.install(*args,apply=True,fake=True,preserve_human=True,fault='interrupt-'+str(index))
            except KeyboardInterrupt:pass
            else:raise AssertionError('Interruption hook not reached')
            active=[p for p in backups.iterdir() if (p/'install-record.json').exists() and tx.read_json(p/'install-record.json')['state']=='installing']
            tx.require(len(active)==1,'Expected one interrupted overlay')
            tx.restore(active[0],True,True);tx.require(tx.inventory(root)==before,'Interrupted install restore mismatch');checks.append('recover install interruption '+str(index))
        result=tx.install(*args,apply=True,fake=True,preserve_human=True)
        b=backups/result['installId'];record=tx.read_json(b/'install-record.json')
        # One fresh installation per actual restore mutation.
        restore_indices=[i for i,e in enumerate(reversed(record['entries'])) if e['action']!='retained']
        tx.restore(b,True,True)
        for index in restore_indices:
            result=tx.install(*args,apply=True,fake=True,preserve_human=True);b=backups/result['installId']
            try:tx.restore(b,True,True,fault=index)
            except KeyboardInterrupt:pass
            else:raise AssertionError('Restore interruption hook not reached')
            tx.restore(b,True,True);tx.require(tx.inventory(root)==before,'Interrupted restore mismatch');checks.append('recover restore interruption '+str(index))
    result={'status':'PASS','candidate':str(candidate),'manifestSha256':digest,'payloadFiles':len(m['files']),'profile':m['profile']['difficulty'],'checks':checks,'syntheticRoot':str(root),'actualGameModified':False,'gameplayAcceptance':'pending'}
    output=tx.REPO/'temp/integration'/('transactions-'+m['profile']['difficulty']+'.json');output.write_bytes(tx.json_bytes(result));print(tx.json_bytes(result).decode())

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--candidate',required=True);p.add_argument('--manifest-sha256',required=True);p.add_argument('--faults',action='store_true')
    a=p.parse_args();run(a.candidate,a.manifest_sha256,a.faults)
