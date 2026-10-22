"""Repackage the pinned match candidates with a committed LocalKit adapter build.

Creates new immutable bundles. Never writes the CS2 tree or modifies old bundles.
"""
import argparse
import copy
import json
from pathlib import Path
import shutil
import subprocess
import uuid
import bot_experiment as tx


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--adapter', required=True)
    parser.add_argument('--releases-root', default='E:/CS2MOD/releases/bot-experiment')
    args = parser.parse_args()
    repo = tx.REPO
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    tx.require(subprocess.run(['git', 'diff', '--quiet'], cwd=repo).returncode == 0 and
               subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=repo).returncode == 0,
               'Commit source changes before packaging the source archive')
    adapter = Path(args.adapter).resolve()
    binaries = ['LocalKitMatchSetup.dll', 'LocalKitMatchSetup.deps.json', 'CS2LocalKit.Core.dll']
    for name in binaries:
        tx.require((adapter / name).is_file(), 'Missing adapter output: ' + name)
    catalog = tx.read_json(repo / 'src/CS2LocalKit.App/BotCandidates.json')
    selected = [c for c in catalog if c.get('PreservesHuman') and c.get('MatchSetup')]
    tx.require(len(selected) == 3 and sorted(c['Difficulty'] for c in selected) == [0, 1, 2],
               'Expected one pinned old match candidate per difficulty')
    output_root = Path(args.releases_root).resolve()
    results = []
    for c in selected:
        source, original = tx.load_candidate(output_root / c['Directory'], c['ManifestSha256'])
        tx.require(original['matchSetup']['adapter'] == 'LocalKitMatchSetup' and
                   original['humanPolicy'] == 'preserve', 'Unexpected source candidate')
        dest = output_root / ('bot-match-closure-' + uuid.uuid4().hex[:12])
        tx.require(not dest.exists(), 'Immutable target already exists')
        shutil.copytree(source, dest)
        manifest = copy.deepcopy(original)
        old_archive = dest / manifest['matchSetup']['sourceArchive']
        old_archive.unlink()
        for name in binaries:
            rel = 'bot/addons/counterstrikesharp/plugins/LocalKitMatchSetup/' + name
            shutil.copyfile(adapter / name, dest / rel)
            entry = next(e for e in manifest['files'] if e['source'] == rel)
            entry['sha256'] = tx.sha(dest / rel)
        archive = dest / ('sources/LocalKitMatchSetup-' + head[:12] + '.zip')
        subprocess.run(['git', '-C', str(repo), 'archive', '--format=zip', '-o', str(archive), head,
                        'src/CS2LocalKit.MatchSetup', 'src/CS2LocalKit.Core', 'data/match-setup'], check=True)
        manifest['builderHead'] = head
        manifest['matchSetup'] = {
            'schemaVersion': 1, 'adapter': 'LocalKitMatchSetup', 'sourceHead': head,
            'sourceArchive': str(archive.relative_to(dest)).replace('\\', '/'),
            'sourceArchiveSha256': tx.sha(archive), 'manualAcceptance': 'pending'
        }
        manifest['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(dest).items()
                                   if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
        (dest / 'experiment-manifest.json').write_bytes(tx.json_bytes(manifest))
        digest = tx.sha(dest / 'experiment-manifest.json')
        (dest / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
        tx.load_candidate(dest, digest)
        results.append({'Difficulty': c['Difficulty'], 'Directory': dest.name,
                        'ManifestSha256': digest, 'PreservesHuman': True, 'MatchSetup': True})
    result = repo / 'temp/match-setup/closure-candidates.json'
    result.parent.mkdir(parents=True, exist_ok=True)
    result.write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(result)


if __name__ == '__main__':
    main()
