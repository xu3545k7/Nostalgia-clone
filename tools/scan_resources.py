#!/usr/bin/env python3
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Assets" / "Resources"
GRAPHIC = RESOURCES / "graphic"
IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".gif"}

def parse_texture_type(meta_path: Path):
    if not meta_path.exists():
        return None
    try:
        text = meta_path.read_text(encoding="utf-8", errors="ignore")
    except Exception:
        return None
    m = re.search(r"^\s*textureType:\s*(\d+)\s*$", text, re.MULTILINE)
    if m:
        try:
            return int(m.group(1))
        except Exception:
            return None
    return None

def scan_graphic_folder():
    results = []
    if not GRAPHIC.exists():
        print(f"GRAPHIC folder not found: {GRAPHIC}")
        return results
    files = [p for p in GRAPHIC.rglob("*") if p.is_file() and p.suffix.lower() in IMAGE_EXTS]
    for f in sorted(files):
        meta = Path(str(f) + ".meta")
        ttype = parse_texture_type(meta)
        results.append((f.relative_to(ROOT), bool(meta.exists()), ttype))
    return results

def find_resource_loads():
    root_dirs = [ROOT / "Assets"]
    regex = re.compile(r'Resources\.(?:LoadAll|Load)\s*(?:<[^>]+>)?\s*\(\s*"([^"]+)"')
    patterns = []
    for rd in root_dirs:
        if not rd.exists():
            continue
        for p in rd.rglob("*.cs"):
            try:
                txt = p.read_text(encoding="utf-8", errors="ignore")
            except Exception:
                continue
            for m in regex.finditer(txt):
                patterns.append((p.relative_to(ROOT), m.group(0), m.group(1)))
    return patterns

def check_resource_paths(resource_paths):
    missing = []
    case_mismatch = []
    for file_ref, call, path in resource_paths:
        target_dir = RESOURCES / path
        if target_dir.exists() and target_dir.is_dir():
            continue
        found_exact = False
        found_ci = False
        for ext in IMAGE_EXTS:
            candidate = RESOURCES / (path + ext)
            if candidate.exists():
                found_exact = True
                break
        if not found_exact:
            for f in RESOURCES.rglob("*"):
                if f.is_file() and f.suffix.lower() in IMAGE_EXTS:
                    rel = f.with_suffix("").relative_to(RESOURCES).as_posix()
                    if rel.lower() == path.lower() or rel.lower().endswith("/" + path.split("/")[-1].lower()):
                        found_ci = True
                        break
        if not found_exact and not found_ci:
            missing.append((file_ref, call, path))
        elif not found_exact and found_ci:
            case_mismatch.append((file_ref, call, path))
    return missing, case_mismatch

def main():
    print("Scanning Assets/Resources/graphic ...")
    entries = scan_graphic_folder()
    print(f"Found {len(entries)} image files under Assets/Resources/graphic")
    non_sprite = [(p,meta,tt) for (p,meta,tt) in entries if tt != 8]
    print(f"Non-sprite or unknown textureType count: {len(non_sprite)}")
    for p,meta,tt in non_sprite[:200]:
        print(f"- {p}  meta={'yes' if meta else 'no'}  textureType={tt}")

    print("\nSearching for Resources.Load calls in Assets...")
    loads = find_resource_loads()
    print(f"Found {len(loads)} Resources.Load/LoadAll calls.")
    for f, call, path in loads:
        print(f"- {f}: {call}  -> '{path}'")

    missing, case_mismatch = check_resource_paths(loads)
    print(f"\nResource paths missing: {len(missing)}")
    for file_ref, call, path in missing:
        print(f"- Missing: '{path}'  (call: {call} in {file_ref})")
    print(f"Case-insensitive matches only (possible casing issue): {len(case_mismatch)}")
    for file_ref, call, path in case_mismatch:
        print(f"- Case mismatch: '{path}'  (call: {call} in {file_ref})")

    rpt = ROOT / "tools" / "scan_resources_report.txt"
    try:
        with rpt.open("w", encoding="utf-8") as fh:
            fh.write("Scan report\n")
            fh.write(f"Found {len(entries)} image files under Assets/Resources/graphic\n")
            fh.write(f"Non-sprite or unknown textureType count: {len(non_sprite)}\n")
            if non_sprite:
                fh.write("Non-sprite samples:\n")
                for p,meta,tt in non_sprite[:200]:
                    fh.write(f"- {p} meta={'yes' if meta else 'no'} textureType={tt}\n")
            fh.write(f"\nFound {len(loads)} Resources.Load/LoadAll calls.\n")
            for f, call, path in loads:
                fh.write(f"- {f}: {call} -> '{path}'\n")
            fh.write(f"\nMissing: {len(missing)}\n")
            for file_ref, call, path in missing:
                fh.write(f"- Missing: '{path}'  (call: {call} in {file_ref})\n")
            fh.write(f"Case-insensitive only: {len(case_mismatch)}\n")
            for file_ref, call, path in case_mismatch:
                fh.write(f"- Case mismatch: '{path}'  (call: {call} in {file_ref})\n")
        print(f"\nWrote report to {rpt}")
    except Exception as e:
        print(f"Failed to write report: {e}")

if __name__ == '__main__':
    main()
