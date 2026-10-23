"""Build only pinned NadeSystem + minimal compatibility/evidence patch; never install.

Data relocation and v1 capability shape reuse build_match_candidate.py at e6b7f4a.
Original source, patched source, license and exact unified diff accompany the DLL.
"""
import argparse
import difflib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx

REF = '009ce03005af68ba68dae62967bddc8e92ad172c'
ARCHIVE_SHA = '268dce5c98be643b40b3e3b72bda5bf5255ab5fe01c400cfe1e6b17baabe4910'
OLD_SMOKE = '48 8B C4 48 89 58 ? 48 89 68 ? 48 89 70 ? 57 41 56 41 57 48 81 EC ? ? ? ? 48 8B B4 24'
NEW_SMOKE = '48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 41 56 41 57 48 83 EC ? 48 8B B4 24 ? ? ? ? 4D 8B F8'


def replace_once(text, old, new):
    # Same unique-anchor rule as the existing match candidate builder.
    tx.require(text.count(old) == 1, 'Patch anchor missing/ambiguous: ' + old[:100])
    return text.replace(old, new)


def patch(source):
    def edit(name, transform):
        p = source / name
        p.write_text(transform(p.read_text(encoding='utf-8-sig')), encoding='utf-8', newline='\n')
    edit('NadeSystem.cs', lambda t: replace_once(t, OLD_SMOKE, NEW_SMOKE))
    edit('NadeSystemPlugin.Lifecycle.cs', lambda t:
         replace_once(replace_once(replace_once(replace_once(t,
             '        AddCommand("bot_nades",', '        RegisterLocalKitDiagnostics();\n        AddCommand("bot_nades",'),
             '            _db.Clear();', '            LogEvidence("map-ending");\n            _evidence.Reset(_botNadesMode, true);\n            _db.Clear();'),
             '        _roundOver = true;', '        LogEvidence("round-end");\n        _roundOver = true;'),
             '            if (_tick % 256 == 0) PruneCooldowns();',
             '            if (_tick % 256 == 0) { Skip("mode_off_tick_batch"); PruneCooldowns(); }'))
    edit('NadeSystemPlugin.Data.cs', lambda t:
         replace_once(replace_once(t,
             '        Server.PrintToConsole($"[NadeSystem] Loaded {loaded} grenades from {DataDir}");',
             '        Server.PrintToConsole($"[NadeSystem] Loaded {loaded} grenades from {DataDir}");\n        LogEvidence("database-loaded");'),
             '            catch (Exception ex)\n            {',
             '            catch (Exception ex)\n            {\n                Skip("database_file_error");\n                Logger.LogError("[NadeSystem] DB file {File}: {Error}", Path.GetFileName(file), ex.Message);'))
    # Logging extensions used by Data.cs are explicitly imported, not implicit usings.
    edit('NadeSystemPlugin.Data.cs', lambda t: 'using Microsoft.Extensions.Logging;\n' + t)
    edit('NadeSystemPlugin.Commands.cs', lambda t:
         replace_once(replace_once(t,
             '        _botNadesMode = val;', '        SetEvidenceMode(val);\n        info.ReplyToCommand(EvidenceJson("mode-readback"));'),
             '            Server.PrintToConsole($"[NadeSystem] bot_nades = {_botNadesMode}");',
             '            info.ReplyToCommand(EvidenceJson("mode-readback"));\n            Server.PrintToConsole($"[NadeSystem] bot_nades = {_botNadesMode}");'))
    # Count only real creation requests; success is after an actual valid entity,
    # separately on every factory path, never in command feedback or radio audio.
    def replay(t):
        t = replace_once(t, '        var gtype    = g.GrenadeType.ToLowerInvariant();',
            '        var gtype    = g.GrenadeType.ToLowerInvariant();\n        var ticket = _evidence.Begin(gtype, "lineup", g.Id, (uint)bot.Index);')
        t = replace_once(t, '                var botPawn = bot.PlayerPawn?.Value;',
            '                if (!_evidence.CanCreate(ticket)) return;\n                var botPawn = bot.PlayerPawn?.Value;')
        for kind in ['flash', 'decoy']:
            t = replace_once(t, f'if ({kind} == null)', f'if ({kind} == null || !{kind}.IsValid)')
        for kind, message in [('flash','flash CreateEntityByName null'), ('decoy','decoy CreateEntityByName null'),
                              ('smoke','smoke native Create returned null'), ('he','HE native Create returned null'),
                              ('molotov','molotov native Create returned null')]:
            t = replace_once(t, f'                        Server.PrintToConsole("[NadeSystem] {message}");',
                f'                        _evidence.Failed(ticket, "null_or_invalid_entity");\n                        Server.PrintToConsole("[NadeSystem] {message}");')
            anchor = ('                    // Don\'t detonate' if kind == 'decoy' else
                      f'                    {kind}.OwnerEntity.Raw     = botPawn.EntityHandle.Raw;')
            tx.require(t.count(anchor) == 1, 'Creation anchor drift: ' + kind)
            if kind == 'decoy':
                t = t.replace(anchor, '                    RecordCreated(ticket, decoy);\n' + anchor)
            elif kind == 'flash':
                t = replace_once(t, '                    flash.Teleport(origin, angles, velocity);\n                    AnnounceGrenadeThrow(bot, gtype);',
                    '                    flash.Teleport(origin, angles, velocity);\n                    RecordCreated(ticket, flash);\n                    AnnounceGrenadeThrow(bot, gtype);')
            else:
                t = t.replace(anchor, anchor + f'\n                    RecordCreated(ticket, {kind});')
        t = replace_once(t, '                    Server.PrintToConsole("[NadeSystem] bot pawn invalid, skipping replay");',
            '                    _evidence.Failed(ticket, "invalid_bot_pawn");\n                    Server.PrintToConsole("[NadeSystem] bot pawn invalid, skipping replay");')
        t = replace_once(t, '                Server.PrintToConsole(\n                    $"[NadeSystem] Unknown grenadeType',
            '                _evidence.Failed(ticket, "unsupported_type");\n                Server.PrintToConsole(\n                    $"[NadeSystem] Unknown grenadeType')
        t = replace_once(t, '                Server.PrintToConsole($"[NadeSystem] SpawnProjectile error: {ex.Message}");',
            '                _evidence.Failed(ticket, "factory_exception");\n                Logger.LogError("[NadeSystem] lineup {Id} {Type}: {Error}", g.Id, gtype, ex.ToString());\n                Server.PrintToConsole($"[NadeSystem] SpawnProjectile error: {ex.Message}");')
        return 'using Microsoft.Extensions.Logging;\n' + t
    edit('NadeSystemPlugin.Replay.cs', replay)
    def instant(t):
        t = replace_once(t, '        var vel = velocity ?? new Vector(0f, 0f, 0f);',
            '        var ticket = _evidence.Begin(gtype, "instant", "special", (uint)bot.Index);\n        var vel = velocity ?? new Vector(0f, 0f, 0f);')
        t = replace_once(t, '                var botPawn = bot.PlayerPawn?.Value;',
            '                if (!_evidence.CanCreate(ticket)) return;\n                var botPawn = bot.PlayerPawn?.Value;')
        for condition, reason in [('botPawn == null || !botPawn.IsValid','invalid_bot_pawn'),
                                  ('smoke == null || !smoke.IsValid','null_or_invalid_entity'),
                                  ('flash == null','null_or_invalid_entity')]:
            new_condition = 'flash == null || !flash.IsValid' if condition == 'flash == null' else condition
            t = replace_once(t, f'if ({condition}) return;',
                f'if ({new_condition}) {{ _evidence.Failed(ticket, "{reason}"); return; }}')
        for kind in ['smoke','flash']:
            anchor = f'                    {kind}.OwnerEntity.Raw     = botPawn.EntityHandle.Raw;'
            if kind == 'smoke':
                t = replace_once(t, anchor, anchor + '\n                    RecordCreated(ticket, smoke);')
            else:
                t = replace_once(t, '                    flash.Teleport(spawnPos, ang, vel);\n                    AnnounceGrenadeThrow(bot, gtype);',
                    '                    flash.Teleport(spawnPos, ang, vel);\n                    RecordCreated(ticket, flash);\n                    AnnounceGrenadeThrow(bot, gtype);')
        t = replace_once(t, '                Server.PrintToConsole($"[NadeSystem] TrySpawnInstantGrenade error: {ex.Message}");',
            '                _evidence.Failed(ticket, "factory_exception");\n                Logger.LogError("[NadeSystem] instant {Type}: {Error}", gtype, ex.ToString());\n                Server.PrintToConsole($"[NadeSystem] TrySpawnInstantGrenade error: {ex.Message}");')
        return 'using Microsoft.Extensions.Logging;\n' + t
    edit('NadeSystemPlugin.SpecialGrenades.cs', instant)
    # Preserve every strategy predicate; add counters only on rejection branches.
    guards = {
        '_botNadesMode == "off"':'mode_off', 'isTakenOver':'human_takeover', '_roundOver':'round_over',
        'mapNades.Count == 0':'no_map_points', 'rules?.GameRules?.FreezePeriod == true':'freeze_period',
        'pawn == null || !pawn.IsValid':'invalid_pawn', 'pawn.Bot == null':'no_native_bot',
        '!bot.PawnIsAlive':'dead_bot', '_replayBots.Contains((uint)bot.Index)':'replay_pending',
        'pos == null':'no_position', 'MathF.Abs(dz) > 85f':'outside_zone_height',
        'dx * dx + dy * dy > g.ZoneRadius * g.ZoneRadius':'outside_zone_radius',
        'IsOnCooldown(g.Id)':'lineup_cooldown',
        'gtype is "he" or "molotov" or "flash" && IsOnProbFailCooldown(g.Id)':'probability_cooldown',
        'gtype == "smoke" && _smokeCooldownBots.Contains((uint)bot.Index)':'smoke_bot_cooldown',
        'tooClose':'smoke_overlap', '!hasLiveEnemy':'no_live_enemy',
        'doDirectionCheck && !FacesThrowDirection(pawn, g)':'facing_away',
        'gtype is "he" or "molotov" && FiredRecently(bot, 1f)':'recent_weapon_fire',
        '!enemyIn400':'no_enemy_within_300', 'intoSmoke':'molotov_into_smoke',
        'inZoneSet.Contains(g.Id)':'flash_zone_already_entered',
        'money == null':'no_money_services', '!costTable.TryGetValue(gtype, out int cost)':'unsupported_cost_type',
        'money.Account < cost':'insufficient_money', '!HasLockedNadeMoney(botIdx, cost)':'freeze_end_budget',
        '!LessModeAllows(gtype, (uint)bot.Index)':'less_round_limit',
        'OptionalTotal >= 3 * teamSize':'normal_round_total_limit',
        'teamCount.Flash >= max':'normal_flash_limit', 'used >= teamSize':'normal_type_limit',
    }
    for name in ['NadeSystemPlugin.ZoneDetection.cs','NadeSystemPlugin.Replay.cs','NadeSystemPlugin.SpecialGrenades.cs']:
        def apply_guards(t):
            for condition, reason in guards.items():
                for action in ['return','continue']:
                    old = f'if ({condition}) {action};'
                    if old in t:
                        t = t.replace(old, f'if ({condition}) {{ Skip("{reason}"); {action}; }}')
            return t
        edit(name, apply_guards)
    edit('NadeSystemPlugin.ZoneDetection.cs', lambda t:
        replace_once(replace_once(t, '&& EvaluateFlashTargets(bot, g, allControllers).BlindableEnemies.Count == 0) continue;',
            '&& EvaluateFlashTargets(bot, g, allControllers).BlindableEnemies.Count == 0) { Skip("no_blindable_enemy"); continue; }'),
            '                : Array.Empty<GrenadeData>();',
            '                : Array.Empty<GrenadeData>();\n            if (nearbyNades.Count == 0) Skip("no_zone_in_grid_cell");'))
    edit('NadeSystemPlugin.Decision.cs', lambda t:
        replace_once(t, '        if (!PassesSituationalCheck(bot, pawn, g, g.GrenadeType, allControllers, flashEvaluation))\n        {',
            '        if (!PassesSituationalCheck(bot, pawn, g, g.GrenadeType, allControllers, flashEvaluation))\n        {\n            Skip("situational_information_probability_or_schedule");'))
    for name in ['NadeEvidence.cs', 'LocalKitDiagnostics.cs']:
        shutil.copyfile(tx.REPO / 'experiments/nades-14192' / name, source / name)


def build(archive, output, dotnet):
    archive, output = Path(archive), tx.no_links(output)
    tx.require(tx.sha(archive) == ARCHIVE_SHA, 'Wrong pinned source archive')
    tx.require(not output.exists(), 'Immutable build output exists')
    output.mkdir(parents=True)
    source = output / 'source'
    with zipfile.ZipFile(archive) as z:
        tx.require(all(not Path(n).is_absolute() and '..' not in Path(n).parts for n in z.namelist()), 'Unsafe archive')
        z.extractall(source)
    before = {p.name:p.read_text(encoding='utf-8-sig') for p in source.glob('*.cs')}
    before_hashes = {p.name:tx.sha(p) for p in source.glob('*.cs')}
    patch(source)
    changes = {}
    diff = []
    for p in sorted(source.glob('*.cs')):
        text = p.read_text(encoding='utf-8-sig'); old = before.get(p.name, '')
        if text != old:
            changes[p.name] = {'before':before_hashes.get(p.name), 'after':tx.sha(p)}
            diff.extend(difflib.unified_diff(old.splitlines(True), text.splitlines(True),
                fromfile='a/'+p.name if p.name in before else '/dev/null', tofile='b/'+p.name))
    (output/'nade-14192.patch').write_text(''.join(diff), encoding='utf-8', newline='\n')
    env = os.environ.copy()
    env.update(DOTNET_CLI_HOME=str(output/'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
    for i, args in enumerate([
        ['restore',str(source/'NadeSystem.csproj'),'--source',str(Path.home()/'.nuget/packages'),'-p:RestoreFallbackFolders=','-p:NuGetAudit=false'],
        ['build',str(source/'NadeSystem.csproj'),'-c','Release','--no-restore','-p:SourceRevisionId='+REF]
    ]):
        p = subprocess.run([dotnet,*args],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (output/f'build-{i}.log').write_bytes(p.stdout)
        tx.require(p.returncode == 0, 'Build failed; see '+str(output/f'build-{i}.log'))
    with zipfile.ZipFile(output/'NadeSystem-patched-source.zip','x',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(source.rglob('*')):
            if p.is_file() and not {'bin','obj'} & set(p.relative_to(source).parts): z.write(p,p.relative_to(source))
    spec=importlib.util.spec_from_file_location('signature_scan',Path(__file__).with_name('scan-windows-signatures.py'))
    scanner=importlib.util.module_from_spec(spec); spec.loader.exec_module(scanner)
    main=(source/'NadeSystem.cs').read_text(encoding='utf-8')
    import re
    patterns=[{'name':'NadeSystem/'+name,'library':'server','pattern':pat} for name,pat in
        [('SmokeCreate-old',OLD_SMOKE),('SmokeCreate',NEW_SMOKE)] +
        [(name,re.search(r'_'+field+r'\s*=.*?:\s*"([^"]+)"',main,re.S).group(1)) for name,field in [('HECreate','heCreate'),('MolotovCreate','molotovCreate')]]]
    game=Path(r'E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game')
    gd=tx.read_json(game/'csgo/addons/counterstrikesharp/gamedata/gamedata.json')
    for name in ['UTIL_CreateEntityByName','CBaseEntity_DispatchSpawn']:
        s=gd[name]['signatures'];patterns.append({'name':'Flash/'+name,'library':s['library'],'pattern':s['windows']})
    scan=scanner.scan(game,patterns)
    tx.require([r['matchCount'] for r in scan['results']]==[0,1,1,1,1,1], 'Native signature mismatch')
    (output/'native-signatures.json').write_bytes(tx.json_bytes(scan))
    result={'upstreamRef':REF,'upstreamPr':'https://github.com/ed0ard/CS2-Bot-NadeSystem/pull/9',
        'upstreamPrHead':'d089e2129a3cc5d9c2d344117ea793ece3b522fa','sourceArchiveSha256':ARCHIVE_SHA,
        'sourceChanges':changes,'dll':str(source/'bin/Release/net10.0/NadeSystem.dll'),
        'dllSha256':tx.sha(source/'bin/Release/net10.0/NadeSystem.dll'),
        'patchedSourceSha256':tx.sha(output/'NadeSystem-patched-source.zip'),
        'scope':'One Windows pattern plus evidence; no AI strategy or baseline upgrade','realGameAccepted':False}
    (output/'build-result.json').write_bytes(tx.json_bytes(result)); print(tx.json_bytes(result).decode())


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--archive',required=True);p.add_argument('--output',required=True);p.add_argument('--dotnet',default='dotnet')
    a=p.parse_args();build(a.archive,a.output,a.dotnet)
