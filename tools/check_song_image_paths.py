#!/usr/bin/env python3
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / 'Assets' / 'Resources'
SONGS = RESOURCES / 'songs'
IMG_EXTS = ['.png', '.jpg', '.jpeg']

def find_case_insensitive(path_without_ext):
    """Return actual path if case-insensitive match exists under Resources."""
    for p in RESOURCES.rglob('*'):
        if not p.is_file():
            continue
        rel = p.with_suffix('').relative_to(RESOURCES).as_posix()
        if rel.lower() == path_without_ext.lower():
            return p
    return None

def check_register(path):
    try:
        txt = path.read_text(encoding='utf-8')
        data = json.loads(txt)
    except Exception as e:
        return [(str(path), 'parse_error', str(e))]

    results = []
    difficulties = data.get('difficulties') or []
    if not difficulties:
        # also support old flat fields
        cr = data.get('coverResourcePath')
        if cr:
            difficulties = [{'coverResourcePath': cr}]

    for d in difficulties:
        cr = d.get('coverResourcePath')
        if not cr:
            continue
        # Normalize path: remove leading/trailing slashes
        rp = cr.strip().strip('/')
        # Check if any image file exists with that base path
        found = False
        for ext in IMG_EXTS:
            cand = RESOURCES / (rp + ext)
            if cand.exists():
                found = True
                results.append((str(path), cr, 'ok', str(cand.relative_to(ROOT))))
                break
        if not found:
            case = find_case_insensitive(rp)
            if case:
                results.append((str(path), cr, 'case_mismatch', str(case.relative_to(ROOT))))
            else:
                results.append((str(path), cr, 'missing', None))

    return results

def main():
    regs = list(SONGS.rglob('register.json'))
    print(f'Found {len(regs)} register.json files under {SONGS}')
    report = []
    for r in regs:
        report.extend(check_register(r))

    missing = [r for r in report if r[2] == 'missing']
    case = [r for r in report if r[2] == 'case_mismatch']
    ok = [r for r in report if r[2] == 'ok']

    print(f'ok: {len(ok)}  case_mismatch: {len(case)}  missing: {len(missing)}')
    for e in missing[:100]:
        print('MISSING image:', e[0], '->', e[1])
    for e in case[:100]:
        print('CASE MISMATCH:', e[0], '->', e[1], 'actual:', e[3])

    out = ROOT / 'tools' / 'song_image_check.txt'
    with out.open('w', encoding='utf-8') as fh:
        fh.write('Song image check\n')
        for r in report:
            fh.write('\t'.join([r[0], r[1], r[2], r[3] if len(r) > 3 and r[3] else '']) + '\n')
    print('Wrote report to', out)

if __name__ == '__main__':
    main()
