"""Seal three complete performance candidates over the reviewed installed substrate."""
import copy
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx


def build(performance):
    performance = Path(performance).resolve()
    results = tx.read_json(performance / 'build-result.json')
    root = Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive')
    data = Path('E:/CS2MOD')
    tx.MARKER = tx.INTEGRATION_MARKER
    marker = tx.read_json(root / tx.MARKER)
    backup = data / 'backups/bot-experiment' / marker['installId']
    tx.restore(backup)  # Read-only exact old integration withdrawal preflight.
    record = tx.read_json(backup / 'install-record.json')
    for e in record['entries']:
        if e['action'] != 'retained' and e['before'] is not None:
            tx.require(tx.identity(backup / ('preimages/' + e['target'])) == e['before'], 'Active preimage drift')
    substrate = tx.reviewed_manifest(backup / 'candidate-manifest.json', root)
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    tx.require(subprocess.run(['git', 'diff', '--quiet'], cwd=tx.REPO).returncode == 0 and
               subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=tx.REPO).returncode == 0, 'Commit before packaging')
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    review = tx.REPO / 'scripts/compatibility/review-14194.json'
    approved = tx.read_json(review)
    candidates = []
    for difficulty in range(3):
        c = next(c for c in reversed(catalog) if c['Difficulty'] == difficulty and c['Directory'].startswith('bot-integrated-14193-'))
        source, original = tx.load_candidate(data / 'releases/bot-experiment' / c['Directory'], c['ManifestSha256'])
        tx.require(original['compatibility14193']['sourceManifestSha256'] in approved['sourceManifests'], 'Unreviewed source substrate')
        for name in ['BotController', 'BotState', 'NadeSystem', 'BotRandomizer']:
            tx.require(tx.sha(source / 'sources' / results[name]['baseSourceArchive']) == results[name]['baseSourceArchiveSha256'], 'Performance source archive drift')
            tx.require(tx.sha(tx.REPO / 'experiments/bot-experiment/performance-14194' / (name + '.patch')) == results[name]['patchSha256'], 'Performance patch drift')
        m = copy.deepcopy(original)
        for key in ['baselineFiles', 'gameBinaries', 'targetBuild', 'runtimeIntegration']:
            m[key] = copy.deepcopy(substrate[key])
        tx.require(m['targetBuild'] == approved['targetBuild'], 'Performance build not reviewed')
        m['coexistence']['reviewedBuild'] = m['targetBuild']
        m.pop('compatibility14193')
        m['builderHead'] = head
        m['performancePatches'] = {name: copy.deepcopy(results[name]) for name in ['BotController', 'BotState', 'NadeSystem', 'BotRandomizer']}
        m['compatibility14194'] = {'sourceManifestSha256': c['ManifestSha256'], 'reviewSha256': tx.sha(review), 'realGameAccepted': False}
        out = data / 'releases/bot-experiment' / ('bot-perf-14194-' + m['profile']['difficulty'].lower() + '-' + head[:7])
        tx.require(not out.exists(), 'Immutable candidate already exists')
        shutil.copytree(source, out)
        changed = []
        for e in m['files']:
            name = next((n for n in m['performancePatches'] if e['target'].endswith('/' + n + '.dll')), None)
            replacement = performance / (name + '.dll') if name else None
            if name:
                tx.require(tx.sha(replacement) == results[name]['dllSha256'], 'Performance DLL drift: ' + name)
            elif e['target'].endswith('/gamedata/gamedata.json'):
                replacement = root / e['target']
            if replacement:
                shutil.copyfile(replacement, out / e['source'])
                e['sha256'] = tx.sha(replacement)
                changed.append(e['target'])
        tx.require(len(changed) == 5, 'Expected four optimized DLLs and current CSS gamedata')
        for component in m['components']:
            if component['name'] in m['performancePatches']:
                name = component['name']
                component['upstreamPerformanceCommit'] = results[name]['upstreamCommit']
                component['nativeSha256' if name == 'BotController' else 'managedSha256'] = results[name]['dllSha256']
                if name == 'BotController': component['version'] = '0.8.0-local.perf14194'
        shutil.copyfile(performance / 'performance-corresponding-source.zip', out / 'sources/performance-corresponding-source.zip')
        shutil.copyfile(performance / 'build-result.json', out / 'sources/performance-build-result.json')
        for name in ['bot_experiment.py', 'runtime_integration.py', 'Invoke-BotExperiment.ps1']:
            shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, out / 'tools' / name)
        wrapper = out / 'tools/Invoke-BotExperiment.ps1'
        wrapper.write_text(wrapper.read_text(encoding='utf-8-sig').replace("'bot_experiment.py'", "'runtime_integration.py'"), encoding='utf-8')
        for name in ['review-14193.json', 'review-14194.json']:
            shutil.copyfile(tx.REPO / 'scripts/compatibility' / name, out / 'tools' / name)
        evidence = {'status': 'ready-for-manual-test', 'targetBuild': m['targetBuild'], 'gameBinaries': m['gameBinaries'],
                    'sourceManifestSha256': c['ManifestSha256'], 'reviewSha256': tx.sha(review),
                    'performancePatches': m['performancePatches'], 'realGameAccepted': False}
        (out / 'integration-review.json').write_bytes(tx.json_bytes(evidence))
        m['coexistence']['evidenceSha256'] = tx.sha(out / 'integration-review.json')
        m['localKitPatch'].update(version=3, nades='Existing SmokeCreate and v2 evidence + indexed record lookups; all 48 files retained')
        (out / 'README.md').write_text('''# CS2 1.41.9.4 Bot performance candidate

Complete Low/Medium/High integration over the exact retained Bot + compatibility + Nade parents.
Four selective performance backports; no wholesale Bot Improver/framework upgrade.
See sources/performance-build-result.json and corresponding source for exact identities.
MatchSetup, lineups, Aim Head/AWP, Nades Off-Max, TAB/side changes and Human CT/T cosmetics remain included.

Use the matching new Controller for preparation preview while the old integration is active.
Its reviewed tools withdraw only that integration delta, then perform a fresh full install preflight.
Restore returns the recorded playable Medium substrate, preserving all three parents and private data.
The public tools/Invoke-BotExperiment.ps1 Preview entry requires that restored substrate.
Install/Restore/switching require explicit user authorization. Agents must not start CS2.
Real gameplay, native Hook execution and FPS/frame-pacing improvement remain unverified.
''', encoding='utf-8')
        m['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(out).items() if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
        (out / 'experiment-manifest.json').write_bytes(tx.json_bytes(m))
        digest = tx.sha(out / 'experiment-manifest.json')
        (out / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
        tx.load_candidate(out, digest)
        candidates.append({**c, 'Directory': out.name, 'ManifestSha256': digest})
    target = tx.REPO / 'temp/perf-14194/candidates.json'
    target.write_bytes(tx.json_bytes(candidates))
    print(tx.json_bytes(candidates).decode())


if __name__ == '__main__':
    build(sys.argv[1])
