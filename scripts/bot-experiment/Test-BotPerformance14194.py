"""Run the actual patched cache methods with fake entities/data, without loading CS2."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True
import bot_experiment as tx


def method(text, signature):
    start = text.index(signature)
    brace = text.find('{', start)
    arrow = text.find('=>', start)
    if 0 <= arrow < brace:
        return text[start:text.index(';', arrow) + 1]
    depth = 1
    end = brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def run(build, release, dotnet):
    build = Path(build).resolve()
    source = build / 'source'
    result = tx.read_json(build / 'build-result.json')
    for name in ['BotState', 'NadeSystem', 'BotRandomizer', 'BotController']:
        assert tx.sha(build / (name + '.dll')) == result[name]['dllSha256']
    work = tx.REPO / 'temp/perf-14194/method-checks'
    work.mkdir(parents=True, exist_ok=True)
    state = (source / 'BotState/BotState.Flashbang.cs').read_text(encoding='utf-8-sig')
    nade = (source / 'NadeSystem/NadeSystemPlugin.Data.cs').read_text(encoding='utf-8-sig')
    roller = (source / 'BotRandomizer/Cosmetics/CosmeticRoller.cs').read_text(encoding='utf-8-sig')
    with zipfile.ZipFile(Path(release) / 'sources/CS2-Bot-Randomizer-1c7c3ebad4b9.zip') as z:
        old = z.read('Cosmetics/CosmeticRoller.cs').decode('utf-8-sig')
    methods = 'partial class FlashChecks {\n' + '\n'.join(method(state, s) for s in [
        'private void InvalidateFlashEntities(', 'private void ClearFlashEntities(', 'private void RefreshFlashEntities(']) + '\n}\n'
    methods += 'partial class NadeChecks {\n' + '\n'.join(method(nade, s) for s in [
        'private void BuildGrenadeIndexes(', 'private GrenadeData? FindMapGrenade(', 'private static (int X, int Y) GetGrenadeZoneGridCell(']) + '\n}\n'
    methods += 'partial class PoolChecks {\n' + '\n'.join(method(roller, s) for s in [
        'private PaintCatalogEntry PickWeaponPaint(', 'private KnifePaintCatalogEntry PickKnifePaint(', 'private T Pick<T>(', 'private T PickWeighted<T>('])
    methods += '\n' + method(old, 'private PaintCatalogEntry PickWeaponPaint(').replace('PickWeaponPaint(', 'OldWeapon(')
    methods += '\n' + method(old, 'private KnifePaintCatalogEntry PickKnifePaint(').replace('PickKnifePaint(', 'OldKnife(') + '\n}\n'
    (work / 'Methods.cs').write_text(methods, encoding='utf-8')
    shutil_source = tx.REPO / 'experiments/bot-experiment/performance-14194/Checks.cs'
    (work / 'Checks.cs').write_bytes(shutil_source.read_bytes())
    (work / 'Checks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
    p = subprocess.run([dotnet, 'run', '--project', str(work / 'Checks.csproj'), '-c', 'Release', '-p:NuGetAudit=false'], cwd=tx.REPO, capture_output=True, text=True)
    (work / 'run.log').write_text(p.stdout + p.stderr, encoding='utf-8')
    tx.require(p.returncode == 0, 'Cache checks failed: ' + str(work / 'run.log'))
    report = json.loads(p.stdout[p.stdout.index('{'):])
    controller = (source / 'BotController/src/features/controller/BotController.cpp').read_text(encoding='utf-8-sig')
    profile = (source / 'BotController/src/features/profile/BotProfile.cpp').read_text(encoding='utf-8-sig')
    update = controller[controller.index('KHook::Return<void> HookedUpdate('):controller.index('KHook::Return<void> HookedUpkeep(')]
    assert update.index('HasAnyLock()') < update.index('CCSBotToSlot(bot)')
    assert 'g_slotToBot' not in controller and 'BotForSlot' not in profile
    assert 'GetRefEHandle().ToInt() != pawnHandle' in profile and 'resolved.pawn != pawn.m_pEntityInstance' in profile
    # Cache discovery is the sole enumeration owner; per-tick behavior still reads handles.
    process = method(state, 'private void ProcessFlashbangAvoidance(')
    assert 'FindAllEntitiesByDesignerName' not in process and 'handle.Value' in process and 'ent.AbsOrigin' in process
    lifecycle = (source / 'BotState/BotState.Lifecycle.cs').read_text(encoding='utf-8-sig')
    for event in ['OnEntityCreated', 'OnEntitySpawned', 'OnEntityDeleted', 'OnMapStart', 'OnMapEnd']:
        assert 'Listeners.' + event in lifecycle
    checks = ['same flash discovery order; clean ticks do not enumerate; lifecycle/map clear rebuilds',
              'Nade duplicate ID/type first-match semantics, missing records, negative grid and reload',
              'same seeded weapon/knife draws and weights; immutable-list identity reuse',
              'native idle update returns before lookup; live pawn serial and slot checks retained']
    report.update(status='PASS', checks=checks, sourceMethodScope='Extracted actual managed method bodies with fake native inputs; native path compiled and statically checked', gameStarted=False)
    (tx.REPO / 'temp/perf-14194/performance-checks.json').write_bytes(tx.json_bytes(report))
    print(tx.json_bytes(report).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    for name in ['build', 'source-release', 'dotnet']: p.add_argument('--' + name, required=True)
    a = p.parse_args()
    run(a.build, a.source_release, a.dotnet)
