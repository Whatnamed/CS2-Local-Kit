"""Read-only PE executable-section scan. JSON input: [{name, library, pattern}].

Reports all overlapping matches and RVAs; never loads a DLL or proves runtime ABI.
Usage: python scan-windows-signatures.py --game-root <CS2/game> --patterns <json> --output <json>
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path


def scan(game_root, patterns):
    binaries = {}
    rows = []
    for item in patterns:
        lib = item['library']
        if not re.fullmatch(r'[A-Za-z0-9_]+', lib):
            raise ValueError(f'Invalid library: {lib}')
        if lib not in binaries:
            paths = [game_root / 'csgo/bin/win64' / f'{lib}.dll',
                     game_root / 'bin/win64' / f'{lib}.dll']
            path = next(p for p in paths if p.is_file())
            raw = path.read_bytes()
            pe = struct.unpack_from('<I', raw, 0x3c)[0]
            if raw[:2] != b'MZ' or raw[pe:pe + 4] != b'PE\0\0':
                raise ValueError(f'Invalid PE: {path}')
            count = struct.unpack_from('<H', raw, pe + 6)[0]
            optional_size = struct.unpack_from('<H', raw, pe + 20)[0]
            sections = []
            for i in range(count):
                off = pe + 24 + optional_size + i * 40
                name = raw[off:off + 8].rstrip(b'\0').decode('ascii')
                _, rva, size, pos = struct.unpack_from('<IIII', raw, off + 8)
                if struct.unpack_from('<I', raw, off + 36)[0] & 0x20000000:
                    sections.append((name, rva, pos, raw[pos:pos + size]))
            binaries[lib] = (path, hashlib.sha256(raw).hexdigest(), sections)
        pattern = item['pattern']
        if not re.fullmatch(r'(?:[\dA-Fa-f]{2}|\?\??)(?:\s+(?:[\dA-Fa-f]{2}|\?\??))*', pattern):
            raise ValueError(f'Invalid hex pattern: {pattern}')
        regex = b''.join(b'.' if '?' in token else re.escape(bytes.fromhex(token))
                         for token in pattern.split())
        matches = []
        for name, rva, pos, data in binaries[lib][2]:
            for match in re.finditer(b'(?=(' + regex + b'))', data, re.DOTALL):
                matches.append({'section': name, 'rva': hex(rva + match.start()),
                                'fileOffset': hex(pos + match.start())})
        if 'expectedOriginal' in item:
            expected = item['expectedOriginal'].split()
            offset = item['patchOffset']
            raw = binaries[lib][0].read_bytes()
            for match in matches:
                pos = int(match['fileOffset'], 16) + offset
                actual = raw[pos:pos + len(expected)]
                match['originalBytes'] = actual.hex(' ').upper()
                match['expectedOriginalMatches'] = len(actual) == len(expected) and all(
                    '?' in token or actual[i] == int(token, 16)
                    for i, token in enumerate(expected))
        rows.append({**item, 'matchCount': len(matches), 'matches': matches})
    return {'scope': 'Static hex patterns in executable PE sections only; no ABI, offsets, vtable, hook, gameplay or performance proof.',
            'binaryHashes': {lib: {'path': str(data[0]), 'sha256': data[1]}
                             for lib, data in binaries.items()}, 'results': rows}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-root', type=Path, required=True)
    parser.add_argument('--patterns', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = scan(args.game_root, json.loads(args.patterns.read_text(encoding='utf-8-sig')))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    for row in result['results']:
        print(f"{row['name']}: {row['matchCount']}")
