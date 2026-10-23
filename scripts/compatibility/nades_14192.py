"""Exact NadeSystem layer over the existing installed 1.41.9.2 recovery.

One DLL replacement and 48 new JSON files; old data, both parents and Human stay
immutable. All actions preview by default. No game launch or Steam writes.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import sys
import uuid

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx

PREFIX = 'game/csgo/addons/counterstrikesharp/plugins/NadeSystem/'
DLL = PREFIX + 'NadeSystem.dll'
OLD_DATA = 'game/csgo/addons/counterstrikesharp/data/NadeSystem/grenades/'
MARKER = 'game/csgo/.cs2-local-kit-nades-14192.json'
COMPAT_MARKER = 'game/csgo/.cs2-local-kit-compat-14192.json'
BUILD = {'PatchVersion':'1.41.9.2','ClientVersion':'2000935','buildId':'26144732'}


def steam_identity(root):
    # LastPlayed/LastOwner are not installation identity. Idle/transfer guards
    # still run; build, app, installation path and all game binaries remain pinned.
    tx.steam_idle(root)
    text=(root.parent.parent/'appmanifest_730.acf').read_text(encoding='utf-8-sig')
    result={}
    for key in ['appid','buildid','installdir']:
        values=re.findall(r'"'+key+r'"\s+"([^"\r\n]*)"',text)
        tx.require(len(values)==1,'Missing/ambiguous Steam identity: '+key)
        result[key]=values[0]
    tx.require(result=={'appid':'730','buildid':BUILD['buildId'],'installdir':root.name},'Steam installation/build mismatch')
    return result


def load(release, expected, fake=False):
    release = tx.no_links(release)
    tx.require(re.fullmatch('[0-9a-f]{64}', expected or '') and tx.sha(release/'nade-manifest.json') == expected, 'Exact manifest digest mismatch')
    m = tx.read_json(release/'nade-manifest.json')
    tx.require(m['schemaVersion'] == 1 and m['lane'] == 'nades-14192' and m['targetBuild'] == BUILD, 'Wrong lane/build')
    tx.require(m['botRef'] == tx.BOT_REF and m['difficulty'] == 'Medium' and m['humanPolicy'] == 'preserve', 'Wrong joint baseline')
    tx.require(bool(m.get('testFixture')) == fake, 'Fake/production mismatch')
    actual = {p:v['sha256'] for p,v in tx.inventory(release).items() if v['kind']=='file' and p not in {'nade-manifest.json','MANIFEST.sha256'}}
    tx.require(actual == m['bundleFiles'], 'Complete bundle file set/hash drift')
    changes = m['changes']
    tx.require(len(changes) == 49 and DLL in changes, 'Exactly one DLL and 48 data files required')
    tx.require(set(m['gameBinaries']) == tx.GAME_BINARIES, 'Incomplete game binary identity')
    for rel,entry in changes.items():
        tx.relative(rel)
        if rel != DLL:
            tx.require(rel.startswith(PREFIX+'grenades/') and re.fullmatch(r'(?:de|cs)_[a-z0-9]+_(flash|he|molotov|smoke)\.json', rel[len(PREFIX+'grenades/'):]), 'Out-of-scope target')
            old = OLD_DATA + Path(rel).name
            tx.require(m['baselineFiles'].get(old) == entry['sha256'] and rel not in m['baselineFiles'], 'Data must be identical copies of original protected JSON')
        else:
            tx.require(rel in m['baselineFiles'] and entry['sha256'] != m['baselineFiles'][rel], 'Missing original DLL identity')
        tx.require(tx.sha(tx.under(release,entry['source'])) == entry['sha256'], 'Payload hash mismatch')
    tx.require(set(m['markers']) == {tx.MARKER,COMPAT_MARKER}, 'Both parent markers required')
    tx.require(set(m['parents']) == {'bot','compat'}, 'Both parent transactions required')
    return release,m


def context(m,root,backups,fake):
    tx.ensure_closed()
    root = tx.validate_root(root,fake); backups = tx.no_links(backups)
    tx.require(str(root) == m['cs2Root'], 'Wrong game root')
    tx.require(root.drive.casefold() == backups.drive.casefold(), 'Backup must share game volume')
    for parent in m['parents'].values():
        folder = tx.no_links(parent['backupDir'])
        for other in [root,folder]:
            tx.require(not backups.is_relative_to(other) and not other.is_relative_to(backups), 'Backup overlaps game/parent')
        tx.require(tx.inventory(folder) == parent['inventory'], 'Parent transaction or backup changed')
        r = tx.read_json(folder/parent['recordName'])
        tx.require(r['state']=='installed' and r['installId']==parent['installId'] and r['cs2Root']==str(root), 'Parent is not the exact active installed transaction')
        marker = tx.read_json(root/parent['marker'])
        tx.require(marker['installId']==r['installId'] and marker['manifestSha256']==r['manifestSha256'], 'Parent marker/record mismatch')
    for p,v in m['markers'].items(): tx.require(tx.identity(root/p)==v,'Parent marker drift')
    tx.require(steam_identity(root)==m['steamIdentity'],'Steam/build changed')
    return root,backups


def images(m,root,state='before'):
    partial = state=='partial'
    for p,h in m['baselineFiles'].items():
        allowed = [{'kind':'file','sha256':h}]
        if p==DLL:
            after = {'kind':'file','sha256':m['changes'][p]['sha256']}
            allowed = allowed+[after] if partial else [after] if state=='after' else allowed
        tx.require(tx.identity(root/p) in allowed, 'Protected file drift: '+p)
    for p,e in m['changes'].items():
        if p==DLL: continue
        allowed = [None] if state=='before' else [None,{'kind':'file','sha256':e['sha256']}] if partial else [{'kind':'file','sha256':e['sha256']}]
        tx.require(tx.identity(tx.under(root,p)) in allowed,'New data target drift: '+p)
    actual = tx.module_inventory(root)
    for folder,rows in actual.items():
        for p in list(rows):
            rel=folder+'/'+p
            if rel not in m['changes']: continue
            if rel==DLL: rows[p]={'kind':'file','sha256':m['baselineFiles'][DLL]}
            else: del rows[p]
    tx.require(actual==m['moduleInventory'],'Module file/directory inventory drift')
    for p,v in m['markers'].items(): tx.require(tx.identity(root/p)==v,'Parent marker drift')


def save(backup,record):
    next_path=backup/'nade-record.next.json'
    next_path.write_bytes(tx.json_bytes(record));os.replace(next_path,backup/'nade-record.json')


def restore(release,expected,root,backups,install_id,apply=False,fake=False,fault=None):
    release,m=load(release,expected,fake);root,backups=context(m,root,backups,fake)
    tx.require(re.fullmatch('[0-9a-f]{32}',install_id or ''),'Exact new Nade install ID required')
    backup=tx.under(backups,install_id);r=tx.read_json(backup/'nade-record.json')
    tx.require(r['installId']==install_id and r['manifestSha256']==expected and r['cs2Root']==str(root),'Nade transaction mismatch')
    tx.require(r['state'] in {'installing','installed','restoring','restore-failed'},'Nade layer is not active')
    tx.require(tx.sha(backup/'nade-manifest.json')==expected,'Backup manifest drift')
    images(m,root,'after' if r['state']=='installed' else 'partial')
    marker=tx.under(root,MARKER);expected_marker=tx.json_bytes({'installId':install_id,'manifestSha256':expected})
    # An interruption before exclusive marker creation can only have unchanged game images.
    missing_marker=not marker.exists() and r['state'] in {'installing','restoring','restore-failed'}
    if missing_marker: images(m,root,'before')
    else: tx.require(marker.is_file() and marker.read_bytes()==expected_marker,'Nade marker mismatch')
    pre=tx.under(backup,'preimages/'+DLL)
    tx.require(tx.sha(pre)==m['baselineFiles'][DLL],'Original Nade DLL backup corrupted')
    tx.require(not fault or fake,'Fault injection is synthetic-only')
    result={'state':'restore-preview','installId':install_id,'backupDir':str(backup),'restoresFiles':49,'parentsPreserved':True}
    if not apply:return result
    r['state']='restoring';save(backup,r)
    try:
        for index,rel in enumerate(sorted(m['changes'])):
            context(m,root,backups,fake)
            target=tx.under(root,rel)
            allowed=[None,{'kind':'file','sha256':m['changes'][rel]['sha256']}] if rel!=DLL else [{'kind':'file','sha256':h} for h in [m['baselineFiles'][DLL],m['changes'][DLL]['sha256']]]
            tx.require(tx.identity(target) in allowed,'Concurrent restore target drift')
            if rel==DLL:
                staged=backup/('restore-'+uuid.uuid4().hex)
                shutil.copyfile(pre,staged);tx.require(tx.sha(staged)==m['baselineFiles'][DLL],'Restore staging drift')
                os.replace(staged,target)
            elif target.exists():target.unlink()
            if fault==index:raise KeyboardInterrupt('Synthetic restore interruption')
        images(m,root,'before')
        if not missing_marker:
            tx.require(marker.read_bytes()==expected_marker,'Nade marker changed');marker.unlink()
        if fault=='after-marker':raise KeyboardInterrupt('Synthetic post-marker restore interruption')
        r['state']='restored';save(backup,r)
    except BaseException:
        r['state']='restore-failed';save(backup,r);raise
    return {**result,'state':'restored'}


def install(release,expected,root,backups,apply=False,fake=False,fault=None):
    release,m=load(release,expected,fake);root,backups=context(m,root,backups,fake)
    tx.require(not release.is_relative_to(root) and not root.is_relative_to(release),'Release/game overlap')
    tx.require(not tx.under(root,MARKER).exists(),'Restore the exact active Nade layer first')
    if backups.exists():
        for p in backups.iterdir():
            tx.no_links(p)
            if p.is_dir() and (p/'nade-record.json').exists():
                r=tx.read_json(p/'nade-record.json')
                tx.require(r['cs2Root']!=str(root) or r['state']=='restored','Unrestored Nade history: '+p.name)
    images(m,root)
    # This candidate uses an already existing, verified empty plugin data directory.
    tx.require(all(tx.under(root,p).parent.is_dir() for p in m['changes']),'Target parent missing')
    tx.require(not fault or fake,'Fault injection is synthetic-only')
    result={'state':'preview','changes':49,'replacedDlls':1,'newDataFiles':48,'difficulty':'Medium','humanPolicy':'preserve',
        'parentInstallIds':{k:v['installId'] for k,v in m['parents'].items()}}
    if not apply:return result
    install_id=uuid.uuid4().hex;backup=tx.under(backups,install_id);backup.mkdir(parents=True)
    shutil.copyfile(release/'nade-manifest.json',backup/'nade-manifest.json')
    pre=tx.under(backup,'preimages/'+DLL);pre.parent.mkdir(parents=True);shutil.copyfile(root/DLL,pre)
    tx.require(tx.sha(pre)==m['baselineFiles'][DLL],'DLL backup mismatch')
    for rel,e in m['changes'].items():
        staged=tx.under(backup,'staging/'+rel);staged.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(tx.under(release,e['source']),staged)
        tx.require(tx.sha(staged)==e['sha256'],'Staging mismatch')
    r={'installId':install_id,'manifestSha256':expected,'cs2Root':str(root),'state':'installing'};save(backup,r)
    if fault=='before-marker':raise KeyboardInterrupt('Synthetic pre-marker interruption')
    images(m,root);context(m,root,backups,fake)
    marker=tx.under(root,MARKER)
    with marker.open('xb') as f:f.write(tx.json_bytes({'installId':install_id,'manifestSha256':expected}))
    try:
        for index,rel in enumerate(sorted(m['changes'])):
            context(m,root,backups,fake)
            tx.require(tx.identity(tx.under(root,rel))==({'kind':'file','sha256':m['baselineFiles'][DLL]} if rel==DLL else None),'Concurrent install target drift')
            os.replace(tx.under(backup,'staging/'+rel),tx.under(root,rel))
            tx.require(tx.sha(root/rel)==m['changes'][rel]['sha256'],'Postimage mismatch')
            if fault==index:raise RuntimeError('Synthetic write failure')
            if fault=='interrupt-'+str(index):raise KeyboardInterrupt('Synthetic install interruption')
        images(m,root,'after')
        if fault=='before-installed-record':raise KeyboardInterrupt('Synthetic install finalization interruption')
        r['state']='installed';save(backup,r)
    except Exception:
        restore(release,expected,root,backups,install_id,True,fake);raise
    return {**result,'state':'installed','installId':install_id,'backupDir':str(backup)}


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['preview','install','restore'])
    for name in ['release','manifest-sha256','root','backup-root']:p.add_argument('--'+name,required=True)
    p.add_argument('--install-id');p.add_argument('--apply',action='store_true');p.add_argument('--fake',action='store_true')
    a=p.parse_args();args=(a.release,a.manifest_sha256,a.root,a.backup_root)
    result=restore(*args,a.install_id,a.apply,a.fake) if a.action=='restore' else install(*args,a.apply and a.action=='install',a.fake)
    print(tx.json_bytes(result).decode())
