"""Rebind the unchanged integration payload to reviewed 14193 binaries; no game writes."""
import copy
import shutil
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx


def build(data=Path('E:/CS2MOD')):
    root = Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive')
    tx.MARKER = tx.INTEGRATION_MARKER
    marker = tx.read_json(root / tx.MARKER)
    backup = data / 'backups/bot-experiment' / marker['installId']
    # Verify the active delta, all retained files and all three immutable parents.
    tx.restore(backup)
    record = tx.read_json(backup / 'install-record.json')
    for e in record['entries']:
        if e['action'] != 'retained' and e['before'] is not None:
            tx.require(tx.identity(backup / ('preimages/' + e['target'])) == e['before'], 'Active preimage drift')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    tx.require(subprocess.run(['git','diff','--quiet'],cwd=tx.REPO).returncode==0 and subprocess.run(['git','diff','--cached','--quiet'],cwd=tx.REPO).returncode==0, 'Commit before packaging')
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    review = tx.REPO / 'scripts/compatibility/review-14193.json'
    approved = tx.read_json(review)
    results = []
    for c in catalog:
        if c['ManifestSha256'] not in approved['sourceManifests']: continue
        source, original = tx.load_candidate(data / 'releases/bot-experiment' / c['Directory'], c['ManifestSha256'])
        m = tx.reviewed_manifest(source / 'experiment-manifest.json', root)
        tx.require(m['baselineFiles'] == tx.reviewed_manifest(backup / 'candidate-manifest.json',root)['baselineFiles'], 'Source substrate differs')
        m.pop('reviewedRetainedFiles')
        out = data / 'releases/bot-experiment' / ('bot-integrated-14193-' + m['profile']['difficulty'].lower() + '-' + head[:7])
        tx.require(not out.exists(), 'Immutable candidate already exists')
        shutil.copytree(source, out)
        m['builderHead'] = head
        m['compatibility14193'] = {'sourceManifestSha256':c['ManifestSha256'],'reviewSha256':tx.sha(review),'sourceHead':head,'realGameAccepted':False}
        # Keep current Windows-identical CSS gamedata, including its upstream Linux fixes.
        for e in m['files']:
            if e['target'].endswith('/gamedata/gamedata.json'):
                shutil.copyfile(root / e['target'], out / e['source']); e['sha256'] = tx.sha(out / e['source'])
        for name in ['bot_experiment.py','runtime_integration.py','Invoke-BotExperiment.ps1']:
            shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, out / 'tools' / name)
        wrapper = out / 'tools/Invoke-BotExperiment.ps1'
        wrapper.write_text(wrapper.read_text(encoding='utf-8-sig').replace("'bot_experiment.py'","'runtime_integration.py'"),encoding='utf-8')
        shutil.copyfile(review,out / 'tools/review-14193.json')
        evidence = {'status':'ready-for-manual-test','targetBuild':m['targetBuild'],'gameBinaries':m['gameBinaries'],
                    'sourceManifestSha256':c['ManifestSha256'],'reviewSha256':tx.sha(review),'realGameAccepted':False,
                    'scope':'123 unique Windows signatures; unchanged Aim offsets; unchanged runtime payload except Windows-identical CSS gamedata. See archived review.'}
        (out/'integration-review.json').write_bytes(tx.json_bytes(evidence))
        m['coexistence']['evidenceSha256'] = tx.sha(out/'integration-review.json')
        (out/'README.md').write_text('''# CS2 1.41.9.3 integration candidate

The 14192 Bot/Human/Nade/MatchSetup payload is retained. The build and binary identities are newly verified, not edits to any previous manifest or installation record.
Use the matching new Controller for read-only preparation previews. Its reviewed tools can withdraw the old integration delta before a fresh full target preflight; three parent transactions and personal Human data remain protected. Install/Restore require separate user authorization. Agents must not start CS2.
The public tools/Invoke-BotExperiment.ps1 Preview entry requires the restored three-layer substrate. While the old integration is active, use the Controller preparation preview or the new tools' Restore without -Apply with its exact install ID.
Real gameplay, Low/High switching and real Restore remain unaccepted. Original 14192 tools remain frozen and reject this build.
''',encoding='utf-8')
        m['bundleFiles'] = {p:v['sha256'] for p,v in tx.inventory(out).items() if v['kind']=='file' and p not in {'experiment-manifest.json','MANIFEST.sha256'}}
        (out/'experiment-manifest.json').write_bytes(tx.json_bytes(m))
        digest = tx.sha(out/'experiment-manifest.json')
        (out/'MANIFEST.sha256').write_text(digest+'  experiment-manifest.json\n')
        tx.load_candidate(out,digest)
        results.append({**copy.deepcopy(c),'Directory':out.name,'ManifestSha256':digest})
    tx.require(len(results)==3,'Expected exactly three reviewed sources')
    target = tx.REPO / 'temp/compat-14193/candidates.json'
    target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(tx.json_bytes(results))
    print(tx.json_bytes(results).decode())


if __name__ == '__main__': build()
