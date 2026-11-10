"""Seal the frozen daily payload for a reviewed CS2 build without live writes."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx


def build(version):
    data = Path('E:/CS2MOD')
    root = Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive')
    review = tx.REPO / f'scripts/compatibility/review-{version}.json'
    approved = tx.read_json(review)
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    candidate = next(c for c in catalog if c['Directory'] == 'bot-daily-14194-b72e06b')
    source, original = tx.load_candidate(data / 'releases/bot-experiment' / candidate['Directory'], candidate['ManifestSha256'])
    tx.MARKER = tx.INTEGRATION_MARKER
    marker = tx.read_json(root / tx.MARKER)
    backup = data / 'backups/bot-experiment' / marker['installId']
    tx.restore(backup)  # Verify the active delta and all three immutable parents without writes.
    m = tx.reviewed_manifest(source / 'experiment-manifest.json', root)
    active = tx.reviewed_manifest(backup / 'candidate-manifest.json', root)
    for key in ['targetBuild', 'baselineFiles', 'gameBinaries', 'moduleInventory', 'runtimeIntegration']:
        tx.require(m[key] == active[key], 'Source and restored substrate differ: ' + key)
    tx.require(m['targetBuild'] == approved['targetBuild'], 'Source did not receive the reviewed transition')
    retained = m.pop('reviewedRetainedFiles')
    # The old Retake payload was byte-identical to the old Valve file. Keep Valve's updated file protected.
    retake = 'game/csgo/cfg/gamemode_retakecasual.cfg'
    if retake in retained:
        entry = next(e for e in m['files'] if e['target'] == retake)
        tx.require(entry['sha256'] == retained[retake]['before'], 'Retake payload was not the old Valve image')
        m['files'].remove(entry)
    else:
        entry = None
    tx.require(m['files'] == [e for e in original['files'] if e != entry] and
               m['dailyPlay'] == original['dailyPlay'], 'Daily payload or contract changed')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    tx.require(subprocess.run(['git', 'diff', '--quiet'], cwd=tx.REPO).returncode == 0 and
               subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=tx.REPO).returncode == 0, 'Commit before sealing')
    output = data / 'releases/bot-experiment' / f'bot-daily-{version}-{head[:7]}'
    tx.require(not output.exists(), 'Immutable candidate already exists')
    shutil.copytree(source, output)
    if entry is not None:
        (output / entry['source']).unlink()
    m['builderHead'] = head
    m[f'compatibility{version}'] = dict(sourceManifestSha256=candidate['ManifestSha256'], reviewSha256=tx.sha(review), realGameAccepted=False)
    for name in ['bot_experiment.py', 'runtime_integration.py']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, output / 'tools' / name)
    shutil.copyfile(review, output / 'tools' / review.name)
    evidence = dict(targetBuild=m['targetBuild'], gameBinaries=m['gameBinaries'],
                    sourceManifestSha256=candidate['ManifestSha256'], reviewSha256=tx.sha(review),
                    runtimePayloadUnchanged=entry is None, realGameAccepted=False,
                    evidence=approved['evidence'])
    (output / 'integration-review.json').write_bytes(tx.json_bytes(evidence))
    m['coexistence']['evidenceSha256'] = tx.sha(output / 'integration-review.json')
    with (output / 'README.md').open('a', encoding='utf-8') as stream:
        build_identity = ' / '.join(m['targetBuild'][key] for key in ['PatchVersion', 'ClientVersion', 'buildId'])
        stream.write(f'\nTarget: CS2 {build_identity}. Daily runtime binaries and daily settings contract are unchanged.\n')
        if entry is not None:
            stream.write('The obsolete Retake payload was removed so the updated Valve config is retained.\n')
        stream.write('The new build identities, review evidence and recovery tools are rebound.\n'
                     'Original manifests and installation records remain frozen. The matching Controller can\n'
                     'withdraw the old integration only, then freshly preflight and install this substrate.\n')
    m['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(output).items()
                        if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
    (output / 'experiment-manifest.json').write_bytes(tx.json_bytes(m))
    digest = tx.sha(output / 'experiment-manifest.json')
    (output / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
    tx.load_candidate(output, digest)
    result = {**candidate, 'Directory': output.name, 'ManifestSha256': digest}
    target = tx.REPO / f'temp/compat-{version}/candidate.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(tx.json_bytes(result))
    print(tx.json_bytes(result).decode())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version', choices=['14195', '14196'])
    build(parser.parse_args().version)
