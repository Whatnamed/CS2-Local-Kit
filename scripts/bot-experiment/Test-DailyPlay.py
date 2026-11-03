"""Actual daily bundle: old-overlay migration, finite image switching, exact recovery in a fake tree."""
import argparse
import copy
import importlib.util
from pathlib import Path
import shutil
import sys

sys.dont_write_bytecode = True
import bot_experiment as tx

spec = importlib.util.spec_from_file_location('integration_test', Path(__file__).with_name('Test-RuntimeIntegration14192.py'))
integration = importlib.util.module_from_spec(spec); spec.loader.exec_module(integration)


def run(release, digest):
    tx.MARKER = tx.INTEGRATION_MARKER
    (tx.REPO / 'temp/integration').mkdir(parents=True, exist_ok=True)
    home, root, package, backups, m, expected = integration.fixture(release, digest)
    baseline = tx.inventory(root)
    parent_images = {p['backupDir']: tx.inventory(p['backupDir']) for p in m['runtimeIntegration']['parents'].values()}
    catalog = tx.read_json(tx.REPO / 'src/CS2LocalKit.App/BotCandidates.json')
    old = next(c for c in catalog if c['ManifestSha256'] == 'fc09b409160d071a60a3d013fbb89d108d704ff4b65731dff96626823f006012')
    source, original = tx.load_candidate(Path(release).parent / old['Directory'], old['ManifestSha256'])
    old_package = home / 'old-package'; shutil.copytree(source, old_package)
    previous = copy.deepcopy(original); previous['testFixture'] = True
    # Cross-build withdrawal has its own transition test; start from its reviewed substrate here.
    for key in ['targetBuild', 'baselineFiles', 'gameBinaries', 'moduleInventory', 'runtimeIntegration']:
        previous[key] = copy.deepcopy(m[key])
    previous['coexistence']['reviewedBuild'] = m['targetBuild']
    previous['coexistence']['humanFiles'] = m['coexistence']['humanFiles']
    (old_package / 'experiment-manifest.json').write_bytes(tx.json_bytes(previous))
    old_digest = tx.sha(old_package / 'experiment-manifest.json')
    old_installed = tx.install(old_package, old_digest, root, backups, apply=True, fake=True, preserve_human=True)
    old_id = old_installed['installId']; old_backup = backups / old_id
    tx.restore(old_backup, apply=True, fake=True)
    tx.require(tx.inventory(root) == baseline, 'Old exact restore baseline mismatch')
    old_images = tx.inventory(old_backup)
    installed = tx.install(package, expected, root, backups, apply=True, fake=True, preserve_human=True)
    backup = backups / installed['installId']
    record_image = (backup / 'install-record.json').read_bytes()
    manifest_image = (backup / 'candidate-manifest.json').read_bytes()
    protected = {p: tx.identity(root / p) for p in m['baselineFiles'] if tx.human_path(p)}
    checks = ['old Low integration exact restore to same substrate', 'one new daily substrate installation']

    def mode(level, enabled):
        p = m['dailyPlay']['profiles'][level]
        current = tx.daily_image_path(root, m, tx.DAILY_PROFILE)
        current.write_bytes((root / p['target']).read_bytes())
        for target in sorted(tx.DAILY_LOADERS):
            current = tx.daily_image_path(root, m, target)
            desired = root / (target if enabled else target + tx.DAILY_SUFFIX)
            if current != desired: current.rename(desired)

    for enabled in [True, False, True]:
        for level in ['Low', 'High', 'Medium']:
            mode(level, enabled)
            before = tx.inventory(root)
            tx.restore(backup, fake=True)
            tx.require(tx.inventory(root) == before, 'Restore preview wrote daily files')
            tx.require((backup / 'install-record.json').read_bytes() == record_image and (backup / 'candidate-manifest.json').read_bytes() == manifest_image, 'Daily switch edited original transaction')
            tx.require(all(tx.identity(root / p) == image for p, image in protected.items()), 'Daily switch changed Human')
            checks.append(f'exact read-only diagnostics {level} / {"enhanced" if enabled else "normal"}')
    # All finite images also permit recovery after a partially completed mode switch.
    path = root / next(t for t in sorted(tx.DAILY_LOADERS) if t.endswith('.dll'))
    path.rename(str(path) + tx.DAILY_SUFFIX)
    tx.restore(backup, fake=True); checks.append('interrupted mode exact recovery preview')

    def rejected(label, path, content):
        before = path.read_bytes() if path.exists() else None
        path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(content)
        state = tx.inventory(root)
        try: tx.restore(backup, fake=True)
        except (ValueError, FileNotFoundError): pass
        else: raise AssertionError('Accepted ' + label)
        tx.require(tx.inventory(root) == state, 'Rejected preview mutated files')
        if before is None: path.unlink()
        else: path.write_bytes(before)
        checks.append('reject ' + label)

    rejected('unknown active profile', root / tx.DAILY_PROFILE, b'unknown profile')
    rejected('unknown profile source', root / m['dailyPlay']['profiles']['Low']['target'], b'unknown source')
    rejected('unknown module', root / 'game/csgo/addons/counterstrikesharp/plugins/Alien.dll', b'unknown module')
    rejected('unknown disabled loader', Path(str(path) + tx.DAILY_SUFFIX), b'unknown DLL')
    tx.restore(backup, apply=True, fake=True)
    tx.require(tx.inventory(root) == baseline, 'Daily exact restore paths/types/hashes mismatch')
    tx.require(tx.inventory(old_backup) == old_images, 'Old recovery chain changed')
    for folder, images in parent_images.items(): tx.require(tx.inventory(folder) == images, 'Parent transaction changed')
    checks += ['exact baseline restored after mutable state', 'old recovery plus three parent transactions unchanged']
    report = dict(status='PASS', candidate=str(release), manifestSha256=digest, checks=checks, syntheticRoot=str(root), actualGameModified=False, installed=False, gameplayAcceptance='pending')
    (tx.REPO / 'temp/daily-play-transactions.json').write_bytes(tx.json_bytes(report))
    print(tx.json_bytes(report).decode())


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True); p.add_argument('--manifest-sha256', required=True)
    args = p.parse_args(); run(args.release, args.manifest_sha256)
