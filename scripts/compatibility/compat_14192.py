"""Exact five-file recovery over an existing Medium + Human installation.

This is a build-specific recovery tool, not a replacement mode installer. The
parent Bot transaction is immutable. Restoring this layer returns its preimages.
All commands preview unless --apply is explicitly supplied; CS2 is never run.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import uuid

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bot-experiment'))
import bot_experiment as tx

MARKER = 'game/csgo/.cs2-local-kit-compat-14192.json'
TARGETS = {
    'game/csgo/addons/counterstrikesharp/bin/win64/counterstrikesharp.dll',
    'game/csgo/addons/counterstrikesharp/gamedata/gamedata.json',
    'game/csgo/addons/BotVision/gamedata.json',
    'game/csgo/addons/BotVision/bin/win64/BotVision.dll',
    'game/csgo/addons/counterstrikesharp/plugins/BotAimImprover/BotAimImprover.dll',
}


def load(release, expected, fake=False):
    release = tx.no_links(release)
    tx.require(tx.sha(release / 'recovery-manifest.json') == expected, 'Manifest digest mismatch')
    m = tx.read_json(release / 'recovery-manifest.json')
    tx.require(m['schemaVersion'] == 1 and m['lane'] == 'compat-14192', 'Wrong recovery lane')
    tx.require(m['targetBuild'] == {'PatchVersion': '1.41.9.2', 'ClientVersion': '2000935', 'buildId': '26144732'}, 'Wrong recovery build')
    tx.require(m['botRef'] == tx.BOT_REF and m['difficulty'] == 'Medium' and m['humanPolicy'] == 'preserve', 'Wrong joint configuration')
    tx.require(bool(m.get('testFixture')) == fake, 'Fake/production mismatch')
    actual = {p:v['sha256'] for p,v in tx.inventory(release).items()
              if v['kind'] == 'file' and p not in {'recovery-manifest.json', 'MANIFEST.sha256'}}
    tx.require(actual == m['bundleFiles'], 'Bundle file set/hash drift')
    tx.require(set(m['changes']) == TARGETS, 'Recovery may only write the five reviewed files')
    tx.require(TARGETS <= set(m['baselineFiles']), 'Missing preimages')
    for rel, entry in m['changes'].items():
        tx.require(tx.sha(tx.under(release, entry['source'])) == entry['sha256'], 'Payload digest mismatch')
        tx.require(entry['sha256'] != m['baselineFiles'][rel], 'Unnecessary recovery write')
    tx.require(set(m['gameBinaries']) == tx.GAME_BINARIES, 'Incomplete game identity')
    return release, m


def context(m, root, backup_root, fake):
    tx.ensure_closed()
    root = tx.validate_root(root, fake)
    tx.require(str(root) == m['cs2Root'], 'Wrong CS2 installation')
    backup_root = tx.no_links(backup_root)
    parent = tx.no_links(m['parent']['backupDir'])
    for other in [root, parent]:
        tx.require(not backup_root.is_relative_to(other) and not other.is_relative_to(backup_root), 'Recovery backups must be separate from game and parent transaction')
    tx.require(root.drive.casefold() == backup_root.drive.casefold(), 'Backups must use the same volume')
    tx.require(tx.inventory(parent) == m['parent']['inventory'], 'Original transaction/backup drift')
    record = tx.read_json(parent / 'install-record.json')
    tx.require(record['state'] == 'installed' and record['installId'] == m['parent']['installId'] and record['cs2Root'] == str(root), 'Parent is not the reviewed active installation')
    tx.require(tx.sha(parent / 'candidate-manifest.json') == record['manifestSha256'], 'Parent candidate mismatch')
    marker = tx.read_json(root / tx.MARKER)
    tx.require(marker['installId'] == record['installId'] and marker['manifestSha256'] == record['manifestSha256'], 'Parent marker mismatch')
    steam = tx.steam_idle(root)
    # The original digest remains provenance, not an installation identity:
    # LastPlayed/LastOwner can legitimately change after an accepted session.
    # Pin app/build/path; images() still verifies all five game binaries and
    # every protected pre/postimage. In-flight writes retain the raw hash guard.
    tx.steam_installation(root, m['targetBuild']['buildId'])
    return root, backup_root, steam


def images(m, root, installed=False, partial=False):
    for rel, before in m['baselineFiles'].items():
        expected = m['changes'][rel]['sha256'] if installed and rel in TARGETS else before
        allowed = [expected]
        if partial and rel in TARGETS:
            allowed.append(m['changes'][rel]['sha256'])
        tx.require(tx.identity(tx.under(root, rel)) in [{'kind':'file', 'sha256':v} for v in allowed], 'Protected image drift: ' + rel)
    # Include directory identities so new executable plugins cannot slip through.
    actual = tx.module_inventory(root)
    for folder, rows in actual.items():
        for path, value in rows.items():
            rel = folder + '/' + path
            if rel not in TARGETS or value.get('kind') != 'file':
                continue
            allowed = {m['changes'][rel]['sha256']} if installed else {m['baselineFiles'][rel]}
            if partial: allowed.add(m['changes'][rel]['sha256'])
            tx.require(value['sha256'] in allowed, 'Module image drift: ' + rel)
            value['sha256'] = m['baselineFiles'][rel]
    tx.require(actual == m['moduleInventory'], 'Plugin/module inventory drift')
    gi = (root / 'game/csgo/gameinfo.gi').read_text(encoding='utf-8-sig')
    tx.require(gi.count('csgo/overrides/botprofile.vpk') == 1 and gi.count('csgo/addons/metamod') == 1, 'Startup entries drift')


def save(backup, record):
    temporary = backup / 'recovery-record.next.json'
    temporary.write_bytes(tx.json_bytes(record))
    os.replace(temporary, backup / 'recovery-record.json')


def restore(release, expected, root, backup_root, install_id, apply=False, fake=False):
    release, m = load(release, expected, fake)
    root, backup_root, steam = context(m, root, backup_root, fake)
    tx.require(len(install_id or '') == 32 and all(c in '0123456789abcdef' for c in install_id), 'Exact recovery install ID required')
    backup = tx.under(backup_root, install_id)
    r = tx.read_json(backup / 'recovery-record.json')
    tx.require(r['installId'] == install_id and r['manifestSha256'] == expected and r['cs2Root'] == str(root), 'Recovery record identity mismatch')
    tx.require(r['state'] in {'installing', 'installed', 'restoring', 'restore-failed'}, 'Recovery layer is not active')
    tx.require(tx.sha(backup / 'recovery-manifest.json') == expected, 'Backup manifest drift')
    partial = r['state'] != 'installed'
    images(m, root, installed=not partial, partial=partial)
    marker = tx.under(root, MARKER)
    expected_marker = tx.json_bytes({'installId':install_id, 'manifestSha256':expected})
    tx.require(marker.is_file() and marker.read_bytes() == expected_marker, 'Recovery marker mismatch')
    for rel in TARGETS:
        tx.require(tx.sha(tx.under(backup, 'preimages/' + rel)) == m['baselineFiles'][rel], 'Recovery preimage corrupted: ' + rel)
    result = {'state':'restore-preview', 'installId':install_id, 'backupDir':str(backup), 'restores':sorted(TARGETS), 'parentPreserved':True}
    if not apply:
        return result
    r['state'] = 'restoring'; save(backup, r)
    try:
        for rel in sorted(TARGETS):
            tx.ensure_closed()
            tx.require(tx.steam_idle(root) == steam, 'Steam changed during recovery')
            context(m, root, backup_root, fake)
            target = tx.under(root, rel)
            tx.require(tx.sha(target) in {m['baselineFiles'][rel], m['changes'][rel]['sha256']}, 'Concurrent file drift')
            staged = backup / ('restore-' + uuid.uuid4().hex)
            shutil.copyfile(tx.under(backup, 'preimages/' + rel), staged)
            tx.require(tx.sha(staged) == m['baselineFiles'][rel], 'Restore staging drift')
            os.replace(staged, target)
        images(m, root)
        tx.require(marker.read_bytes() == expected_marker, 'Recovery marker changed')
        marker.unlink()
        r['state'] = 'restored'; save(backup, r)
    except BaseException:
        r['state'] = 'restore-failed'; save(backup, r)
        raise
    return {**result, 'state':'restored'}


def install(release, expected, root, backup_root, apply=False, fake=False, fault=None):
    release, m = load(release, expected, fake)
    root, backup_root, steam = context(m, root, backup_root, fake)
    tx.require(not release.is_relative_to(root) and not root.is_relative_to(release), 'Bundle and game roots overlap')
    tx.require(not tx.under(root, MARKER).exists(), 'Restore the active recovery layer first')
    if backup_root.exists():
        for path in backup_root.iterdir():
            tx.no_links(path)
            if path.is_dir() and (path / 'recovery-record.json').exists():
                old = tx.read_json(path / 'recovery-record.json')
                tx.require(old['cs2Root'] != str(root) or old['state'] == 'restored', 'Unrestored recovery history: ' + path.name)
    images(m, root)
    tx.require(not fault or fake, 'Fault injection is synthetic-only')
    result = {'state':'preview', 'changes':sorted(TARGETS), 'retainedFiles':len(m['baselineFiles'])-len(TARGETS), 'parentInstallId':m['parent']['installId'], 'difficulty':'Medium', 'humanPolicy':'preserve'}
    if not apply:
        return result
    install_id = uuid.uuid4().hex
    backup = tx.under(backup_root, install_id); backup.mkdir(parents=True)
    shutil.copyfile(release / 'recovery-manifest.json', backup / 'recovery-manifest.json')
    for rel, entry in m['changes'].items():
        before = tx.under(backup, 'preimages/' + rel); before.parent.mkdir(parents=True, exist_ok=True)
        staged = tx.under(backup, 'staging/' + rel); staged.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / rel, before); shutil.copyfile(tx.under(release, entry['source']), staged)
        tx.require(tx.sha(before) == m['baselineFiles'][rel] and tx.sha(staged) == entry['sha256'], 'Backup/staging verification failed')
    r = {'installId':install_id, 'manifestSha256':expected, 'cs2Root':str(root), 'state':'installing'}
    save(backup, r)
    # Exclusive marker creation arbitrates concurrent installations before writes.
    images(m, root); context(m, root, backup_root, fake)
    with tx.under(root, MARKER).open('xb') as stream:
        stream.write(tx.json_bytes({'installId':install_id, 'manifestSha256':expected}))
    try:
        for index, rel in enumerate(sorted(TARGETS)):
            tx.ensure_closed(); context(m, root, backup_root, fake)
            tx.require(tx.steam_idle(root) == steam and tx.sha(root / rel) == m['baselineFiles'][rel], 'Concurrent target/Steam drift')
            os.replace(tx.under(backup, 'staging/' + rel), tx.under(root, rel))
            tx.require(tx.sha(root / rel) == m['changes'][rel]['sha256'], 'Postimage verification failed')
            if fault == index:
                raise RuntimeError('Synthetic write failure')
            if fault == 'interrupt-' + str(index):
                raise KeyboardInterrupt('Synthetic hard interruption')
        images(m, root, installed=True)
        r['state'] = 'installed'; save(backup, r)
    except Exception:
        restore(release, expected, root, backup_root, install_id, True, fake)
        raise
    return {**result, 'state':'installed', 'installId':install_id, 'backupDir':str(backup)}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['preview', 'install', 'restore'])
    p.add_argument('--release', required=True); p.add_argument('--manifest-sha256', required=True)
    p.add_argument('--root', required=True); p.add_argument('--backup-root', required=True)
    p.add_argument('--install-id'); p.add_argument('--apply', action='store_true')
    p.add_argument('--fake', action='store_true')
    a = p.parse_args()
    if a.action == 'restore':
        result = restore(a.release, a.manifest_sha256, a.root, a.backup_root, a.install_id, a.apply, a.fake)
    else:
        result = install(a.release, a.manifest_sha256, a.root, a.backup_root, a.apply and a.action == 'install', a.fake)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
