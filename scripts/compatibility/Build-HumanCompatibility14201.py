"""Seal the Windows Human-only overlay for the reviewed 1.42.0.1 transition."""
from pathlib import Path
import argparse
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx

PLUGINS = 'game/csgo/addons/counterstrikesharp/plugins'
METAMOD = 'game/csgo/addons/metamod'
CSS = 'game/csgo/addons/counterstrikesharp'
REVIEW = Path(__file__).with_name('review-14201.json')


def build(stage, root, data):
    tx.MARKER = tx.INTEGRATION_MARKER
    tx.ensure_closed()
    root = tx.validate_root(root, False)
    data = tx.no_links(data)
    marker = tx.read_json(tx.under(root, tx.MARKER))
    backup = tx.under(data / 'backups/bot-experiment', marker['installId'])
    manifest = backup / 'candidate-manifest.json'
    manifest_sha = tx.sha(manifest)
    review = tx.read_json(REVIEW)
    tx.require(manifest_sha in review['sourceManifests'] and manifest_sha == marker['manifestSha256'], 'Wrong daily source')
    old = tx.reviewed_manifest(manifest, root)
    tx.integration_context(old, root)
    tx.require(old['targetBuild'] == review['targetBuild'], 'Wrong target build')
    tx.human_startup(root)

    archives = {
        'metamod': ('mm.zip', 'ac9d806337e136947c2d61ea821d87786d9b1687f91709324200acedef930d91'),
        'css': ('css.zip', '9a8881f83054a066f3f105d7f97b7c8a683104a4b5a381ea048d6b9a07c11203'),
    }
    # Verify the actual archive and read payload files from it, independently of unpacked staging.
    from zipfile import ZipFile
    for archive, expected in archives.values():
        tx.require(tx.sha(stage / archive) == expected, 'Upstream archive identity mismatch: ' + archive)

    baseline = old['baselineFiles']
    modules = old['moduleInventory']
    isolated = {}
    for name, image in modules[PLUGINS].items():
        path = Path(name)
        if (image['kind'] == 'file' and path.name.casefold() == (path.parent.name + '.dll').casefold()
                and path.parts[0] not in {'InventorySimulator', 'disabled'}):
            isolated[f'{PLUGINS}/{name}'] = image
    for name in ('BotController', 'BotHider', 'BotVision'):
        isolated[f'{METAMOD}/{name}.vdf'] = modules[METAMOD][name + '.vdf']
    core = f'{CSS}/configs/core.json'
    tx.require(tx.sha(root / core) == baseline[core], 'CSS core settings drift')
    config = tx.read_json(root / core)
    config['AutoUpdateEnabled'] = False
    patched = stage / 'css-build/Release/counterstrikesharp.dll'
    inputs = {f'{CSS}/bin/win64/counterstrikesharp.dll': patched.read_bytes(), core: tx.json_bytes(config)}
    with ZipFile(stage / 'mm.zip') as mm:
        inputs.update({f'game/csgo/{p}': mm.read(p) for p in sorted(tx.MM_FILES)})
    with ZipFile(stage / 'css.zip') as css:
        for name in css.namelist():
            if name.endswith('/'):
                continue
            target = f'game/csgo/{name}'
            if target.startswith(f'{CSS}/dotnet/'):
                tx.require(tx.digest(css.read(name)) == baseline[target], 'CSS host runtime drift: ' + target)
            if target.startswith(f'{CSS}/api/'):
                content = css.read(name)
                if tx.digest(content) != baseline.get(target):
                    inputs[target] = content
        gamedata = tx.read_json(stage / 'css/addons/counterstrikesharp/gamedata/gamedata.json')
        name = f'{CSS}/gamedata/gamedata.json'
        tx.require(tx.sha(stage / 'css/addons/counterstrikesharp/gamedata/gamedata.json') ==
                   tx.digest(css.read(name[len('game/csgo/'):])), 'Extracted gamedata drift')
        # Linux signature already checks the returned weapon pointer. Match the corresponding Windows compare.
        removal = gamedata['CBasePlayerPawn_RemovePlayerItem']['signatures']
        removal['windows'] += ' ? ? ? ? 48 3B C3'
        inputs[name] = tx.json_bytes(gamedata)

    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    output = data / 'releases/bot-experiment' / f'human-14201-{head[:7]}'
    output.mkdir()
    files = []
    for target, content in sorted(inputs.items()):
        relative = 'payload/' + target
        path = output / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        surface = 'framework' if target[len('game/csgo/'):] in tx.MM_FILES else 'human-framework'
        files.append(dict(target=target, source=relative, sha256=tx.digest(content), surface=surface))

    tools = output / 'tools'
    tools.mkdir()
    scripts = tx.REPO / 'scripts/bot-experiment'
    entry = 'Invoke-BotExperiment.ps1'
    wrapper = (scripts / entry).read_text(encoding='utf-8-sig')
    (tools / entry).write_text(wrapper.replace("'bot_experiment.py'", "'runtime_integration.py'"), encoding='utf-8')
    for name in ('bot_experiment.py', 'runtime_integration.py'):
        shutil.copyfile(scripts / name, tools / name)
    shutil.copyfile(REVIEW, tools / REVIEW.name)
    for name in ('Build-Css14201.ps1', 's2sdk-128.patch'):
        shutil.copyfile(Path(__file__).with_name(name), tools / name)
    for name in ('LICENSE', 'LICENSE.GPL3', 'LICENSE.MIT'):
        shutil.copyfile(stage / 'css-src' / name, output / f'CounterStrikeSharp-{name}')
    evidence = tx.REPO / review['evidence']
    shutil.copyfile(evidence, output / 'README.md')
    coexistence = dict(old['coexistence'], isolatedModules=isolated, evidenceSha256=tx.sha(evidence))
    m = {k: old[k] for k in ('schemaVersion', 'lane', 'status', 'testFixture', 'targetBuild', 'gameBinaries',
                             'baselineFiles', 'moduleInventory', 'runtimeIntegration', 'humanPolicy')}
    m.update(humanOnly=True, files=files, coexistence=coexistence, builderHead=head, sourceManifestSha256=manifest_sha,
             metamod=dict(build=1491, archiveSha256=archives['metamod'][1]), cssVersion='1.0.380-local.14201',
             cssArchiveSha256=archives['css'][1],
             bundleFiles={p: v['sha256'] for p, v in tx.inventory(output).items() if v['kind'] == 'file'})
    target = output / 'experiment-manifest.json'
    target.write_bytes(tx.json_bytes(m))
    digest = tx.sha(target)
    (output / 'MANIFEST.sha256').write_text(digest + '  experiment-manifest.json\n')
    tx.load_candidate(output, digest)
    print(tx.json_bytes(dict(Directory=output.name, ManifestSha256=digest, PreservesHuman=True,
                             RuntimeIntegration=True, HumanOnly=True, Difficulty=1)).decode())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', type=Path, required=True)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--data', type=Path, required=True)
    args = parser.parse_args()
    build(args.stage, args.root, args.data)
