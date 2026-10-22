"""Bind a verified e18c029 payload to an exact reviewed build and preserve-Human transaction.

No dependency upgrades, DLL rebuilds, live writes, or floating source selection.
The supplied review is a human-readable compatibility decision, never a runtime acceptance.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import uuid
import zipfile

import bot_experiment as tx


def build(source, source_sha, evidence, evidence_sha, root, releases_root):
    tx.ensure_closed()
    root = tx.validate_root(root, False)
    steam = tx.steam_idle(root)
    source, original = tx.load_candidate(source, source_sha)
    evidence = tx.no_links(evidence)
    tx.require(tx.sha(evidence) == evidence_sha, 'Compatibility review digest mismatch')
    review = tx.read_json(evidence)
    tx.require(review['status'] == 'ready-for-manual-test' and review['botRef'] == tx.BOT_REF,
               'Joint review has not passed')
    tx.require(source_sha in review['sourceManifests'].values(), 'Payload is not covered by this review')
    tx.require(review['gameBinaries'].keys() == tx.GAME_BINARIES, 'Incomplete reviewed binary set')
    for rel, expected in review['gameBinaries'].items():
        tx.require(tx.sha(root / rel) == expected, 'Reviewed game binary drift: ' + rel)
    for rel, expected in review['prerequisiteFiles'].items():
        tx.require(tx.sha(tx.under(root, rel)) == expected, 'Reviewed prerequisite drift: ' + rel)
    for rel, expected in review['evidenceFiles'].items():
        tx.require(tx.sha(tx.under(evidence.parent, rel)) == expected, 'Review evidence drift: ' + rel)
    for lane, count in [('bot', 81), ('css', 28), ('human', 14)]:
        report = tx.read_json(evidence.parent / (lane + '-signatures.json'))
        rows = report['results']
        tx.require(len(rows) == count and all(r['matchCount'] == 1 and
                   all(m.get('expectedOriginalMatches', True) for m in r['matches']) for r in rows),
                   'Signature/original-byte gate failed: ' + lane)
        for binary in report['binaryHashes'].values():
            tx.require(tx.sha(binary['path']) == binary['sha256'], 'Scan binary drift')
    destination = tx.no_links(releases_root) / ('bot-joint-e18c029-' + uuid.uuid4().hex[:12])
    stage = tx.REPO / 'temp/bot-joint-build' / destination.name
    tx.require(not stage.exists() and not destination.exists(), 'Output already exists')
    shutil.copytree(source, stage)
    for name in ['bot_experiment.py', 'Invoke-BotExperiment.ps1']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, stage / 'tools' / name)
    for name in ['README.md', 'MANUAL-TEST.md']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, stage / name)
    evidence_out = stage / 'compatibility'
    evidence_out.mkdir()
    shutil.copyfile(evidence, evidence_out / 'review.json')
    for rel in review['evidenceFiles']:
        target = tx.under(evidence_out, rel)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(tx.under(evidence.parent, rel), target)
    baseline = {}
    for folder in ['addons', 'cfg', 'overrides']:
        start = root / 'game/csgo' / folder
        if not start.exists():
            continue
        for path, value in tx.inventory(start).items():
            if value['kind'] == 'file' and not path.startswith('counterstrikesharp/logs/'):
                baseline['game/csgo/' + folder + '/' + path] = value['sha256']
    baseline.update(review['gameBinaries'])
    for rel in ['game/csgo/gameinfo.gi', 'game/csgo/steam.inf']:
        baseline[rel] = tx.sha(root / rel)
    manifest = {**original, 'targetBuild': review['targetBuild'], 'gameBinaries': review['gameBinaries'],
                'baselineFiles': baseline, 'moduleInventory': tx.module_inventory(root),
                'humanPolicy': 'preserve', 'sourceManifestSha256': source_sha,
                'builderHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip(),
                'excludedModules': [n for n in original['excludedModules'] if n != 'Human cosmetics']}
    human = {p: h for p, h in baseline.items() if tx.human_path(p)}
    tx.require(human == review['humanFiles'], 'Human review must cover the complete current file set')
    manifest['coexistence'] = {'status': 'ready-for-manual-test', 'reviewedBuild': review['targetBuild'],
                              'evidenceSha256': evidence_sha, 'humanFiles': human,
                              'isolatedModules': review['isolatedModules']}
    manifest['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(stage).items()
                               if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
    (stage / 'experiment-manifest.json').write_bytes(tx.json_bytes(manifest))
    manifest_sha = tx.sha(stage / 'experiment-manifest.json')
    (stage / 'MANIFEST.sha256').write_text(manifest_sha + '  experiment-manifest.json\n')
    tx.load_candidate(stage, manifest_sha)
    tx.make_plan(stage, manifest_sha, root, Path('E:/CS2MOD/backups/bot-experiment'), preserve_human=True)
    tx.require(tx.steam_idle(root) == steam, 'Steam changed during candidate assembly')
    tx.ensure_closed()
    tx.require(stage.is_relative_to(tx.REPO / 'temp/bot-joint-build'), 'Stage escaped workspace')
    tx.no_links(stage); tx.no_links(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    os.rename(stage, destination)
    archive = Path(str(destination) + '.zip')
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for p, value in tx.inventory(destination).items():
            if value['kind'] == 'file':
                z.write(destination / p, p)
    result = {'releaseDir': str(destination), 'manifestSha256': manifest_sha,
              'zip': str(archive), 'zipSha256': tx.sha(archive), 'profile': manifest['profile'],
              'sourceManifestSha256': source_sha, 'compatibilityEvidenceSha256': evidence_sha,
              'payloadFileCount': len(manifest['files']), 'installed': False, 'realGameAccepted': False}
    Path(str(destination) + '.artifact.json').write_bytes(tx.json_bytes(result))
    print(tx.json_bytes(result).decode())
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source', required=True)
    p.add_argument('--source-manifest-sha256', required=True)
    p.add_argument('--evidence', required=True)
    p.add_argument('--evidence-sha256', required=True)
    p.add_argument('--root', required=True)
    p.add_argument('--releases-root', default='E:/CS2MOD/releases/bot-experiment')
    a = p.parse_args()
    build(a.source, a.source_manifest_sha256, a.evidence, a.evidence_sha256, a.root, a.releases_root)
