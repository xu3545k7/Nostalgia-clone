#!/usr/bin/env python3
import sys
from pathlib import Path

def inspect_png(p):
    p = Path(p)
    out = {'path': str(p)}
    out['exists'] = p.exists()
    if not p.exists():
        return out
    try:
        size = p.stat().st_size
        out['size'] = size
        with p.open('rb') as f:
            sig = f.read(8)
            out['sig'] = sig.hex()
            if sig != b'\x89PNG\r\n\x1a\n':
                out['png_valid'] = False
                return out
            # read first chunk
            length_bytes = f.read(4)
            chunk_type = f.read(4)
            length = int.from_bytes(length_bytes, 'big')
            chunk = f.read(length)
            # next 4 bytes crc
            crc = f.read(4)
            if chunk_type != b'IHDR':
                # seek forward to find IHDR (defensive)
                f.seek(8)
                found = False
                while True:
                    lb = f.read(4)
                    if not lb or len(lb) < 4:
                        break
                    l = int.from_bytes(lb, 'big')
                    t = f.read(4)
                    if t == b'IHDR':
                        chunk = f.read(l)
                        found = True
                        break
                    f.seek(l+4, 1)
                if not found:
                    out['png_valid'] = False
                    return out
            if len(chunk) >= 13:
                w = int.from_bytes(chunk[0:4], 'big')
                h = int.from_bytes(chunk[4:8], 'big')
                bit = chunk[8]
                col = chunk[9]
                out['png_valid'] = True
                out['width'] = w
                out['height'] = h
                out['bitdepth'] = bit
                out['colortype'] = col
            else:
                out['png_valid'] = False
    except Exception as e:
        out['error'] = str(e)
    return out

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print('Usage: inspect_pngs.py <file1.png> [file2.png ...]')
        sys.exit(1)
    for p in sys.argv[1:]:
        r = inspect_png(p)
        print('---')
        print('path:', r.get('path'))
        print('exists:', r.get('exists'))
        if not r.get('exists'):
            continue
        print('size:', r.get('size'))
        print('png_valid:', r.get('png_valid', False))
        if 'sig' in r:
            print('sig (hex):', r.get('sig'))
        if r.get('png_valid'):
            print('width x height:', f"{r.get('width')} x {r.get('height')}")
            print('bitdepth:', r.get('bitdepth'))
            print('colortype:', r.get('colortype'))
        if 'error' in r:
            print('error:', r['error'])
