"""Actual complete Nade payload; all mutations constrained to explicit fake trees."""
import argparse
import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode=True
import nades_14192 as n
tx=n.tx


def run(candidate,digest,public_only=False):
    candidate,original=n.load(candidate,digest)
    folder=tx.REPO/'temp/nade-tests';folder.mkdir(parents=True,exist_ok=True)
    home=Path(tempfile.mkdtemp(dir=folder));root=home/'temp/steamapps/common/cs2';package=home/'package';backups=home/'backups'
    shutil.copytree(candidate,package)
    def write(p,value):
        p.parent.mkdir(parents=True,exist_ok=True)
        p.write_bytes(value if isinstance(value,bytes) else value.encode())
    for p in original['baselineFiles']:write(root/p,'synthetic protected '+p)
    for folder,rows in original['moduleInventory'].items():
        for p,v in rows.items():
            target=root/folder/p
            if v['kind']=='directory':target.mkdir(parents=True,exist_ok=True)
            elif not target.exists():write(target,'synthetic module '+p)
    for p,e in original['changes'].items():
        if p!=n.DLL:write(root/(n.OLD_DATA+Path(p).name),(candidate/e['source']).read_bytes())
    write(root/'.bot-experiment-fake-root','synthetic-test-only')
    write(root/'game/csgo/addons/metamod/metaplugins.ini','; synthetic baseline, no active entries\n')
    steam=root.parent.parent/'appmanifest_730.acf'
    write(steam,'"AppState" { "appid" "730" "StateFlags" "4" "buildid" "26144732" "installdir" "cs2" "LastPlayed" "1" }')
    m=copy.deepcopy(original);m['testFixture']=True;m['cs2Root']=str(root)
    for key,parent in m['parents'].items():
        p=home/'parents'/key/parent['installId'];parent['backupDir']=str(p)
        manifest=p/('candidate-manifest.json' if key=='bot' else 'recovery-manifest.json')
        write(manifest,'immutable '+key+' source');manifest_sha=tx.sha(manifest)
        write(p/parent['recordName'],tx.json_bytes({'state':'installed','installId':parent['installId'],'cs2Root':str(root),'manifestSha256':manifest_sha}))
        write(p/'preimages/sentinel','original '+key+' backup')
        write(root/parent['marker'],tx.json_bytes({'installId':parent['installId'],'manifestSha256':manifest_sha}))
        parent['inventory']=tx.inventory(p)
    write(home/'private-presets/sentinel.json','personal data')
    m['markers']={p:tx.identity(root/p) for p in m['markers']}
    m['baselineFiles']={p:tx.sha(root/p) for p in original['baselineFiles']}
    m['gameBinaries']={p:tx.sha(root/p) for p in tx.GAME_BINARIES}
    m['moduleInventory']=tx.module_inventory(root);m['steamIdentity']=n.steam_identity(root)
    (package/'nade-manifest.json').write_bytes(tx.json_bytes(m));expected=tx.sha(package/'nade-manifest.json')
    args=(package,expected,root,backups)
    before=tx.inventory(root);parents_before=tx.inventory(home/'parents');private_before=tx.inventory(home/'private-presets')
    checks=[]
    def same():
        assert tx.inventory(root)==before,'Game did not restore byte-for-byte'
        assert tx.inventory(home/'parents')==parents_before,'Parent changed'
        assert tx.inventory(home/'private-presets')==private_before,'Preset changed'
    def public(action,apply=False,ident=None,good=True):
        command=['pwsh','-NoProfile','-File',str(package/'tools/Invoke-Nades14192.ps1'),'-Action',action,
            '-ReleaseDir',str(package),'-ManifestSha256',expected,'-Cs2Root',str(root),'-BackupRoot',str(backups),'-AllowFakeTree']
        if apply:command+=['-Apply']
        if ident:command+=['-InstallId',ident]
        p=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace')
        assert (p.returncode==0)==good,p.stdout+p.stderr
        return json.loads(p.stdout) if good else None
    snapshot=tx.inventory(home);public('Preview');assert tx.inventory(home)==snapshot;checks.append('public-preview-zero-writes')
    active=public('Install',True)
    assert all(tx.sha(root/p)==e['sha256'] for p,e in m['changes'].items())
    assert len(list((root/(n.PREFIX+'grenades')).glob('*.json')))==48
    snapshot=tx.inventory(home);public('Restore',ident=active['installId']);assert tx.inventory(home)==snapshot
    checks.append('public-restore-preview-zero-writes')
    public('Restore',True,active['installId']);same();checks.append('public-complete-49-file-roundtrip')
    if public_only:
        for path in [package/next(iter(m['changes'].values()))['source'],package/'unknown-extra.txt']:
            data=path.read_bytes() if path.exists() else None
            write(path,b'package tamper')
            snapshot=tx.inventory(home);public('Preview',good=False);assert tx.inventory(home)==snapshot
            if data is None:path.unlink()
            else:path.write_bytes(data)
            checks.append('public-reject-bundle-tamper-'+path.name)
        result={'status':'PASS','checks':checks,'checkCount':len(checks),'actualPayloadFiles':49,
            'candidateManifestSha256':digest,'bothParentsUnchanged':True,'byteForByteRestored':True,'realTargetApplied':False}
        (tx.REPO/'temp/evidence/final-public-roundtrip.json').write_bytes(tx.json_bytes(result))
        print(tx.json_bytes(result).decode());return
    # Accelerate process discovery only in in-process, positively identified fake
    # tests. Public subprocesses above retain the real CS2/Steam and fake-root guards.
    real_closed=tx.ensure_closed
    tx.ensure_closed=lambda: tx.require(root.is_relative_to(tx.REPO/'temp') and (root/'.bot-experiment-fake-root').read_text()=='synthetic-test-only','Not fake')
    # The two fake parent snapshots are invariant during fault injection. Cache
    # just those exact inventory reads; same() still hashes their whole ancestor
    # tree from disk after EVERY rollback. Public subprocess guard tests below
    # use uncached production code, including Steam, links and parent preimages.
    real_inventory=tx.inventory
    parent_cache={Path(v['backupDir']):real_inventory(Path(v['backupDir'])) for v in m['parents'].values()}
    tx.inventory=lambda p: copy.deepcopy(parent_cache[Path(p)]) if Path(p) in parent_cache else real_inventory(p)
    # In this fault-only phase the harness creates no links and exclusively owns
    # home. Replace repeated ancestor reparse syscalls with a hard fake-home bound;
    # do not mock file reads, hashes, writes, rename, delete or byte-for-byte checks.
    # Public entry tests before/after retain the production reparse/path guards.
    real_no_links=tx.no_links
    def fake_bound(path):
        p=Path(os.path.abspath(path))
        tx.require(p.is_relative_to(home),'Fault test escaped its synthetic home')
        return p
    tx.no_links=fake_bound
    for index in range(49):
        try:n.install(*args,True,True,index)
        except RuntimeError as e:assert 'Synthetic write failure' in str(e)
        else:raise AssertionError('Expected write fault')
        same();checks.append('rollback-after-write-'+str(index))
    for index in range(49):
        try:n.install(*args,True,True,'interrupt-'+str(index))
        except KeyboardInterrupt:pass
        else:raise AssertionError('Expected interruption')
        ident=tx.read_json(root/n.MARKER)['installId']
        n.restore(*args,ident,True,True);same();checks.append('recover-install-interruption-'+str(index))
    try:n.install(*args,True,True,'before-marker')
    except KeyboardInterrupt:pass
    pending=[p for p in backups.iterdir() if tx.read_json(p/'nade-record.json')['state']=='installing']
    assert len(pending)==1
    n.restore(*args,pending[0].name,True,True);same();checks.append('recover-before-marker-interruption')
    try:n.install(*args,True,True,'before-installed-record')
    except KeyboardInterrupt:pass
    pending=[p for p in backups.iterdir() if tx.read_json(p/'nade-record.json')['state']=='installing']
    assert len(pending)==1
    n.restore(*args,pending[0].name,True,True);same();checks.append('recover-install-finalization-interruption')
    # Every restore boundary also supports exact repeat after interruption.
    for index in range(49):
        active=n.install(*args,True,True)
        try:n.restore(*args,active['installId'],True,True,index)
        except KeyboardInterrupt:pass
        else:raise AssertionError('Expected restore interruption')
        n.restore(*args,active['installId'],True,True);same();checks.append('recover-restore-interruption-'+str(index))
    active=n.install(*args,True,True)
    try:n.restore(*args,active['installId'],True,True,'after-marker')
    except KeyboardInterrupt:pass
    n.restore(*args,active['installId'],True,True);same();checks.append('recover-after-restore-marker-removal')
    tx.ensure_closed=real_closed
    tx.inventory=real_inventory
    tx.no_links=real_no_links
    # Rejection is read-only, including target collision, unknown plugin and parents.
    targets=[root/tx.MARKER,root/n.COMPAT_MARKER,root/'game/bin/win64/engine2.dll',root/tx.HUMAN_GAMEDATA,
        Path(m['parents']['compat']['backupDir'])/'preimages/sentinel',Path(m['parents']['bot']['backupDir'])/'preimages/sentinel']
    for target in targets:
        data=target.read_bytes();target.write_bytes(data+b' drift');snapshot=tx.inventory(home)
        public('Install',True,good=False);assert tx.inventory(home)==snapshot
        target.write_bytes(data);same();checks.append('reject-drift-'+target.name)
    for rel in [next(p for p in m['changes'] if p!=n.DLL),'game/csgo/addons/counterstrikesharp/plugins/Unknown/User.dll']:
        target=root/rel;missing=not target.parent.exists();write(target,'unexpected user file');snapshot=tx.inventory(home)
        public('Install',True,good=False);assert tx.inventory(home)==snapshot
        target.unlink()
        if missing:target.parent.rmdir()
        same();checks.append('reject-collision-or-unknown-'+Path(rel).name)
    original_steam=steam.read_bytes()
    for old,new in [('"StateFlags" "4"','"StateFlags" "6"'),('"26144732"','"99999999"')]:
        write(steam,original_steam.decode().replace(old,new));snapshot=tx.inventory(home)
        public('Install',True,good=False);assert tx.inventory(home)==snapshot
        steam.write_bytes(original_steam);checks.append('reject-steam-'+new)
    active=public('Install',True);snapshot=tx.inventory(home);public('Install',True,good=False);assert tx.inventory(home)==snapshot;checks.append('reject-double-install')
    pre=Path(active['backupDir'])/'preimages'/n.DLL;data=pre.read_bytes();pre.write_bytes(b'corrupt');snapshot=tx.inventory(home)
    public('Restore',True,active['installId'],good=False);assert tx.inventory(home)==snapshot;pre.write_bytes(data);checks.append('reject-corrupt-preimage')
    p=root/n.DLL;data=p.read_bytes();p.write_bytes(b'user edit');snapshot=tx.inventory(home)
    public('Restore',True,active['installId'],good=False);assert tx.inventory(home)==snapshot;p.write_bytes(data);checks.append('reject-restore-dll-drift')
    snapshot=tx.inventory(home);public('Restore',True,m['parents']['compat']['installId'],good=False);assert tx.inventory(home)==snapshot;checks.append('reject-parent-id-as-nade-id')
    # Normal game metadata can change; no guard on original transactions is edited.
    write(steam,original_steam.decode().replace('"LastPlayed" "1"','"LastPlayed" "999"'))
    public('Restore',ident=active['installId']);public('Restore',True,active['installId']);same()
    steam.write_bytes(original_steam);checks.append('restore-after-benign-steam-metadata-change')
    exe=root/'game/bin/win64/cs2.exe';data=exe.read_bytes();exe.write_bytes(b'MZsynthetic');snapshot=tx.inventory(home)
    public('Install',True,good=False);assert tx.inventory(home)==snapshot;exe.write_bytes(data);same();checks.append('fake-mode-rejects-pe')
    result={'status':'PASS','checks':checks,'checkCount':len(checks),'actualPayloadFiles':49,'protectedFiles':len(m['baselineFiles']),
        'bothParentsUnchanged':True,'personalPresetsUnchanged':True,'byteForByteRestored':True,
        'candidateManifestSha256':digest,'realTargetApplied':False}
    (tx.REPO/'temp/evidence/nade-transaction-tests.json').write_bytes(tx.json_bytes(result))
    print(json.dumps({k:v for k,v in result.items() if k!='checks'},indent=2))


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--candidate',required=True);p.add_argument('--manifest-sha256',required=True)
    p.add_argument('--public-only',action='store_true',help='Recheck final packaging with identical previously fault-tested runtime/payload')
    a=p.parse_args();run(a.candidate,a.manifest_sha256,a.public_only)
