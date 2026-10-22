"""Bounded Bot experiment transaction. Python 3.12+, Windows, standard library only.

The manifest digest is an explicit input, never inferred from a mutable latest link.
Nothing writes to the target without apply=True. This module never starts CS2.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import uuid

BOT_REF = 'e18c029a76b7515ea5cb15cb08c41f03de4c0a57'
MARKER = 'game/csgo/.cs2-local-kit-bot-experiment.json'
REPO = Path(__file__).resolve().parents[2]
MM_FILES = {'addons/metamod/bin/win64/metamod.2.cs2.dll', 'addons/metamod/bin/win64/server.dll'}
PLUGINS = {'BotAI', 'BotAimImprover', 'BotBuy', 'BotRandomizer', 'BotState', 'NadeSystem', 'BotControllerImpl', 'BotHiderImpl', 'LocalKitMatchSetup'}
GAME_BINARIES = {'game/csgo/bin/win64/server.dll','game/bin/win64/engine2.dll','game/csgo/bin/win64/client.dll','game/bin/win64/schemasystem.dll','game/bin/win64/cs2.exe'}
HUMAN_PLUGIN = 'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator'
HUMAN_CONFIG = 'game/csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator'
HUMAN_GAMEDATA = 'game/csgo/addons/counterstrikesharp/gamedata/inventory-simulator.json'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + '\n').encode('utf-8')


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, f'Duplicate JSON key: {key}')
            result[key] = value
        return result
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def no_links(path):
    path = Path(os.path.abspath(path))
    for probe in [path, *path.parents]:
        try:
            info = probe.lstat()
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(info.st_mode) and not (getattr(info, 'st_file_attributes', 0) & 0x400), f'Reparse/link refused: {probe}')
    return path


def relative(value):
    require(isinstance(value, str) and value and '\\' not in value and ':' not in value, f'Invalid relative path: {value}')
    require(not value.startswith('/') and all(p not in {'', '.', '..'} and not p.endswith((' ', '.')) for p in value.split('/')), f'Unsafe relative path: {value}')
    require(not re.search(r'[<>"|?*\x00-\x1f]', value), f'Unsafe Windows path: {value}')
    require(all(p.split('.')[0].upper() not in {'CON','PRN','AUX','NUL',*[f'COM{i}' for i in range(1,10)],*[f'LPT{i}' for i in range(1,10)]} for p in value.split('/')), f'Reserved Windows name: {value}')
    return value


def under(root, value):
    return no_links(no_links(root) / relative(value))


def inventory(root):
    """Never follows a junction; include directories so empty-tree restoration is testable."""
    root = no_links(root)
    if not root.exists():
        return {}
    require(root.is_dir(), f'Expected directory: {root}')
    result = {}
    seen = set()
    def walk(folder):
        for p in sorted(folder.iterdir()):
            no_links(p)
            key = p.relative_to(root).as_posix()
            require(key.casefold() not in seen, f'Case collision: {key}')
            seen.add(key.casefold())
            if p.is_dir():
                result[key] = {'kind': 'directory'}
                walk(p)
            else:
                require(p.is_file(), f'Unsupported file type: {p}')
                result[key] = {'kind': 'file', 'sha256': sha(p)}
    walk(root)
    return result


def identity(path):
    path = no_links(path)
    if not path.exists():
        return None
    if path.is_dir():
        return {'kind': 'directory', 'sha256': digest(json_bytes(inventory(path)))}
    require(path.is_file(), f'Unsupported target: {path}')
    return {'kind': 'file', 'sha256': sha(path)}


def ensure_closed():
    if os.name == 'nt':
        output = subprocess.check_output(['tasklist', '/FI', 'IMAGENAME eq cs2.exe', '/NH'], text=True, errors='replace')
        require(not re.search(r'(?im)^cs2\.exe\s', output), 'cs2.exe is running; operation refused')


def steam_idle(root):
    """Read the local Steam update state; never changes Steam settings."""
    path = no_links(Path(root).parent.parent / 'appmanifest_730.acf')
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    def value(key, required=False):
        matches = re.findall(r'"'+re.escape(key)+r'"\s+"([^"\r\n]*)"', text)
        require(len(matches) <= 1 and (not required or len(matches) == 1), 'Steam state missing/ambiguous: '+key)
        return matches[0] if matches else None
    require(value('StateFlags', True) == '4', 'Steam update/install activity or unknown state; operation refused')
    build = value('buildid', True)
    require(value('TargetBuildID') in {None, '', '0', build}, 'Steam target build is changing')
    for total, done in [('BytesToDownload','BytesDownloaded'),('BytesToStage','BytesStaged')]:
        a,b = value(total),value(done)
        require(a is None and b is None or a is not None and a == b, 'Steam transfer/staging incomplete')
    return digest(raw)


def human_path(path):
    return path == HUMAN_GAMEDATA or any(path.startswith(p+'/') for p in [HUMAN_PLUGIN,HUMAN_CONFIG])


def validate_coexistence(m):
    policy = m.get('humanPolicy', 'isolate')
    require(policy in {'isolate','preserve'}, 'Unknown Human policy')
    if policy == 'isolate':
        require('coexistence' not in m, 'Coexistence evidence requires preserve policy')
        return
    c = m['coexistence']
    require(c['status'] == 'ready-for-manual-test' and c['reviewedBuild'] == m['targetBuild'], 'Joint compatibility gate has not passed for this exact build')
    require(re.fullmatch('[0-9a-f]{64}', c.get('evidenceSha256','')), 'Exact joint compatibility evidence digest required')
    protected = {p:h for p,h in m['baselineFiles'].items() if human_path(p)}
    required = {HUMAN_PLUGIN+'/InventorySimulator.dll', HUMAN_CONFIG+'/inventories.json', HUMAN_GAMEDATA}
    require(required <= set(protected) and protected == c['humanFiles'], 'Complete Human plugin/config/gamedata preimages required')
    require(not any(human_path(e['target']) for e in m['files']), 'Joint payload cannot overwrite Human projection')
    for rel in c['isolatedModules']:
        relative(rel)
        require(not human_path(rel+'/') and rel != HUMAN_PLUGIN, 'Human plugin cannot be isolated in joint mode')


def validate_root(root, fake):
    root = no_links(root)
    require((root / 'game/csgo').is_dir(), 'Explicit CS2 root must contain game/csgo')
    if fake:
        require(root.is_relative_to(REPO / 'temp'), 'Fake root must be inside this worktree temp/')
        require((root / '.bot-experiment-fake-root').read_text() == 'synthetic-test-only', 'Fake-root marker missing')
        require((root / 'game/bin/win64/cs2.exe').read_bytes()[:2] != b'MZ', 'Fake mode refuses a real executable')
    return root


def payload_allowed(target, surface):
    prefix = 'game/csgo/'
    if not target.startswith(prefix):
        return False
    rel = target[len(prefix):]
    if surface == 'framework':
        return rel in MM_FILES
    if surface != 'bot':
        return False
    parts = rel.split('/')
    if rel == 'overrides/botprofile.vpk':
        return True
    if rel.startswith('cfg/'):
        return len(parts) == 2 and rel.endswith('.cfg') and 'rush' not in rel.lower()
    if rel.startswith('addons/counterstrikesharp/plugins/'):
        return len(parts) >= 5 and parts[3] in PLUGINS
    if rel.startswith('addons/counterstrikesharp/shared/'):
        return len(parts) >= 5 and parts[3] in {'BotHiderApi','BotControllerApi','0Harmony'}
    if rel.startswith('addons/counterstrikesharp/data/NadeSystem/'):
        return True
    if rel.startswith('addons/metamod/'):
        return rel in {f'addons/metamod/{n}.vdf' for n in ['BotHider','BotController','BotVision']}
    return any(rel.startswith(f'addons/{n}/') for n in ['BotHider','BotController','BotVision'])


def load_candidate(release, expected_sha, fake=False):
    release = no_links(release)
    manifest_path = under(release, 'experiment-manifest.json')
    require(re.fullmatch('[0-9a-f]{64}', expected_sha or '') and sha(manifest_path) == expected_sha, 'Explicit manifest SHA256 mismatch')
    m = read_json(manifest_path)
    require(m['schemaVersion'] == 1 and m['lane'] == 'bot-experiment' and m['status'] == 'candidate', 'Unsupported experiment manifest')
    require(m['botRef'] == BOT_REF and m['metamod']['build'] == 1481 and m['cssVersion'] == '1.0.376', 'Experiment source/framework drift')
    require(bool(m.get('testFixture', False)) == bool(fake), 'Synthetic and production manifests cannot be interchanged')
    require(set(m['gameBinaries']) == GAME_BINARIES and GAME_BINARIES <= set(m['baselineFiles']), 'Five exact game binary identities are required')
    # A release is an exact file set, including scripts, licenses and corresponding sources.
    actual = {k:v['sha256'] for k,v in inventory(release).items() if v['kind']=='file' and k not in {'experiment-manifest.json','MANIFEST.sha256'}}
    require(actual == m['bundleFiles'], 'Bundle file set/hash mismatch')
    seen = set()
    for e in m['files']:
        key = relative(e['target']).casefold()
        require(key not in seen, f'Duplicate/case-colliding target: {key}')
        seen.add(key)
        require(payload_allowed(e['target'], e['surface']), f'Out-of-lane target: {key}')
        require(sha(under(release, e['source'])) == e['sha256'], f'Payload hash mismatch: {key}')
    require({e['target'][len('game/csgo/'):] for e in m['files'] if e['surface']=='framework'} == MM_FILES, 'Incomplete framework overlay')
    for path in m['baselineFiles']:
        relative(path)
    validate_profile(release, m)
    validate_coexistence(m)
    return release, m


def validate_profile(release, m):
    # Original Medium-only schema-1 bundles remain readable; extended variants
    # explicitly bind source DB, immutable VPK and the selected difficulty.
    if 'profile' not in m:
        require(m.get('defaultDifficulty', 'Medium') == 'Medium', 'Profile metadata required')
        return
    p = m['profile']; level = p['difficulty']
    require(level in {'Low','Medium','High'} and m['defaultDifficulty'] == level, 'Profile selection mismatch')
    require(p['upstreamPath'] == f'overrides/{level}/botprofile.db' and p['dbSource'] == f'profiles/{level}/botprofile.db', 'Profile source mismatch')
    db = under(release, p['dbSource'])
    require(sha(db) == p['dbSha256'], 'Profile DB hash mismatch')
    entries = [e for e in m['files'] if e['target'] == 'game/csgo/overrides/botprofile.vpk']
    require(len(entries) == 1 and entries[0]['sha256'] == p['vpkSha256'], 'Profile VPK selection mismatch')
    # Validate the exact VPK v2 layout emitted by the existing project packer.
    import struct
    data = under(release, entries[0]['source']).read_bytes()
    require(len(data) >= 28 and struct.unpack_from('<II', data) == (0x55AA1234,2), 'Invalid profile VPK')
    size = struct.unpack_from('<I', data,8)[0]
    expected = db.read_bytes()
    import zlib
    tree = data[28:28+size]
    prefix = b'db\x00 \x00botprofile\x00'
    require(tree.startswith(prefix), 'Unexpected profile VPK entry')
    offset = len(prefix)
    crc,preload,archive,entry_offset,length,terminator = struct.unpack_from('<IHHIIH',tree,offset)
    require(crc == zlib.crc32(expected) and preload == 0 and archive == 0x7fff and entry_offset == 0 and length == len(expected) and terminator == 0xffff, 'Profile VPK metadata mismatch')
    require(struct.unpack_from('<IIII',data,12)==(len(expected),0,48,0) and tree[offset+18:]==b'\x00\x00\x00' and data[28+size:28+size+length]==expected and len(data)==28+size+length+48, 'Profile VPK does not embed exact DB')
    hashes=data[-48:]
    require(hashes[:16]==hashlib.md5(tree).digest() and hashes[16:32]==hashlib.md5(b'').digest() and hashes[32:]==hashlib.md5(data[:-16]).digest(), 'Profile VPK checksum mismatch')


def module_inventory(root):
    result = {}
    for rel in ['game/csgo/addons/counterstrikesharp/plugins', 'game/csgo/addons/metamod']:
        folder = under(root, rel)
        result[rel] = inventory(folder)
    addons=under(root,'game/csgo/addons')
    result['valveServerPluginDeclarations']={p.name:identity(p) for p in sorted(addons.glob('*.vdf'))}
    # MetaMod plugins in metaplugins.ini can bypass VDF isolation: reject active lines.
    ini = under(root, 'game/csgo/addons/metamod/metaplugins.ini')
    if ini.exists():
        require(not any(line.strip() and not line.lstrip().startswith(';') for line in ini.read_text(encoding='utf-8-sig').splitlines()), 'Active metaplugins.ini entries require separate review')
    return result


def gameinfo_bytes(root):
    raw = under(root, 'game/csgo/gameinfo.gi').read_bytes()
    text = raw.decode('utf-8-sig')
    require(len(re.findall(r'(?m)^\s*SearchPaths\s*$', text)) == 1, 'Ambiguous SearchPaths')
    require(len(re.findall(r'(?m)^\s*Game\s+csgo/addons/metamod\s*$', text)) == 1, 'MetaMod startup entry must occur exactly once')
    require(not re.search(r'(?im)^\s*Game\s+[^\r\n]*(?:overrides|addons/(?!metamod(?:\s|$)))', text), 'Unknown addon/override startup entry')
    matches = list(re.finditer(r'(?m)^([ \t]*)SearchPaths[ \t]*\r?\n[ \t]*\{[^\r\n]*\r?\n', text))
    require(len(matches)==1, 'Missing SearchPaths insertion point')
    newline = '\r\n' if '\r\n' in text else '\n'
    insert = matches[0].end()
    updated = text[:insert] + '\t\t\tGame\tcsgo/overrides/botprofile.vpk' + newline + text[insert:]
    return raw, (b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + updated.encode('utf-8')


def active_history(root, backup_root):
    if backup_root.exists():
        no_links(backup_root)
        for p in backup_root.iterdir():
            no_links(p)
            if not p.is_dir():
                continue
            record_path = under(p, 'install-record.json')
            if record_path.exists():
                r = read_json(record_path)
                require(r.get('cs2Root') != str(root) or r.get('state') in {'restored','rolled-back'}, f'Unrestored transaction: {p.name}')


def make_plan(release, manifest_sha, root, backup_root, fake=False, isolate_human=False, preserve_human=False):
    ensure_closed()
    root = validate_root(root, fake)
    steam_idle(root)
    release, m = load_candidate(release, manifest_sha, fake)
    require(not (preserve_human and isolate_human), 'Human policies are mutually exclusive')
    require(preserve_human == (m.get('humanPolicy','isolate') == 'preserve'), 'Explicit preserve-Human intent must match candidate policy')
    backup_root = no_links(backup_root)
    require(not backup_root.is_relative_to(root) and not root.is_relative_to(backup_root), 'Backup and target roots must be disjoint')
    require(not release.is_relative_to(root) and not root.is_relative_to(release), 'Release and target roots must be disjoint')
    require(root.drive.casefold() == backup_root.drive.casefold(), 'Isolation requires backup on the same volume')
    require(not under(root, MARKER).exists(), 'Active experiment marker; use exact install ID to restore')
    active_history(root, backup_root)
    for rel, expected in m['baselineFiles'].items():
        require(identity(under(root, rel)) == {'kind':'file','sha256':expected}, f'Baseline identity mismatch: {rel}')
    for rel, expected in m['gameBinaries'].items():
        require(sha(under(root, rel)) == expected, f'Game binary drift: {rel}')
    inf = under(root, 'game/csgo/steam.inf').read_text()
    for key, value in m['targetBuild'].items():
        if key != 'buildId':
            require(re.search(rf'(?m)^{re.escape(key)}={re.escape(value)}\s*$', inf), f'CS2 build drift: {key}')
    steam_manifest = no_links(root.parent.parent / 'appmanifest_730.acf')
    require(re.search(r'"buildid"\s+"'+re.escape(m['targetBuild']['buildId'])+'"', steam_manifest.read_text()), 'Steam buildId mismatch')
    require(module_inventory(root) == m['moduleInventory'], 'Plugin/module inventory drift')
    original_gi, new_gi = gameinfo_bytes(root)
    entries = []
    isolated = []
    plugin_dir = under(root, 'game/csgo/addons/counterstrikesharp/plugins')
    if plugin_dir.exists():
        for p in sorted(plugin_dir.iterdir()):
            no_links(p)
            if p.is_dir() and p.name in PLUGINS:
                require(not any(v['kind']=='file' for v in inventory(p).values()), f'Unrecognized old Bot residue: {p.name}')
                continue
            if p.is_dir() and inventory(p) or p.is_file() and p.suffix.lower()=='.dll':
                if p.name == 'InventorySimulator':
                    if m.get('humanPolicy') == 'preserve':
                        continue
                    require(isolate_human, 'Explicit -IsolateHumanCosmetics required')
                isolated.append(p.relative_to(root).as_posix())
    meta = under(root, 'game/csgo/addons/metamod')
    for p in meta.glob('*.vdf'):
        if p.name.lower() == 'counterstrikesharp.vdf':
            continue
        content = p.read_text(encoding='utf-8-sig')
        if re.search(r'(?i)metamod plugin|addons[/\\].*\.dll', content):
            require(p.name not in {'BotHider.vdf','BotVision.vdf','BotController.vdf'}, 'Old Bot module declaration')
            isolated.append(p.relative_to(root).as_posix())
    for p in under(root,'game/csgo/addons').glob('*.vdf'):
        if p.name.lower() not in {'metamod.vdf','metamod_x64.vdf'}:
            isolated.append(p.relative_to(root).as_posix())
    if m.get('humanPolicy') == 'preserve':
        require({p:identity(under(root,p)) for p in isolated} == m['coexistence']['isolatedModules'], 'Unknown/unreviewed executable module; joint isolation ownership mismatch')
    for rel in isolated:
        entries.append({'target':rel,'action':'isolated','before':identity(under(root,rel)),'after':None,'surface':'isolation'})
    for e in m['files']:
        before = identity(under(root,e['target']))
        require(before is None or before['kind']=='file', f'File target became directory: {e["target"]}')
        # Only known baseline preimages can be overwritten. A newly appearing collision is not ours.
        require(before is None or e['target'] in m['baselineFiles'], f'Unknown target conflict: {e["target"]}')
        after = {'kind':'file','sha256':e['sha256']}
        action = 'retained' if before == after else 'overwritten' if before else 'created'
        entries.append({**e,'action':action,'before':before,'after':after})
    entries.append({'target':'game/csgo/gameinfo.gi','action':'modified','surface':'gameinfo','before':{'kind':'file','sha256':digest(original_gi)},'after':{'kind':'file','sha256':digest(new_gi)}})
    targets = {e['target'] for e in entries}
    for rel, value in m['baselineFiles'].items():
        if rel in targets or any(rel.startswith(p+'/') for p in isolated):
            continue
        entries.append({'target':rel,'action':'retained','surface':'protected','before':{'kind':'file','sha256':value},'after':{'kind':'file','sha256':value}})
    require(len({e['target'].casefold() for e in entries})==len(entries), 'Plan target collision')
    return root, release, m, entries, new_gi


def save_record(backup, record):
    tmp = under(backup, 'install-record.next.json')
    tmp.write_bytes(json_bytes(record))
    os.replace(tmp, under(backup, 'install-record.json'))


def preflight_restore(root, backup, record, partial=False):
    ensure_closed()
    steam_idle(root)
    require(record['schemaVersion']==1 and record['lane']=='bot-experiment', 'Unsupported install record')
    require(record['cs2Root']==str(root) and record['installId']==backup.name, 'Install ID / root mismatch')
    require(record['state'] in {'installed','installing','restoring','restore-failed'}, 'Transaction is not active')
    require(sha(under(backup,'candidate-manifest.json'))==record['manifestSha256'], 'Backup manifest corrupted')
    candidate = read_json(under(backup,'candidate-manifest.json'))
    validate_coexistence(candidate)
    require(record.get('humanPolicy','isolate') == candidate.get('humanPolicy','isolate') and record.get('coexistence') == candidate.get('coexistence'), 'Recorded Human policy/evidence mismatch')
    require(record.get('profile') == candidate.get('profile'), 'Recorded profile mismatch')
    if record.get('profile'):
        selected = [e for e in record['entries'] if e['target']=='game/csgo/overrides/botprofile.vpk']
        require(len(selected)==1 and selected[0]['after']['sha256']==record['profile']['vpkSha256'], 'Recorded VPK mismatch')
    if not partial:
        require(module_inventory(root)==record['moduleInventoryAfter'], 'Installed plugin/module inventory drift')
    targets=set()
    for e in record['entries']:
        rel=relative(e['target']);key=rel.casefold()
        require(key not in targets, 'Duplicate record target');targets.add(key)
        require(rel==MARKER or rel in record['baselineFiles'] or payload_allowed(rel,e['surface']) or e['action']=='isolated' and rel.startswith(('game/csgo/addons/counterstrikesharp/plugins/','game/csgo/addons/metamod/')), f'Unsafe record target: {rel}')
        live=identity(under(root,rel))
        if e['action']=='retained':
            require(live==e['before'], f'Retained protected file drift: {rel}')
            continue
        if e['before'] is not None:
            require(identity(under(backup,'preimages/'+rel))==e['before'], f'Backup missing/corrupt: {rel}')
        allowed=[e['after']]
        if partial:
            allowed.append(e['before'])
        require(live in allowed, f'Installed image drift / isolation conflict: {rel}')
        if e['action']=='isolated' and live is None:
            require(identity(under(backup,'isolated/'+rel))==e['before'], f'Isolated backup missing: {rel}')
    # Reject unknown files inside created Bot directories before touching any other file.
    for rel in record['createdDirectories']:
        p=under(root,rel)
        if not p.exists():continue
        for child, value in inventory(p).items():
            full=rel+'/'+child
            if value['kind']=='file':
                require(full.casefold() in targets, f'Unknown file in created directory: {full}')


def restore(backup, apply=False, fake=False):
    backup=no_links(backup)
    require(backup.is_dir(), 'Exact backup directory is required')
    record=read_json(under(backup,'install-record.json'))
    root=validate_root(record['cs2Root'],fake)
    require(bool(record['testFixture'])==bool(fake), 'Fake/production record mismatch')
    partial=record['state']!='installed'
    preflight_restore(root,backup,record,partial)
    if not apply:return record
    record['state']='restoring';save_record(backup,record)
    try:
        steam_identity = steam_idle(root)
        for e in reversed(record['entries']):
            if e['action']=='retained':continue
            ensure_closed()
            require(steam_idle(root) == steam_identity, 'Steam manifest changed during restoration; preserve the exact recovery record')
            target=under(root,e['target']);live=identity(target)
            if live==e['before']:continue
            require(live==e['after'], f'Drift after preflight: {e["target"]}')
            if e['action']=='isolated':
                source=under(backup,'isolated/'+e['target'])
                require(identity(source)==e['before'] and not target.exists(), 'Isolation conflict after preflight')
                os.replace(source,target)
            elif e['before'] is None:
                require(target.is_file(), 'Created file type drift')
                target.unlink()
            else:
                staged=under(backup,'restore-staging/'+uuid.uuid4().hex)
                staged.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(under(backup,'preimages/'+e['target']),staged)
                require(identity(staged)==e['before'], 'Restoration staging mismatch')
                os.replace(staged,target)
            require(identity(target)==e['before'], f'Restore verification failed: {e["target"]}')
        for rel in sorted(set(record['createdDirectories']),key=lambda x:x.count('/'),reverse=True):
            p=under(root,rel)
            if p.exists():
                require(p.is_dir() and not list(p.iterdir()), f'Created directory not empty: {rel}')
                p.rmdir()
        require(all(identity(under(root,e['target']))==e['before'] for e in record['entries']), 'Post-restore identity mismatch')
        record['state']='restored';save_record(backup,record)
        return record
    except Exception:
        record['state']='restore-failed';save_record(backup,record)
        raise


def install(release, manifest_sha, root, backup_root, apply=False, fake=False, isolate_human=False, fault=None, preserve_human=False):
    root,release,m,entries,new_gi=make_plan(release,manifest_sha,root,backup_root,fake,isolate_human,preserve_human)
    require(not fault or fake, 'Fault injection is fake-tree only')
    if not apply:return {'state':'preview','entries':entries}
    install_id=uuid.uuid4().hex
    backup=under(backup_root,install_id)
    marker={'installId':install_id,'manifestSha256':manifest_sha,'backupRoot':str(no_links(backup_root))}
    if m.get('humanPolicy') == 'preserve': marker['humanPolicy']='preserve'
    marker_bytes=json_bytes(marker)
    # Marker is applied first and restored last.
    entries.insert(0,{'target':MARKER,'surface':'marker','action':'created','before':None,'after':{'kind':'file','sha256':digest(marker_bytes)}})
    backup.mkdir(parents=True)
    shutil.copyfile(release/'experiment-manifest.json',backup/'candidate-manifest.json')
    record={'schemaVersion':1,'lane':'bot-experiment','installId':install_id,'cs2Root':str(root),'manifestSha256':manifest_sha,'testFixture':fake,'state':'installing','entries':entries,'createdDirectories':[],'baselineFiles':m['baselineFiles']}
    if 'profile' in m: record['profile']=m['profile']
    if 'humanPolicy' in m: record['humanPolicy']=m['humanPolicy']
    if 'coexistence' in m: record['coexistence']=m['coexistence']
    # Copy and verify every preimage before the first mutation of the target.
    for e in entries:
        if e['before'] is not None and e['action']!='retained':
            dest=under(backup,'preimages/'+e['target']);dest.parent.mkdir(parents=True,exist_ok=True)
            source=under(root,e['target'])
            if source.is_dir():shutil.copytree(source,dest)
            else:shutil.copyfile(source,dest)
            require(identity(dest)==e['before'], 'Preimage backup verification failed')
        if e['action'] not in {'isolated','retained'}:
            staged=under(backup,'staging/'+e['target']);staged.parent.mkdir(parents=True,exist_ok=True)
            if e['target']==MARKER:staged.write_bytes(marker_bytes)
            elif e['surface']=='gameinfo':staged.write_bytes(new_gi)
            else:shutil.copyfile(under(release,e['source']),staged)
            require(identity(staged)==e['after'], 'Candidate staging verification failed')
    # Record missing parents before writes, making hard-interruption recovery deterministic.
    parents=set()
    for e in entries:
        if e['action'] in {'created','overwritten','modified'}:
            p=under(root,e['target']).parent
            while p!=root and not p.exists():parents.add(p.relative_to(root).as_posix());p=p.parent
    record['createdDirectories']=sorted(parents,key=lambda x:x.count('/'))
    save_record(backup,record)
    try:
        steam_identity = steam_idle(root)
        for e in entries:
            if e['action']=='retained':continue
            ensure_closed()
            require(steam_idle(root) == steam_identity, 'Steam manifest changed during installation; preserve the exact recovery record')
            target=under(root,e['target'])
            require(identity(target)==e['before'], f'Changed after preflight: {e["target"]}')
            target.parent.mkdir(parents=True,exist_ok=True)
            if e['action']=='isolated':
                dest=under(backup,'isolated/'+e['target']);dest.parent.mkdir(parents=True,exist_ok=True)
                os.replace(target,dest)
            elif e['surface']=='marker':
                with target.open('xb') as stream:stream.write(marker_bytes)
            else:
                os.replace(under(backup,'staging/'+e['target']),target)
            require(identity(target)==e['after'], f'Postimage verification failed: {e["target"]}')
            if fault==e['surface']:raise RuntimeError('Injected failure: '+fault)
            if fault==e['surface']+'-interrupt':raise KeyboardInterrupt('Injected hard interruption: '+fault)
        require(all(identity(under(root,e['target']))==e['after'] for e in entries),'Installed image verification failed')
        record['moduleInventoryAfter']=module_inventory(root)
        record['state']='installed';save_record(backup,record)
        return {'state':'installed','installId':install_id,'backupDir':str(backup),'entries':entries}
    except Exception as error:
        # The same all-or-nothing preflight protects both rollback and later manual recovery.
        try:
            restored=restore(backup,apply=True,fake=fake)
            restored['state']='rolled-back';save_record(backup,restored)
        except Exception as rollback_error:
            raise RuntimeError(f'Install interrupted. Preserve backup {backup}; recovery blocked: {rollback_error}') from error
        raise RuntimeError(f'Install aborted and rolled back; installId={install_id}: {error}') from error


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['preview','install','restore','diagnostics'])
    parser.add_argument('--release');parser.add_argument('--manifest-sha256');parser.add_argument('--root')
    parser.add_argument('--backup-root',default=r'E:\CS2MOD\backups\bot-experiment')
    parser.add_argument('--install-id');parser.add_argument('--apply',action='store_true')
    parser.add_argument('--isolate-human',action='store_true');parser.add_argument('--fake',action='store_true')
    parser.add_argument('--preserve-human',action='store_true')
    args=parser.parse_args()
    if args.action in {'restore','diagnostics'}:
        require(re.fullmatch('[0-9a-f]{32}',args.install_id or ''),'Exact install ID is required')
        result=restore(under(args.backup_root,args.install_id),apply=args.apply and args.action=='restore',fake=args.fake)
    else:
        require(args.root and args.release,'Explicit root and release are required')
        result=install(args.release,args.manifest_sha256,args.root,args.backup_root,apply=args.apply and args.action=='install',fake=args.fake,isolate_human=args.isolate_human,preserve_human=args.preserve_human)
    result['mode']='apply' if args.apply and args.action in {'install','restore'} else 'preview-read-only'
    print(json.dumps(result,indent=2,ensure_ascii=False))


if __name__=='__main__':
    main()
