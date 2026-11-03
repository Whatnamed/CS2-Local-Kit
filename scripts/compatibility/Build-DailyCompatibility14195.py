"""Seal the frozen daily payload for the reviewed 1.41.9.5 build; no live writes."""
import copy
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx


def build():
    data = Path('E:/CS2MOD')
    root = Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive')
    review = tx.REPO / 'scripts/compatibility/review-14195.json'
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
    tx.require(m['targetBuild'] == tx.read_json(review)['targetBuild'], 'Source did not receive the reviewed transition')
    m.pop('reviewedRetainedFiles')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    tx.require(subprocess.run(['git', 'diff', '--quiet'], cwd=tx.REPO).returncode == 0 and
               subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=tx.REPO).returncode == 0, 'Commit before sealing')
    output = data / 'releases/bot-experiment' / ('bot-daily-14195-' + head[:7])
    tx.require(not output.exists(), 'Immutable candidate already exists')
    shutil.copytree(source, output)
    m['builderHead'] = head
    m['compatibility14195'] = dict(sourceManifestSha256=candidate['ManifestSha256'], reviewSha256=tx.sha(review), realGameAccepted=False)
    for name in ['bot_experiment.py', 'runtime_integration.py']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, output / 'tools' / name)
    shutil.copyfile(review, output / 'tools' / review.name)
    evidence = dict(targetBuild=m['targetBuild'], gameBinaries=m['gameBinaries'],
                    sourceManifestSha256=candidate['ManifestSha256'], reviewSha256=tx.sha(review),
                    runtimePayloadUnchanged=True, realGameAccepted=False,
                    evidence=tx.read_json(review)['evidence'])
    (output / 'integration-review.json').write_bytes(tx.json_bytes(evidence))
    m['coexistence']['evidenceSha256'] = tx.sha(output / 'integration-review.json')
    with (output / 'README.md').open('a', encoding='utf-8') as stream:
        stream.write('\nTarget: CS2 1.41.9.5 / 2000940 / 26393695. All daily runtime payload files are unchanged.\n'
                     'Only the new candidate build identities, review evidence and recovery tools are rebound.\n'
                     'Original manifests and installation records remain frozen. The matching Controller can\n'
                     'withdraw the old integration only, then freshly preflight and install this substrate.\n')
    tx.require(m['files'] == original['files'] and m['dailyPlay'] == original['dailyPlay'], 'Daily payload or contract changed')
    m['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(output).items()
                        if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
    (output / 'experiment-manifest.json').write_bytes(tx.json_bytes(m))
    digest = tx.sha(output / 'experiment-manifest.json')
    (output / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
    tx.load_candidate(output, digest)
    result = {**copy.deepcopy(candidate), 'Directory': output.name, 'ManifestSha256': digest}
    target = tx.REPO / 'temp/compat-14195/candidate.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(tx.json_bytes(result))
    print(tx.json_bytes(result).decode())


if __name__ == '__main__':
    build()
