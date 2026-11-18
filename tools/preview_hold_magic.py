# -*- coding: utf-8 -*-
"""魔法陣的多段徑向斷面＋第二色系：離線並排比較。

左欄是舊的兩段漸層（_InnerColor -> _OuterColor 一條 smoothstep），右欄是現在的
做法。改 STAGE_EDGES / TONE_* / ACCENT_* / FAMILIES 就能重調，不用開 Unity。
換色的半徑取自雕花自己的筆畫空檔（量法見下面的註解）；FAMILIES 要和
ParticleEffectPlayer.ResolveAccentFamily 保持同一份。

沿用 tools/preview_hold_magic.py 的做法（三層各自用自己的 _RadialScale 映射回
共用半徑），只把顏色那一段換成多段：
    白熱核心 → 本色 → 溝 → 交接 → 金 → 溝 → 鉑邊
"""
import io
import os
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, 'Assets', 'Resources', 'Effects', 'HoldMagic')
# 輸出可以用 --out 改名：上一張還開在看圖程式裡的話，覆寫會被檔案鎖擋下來。
OUT = os.path.join(ROOT, 'tools', 'hold_magic_stages.png')
if '--out' in sys.argv:
    OUT = sys.argv[sys.argv.index('--out') + 1]

N = 420
LAYERS = [
    ('hold_magic_outer_v2.png', 1.025, 0.82),
    ('hold_magic_triangle_v2.png', 0.830, 0.90),
    ('hold_magic_inner_v2.png', 0.650, 0.84),
]

GLOW = 1.90
GRADIENT_STRENGTH = 1.00
STROKE_HIGHLIGHT = 0.20
CORE_BOOST = 0.62
FEATHER_FLOOR = 0.38
MID_SATURATION = 0.55
HIGHLIGHT_GILDING_BIAS = 1.7
GRADIENT_START = 0.478
GRADIENT_END = 0.522

# 多段：換色的半徑取自 rings.py 量到的**筆畫空檔**，不是隨便切的。
# 換在空檔上，每一圈雕花就整圈同一個色；換在筆畫上只會把一圈染成兩半。
# 五個邊界（核心止／第一段止／金段止／冠止／邊止）；鍍金交接另外由
# GRADIENT_START/END 決定，它同時是色相換手的地方。
STAGE_EDGES = [0.190, 0.412, 0.688, 0.802, 0.912]
STAGE_EDGE_W = 0.022
# 一條純量的色階：核心白熱 → 本色 → 壓深 → 金 → 鉑亮主環 → 外緣收深
TONE_CORE = 0.75
TONE_BODY = 0.0
TONE_GOLD = 0.0
TONE_DEEP = -0.85
TONE_GOLD_DEEP = -0.72
TONE_CROWN = 0.45
TONE_EDGE = -0.82

# 藏色：凹下去的段吃第二色系，凸起的段留本色（相位和明暗相反）。
ACCENT_CORE = 0.12
ACCENT_BODY = 0.0
ACCENT_GOLD = 0.0
ACCENT_DEEP = 0.60
ACCENT_GOLD_DEEP = 0.85
ACCENT_CROWN = 0.18
ACCENT_EDGE = 0.80
ACCENT_FOLLOW = 0.90
# 手定的第二色系表（和 ParticleEffectPlayer.ResolveAccentFamily 同一份）。
FAMILIES = {
    'gold':   ((0.52, 0.62, 0.18), (0.78, 1.25, 0.34), (1.05, 1.62, 0.92)),
    'violet': ((0.22, 0.20, 0.95), (0.32, 0.70, 1.70), (0.88, 1.45, 2.05)),
    'cream':  ((0.16, 0.20, 0.62), (0.34, 0.58, 1.35), (0.92, 1.25, 1.70)),
    'green':  ((0.62, 0.38, 0.08), (1.15, 0.78, 0.18), (1.55, 1.25, 0.55)),
    'blue':   ((0.05, 0.38, 0.46), (0.12, 0.88, 1.00), (0.72, 1.40, 1.45)),
    # 「完全完美」專屬（ParticleEffectPlayer.FlawlessPalette）
    'fl_in':  ((0.10, 0.62, 0.38), (0.20, 1.35, 0.85), (0.75, 1.70, 1.45)),
    'fl_out': ((0.14, 0.22, 0.95), (0.30, 0.72, 1.85), (0.90, 1.50, 2.10)),
}
FLAWLESS_GOLD = (1.70, 1.15, 0.38)
FLAWLESS_GOLD_HI = (2.00, 1.80, 1.25)
# 七段的點綴量：一般 / 完全完美
NORMAL_ACCENTS = (0.12, 0.00, 0.60, 0.00, 0.85, 0.18, 0.80)
FLAWLESS_ACCENTS = (0.20, 0.10, 1.00, 0.00, 1.00, 0.00, 0.35)
# 七段的明暗。完全完美那一組**藏色的段是亮的** —— 分隔交給色相，不靠暗帶。
NORMAL_TONES = (0.75, 0.00, -0.85, 0.00, -0.72, 0.45, -0.82)
FLAWLESS_TONES = (0.75, 0.00, -0.30, 0.00, -0.40, 0.20, -0.70)
# 分段：核心止／第一段止／金段止／冠止／邊止／交接起／交接止
NORMAL_STAGES = (0.190, 0.412, 0.688, 0.802, 0.912, 0.478, 0.522)
FLAWLESS_STAGES = (0.140, 0.505, 0.660, 0.875, 0.945, 0.555, 0.575)

GOLD = (1.58, 0.98, 0.26)
GOLD_HI = (1.80, 1.28, 0.48)
VIOLET_RIM = (1.30, 0.62, 1.92)
VIOLET_RIM_HI = (1.68, 1.05, 2.15)
JUDGE = {
    'JUST': (0.9559, 0.9559, 0.6786),
    'GREAT': (0.1800, 1.0000, 0.2229),
    'GOOD': (0.1839, 0.2650, 1.0000),
}
LUMA = np.array([0.2126, 0.7152, 0.0722])


def smoothstep(a, b, x):
    t = np.clip((x - a) / max(abs(b - a), 1e-6), 0.0, 1.0)
    if b < a:
        t = np.clip((a - x) / max(a - b, 1e-6), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def tone_up(c, k):
    c = np.asarray(c, dtype=np.float64)
    peak = c.max()
    return (c + (peak - c) * (k * 0.62)) * (1.0 + 0.34 * k)


def norm(c, peak=1.05):
    m = max(c)
    return tuple(v * peak / m for v in c)


def hi(c):
    n = norm(c)
    return tuple(min(2.2, (v + (1.05 - v) * 0.42) * 1.45) for v in n)


def accent_family(key):
    d, m, l = FAMILIES[key]
    return {'deep': np.asarray(d), 'mid': np.asarray(m), 'light': np.asarray(l)}


def family_pick(fam, t):
    """0 = 深、0.5 = 中、1 = 淺。"""
    lo = fam['deep'] + (fam['mid'] - fam['deep']) * np.clip(t * 2.0, 0, 1)
    hi_ = fam['mid'] + (fam['light'] - fam['mid']) * np.clip((t - 0.5) * 2.0, 0, 1)
    return np.where(t < 0.5, lo, hi_)


def seal_tone(c, tone):
    """把顏色往白熱（tone>0）或往深處（tone<0）推。c 可以是 ...x3。"""
    c = np.asarray(c, dtype=np.float64)
    peak = c.max(axis=-1, keepdims=True)
    luma = (c * LUMA).sum(-1, keepdims=True)
    # 往上推只**少量**去飽和：去太多的話金會變成淡暖白，判定色會變成白，
    # 兩個都是把顏色洗掉。亮度由增益負責。
    up = (c + (peak - c) * 0.35) * 1.45
    down = np.maximum(0.0, (luma + (c - luma) * 1.25) * 0.18)
    t = np.clip(tone, -1.0, 1.0)
    pos = np.clip(t, 0.0, 1.0)
    neg = np.clip(-t, 0.0, 1.0)
    return c + (up - c) * pos + (down - c) * neg


def ramp(radius, inner, outer, hi_in, hi_out, multi, fam_in, fam_out):
    """回傳 (ink, highlight)，兩個都是 ...x3。"""
    if not multi:
        gilding = smoothstep(GRADIENT_START, GRADIENT_END, radius) * GRADIENT_STRENGTH
        g = gilding[..., None]
        ink = np.asarray(inner) * (1 - g) + np.asarray(outer) * g
        luma = (ink * LUMA).sum(-1, keepdims=True)
        mid = 1.0 + MID_SATURATION * 4.0 * g * (1.0 - g)
        ink = luma + (ink - luma) * mid
        gh = np.power(gilding, HIGHLIGHT_GILDING_BIAS)[..., None]
        highlight = np.asarray(hi_in) * (1 - gh) + np.asarray(hi_out) * gh
        return ink, highlight

    # 色相只換一次，換在鍍金交接那道縫上。
    gild = smoothstep(GRADIENT_START, GRADIENT_END, radius)
    g = gild[..., None]
    base = np.asarray(inner) * (1 - g) + np.asarray(outer) * g
    base_hi = np.asarray(hi_in) * (1 - g) + np.asarray(hi_out) * g

    # 明暗的節奏由一條純量色階帶，每一段都在一道縫上交棒。
    gold_seam = (GRADIENT_START + GRADIENT_END) * 0.5
    e = (STAGE_EDGES[0], STAGE_EDGES[1], gold_seam,
         STAGE_EDGES[2], STAGE_EDGES[3], STAGE_EDGES[4])
    w = STAGE_EDGE_W
    tone = np.full(radius.shape, TONE_CORE)
    targets = (TONE_BODY, TONE_DEEP, TONE_GOLD, TONE_GOLD_DEEP,
               TONE_CROWN, TONE_EDGE)
    for edge, target in zip(e, targets):
        s = smoothstep(edge - w, edge + w, radius)
        tone = tone * (1 - s) + target * s

    # 藏色的量：和明暗相反相位，凹下去的段才吃。
    accent = np.full(radius.shape, ACCENT_CORE)
    a_targets = (ACCENT_BODY, ACCENT_DEEP, ACCENT_GOLD, ACCENT_GOLD_DEEP,
                 ACCENT_CROWN, ACCENT_EDGE)
    for edge, target in zip(e, a_targets):
        sstep = smoothstep(edge - w, edge + w, radius)
        accent = accent * (1 - sstep) + target * sstep

    # 選支：跟著明暗走，亮的段配淺支、凹的段配深支。
    member = np.clip(tone * ACCENT_FOLLOW + 0.5, 0.0, 1.0)[..., None]
    accent_ink = (family_pick(accent_family(fam_in), member) * (1 - g)
                  + family_pick(accent_family(fam_out), member) * g)
    a = accent[..., None]
    base = base * (1 - a) + accent_ink * a
    base_hi = base_hi * (1 - a * 0.7) + accent_ink * (a * 0.7)

    ink = seal_tone(base, tone[..., None])
    highlight = seal_tone(base_hi, tone[..., None] * 0.6)
    return ink, highlight


def layer_rgba(path, size_px, radial_scale, opacity, inner, outer, hi_in, hi_out,
               multi, fam_in, fam_out):
    tex = Image.open(path).convert('RGBA').resize((size_px, size_px), Image.LANCZOS)
    a = np.asarray(tex).astype(np.float64) / 255.0
    coverage = a[..., 3]

    u = (np.arange(size_px) + 0.5) / size_px - 0.5
    cy, cx = np.meshgrid(u, u, indexing='ij')
    radius = np.clip(np.sqrt(cx * cx + cy * cy) * 2.0 * radial_scale, 0.0, 1.0)

    ink, highlight = ramp(radius, inner, outer, hi_in, hi_out, multi, fam_in, fam_out)

    stroke = smoothstep(0.80, 1.0, coverage)[..., None]
    hot = smoothstep(0.93, 1.0, coverage)[..., None]
    feather = (FEATHER_FLOOR + (1.0 - FEATHER_FLOOR)
               * smoothstep(0.0, 0.45, coverage))[..., None]
    ink = ink * (1 - stroke * STROKE_HIGHLIGHT) + highlight * (stroke * STROKE_HIGHLIGHT)
    ink = ink * (1.0 + CORE_BOOST * hot) * feather
    return ink * GLOW, coverage * opacity


def compose(inner, outer, hi_in, hi_out, multi, fam_in, fam_out):
    dst = np.zeros((N, N, 3), dtype=np.float64)
    dst[..., 0], dst[..., 1], dst[..., 2] = 0.055, 0.028, 0.040
    for name, size, opacity in LAYERS:
        px = max(8, int(round(N * size / 1.025)))
        rgb, alpha = layer_rgba(os.path.join(ART, name), px, size / 1.025,
                                opacity, inner, outer, hi_in, hi_out, multi,
                                fam_in, fam_out)
        off = (N - px) // 2
        sl = (slice(off, off + px), slice(off, off + px))
        al = alpha[..., None]
        dst[sl] = dst[sl] * (1 - al) + rgb * al
    return dst


def tonemap(img):
    img = img / (1.0 + img)
    return np.clip(np.power(np.clip(img * 1.9, 0, 1), 1 / 1.6), 0, 1)


def main():
    # 六種組合：判定（內圈）和 precise（外圈）是兩個獨立的輸入 ——
    # 力度對而時間錯就是「GOOD + 紫邊」。
    fam = {'JUST': 'cream', 'GREAT': 'green', 'GOOD': 'blue'}
    rows = []
    for k in ('JUST', 'GREAT', 'GOOD'):
        rows.append((k, norm(JUDGE[k]), GOLD, hi(JUDGE[k]), GOLD_HI, fam[k], 'gold',
                     NORMAL_ACCENTS, NORMAL_TONES, NORMAL_STAGES))
        rows.append((k + ' +精準', norm(JUDGE[k]), VIOLET_RIM, hi(JUDGE[k]),
                     VIOLET_RIM_HI, fam[k], 'violet', NORMAL_ACCENTS, NORMAL_TONES,
                     NORMAL_STAGES))
    # 第七種：時間和力度同時做到。自己一套顏色，不是前六種的其中一格。
    rows.append(('完全完美', FLAWLESS_GOLD, VIOLET_RIM, FLAWLESS_GOLD_HI,
                 VIOLET_RIM_HI, 'fl_in', 'fl_out', FLAWLESS_ACCENTS,
                 FLAWLESS_TONES, FLAWLESS_STAGES))
    sheet = Image.new('RGB', (N * 2, N * len(rows)), (10, 10, 12))
    for i, (label, a, b, ha, hb, fi, fo, amounts, tones, stages) in enumerate(rows):
        global ACCENT_CORE, ACCENT_BODY, ACCENT_DEEP, ACCENT_GOLD
        global ACCENT_GOLD_DEEP, ACCENT_CROWN, ACCENT_EDGE
        global TONE_CORE, TONE_BODY, TONE_DEEP, TONE_GOLD
        global TONE_GOLD_DEEP, TONE_CROWN, TONE_EDGE
        (ACCENT_CORE, ACCENT_BODY, ACCENT_DEEP, ACCENT_GOLD,
         ACCENT_GOLD_DEEP, ACCENT_CROWN, ACCENT_EDGE) = amounts
        (TONE_CORE, TONE_BODY, TONE_DEEP, TONE_GOLD,
         TONE_GOLD_DEEP, TONE_CROWN, TONE_EDGE) = tones
        global STAGE_EDGES, GRADIENT_START, GRADIENT_END
        STAGE_EDGES = list(stages[:5])
        GRADIENT_START, GRADIENT_END = stages[5], stages[6]
        for j, multi in enumerate((False, True)):
            img = tonemap(compose(a, b, ha, hb, multi, fi, fo))
            sheet.paste(Image.fromarray((img * 255).astype(np.uint8)), (j * N, i * N))
        print(label, 'ok')
    sheet.save(OUT)
    print(OUT)
    print('左＝現狀（兩段），右＝多段')



main()
