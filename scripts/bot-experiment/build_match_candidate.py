"""Derive a new immutable candidate; never install, restore, or edit the live game.

Exact upstream sources are exported to task-local temp before applying the small
diagnostic patch. Corresponding patched sources and original licenses ship together.
The original preserve-Human manifest supplies the post-restore baseline, never the
currently installed overlay. Full live preflight remains required after exact restore.
"""
import argparse
import copy
import os
from pathlib import Path
import shutil
import subprocess
import uuid
import zipfile
import bot_experiment as tx


def replace_once(text, old, new):
    tx.require(text.count(old) == 1, 'Patch anchor missing/ambiguous: ' + old[:80])
    return text.replace(old, new)


def export(repo, ref, dest):
    tx.require(subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip() == ref, 'Wrong source ref')
    subprocess.run(['git', '-C', str(repo), 'archive', '--format=zip', '-o', str(dest.with_suffix('.zip')), ref], check=True)
    with zipfile.ZipFile(dest.with_suffix('.zip')) as z:
        z.extractall(dest)


def patch_sources(upstream, out, dotnet):
    pin = tx.read_json(tx.REPO / 'experiments/bot-experiment/candidate-20261020.json')
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    results = []
    for name in ['BotAimImprover', 'NadeSystem']:
        rel = 'addons/counterstrikesharp/plugins/' + name
        dest = out / name
        export(upstream / rel, pin['submodules'][rel], dest)
        before = {p.name: tx.sha(p) for p in dest.glob('*.cs')}
        main = dest / (name + '.cs')
        text = main.read_text(encoding='utf-8-sig')
        if name == 'BotAimImprover':
            text = replace_once(text, 'public class BotAimImprover : BasePlugin', 'public partial class BotAimImprover : BasePlugin')
            text = replace_once(text, '        AddCommand("bot_aim",', '        RegisterLocalKitDiagnostics();\n        AddCommand("bot_aim",')
            text = replace_once(text, '            Server.PrintToConsole(reply);', '            _localKitCount = 0; _localKitEpoch++;\n            info.ReplyToCommand(reply);\n            Logger.LogInformation("{Reply}", reply);\n            Server.PrintToConsole(reply);')
            text = replace_once(text, '            // One-time confirmation that the override path actually runs end-to-end.', '            _localKitCount++;\n            // One-time confirmation that the override path actually runs end-to-end.')
            main.write_text(text, encoding='utf-8')
            extra = '''using System;
using System.Text.Json;
using CounterStrikeSharp.API.Core.Capabilities;
namespace BotAimImprover;
public partial class BotAimImprover
{
    private long _localKitCount, _localKitEpoch;
    private void RegisterLocalKitDiagnostics()
    {
        Capabilities.RegisterPluginCapability(new PluginCapability<Tuple<Action<string>, Func<string>>>("localkit:aim:v1"), () =>
            Tuple.Create<Action<string>, Func<string>>(mode => {
                _aimMode = mode switch { "head" => AimMode.HEAD, "body" => AimMode.BODY, "mixed" => AimMode.MIXED, _ => throw new ArgumentException("Invalid aim mode") };
                _localKitEpoch++; _localKitCount = 0;
            }, () => JsonSerializer.Serialize(new { Mode = _aimMode.ToString().ToLowerInvariant(), Epoch = _localKitEpoch, Count = _localKitCount })));
    }
}
'''
        else:
            lifecycle = dest / 'NadeSystemPlugin.Lifecycle.cs'
            lifecycle.write_text(replace_once(lifecycle.read_text(encoding='utf-8-sig'), '        AddCommand("bot_nades",', '        RegisterLocalKitDiagnostics();\n        AddCommand("bot_nades",'), encoding='utf-8')
            commands = dest / 'NadeSystemPlugin.Commands.cs'
            commands.write_text(replace_once(commands.read_text(encoding='utf-8-sig'), '        _botNadesMode = val;', '        _botNadesMode = val;\n        _localKitEpoch++; _localKitCount = 0;\n        info.ReplyToCommand($"[NadeSystem] mode={_botNadesMode}; DB={_db.Count}; map={_mapNades.Count}");'), encoding='utf-8')
            audio = dest / 'NadeSystemPlugin.Audio.cs'
            audio.write_text(replace_once(audio.read_text(encoding='utf-8-sig'), '    private void AnnounceGrenadeThrow(CCSPlayerController bot, string gtype)\n    {', '    private void AnnounceGrenadeThrow(CCSPlayerController bot, string gtype)\n    {\n        _localKitCount++;'), encoding='utf-8')
            extra = '''using System;
namespace NadeSystem;
public partial class NadeSystemPlugin
{
    private long _localKitCount, _localKitEpoch;
    private void RegisterLocalKitDiagnostics()
    {
        Capabilities.RegisterPluginCapability(new PluginCapability<Tuple<Action<string>, Func<string>>>("localkit:nades:v1"), () =>
            Tuple.Create<Action<string>, Func<string>>(mode => {
                if (mode != "off" && mode != "less" && mode != "normal" && mode != "more" && mode != "max") throw new ArgumentException("Invalid nade mode");
                _botNadesMode = mode; _localKitEpoch++; _localKitCount = 0;
            }, () => JsonSerializer.Serialize(new { Mode = _botNadesMode, Epoch = _localKitEpoch, Count = _localKitCount, DatabaseCount = _db.Count, MapCount = _mapNades.Count, DataPath = DataDir })));
    }
}
'''
            extra = 'using System.Text.Json;\nusing CounterStrikeSharp.API.Core.Capabilities;\n' + extra
        (dest / 'LocalKitDiagnostics.cs').write_text(extra, encoding='utf-8')
        project = dest / (name + '.csproj')
        for i, args in enumerate([
            ['restore', str(project), '--source', str(Path.home() / '.nuget/packages'), '-p:RestoreFallbackFolders=', '-p:NuGetAudit=false'],
            ['build', str(project), '-c', 'Release', '--no-restore', '-p:SourceRevisionId=' + pin['submodules'][rel]]
        ]):
            p = subprocess.run([dotnet, *args], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            (out / (name + '-' + str(i) + '.log')).write_bytes(p.stdout)
            tx.require(p.returncode == 0, 'Patched component build failed: ' + name)
        # Full original and patched corresponding sources are packaged; the manifest
        # records only the intentional source delta (no build-output/cache archive).
        changes = {p.name: {'before': before.get(p.name), 'after': tx.sha(p)} for p in dest.glob('*.cs') if before.get(p.name) != tx.sha(p)}
        source_zip = out / (name + '-localkit-diagnostics.zip')
        with zipfile.ZipFile(source_zip, 'x', compression=zipfile.ZIP_DEFLATED) as z:
            for p in dest.rglob('*'):
                if p.is_file() and not any(v in {'bin', 'obj'} for v in p.relative_to(dest).parts):
                    z.write(p, str(p.relative_to(dest)))
        results.append({'name': name, 'ref': pin['submodules'][rel], 'sourceChanges': changes, 'sourceArchive': str(source_zip), 'output': str(dest / 'bin/Release/net10.0')})
    return results


def derive(source, digest, output, patches, adapter=None):
    source, original = tx.load_candidate(source, digest)
    tx.require(original.get('humanPolicy') == 'preserve', 'Preserve-Human source required')
    tx.no_links(output); tx.require(not output.exists(), 'Immutable destination exists')
    shutil.copytree(source, output)
    m = copy.deepcopy(original)
    for e in m['files']:
        old = 'addons/counterstrikesharp/data/NadeSystem/grenades/'
        if old in e['target']:
            new = 'addons/counterstrikesharp/plugins/NadeSystem/grenades/'
            new_source = e['source'].replace(old, new)
            dst = output / new_source; dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(output / e['source'], dst)
            e['source'] = new_source; e['target'] = e['target'].replace(old, new)
    tx.require(sum('/plugins/NadeSystem/grenades/' in e['target'] for e in m['files']) == 48, 'Expected 48 grenade files')
    def asset(file, rel):
        path = output / ('bot/' + rel); path.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(file, path)
        entry = {'target': 'game/csgo/' + rel, 'source': 'bot/' + rel, 'surface': 'bot', 'sha256': tx.sha(path)}
        m['files'] = [e for e in m['files'] if e['target'] != entry['target']] + [entry]
    for patch in patches:
        for ext in ['.dll', '.deps.json']:
            asset(Path(patch['output']) / (patch['name'] + ext), 'addons/counterstrikesharp/plugins/' + patch['name'] + '/' + patch['name'] + ext)
        shutil.copyfile(patch['sourceArchive'], output / 'sources' / Path(patch['sourceArchive']).name)
    for name in ['bot_experiment.py', 'Invoke-BotExperiment.ps1']:
        shutil.copyfile(tx.REPO / 'scripts/bot-experiment' / name, output / 'tools' / name)
    m['localKitPatch'] = {'version': 1, 'sourceManifestSha256': digest, 'dataLayout': 'plugin-grenades-v1', 'components': [{k:v for k,v in p.items() if k not in {'output','sourceArchive'}} for p in patches], 'liveInstall': False}
    m['builderHead'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=tx.REPO, text=True).strip()
    if adapter:
        for p in adapter.iterdir():
            if p.is_file() and p.name.startswith(('LocalKit.MatchSetup', 'CS2LocalKit.Core')) and p.suffix in {'.dll','.json'}:
                asset(p, 'addons/counterstrikesharp/plugins/LocalKitMatchSetup/' + p.name)
        # Core is intentionally dependency-free. Its catalog is embedded, not user data.
        cfg = output / 'bot/cfg/gamemode_competitive_offline.cfg'
        cfg.write_text('// LocalKit bootstrap: adapter owns bounded pre-score bot initialization.\nbot_quota 0\nbot_quota_mode normal\nexec my_bot_normal_config.cfg\n', encoding='utf-8')
        for e in m['files']:
            if e['source'] == 'bot/cfg/gamemode_competitive_offline.cfg': e['sha256'] = tx.sha(cfg)
        m['matchSetup'] = {'schemaVersion': 1, 'adapter': 'LocalKitMatchSetup', 'manualAcceptance': 'pending'}
    m['bundleFiles'] = {p:v['sha256'] for p,v in tx.inventory(output).items() if v['kind'] == 'file' and p not in {'experiment-manifest.json','MANIFEST.sha256'}}
    (output / 'experiment-manifest.json').write_bytes(tx.json_bytes(m))
    sha = tx.sha(output / 'experiment-manifest.json'); (output / 'MANIFEST.sha256').write_text(sha + '  experiment-manifest.json\n')
    tx.load_candidate(output, sha)
    return {'directory': str(output), 'manifestSha256': sha, 'profile': m['profile']['difficulty'], 'files': len(m['files']), 'installed': False, 'livePreview': 'requires original exact restore first'}


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--upstream', required=True); p.add_argument('--dotnet', default='dotnet')
    p.add_argument('--adapter'); p.add_argument('--releases-root', default='E:/CS2MOD/releases/bot-experiment')
    a = p.parse_args()
    build = tx.REPO / 'temp/match-patch' / uuid.uuid4().hex; build.mkdir(parents=True)
    patches = patch_sources(Path(a.upstream), build, a.dotnet)
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    results = []
    for c in catalog:
        if not c.get('PreservesHuman') or c.get('MatchSetup'): continue
        target = Path(a.releases_root) / ('bot-match-e18c029-' + uuid.uuid4().hex[:12])
        results.append(derive(Path(a.releases_root)/c['Directory'], c['ManifestSha256'], target, patches, Path(a.adapter) if a.adapter else None))
    (tx.REPO / 'temp/match-setup/candidates.json').write_bytes(tx.json_bytes(results))
    print(tx.json_bytes(results).decode())
