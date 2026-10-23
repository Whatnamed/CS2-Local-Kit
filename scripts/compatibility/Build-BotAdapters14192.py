"""Rebuild only frozen BotVision and BotAimImprover compatibility boundaries."""
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


def build(source, source_sha, cmake, dotnet):
    source, manifest = tx.load_candidate(source, source_sha)
    freeze = tx.read_json(tx.REPO / 'experiments/bot-experiment/candidate-20261020.json')
    index = tx.read_json(source / 'sources/source-index.json')
    work = tx.REPO / 'temp/bot-adapters-14192'
    tx.require(not work.exists(), 'Fresh adapter source directory required')
    work.mkdir(parents=True)
    def extract(ref, target):
        archive = next(row['archive'] for row in index if row['ref'] == ref)
        with zipfile.ZipFile(source / 'sources' / archive) as z:
            for entry in z.infolist():
                if entry.is_dir(): continue
                destination = tx.under(target, entry.filename)
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(z.read(entry))
    vision = work / 'BotVision'
    prefix = 'addons/BotVision'
    for row in freeze['recursiveSubmodules']:
        if row['path'] == prefix or row['path'].startswith(prefix + '/'):
            extract(row['ref'], vision / row['path'][len(prefix):].lstrip('/'))
    aim = work / 'BotAimImprover'
    extract(freeze['submodules']['addons/counterstrikesharp/plugins/BotAimImprover'], aim)
    # Exact source-level backport of s2sdk #116; the same method fixes HL2SDK #438.
    header = vision / 'src/core/cs2_sdk/libraries/s2sdk/public/engine2/ivengineserver2.h'
    raw = header.read_bytes()
    anchor = b'\tvirtual void\tMakeSpawnGroupActive( SpawnGroupHandle_t spawnGroup ) = 0;'
    addition = b'\tvirtual bool\tIsSpawnGroupLoadedForClient( CPlayerSlot nSlot, SpawnGroupHandle_t spawnGroup ) = 0;'
    tx.require(raw.count(anchor) == 1 and addition not in raw, 'Unexpected BotVision SDK header')
    newline = b'\r\n' if b'\r\n' in raw else b'\n'
    header.write_bytes(raw.replace(anchor, anchor + newline + addition))
    # Current PE schema registration proves this one field moved by eight bytes.
    schema = tx.read_json(tx.REPO / 'temp/evidence/schema.json')
    actual = next(r['offset'] for c in schema['classes'] if c['name'] == 'CCSPlayerPawn' for r in c['fields'] if r['name'] == 'm_pBot')
    tx.require(actual == '0x1500', 'Unreviewed m_pBot offset')
    source_file = aim / 'BotAimImprover.cs'
    raw = source_file.read_bytes()
    old = b'ts: 0x5994, en: 0x5A00, vis: 0x5A04, pbot: 0x14F8,'
    new = b'ts: 0x5994, en: 0x5A00, vis: 0x5A04, pbot: 0x1500,'
    tx.require(raw.count(old) == 1, 'Unexpected frozen Aim offsets')
    source_file.write_bytes(raw.replace(old, new))
    out = tx.REPO / 'temp/evidence'
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(work / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    commands = [
        [cmake, '-S', str(vision), '-B', str(work / 'vision-build'), '-G', 'Visual Studio 17 2022', '-A', 'x64', '-DCMAKE_DISABLE_FIND_PACKAGE_Git=ON', '-DPLUGIN_VERSION=0.4.0-local.14192'],
        [cmake, '--build', str(work / 'vision-build'), '--config', 'Release', '--parallel', '4'],
        [dotnet, 'restore', str(aim / 'BotAimImprover.csproj'), '--source', str(Path.home() / '.nuget/packages'), '-p:NuGetAudit=false', '-p:RestoreFallbackFolders='],
        [dotnet, 'build', str(aim / 'BotAimImprover.csproj'), '-c', 'Release', '--no-restore', '-p:NuGetAudit=false', '-p:SourceRevisionId=d796572+14192'],
    ]
    for i, command in enumerate(commands):
        p = subprocess.run(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (out / ('bot-adapter-build-' + str(i) + '.log')).write_bytes(p.stdout)
        tx.require(p.returncode == 0, 'Adapter build failed at step ' + str(i))
        print('adapter build', i, 'PASS', flush=True)
    native = work / 'vision-build/package/addons/BotVision/bin/win64/BotVision.dll'
    managed = aim / 'bin/Release/net10.0/BotAimImprover.dll'
    shutil.copyfile(native, out / 'botvision-native.dll')
    shutil.copyfile(managed, out / 'botaim-managed.dll')
    # Exact patched sources and all native dependencies for reproducibility/license obligations.
    with zipfile.ZipFile(out / 'bot-adapters-corresponding-source.zip', 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for folder in [vision, aim]:
            for p in folder.rglob('*'):
                if p.is_file() and not (folder == aim and p.relative_to(folder).parts[0] in {'bin','obj'}):
                    z.write(p, p.relative_to(work).as_posix())
        z.write(Path(__file__), 'BUILD/Build-BotAdapters14192.py')
        z.writestr('BUILD/README.txt', 'Frozen BotVision 9884848 and BotAimImprover d796572, complete recursive refs in the baseline source manifest. Only public/engine2/ivengineserver2.h adds IsSpawnGroupLoadedForClient after MakeSpawnGroupActive; only Windows PBot offset changes 0x14F8 to 0x1500. Build with the commands in bot-adapter-build.json. Native CMake version disables Git discovery so no parent-repository ref is mistaken for the upstream source ref. Managed deps.json/API references remain unchanged.\n')
    report = {'status':'PASS', 'commands':commands, 'botVisionSha256':tx.sha(native), 'botAimSha256':tx.sha(managed),
              'changes':['BotVision s2sdk IsSpawnGroupLoadedForClient insertion', 'BotAimImprover Windows PBot 0x14F8 -> 0x1500'],
              'baseVisionRef':freeze['submodules'][prefix], 'baseAimRef':freeze['submodules']['addons/counterstrikesharp/plugins/BotAimImprover']}
    (out / 'bot-adapter-build.json').write_bytes(tx.json_bytes(report))
    print(tx.json_bytes(report).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    for flag in ['source','source-sha','cmake','dotnet']: p.add_argument('--' + flag, required=True)
    a = p.parse_args(); build(a.source, a.source_sha, a.cmake, a.dotnet)
