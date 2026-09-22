#!/usr/bin/env python3
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]  # Nostalgia-clone root
WORKSPACE = ROOT.parents[1] if len(ROOT.parents) > 1 else ROOT
SONGS = ROOT / 'Assets' / 'Resources' / 'songs'
BACKUP_DIR = ROOT / 'tools' / 'corrupt_image_backups'
IMG_EXTS = ['.png', '.jpg', '.jpeg']

def is_png_valid(p: Path) -> bool:
    try:
        with p.open('rb') as f:
            sig = f.read(8)
            if sig != b'\x89PNG\r\n\x1a\n':
                return False
            # read next chunk header
            length_bytes = f.read(4)
            if len(length_bytes) < 4:
                return False
            chunk_type = f.read(4)
            if len(chunk_type) < 4:
                return False
            # ensure IHDR present (common case)
            if chunk_type != b'IHDR':
                # try to find IHDR in following chunks (defensive)
                f.seek(8)
                found = False
                while True:
                    lb = f.read(4)
                    if not lb or len(lb) < 4:
                        break
                    l = int.from_bytes(lb, 'big')
                    t = f.read(4)
                    if t == b'IHDR':
                        found = True
                        break
                    # skip chunk data + crc
                    f.seek(l + 4, 1)
                return found
            return True
    except Exception:
        return False

def find_replacements(name: str):
    # search workspace for files with same name (case-insensitive on Windows)
    candidates = []
    for p in WORKSPACE.rglob(name):
        if p.is_file():
            candidates.append(p)
    # also try matching by stem with other extensions
    stem = Path(name).stem
    for ext in IMG_EXTS:
        for p in WORKSPACE.rglob(stem + ext):
            if p.is_file() and p.name != name:
                candidates.append(p)
    # deduplicate and sort by file size desc
    uniq = {}
    for c in candidates:
        try:
            uniq[str(c.resolve())] = c
        except Exception:
            uniq[str(c)] = c
    cand_list = list(uniq.values())
    cand_list.sort(key=lambda p: p.stat().st_size if p.exists() else 0, reverse=True)
    return cand_list

def ensure_dir(p: Path):
    if not p.exists():
        p.mkdir(parents=True, exist_ok=True)

def backup_and_replace(target: Path, src: Path):
    rel = target.relative_to(ROOT)
    backup_target = BACKUP_DIR / rel
    ensure_dir(backup_target.parent)
    # backup original
    shutil.copy2(target, backup_target)
    # copy replacement
    shutil.copy2(src, target)
    # copy meta if exists alongside candidate
    candidate_meta = src.parent / (src.name + '.meta')
    target_meta = target.parent / (target.name + '.meta')
    if candidate_meta.exists():
        shutil.copy2(candidate_meta, target_meta)

def main():
    if not SONGS.exists():
        print('Songs folder not found:', SONGS)
        return 1

    ensure_dir(BACKUP_DIR)
    corrupt = []
    for p in SONGS.rglob('*.png'):
        if not is_png_valid(p):
            corrupt.append(p)

    print(f'Found {len(corrupt)} corrupt PNG(s) under {SONGS}')
    report_lines = []
    for t in corrupt:
        print('\nProcessing:', t)
        reps = find_replacements(t.name)
        # exclude exact same path
        reps = [r for r in reps if r.resolve() != t.resolve()]
        if not reps:
            # try case-insensitive search by scanning names
            all_cands = [p for p in WORKSPACE.rglob('*') if p.is_file() and p.name.lower() == t.name.lower() and p.resolve() != t.resolve()]
            all_cands.sort(key=lambda p: p.stat().st_size if p.exists() else 0, reverse=True)
            reps = all_cands

        if not reps:
            print('  No replacement found for', t)
            report_lines.append(f'{t}\tMISSING_REPLACEMENT')
            continue

        chosen = reps[0]
        try:
            backup_and_replace(t, chosen)
            print('  Replaced with:', chosen)
            report_lines.append(f'{t}\tREPLACED_WITH\t{chosen}')
        except Exception as e:
            print('  Failed to replace:', e)
            report_lines.append(f'{t}\tREPLACE_FAILED\t{e}')

    out = ROOT / 'tools' / 'replace_corrupt_pngs_report.txt'
    with out.open('w', encoding='utf-8') as fh:
        for l in report_lines:
            fh.write(l + '\n')

    print('\nReport written to', out)
    return 0

if __name__ == '__main__':
    sys.exit(main())
