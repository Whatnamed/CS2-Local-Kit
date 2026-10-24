"""Package the exact installed Medium/Human baseline with five reviewed files.

Reads live files and immutable source bundles; writes a new release only.
The original Bot installation record, user presets and fixtures are never copied
into a distributable payload or changed. Supply sealed current-build evidence.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx
import compat_14192 as recovery


def build(root, parent, source, source_sha, native, evidence, evidence_sha, output):
    root = tx.validate_root(root, False); tx.ensure_closed(); steam = tx.steam_idle(root)
    parent = tx.no_links(parent); evidence = tx.no_links(evidence)
    tx.require(tx.sha(evidence) == evidence_sha, 'Review digest mismatch')
    review = tx.read_json(evidence)
    tx.require(review['status'] == 'ready-for-manual-test' and review['realGameAccepted'] is False, 'Review is not a bounded candidate decision')
    tx.require(review['gameBinaries'] == {p:tx.sha(root / p) for p in tx.GAME_BINARIES}, 'Reviewed game changed')
    for rel, expected in review['evidenceFiles'].items():
        tx.require(tx.sha(tx.under(evidence.parent, rel)) == expected, 'Evidence changed: ' + rel)
    source, original = tx.load_candidate(source, source_sha)
    tx.require(original['humanPolicy'] == 'preserve' and original['profile']['difficulty'] == 'Medium', 'Requires frozen Medium joint source')
    record = tx.read_json(parent / 'install-record.json')
    tx.require(record['state'] == 'installed' and record['manifestSha256'] == source_sha and record['cs2Root'] == str(root), 'Wrong parent installation')
    tx.require(tx.sha(parent / 'candidate-manifest.json') == source_sha, 'Parent candidate changed')
    tx.require(record['installId'] == parent.name and record['humanPolicy'] == 'preserve', 'Parent identity/policy mismatch')
    drift = []
    for e in record['entries']:
        if tx.identity(tx.under(root, e['target'])) != e['after']:
            drift.append(e['target'])
        if e['before'] is not None and e['action'] != 'retained':
            tx.require(tx.identity(tx.under(parent, 'preimages/' + e['target'])) == e['before'], 'Parent preimage drift')
    tx.require(set(drift) <= tx.GAME_BINARIES | {'game/csgo/steam.inf'}, 'Unexpected installation drift: ' + str(drift))
    tx.require(tx.module_inventory(root) == record['moduleInventoryAfter'], 'Installed module inventory changed')
    baseline = {e['target']:tx.sha(root / e['target']) for e in record['entries'] if e['after'] is not None and e['after']['kind'] == 'file'}
    for e in original['files']:
        tx.require(tx.sha(root / e['target']) == e['sha256'], 'Original Medium payload changed')
    native = tx.no_links(native)
    tx.require(tx.sha(native) == review['nativeSha256'], 'Unreviewed CSS native DLL')
    output = tx.no_links(output)
    tx.require(not output.exists() and not Path(str(output) + '.zip').exists(), 'Release already exists')
    tx.require(not output.is_relative_to(root) and not root.is_relative_to(output), 'Output overlaps game')
    output.mkdir(parents=True)
    # Complete, already verified Bot payload and corresponding sources, unchanged.
    shutil.copytree(source, output / 'baseline-bot')
    changes = {}
    for rel in sorted(recovery.TARGETS):
        target = tx.under(output, 'payload/' + rel); target.parent.mkdir(parents=True, exist_ok=True)
        names = {
            'game/csgo/addons/counterstrikesharp/gamedata/gamedata.json':'css-gamedata.json',
            'game/csgo/addons/BotVision/gamedata.json':'botvision-gamedata.json',
            'game/csgo/addons/BotVision/bin/win64/BotVision.dll':'botvision-native.dll',
            'game/csgo/addons/counterstrikesharp/plugins/BotAimImprover/BotAimImprover.dll':'botaim-managed.dll',
        }
        src = native if rel.endswith('/counterstrikesharp.dll') else evidence.parent / names[rel]
        shutil.copyfile(src, target)
        changes[rel] = {'source':'payload/' + rel, 'sha256':tx.sha(target)}
        tx.require(changes[rel]['sha256'] == review['payloadSha256'][rel], 'Unreviewed payload: ' + rel)
    for name in ['compat_14192.py', 'Invoke-Recovery14192.ps1']:
        target = output / 'tools' / name; target.parent.mkdir(exist_ok=True)
        shutil.copyfile(Path(__file__).parent / name, target)
    shutil.copyfile(tx.REPO / 'scripts/bot-experiment/bot_experiment.py', output / 'tools/bot_experiment.py')
    shutil.copyfile(Path(__file__).parent / 'Recovery14192.md', output / 'README.md')
    shutil.copyfile(evidence, output / 'review.json')
    for rel in review['evidenceFiles']:
        target = tx.under(output, 'evidence/' + rel); target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(tx.under(evidence.parent, rel), target)
    # GPL native rebuild: complete recursive corresponding source archive,
    # build instructions and license are included, not only an opaque DLL.
    tx.require(tx.sha(evidence.parent / 'css-corresponding-source.zip') == review['sourceArchiveSha256'], 'Corresponding source archive drift')
    shutil.copyfile(evidence.parent / 'css-corresponding-source.zip', output / 'css-corresponding-source.zip')
    tx.require(tx.sha(evidence.parent / 'bot-adapters-corresponding-source.zip') == review['adapterSourceArchiveSha256'], 'Adapter source archive drift')
    shutil.copyfile(evidence.parent / 'bot-adapters-corresponding-source.zip', output / 'bot-adapters-corresponding-source.zip')
    m = {'schemaVersion':1, 'lane':'compat-14192', 'status':'candidate', 'realGameAccepted':False,
         'targetBuild':review['targetBuild'], 'botRef':tx.BOT_REF, 'difficulty':'Medium', 'humanPolicy':'preserve',
         'cs2Root':str(root), 'builderHead':subprocess.check_output(['git','rev-parse','HEAD'], cwd=tx.REPO, text=True).strip(),
         'sourceManifestSha256':source_sha, 'reviewSha256':evidence_sha,
         'parent':{'installId':record['installId'], 'backupDir':str(parent), 'inventory':tx.inventory(parent)},
         'steamManifestSha256':steam, 'baselineFiles':baseline, 'gameBinaries':review['gameBinaries'],
         'moduleInventory':tx.module_inventory(root), 'changes':changes,
         'bundleFiles':{p:v['sha256'] for p,v in tx.inventory(output).items() if v['kind']=='file'}}
    (output / 'recovery-manifest.json').write_bytes(tx.json_bytes(m))
    digest = tx.sha(output / 'recovery-manifest.json')
    (output / 'MANIFEST.sha256').write_text(digest + '  recovery-manifest.json\n')
    recovery.install(output, digest, root, Path('E:/CS2MOD/backups/compat-14192'))
    tx.require(tx.steam_idle(root) == steam, 'Steam changed during packaging')
    archive = Path(str(output) + '.zip')
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for path, value in tx.inventory(output).items():
            if value['kind'] == 'file': z.write(output / path, path)
    result = {'releaseDir':str(output), 'manifestSha256':digest, 'zip':str(archive), 'zipSha256':tx.sha(archive),
              'parentInstallId':record['installId'], 'retainedBotPayloadFiles':len(original['files']),
              'changedFiles':changes, 'installed':False, 'realGameAccepted':False}
    Path(str(output) + '.artifact.json').write_bytes(tx.json_bytes(result))
    print(tx.json_bytes(result).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    for flag in ['root','parent','source','source-sha','native','evidence','evidence-sha','output']:
        p.add_argument('--' + flag, required=True)
    a = p.parse_args()
    build(a.root, a.parent, a.source, a.source_sha, a.native, a.evidence, a.evidence_sha, a.output)
