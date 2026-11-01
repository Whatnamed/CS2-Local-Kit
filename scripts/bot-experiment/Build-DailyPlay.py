"""Seal one daily-play substrate from the three frozen performance candidates. No live writes."""
import argparse
import copy
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import bot_experiment as tx


def build(source_root, adapter, output):
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    sources = {}
    for level in ['Low', 'Medium', 'High']:
        c = next(c for c in catalog if c['Directory'] == f'bot-perf-14194-{level.lower()}-6196707')
        sources[level] = tx.load_candidate(Path(source_root) / c['Directory'], c['ManifestSha256'])
    medium, base = sources['Medium']
    expected = {e['target']: e['sha256'] for e in base['files'] if e['target'] != tx.DAILY_PROFILE}
    for _, m in sources.values():
        tx.require({e['target']: e['sha256'] for e in m['files'] if e['target'] != tx.DAILY_PROFILE} == expected,
                   'Performance candidates differ outside profile')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    tx.require(subprocess.run(['git', 'diff', '--quiet'], cwd=tx.REPO).returncode == 0 and
               subprocess.run(['git', 'diff', '--cached', '--quiet'], cwd=tx.REPO).returncode == 0, 'Commit implementation before sealing')
    output = tx.no_links(output)
    tx.require(not output.exists(), 'Immutable output already exists')
    shutil.copytree(medium, output)
    m = copy.deepcopy(base)
    m['builderHead'] = head
    profiles = {}
    for level, (release, original) in sources.items():
        p = original['profile']
        db = output / p['dbSource']; db.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(release / p['dbSource'], db)
        e = next(e for e in original['files'] if e['target'] == tx.DAILY_PROFILE)
        source = f'bot/overrides/localkit-profiles/{level}.vpk'
        path = output / source; path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(release / e['source'], path)
        target = f'game/csgo/overrides/localkit-profiles/{level}.vpk'
        m['files'].append(dict(target=target, source=source, surface='bot', sha256=tx.sha(path)))
        profiles[level] = dict(target=target, sha256=tx.sha(path), profile=p)
    adapter = Path(adapter)
    for e in m['files']:
        if '/LocalKitMatchSetup/' in e['target']:
            source = adapter / Path(e['target']).name
            shutil.copyfile(source, output / e['source']); e['sha256'] = tx.sha(source)
    for name in ['bot_experiment.py', 'runtime_integration.py']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, output / 'tools' / name)
    m['dailyPlay'] = dict(schemaVersion=1, profiles=profiles, modeFiles=sorted(tx.DAILY_LOADERS),
                          settings='app-data/match-setup/daily-play.json', manualAcceptance='pending')
    archive = output / f'sources/LocalKitDailyPlay-{head[:12]}.zip'
    subprocess.run(['git', 'archive', '--format=zip', '-o', str(archive), head,
                    'src/CS2LocalKit.Core', 'src/CS2LocalKit.MatchSetup', 'data/match-setup',
                    'scripts/bot-experiment'], cwd=tx.REPO, check=True)
    m['matchSetup'].update(sourceHead=head, sourceArchive=archive.relative_to(output).as_posix(), sourceArchiveSha256=tx.sha(archive))
    (output / 'README.md').write_text('''# Daily local play candidate

One substrate, three verified mutable profiles. Controller auto-saves daily settings;
close CS2 and open Controller to apply profile/mode files before starting the game.
Then use -insecure and choose any local map normally. No Prepare or map request.
Aim/Nades are read independently on each map. Selected lineups default off and run
once before scoring after the sole human picks a side. Runtime readback is not gameplay acceptance.

System Settings > Check component preview > Install/upgrade performs the one-time
exact recovery of the old integration then installs this substrate. The original
three parent transactions and Human fixture sidecar are preserved. Daily difficulty
and ordinary/enhanced mode changes never restore/reinstall this substrate.

Normal mode disables only owned Bot DLL/VDF, the profile and the two enhanced CFG
entry files; shared CSS, MetaMod and InventorySimulator remain loaded. No online mode.
The existing gamemode CFG exec of the absent enhanced entry may print a harmless
missing-CFG message in normal mode; it does not execute enhanced settings.

Exact Restore validates all finite owned images, normalizes them to the recorded
postimage, then restores original preimages with the same transaction. Unknown files,
build changes, source drift and ambiguous active/disabled files block writes.
Never restore parent transactions underneath the active substrate.

This is an uninstalled candidate. Gameplay, TAB, persona, CT/T cosmetics and FPS
acceptance must be performed by the user in real CS2.
''', encoding='utf-8')
    m['bundleFiles'] = {p: v['sha256'] for p, v in tx.inventory(output).items() if v['kind'] == 'file' and p not in {'experiment-manifest.json', 'MANIFEST.sha256'}}
    (output / 'experiment-manifest.json').write_bytes(tx.json_bytes(m))
    digest = tx.sha(output / 'experiment-manifest.json')
    (output / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
    tx.load_candidate(output, digest)
    result = dict(Difficulty=1, Directory=output.name, ManifestSha256=digest, PreservesHuman=True, MatchSetup=True, RuntimeIntegration=True, DailyPlay=True)
    print(tx.json_bytes(result).decode())
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root', default='E:/CS2MOD/releases/bot-experiment')
    p.add_argument('--adapter', required=True); p.add_argument('--output', required=True)
    args = p.parse_args()
    build(args.source_root, args.adapter, args.output)
