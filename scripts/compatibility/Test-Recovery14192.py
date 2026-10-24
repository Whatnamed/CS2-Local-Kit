"""Exercise actual recovery payload and public entry exclusively in fake trees."""
import argparse
import copy
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx
import compat_14192 as recovery


def run(candidate, digest):
    candidate, original = recovery.load(candidate, digest)
    temp = tx.REPO / 'temp/recovery-tests'; temp.mkdir(parents=True, exist_ok=True)
    home = Path(tempfile.mkdtemp(dir=temp))
    root = home / 'temp/steamapps/common/cs2'; parent = home / 'parent' / original['parent']['installId']
    package = home / 'package'; package.mkdir()
    for name in ['compat_14192.py', 'bot_experiment.py', 'Invoke-Recovery14192.ps1']:
        (package / 'tools').mkdir(exist_ok=True)
        source = tx.REPO/'scripts/bot-experiment'/name if name=='bot_experiment.py' else Path(__file__).parent/name
        shutil.copyfile(source, package / 'tools' / name)
    def write(p, value):
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(value if isinstance(value, bytes) else value.encode())
    for rel in original['baselineFiles']:
        write(root / rel, 'synthetic protected file ' + rel)
    write(root / '.bot-experiment-fake-root', 'synthetic-test-only')
    write(root / 'game/csgo/addons/metamod/metaplugins.ini', '; synthetic baseline, no active entries\n')
    write(root / 'game/csgo/gameinfo.gi', 'SearchPaths\n{\n Game csgo/overrides/botprofile.vpk\n Game csgo/addons/metamod\n Game csgo\n}\n')
    write(root.parent.parent / 'appmanifest_730.acf', '"AppState" { "appid" "730" "installdir" "cs2" "StateFlags" "4" "buildid" "26144732" }')
    marker = {'installId':parent.name, 'manifestSha256':'a'*64}
    write(root / tx.MARKER, tx.json_bytes(marker))
    write(parent / 'candidate-manifest.json', b'synthetic original immutable candidate')
    marker['manifestSha256'] = tx.sha(parent / 'candidate-manifest.json')
    write(root / tx.MARKER, tx.json_bytes(marker))
    write(parent / 'install-record.json', tx.json_bytes({'state':'installed', 'installId':parent.name, 'cs2Root':str(root), 'manifestSha256':marker['manifestSha256']}))
    write(parent / 'preimages/sentinel.json', 'original recovery backup')
    write(home / 'private-presets/personal.json', 'private preset sentinel')
    m = copy.deepcopy(original); m['testFixture'] = True; m['cs2Root'] = str(root)
    m['parent'] = {'backupDir':str(parent), 'installId':parent.name, 'inventory':tx.inventory(parent)}
    m['baselineFiles'] = {p:tx.sha(root / p) for p in original['baselineFiles']}
    m['gameBinaries'] = {p:tx.sha(root / p) for p in tx.GAME_BINARIES}
    m['moduleInventory'] = tx.module_inventory(root)
    m['steamManifestSha256'] = tx.sha(root.parent.parent / 'appmanifest_730.acf')
    for rel, entry in m['changes'].items():
        write(package / entry['source'], (candidate / entry['source']).read_bytes())
    m['bundleFiles'] = {p:v['sha256'] for p,v in tx.inventory(package).items() if v['kind']=='file'}
    write(package / 'recovery-manifest.json', tx.json_bytes(m)); expected = tx.sha(package / 'recovery-manifest.json')
    backups = home / 'backups'
    args = (package, expected, root, backups)
    before = tx.inventory(root); parent_before = tx.inventory(parent); private_before = tx.inventory(home / 'private-presets')
    checks = []
    def same():
        assert tx.inventory(root) == before
        assert tx.inventory(parent) == parent_before
        assert tx.inventory(home / 'private-presets') == private_before
    def public(action, apply=False, install_id=None, good=True):
        command = ['pwsh', '-NoProfile', '-File', str(package / 'tools/Invoke-Recovery14192.ps1'), '-Action', action,
                   '-ReleaseDir', str(package), '-ManifestSha256', expected, '-Cs2Root', str(root),
                   '-BackupRoot', str(backups), '-AllowFakeTree']
        if apply: command.append('-Apply')
        if install_id: command += ['-InstallId', install_id]
        p = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
        assert (p.returncode == 0) == good, p.stdout + p.stderr
        return json.loads(p.stdout) if good else None
    all_before = tx.inventory(home); public('Preview'); assert tx.inventory(home) == all_before; checks.append('preview-zero-writes')
    installed = public('Install', True)
    steam_path=root.parent.parent/'appmanifest_730.acf'
    steam_before=steam_path.read_bytes();steam_path.write_bytes(steam_before+b'\n"LastPlayed" "123"\n')
    snapshot=tx.inventory(home);public('Restore',install_id=installed['installId']);assert tx.inventory(home)==snapshot
    checks.append('restore-allows-noncritical-metadata-drift')
    for changed in [steam_before.replace(b'26144732',b'26144733'),steam_before.replace(b'"4"',b'"6"'),steam_before.replace(b'"730"',b'"731"')]:
        steam_path.write_bytes(changed);snapshot=tx.inventory(home);public('Restore',install_id=installed['installId'],good=False);assert tx.inventory(home)==snapshot
    steam_path.write_bytes(steam_before);checks.append('restore-rejects-build-state-app-drift')
    assert all(tx.sha(root / p) == e['sha256'] for p,e in m['changes'].items())
    assert all(tx.sha(root / p) == h for p,h in m['baselineFiles'].items() if p not in recovery.TARGETS)
    all_before = tx.inventory(home); public('Restore', install_id=installed['installId']); assert tx.inventory(home) == all_before
    public('Restore', True, installed['installId']); same(); checks.append('actual-five-file-public-roundtrip')
    # Every write can fail: rollback is byte-for-byte, leaving parent untouched.
    for index in range(len(recovery.TARGETS)):
        try: recovery.install(*args, apply=True, fake=True, fault=index)
        except RuntimeError as e: assert 'Synthetic write failure' in str(e)
        else: raise AssertionError('Expected injected failure')
        same(); checks.append('rollback-after-write-' + str(index))
    for index in range(len(recovery.TARGETS)):
        try: recovery.install(*args, apply=True, fake=True, fault='interrupt-' + str(index))
        except KeyboardInterrupt: pass
        else: raise AssertionError('Expected interruption')
        active = tx.read_json(root / recovery.MARKER)['installId']
        recovery.restore(*args, active, apply=True, fake=True); same(); checks.append('interruption-recovery-' + str(index))
    # Reject changed prerequisites before any target or history mutation.
    for rel in [tx.MARKER, 'game/bin/win64/engine2.dll', tx.HUMAN_GAMEDATA]:
        p = root / rel; data = p.read_bytes(); p.write_bytes(data + b' drift')
        snapshot = tx.inventory(home); public('Install', True, good=False); assert tx.inventory(home) == snapshot
        p.write_bytes(data); same(); checks.append('reject-drift-' + rel)
    unknown = root / 'game/csgo/addons/counterstrikesharp/plugins/Unknown/User.dll'
    write(unknown, 'unknown executable')
    snapshot = tx.inventory(home); public('Install', True, good=False); assert tx.inventory(home) == snapshot
    unknown.unlink(); unknown.parent.rmdir(); same(); checks.append('reject-new-plugin')
    original_record = (parent / 'install-record.json').read_bytes()
    write(parent / 'install-record.json', original_record + b' ')
    snapshot = tx.inventory(home); public('Install', True, good=False); assert tx.inventory(home) == snapshot
    write(parent / 'install-record.json', original_record); same(); checks.append('reject-parent-drift')
    installed = public('Install', True)
    snapshot = tx.inventory(home); public('Install', True, good=False); assert tx.inventory(home) == snapshot
    checks.append('reject-double-install')
    preimage = backups / installed['installId'] / 'preimages' / sorted(recovery.TARGETS)[0]
    data = preimage.read_bytes(); preimage.write_bytes(b'corrupt')
    snapshot = tx.inventory(home); public('Restore', True, installed['installId'], good=False); assert tx.inventory(home) == snapshot
    preimage.write_bytes(data); checks.append('reject-corrupt-preimage')
    live = root / sorted(recovery.TARGETS)[0]; data = live.read_bytes(); live.write_bytes(b'user change')
    snapshot = tx.inventory(home); public('Restore', True, installed['installId'], good=False); assert tx.inventory(home) == snapshot
    live.write_bytes(data); public('Restore', True, installed['installId']); same(); checks.append('reject-restore-drift')
    # No fake fixture is allowed to target a real PE executable.
    exe = root / 'game/bin/win64/cs2.exe'; data = exe.read_bytes(); exe.write_bytes(b'MZfake-pe')
    snapshot = tx.inventory(home); public('Install', True, good=False); assert tx.inventory(home) == snapshot
    exe.write_bytes(data); same(); checks.append('fake-rejects-pe')
    result = {'status':'PASS', 'candidate':str(candidate), 'manifestSha256':digest, 'checks':checks,
              'actualPayloadFiles':len(recovery.TARGETS), 'protectedFiles':len(m['baselineFiles']), 'parentUnchanged':True,
              'privatePresetsUnchanged':True, 'byteForByteRestored':True, 'realTargetApplied':False}
    (tx.REPO / 'temp/evidence/recovery-tests.json').write_bytes(tx.json_bytes(result))
    print(tx.json_bytes(result).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--candidate', required=True); p.add_argument('--manifest-sha256', required=True)
    a = p.parse_args(); run(a.candidate, a.manifest_sha256)
