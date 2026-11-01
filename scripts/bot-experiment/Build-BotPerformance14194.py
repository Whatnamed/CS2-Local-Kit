"""Backport four independent performance fixes onto the frozen integration sources."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True
import bot_experiment as tx

PATCHES = tx.REPO / 'experiments/bot-experiment/performance-14194'
MODULES = {
    'BotState': ('CS2-Smarter-Bot-9e9f8f1b2fd2.zip', '8038f8a6d2ab0d354d5787841dc6fa25e92166b7'),
    'NadeSystem': ('NadeSystem-patched-source.zip', 'ca3b63875ae1b20bc59089cc1d2adbb61fa0416f'),
    'BotRandomizer': ('CS2-Bot-Randomizer-1c7c3ebad4b9.zip', 'ded53e4278af6def771fbf23ff46998428ea9b33'),
    'BotController': ('CS2-Bot-Controller-582e9132c10b.zip', '0c0209a5f08266571b3ea2a025b996a64272cd56'),
}


def build(release, digest, output, dotnet, cmake):
    release, manifest = tx.load_candidate(release, digest)
    output = Path(output).resolve()
    tx.require(not output.exists(), 'Fresh performance build directory required')
    output.mkdir(parents=True)
    commands = []
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(output / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')

    def run(command):
        commands.append(command)
        p = subprocess.run(command, cwd=tx.REPO, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log = output / ('build-' + str(len(commands)) + '.log')
        log.write_bytes(p.stdout)
        tx.require(p.returncode == 0, 'Build failed; see ' + str(log))

    def extract(archive, target):
        with zipfile.ZipFile(archive) as z:
            for e in z.infolist():
                if e.is_dir(): continue
                p = tx.under(target, e.filename)
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(z.read(e))

    reports = {}
    for name, (archive, commit) in MODULES.items():
        source = output / 'source' / name
        extract(release / 'sources' / archive, source)
        if name == 'BotController':
            index = tx.read_json(release / 'sources/source-index.json')
            prefix = 'addons/BotController/'
            for row in manifest['recursiveSubmodules']:
                if row['path'].startswith(prefix):
                    a = next(i['archive'] for i in index if i['ref'] == row['ref'])
                    extract(release / 'sources' / a, source / row['path'][len(prefix):])
            # Retain the same IVEngineServer2 ABI repair already used by BotVision.
            header = source / 'src/core/cs2_sdk/libraries/s2sdk/public/engine2/ivengineserver2.h'
            raw = header.read_bytes()
            anchor = b'\tvirtual void\tMakeSpawnGroupActive( SpawnGroupHandle_t spawnGroup ) = 0;'
            addition = b'\tvirtual bool\tIsSpawnGroupLoadedForClient( CPlayerSlot nSlot, SpawnGroupHandle_t spawnGroup ) = 0;'
            tx.require(raw.count(anchor) == 1 and addition not in raw, 'Unexpected frozen SDK header')
            newline = b'\r\n' if b'\r\n' in raw else b'\n'
            header.write_bytes(raw.replace(anchor, anchor + newline + addition))
        patch = PATCHES / (name + '.patch')
        relative = source.relative_to(tx.REPO).as_posix()
        run(['git', 'apply', '--check', '--ignore-space-change', '--directory=' + relative, str(patch)])
        run(['git', 'apply', '--ignore-space-change', '--directory=' + relative, str(patch)])
        if name != 'BotController':
            # BotState consumes the existing public API, which is deliberately unchanged.
            if name == 'BotState':
                api = next(e for e in manifest['files'] if e['target'].endswith('/BotControllerApi.dll'))
                (source / 'libs').mkdir(exist_ok=True)
                shutil.copyfile(release / api['source'], source / 'libs/BotControllerApi.dll')
            project = source / (name + '.csproj')
            run([dotnet, 'restore', str(project), '--source', str(Path.home() / '.nuget/packages'), '-p:NuGetAudit=false', '-p:RestoreFallbackFolders='])
            run([dotnet, 'build', str(project), '-c', 'Release', '--no-restore', '-p:NuGetAudit=false', '-p:SourceRevisionId=localkit-perf14194+' + commit[:12]])
            dll = source / 'bin/Release/net10.0' / (name + '.dll')
        else:
            native = output / 'native-build'
            run([cmake, '-S', str(source), '-B', str(native), '-G', 'Visual Studio 17 2022', '-A', 'x64', '-DCMAKE_DISABLE_FIND_PACKAGE_Git=ON', '-DPLUGIN_VERSION=0.8.0-local.perf14194'])
            run([cmake, '--build', str(native), '--config', 'Release', '--parallel', '4'])
            dll = native / 'package/addons/BotController/bin/win64/BotController.dll'
        shutil.copyfile(dll, output / (name + '.dll'))
        reports[name] = {'baseSourceArchive': archive, 'baseSourceArchiveSha256': tx.sha(release / 'sources' / archive),
                         'upstreamCommit': commit, 'patchSha256': tx.sha(patch), 'dllSha256': tx.sha(dll)}
        print(name, 'PASS', flush=True)
    reports['commands'] = commands
    reports['sourceManifestSha256'] = digest
    reports['gameplayAcceptance'] = 'pending'
    (output / 'build-result.json').write_bytes(tx.json_bytes(reports))
    with zipfile.ZipFile(output / 'performance-corresponding-source.zip', 'x', zipfile.ZIP_DEFLATED) as z:
        for p in (output / 'source').rglob('*'):
            if p.is_file() and not {'bin', 'obj', '.git'} & set(p.relative_to(output / 'source').parts):
                z.write(p, p.relative_to(output))
        for p in PATCHES.glob('*'): z.write(p, 'BUILD/' + p.name)
        z.write(Path(__file__), 'BUILD/' + Path(__file__).name)
        z.write(output / 'build-result.json', 'BUILD/build-result.json')
    print(tx.json_bytes(reports).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    for name in ['source', 'manifest-sha256', 'output', 'dotnet', 'cmake']: p.add_argument('--' + name, required=True)
    a = p.parse_args()
    build(a.source, a.manifest_sha256, a.output, a.dotnet, a.cmake)
