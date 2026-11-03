"""Exercise the reviewed transition on a synthetic installation, never real CS2."""
import copy
import importlib.util
import sys
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'bot-experiment'))
import bot_experiment as tx
spec=importlib.util.spec_from_file_location('integration_tests',tx.REPO/'scripts/bot-experiment/Test-RuntimeIntegration14192.py')
old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)


def run(review_name='review-14193.json'):
    approved=tx.read_json(tx.REPO/'scripts/compatibility'/review_name)
    catalog=tx.read_json(tx.REPO/'src/CS2LocalKit.App/BotCandidates.json')
    candidate=next(c for c in catalog if c['ManifestSha256'] in approved['sourceManifests'])
    source=Path('E:/CS2MOD/releases/bot-experiment')/candidate['Directory']
    digest=candidate['ManifestSha256']
    home,root,package,backups,m,expected=old.fixture(source,digest)
    tx.MARKER=tx.INTEGRATION_MARKER
    installed=tx.install(package,expected,root,backups,apply=True,fake=True,preserve_human=True)
    backup=backups/installed['installId']
    original_record=tx.read_json(backup/'install-record.json')
    frozen=tx.sha(backup/'candidate-manifest.json')
    parent_images={p['backupDir']:tx.inventory(p['backupDir']) for p in m['runtimeIntegration']['parents'].values()}
    review=copy.deepcopy(approved)
    review['sourceManifests']=[expected]
    before=tx.inventory(root)
    for p,v in review['retainedFiles'].items():
        v['before']=tx.sha(root/p)
        old.write(root/p, ('updated binary '+p) if p!='game/csgo/steam.inf' else f"PatchVersion={review['targetBuild']['PatchVersion']}\nClientVersion={review['targetBuild']['ClientVersion']}\n")
        v['after']=tx.sha(root/p)
    acf=root.parent.parent/'appmanifest_730.acf'
    acf.write_text(acf.read_text().replace(m['targetBuild']['buildId'],review['targetBuild']['buildId']))
    review['gameBinaries']={p:tx.sha(root/p) for p in tx.GAME_BINARIES}
    read_json=tx.read_json
    def read(path):
        return copy.deepcopy(review) if Path(path).name==review_name else read_json(path)
    checks=[]
    with patch.object(tx,'read_json',side_effect=read):
        tree=tx.inventory(home);tx.restore(backup,fake=True)
        assert tree==tx.inventory(home);checks.append('cross-build Restore preview writes nothing')
        def reject(label,path,data):
            original=path.read_bytes();path.write_bytes(data);state=tx.inventory(home)
            try:
                try:tx.restore(backup,True,True)
                except ValueError:pass
                else:raise AssertionError('Accepted '+label)
                assert tx.inventory(home)==state
            finally:path.write_bytes(original)
            checks.append('reject '+label+' before writes')
        reject('unreviewed binary',root/'game/csgo/bin/win64/server.dll',b'unknown binary')
        reject('unreviewed gamedata',root/'game/csgo/addons/counterstrikesharp/gamedata/gamedata.json',b'{}')
        reject('Human drift',root/tx.HUMAN_FIXTURE,b'private external edit')
        reject('parent backup drift',Path(m['runtimeIntegration']['parents']['compat']['backupDir'])/'preimages/sentinel',b'bad')
        reject('changed integration DLL',root/'game/csgo/addons/counterstrikesharp/plugins/BotAimImprover/BotAimImprover.dll',b'bad DLL')
        reject('Steam busy',acf,acf.read_text().replace('"4"','"6"').encode())
        review['sourceManifests']=[]
        try:tx.restore(backup,fake=True)
        except ValueError:checks.append('unlisted manifest cannot inherit review')
        else:raise AssertionError('Unknown source accepted')
        review['sourceManifests']=[expected]
        # Interrupted restoration must resume using original records and exact old preimages.
        changed=[i for i,e in enumerate(reversed(installed['entries'])) if e['action']!='retained']
        try:tx.restore(backup,True,True,fault=changed[0])
        except KeyboardInterrupt:pass
        else:raise AssertionError('Interruption not reached')
        tx.restore(backup,True,True);checks.append('cross-build interrupted Restore resumes')
        wanted=copy.deepcopy(before)
        for e in installed['entries']:
            if e['action']=='retained':continue
            if e['before'] is None:wanted.pop(e['target'],None)
            else:wanted[e['target']]=e['before']
        saved=tx.read_json(backup/'install-record.json')
        for d in saved['createdDirectories']:wanted.pop(d,None)
        for p,v in review['retainedFiles'].items():wanted[p]={'kind':'file','sha256':v['after']}
        assert tx.inventory(root)==wanted
        assert tx.sha(backup/'candidate-manifest.json')==frozen
        assert saved['entries']==original_record['entries'] and saved['coexistence']==original_record['coexistence']
        assert all(tx.inventory(p)==v for p,v in parent_images.items())
        checks.append('restore preserves updated Valve/CSS files, original manifest, record images, Human and three parents')
    print(tx.json_bytes({'status':'PASS','checks':checks,'syntheticRoot':str(root),'actualGameModified':False}).decode())


if __name__=='__main__':run(sys.argv[1] if len(sys.argv)>1 else 'review-14193.json')
