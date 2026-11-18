# -*- coding: utf-8 -*-
"""遊玩畫面兩側的側幕：專門烘一張「左腿」，右邊在 C# 那邊用 UV 鏡射。

形狀：上緣滿寬 → 中段被綁帶束起來 → 綁帶底下重新散開 → 下擺 alpha 漸漸淡出。
內緣是它自己的一條弧（兩段四分之一橢圓接在綁帶上），整條掛金繩和穗子。
"""
import math
import numpy as np
from PIL import Image, ImageDraw

SS = 4
OUT = r"D:\Nostalgia\Nostalgia-clone\Assets\Resources\UI\GameplayLegDrape.png"
W, H = 620, 1560
UNIT = 146.88                          # 和簷幕同一個尺度，金邊／穗子才一樣大

BODY = (0.218, 0.014, 0.044)
LAMP = (1.00, 0.88, 0.66)
GOLD = (0.878, 0.802, 0.596)

PLEATS = 7
SUB = 9
ROWS = 180
HEADER = UNIT * 0.26
GATHERS = 9
BATTEN = UNIT * 0.20
GOLDW = UNIT * 0.072
FRINGE = UNIT * 0.086
FRINGE_PITCH = UNIT * 0.086
FRINGE_W = UNIT * 0.050

# ── 綁帶 ────────────────────────────────────────────────────────────
TIE = 0.52                             # 綁帶在整條的哪個高度
PINCH = 0.50                           # 綁帶處收到滿寬的幾成
SKIRT = 0.66                           # 綁帶底下重新散開到幾成
TIE_H = UNIT * 0.30                    # 綁帶本身多高

# ── 下擺淡出 ────────────────────────────────────────────────────────
FADE0, FADE1 = 0.74, 1.00


def clamp01(v):
    return 0.0 if v < 0 else 1.0 if v > 1 else v


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def velvet(value):
    valley = tuple(c * 0.20 for c in BODY)
    crest = mix(tuple(min(1.0, c * 2.55) for c in BODY), LAMP, 0.10)
    if value <= 1.0:
        return mix(valley, BODY, clamp01(value))
    return mix(BODY, crest, clamp01(value - 1.0))


def inner(t):
    """內緣：兩段四分之一橢圓，接在綁帶上。

    用橢圓不用直線：布被綁起來的地方是**擠**進去的，上面那一段會鼓出來；直線會讀成
    一個三角形。綁帶底下的導數在起點很陡 —— 布是被放開的，馬上就散。
    """
    if t <= TIE:
        s = t / TIE
        f = PINCH + (1.0 - PINCH) * math.sqrt(max(0.0, 1.0 - s * s))
    else:
        s = (t - TIE) / (1.0 - TIE)
        # 綁帶底下只散開一小段就幾乎直下 —— 重力比布的彈性大得多。
        f = PINCH + (SKIRT - PINCH) * s ** 0.42
    return W * f


def yat(t):
    return HEADER + (H - HEADER) * t


polys = []

# ── 抽褶頂帶 ────────────────────────────────────────────────────────
g = GATHERS * 12
for i in range(g):
    x0, x1 = W * i / g, W * (i + 1) / g
    rib = math.sin(math.pi * ((i % 12) + 0.5) / 12.0) ** 0.9
    polys.append(([(x0, 0.0), (x1, 0.0), (x1, HEADER + 2), (x0, HEADER + 2)],
                  velvet(0.46 + 1.12 * rib)))

# ── 直褶 ────────────────────────────────────────────────────────────


def hash01(i):
    """固定的偽亂數。每次烘出來要一樣，不然改個無關的參數布就整片換了。"""
    return (math.sin(i * 12.9898) * 43758.5453) % 1.0


def strip(u0, u1, t0, t1, value):
    polys.append(([(u0 * inner(t0), yat(t0)), (u1 * inner(t0), yat(t0)),
                   (u1 * inner(t1), yat(t1)), (u0 * inner(t1), yat(t1))], velvet(value)))


# 上段：規矩的直褶，布還掛在桿上。
UPPER_ROWS = int(ROWS * TIE)
for k in range(PLEATS):
    a0, a1 = k / PLEATS, (k + 1) / PLEATS
    var = 1.0 + 0.20 * math.sin(k * 7.2 + 1.7)
    for m in range(SUB):
        v0 = a0 + (a1 - a0) * m / SUB
        v1 = a0 + (a1 - a0) * (m + 1) / SUB
        local = ((v0 + v1) * 0.5 - a0) / (a1 - a0)
        roll = math.sin(math.pi * local ** 0.66) ** 0.85
        for i in range(UPPER_ROWS):
            t0, t1 = TIE * i / UPPER_ROWS, TIE * (i + 1) / UPPER_ROWS
            tm = (t0 + t1) * 0.5
            lit = 1.0 + 0.20 * ((v0 + v1) * 0.5)
            fall = 1.0 - 0.26 * tm ** 1.1
            squeeze = 1.0 - 0.40 * math.exp(-((tm - TIE) / 0.085) ** 2)
            strip(v0, v1, t0, t1, (0.22 + 1.30 * roll) * lit * fall * var * squeeze)

# 下段：布在綁帶上被抓成一把，放開之後往下垂。
#
# **褶要多、要不等寬、還要會漂。** 等寬等距的褶讀起來是瓦楞板 —— 真的布每一摺的
# 寬度都不一樣，而且往下走的時候會慢慢偏開、有幾摺走到一半就化掉。下面三件事各對
# 應一個：widths 給不等寬，drift 給漂移，settle 讓深摺往下變淺。
LOWER = 15
LOWER_ROWS = ROWS - UPPER_ROWS
widths = [0.55 + 0.95 * hash01(k * 3 + 1) for k in range(LOWER)]
total_w = sum(widths)
bounds = [0.0]
for w in widths:
    bounds.append(bounds[-1] + w / total_w)
# **漂移掛在邊界上，不是掛在褶上。**
#
# 每一道褶各自漂的話，第 k 摺的右緣和第 k+1 摺的左緣會漂到不同的地方 —— 兩片之間
# 就裂出一條細縫，背景直接透出來。邊界只有一份，兩邊共用，縫就不可能存在。
# 頭尾兩個邊界固定不漂：它們是布的輪廓，一漂就和金繩、外緣對不上。
bphase = [hash01(i * 7 + 11) * 6.2832 for i in range(LOWER + 1)]
bamp = [0.0] + [0.018 * (0.4 + hash01(i * 7 + 13)) for i in range(1, LOWER)] + [0.0]


def bdrift(i, sv):
    return bamp[i] * math.sin(bphase[i] + sv * 2.6) * sv ** 0.7


for k in range(LOWER):
    a0, a1 = bounds[k], bounds[k + 1]
    peak = 0.46 + 0.34 * hash01(k * 3 + 2)
    var = 1.0 + 0.26 * math.sin(k * 5.1 + 0.8)
    # 有幾摺一開始很深，往下就化掉了。
    settle = 0.35 + 0.65 * hash01(k * 3 + 7)

    def drift(local_u, sv, _k=k):
        return bdrift(_k, sv) + (bdrift(_k + 1, sv) - bdrift(_k, sv)) * local_u

    for m in range(SUB):
        v0 = a0 + (a1 - a0) * m / SUB
        v1 = a0 + (a1 - a0) * (m + 1) / SUB
        local = ((v0 + v1) * 0.5 - a0) / (a1 - a0)
        roll = math.sin(math.pi * local ** peak) ** 0.85
        for i in range(LOWER_ROWS):
            sv0, sv1 = i / LOWER_ROWS, (i + 1) / LOWER_ROWS
            t0 = TIE + (1.0 - TIE) * sv0
            t1 = TIE + (1.0 - TIE) * sv1
            svm = (sv0 + sv1) * 0.5
            lit = 1.0 + 0.18 * ((v0 + v1) * 0.5)
            fall = 1.0 - 0.30 * (TIE + (1.0 - TIE) * svm) ** 1.1
            # 剛從綁帶放開的地方最擠，往下鬆開。
            flare = 1.0 - 0.34 * math.exp(-(svm / 0.13) ** 2)
            contrast = 1.0 - (1.0 - settle) * svm ** 0.8
            value = (0.22 + 1.30 * (0.5 + (roll - 0.5) * contrast)) * lit * fall * var * flare
            l0 = (v0 - a0) / (a1 - a0)
            l1 = (v1 - a0) / (a1 - a0)
            polys.append((
                [((v0 + drift(l0, sv0)) * inner(t0), yat(t0)),
                 ((v1 + drift(l1, sv0)) * inner(t0), yat(t0)),
                 ((v1 + drift(l1, sv1)) * inner(t1), yat(t1)),
                 ((v0 + drift(l0, sv1)) * inner(t1), yat(t1))], velvet(value)))

# ── 內緣的金繩 ──────────────────────────────────────────────────────
EDGE = ROWS * 2
edge_pts = [(inner(i / EDGE), yat(i / EDGE)) for i in range(EDGE + 1)]
for i in range(EDGE):
    a, b = edge_pts[i], edge_pts[i + 1]
    shade = clamp01(1.0 - (i / EDGE) ** 0.85)
    base = mix(mix(GOLD, (0.075, 0.04, 0.025), 0.46), GOLD, clamp01(shade ** 1.5))
    twist = 0.64 if (i % 2) else 1.16
    polys.append(([a, b, (b[0] + GOLDW, b[1]), (a[0] + GOLDW, a[1])],
                  tuple(clamp01(c * twist) for c in base)))
    hi = tuple(clamp01(c * 1.32 + 0.09) for c in base)
    polys.append(([a, b, (b[0] + GOLDW * 0.32, b[1]), (a[0] + GOLDW * 0.32, a[1])], hi))

# ── 穗邊 ────────────────────────────────────────────────────────────
cum = [0.0]
for i in range(1, EDGE + 1):
    cum.append(cum[-1] + math.hypot(edge_pts[i][0] - edge_pts[i - 1][0],
                                    edge_pts[i][1] - edge_pts[i - 1][1]))
total = cum[-1]
n = max(1, int(round(total / FRINGE_PITCH)))
pale = tuple(min(1.0, c * 1.18 + 0.08) for c in GOLD)
deep_g = tuple(c * 0.52 for c in GOLD)
edge_g = tuple(c * 0.74 for c in GOLD)
half = FRINGE_W * 0.5
j = 1
for m in range(n):
    want = (m + 0.5) * total / n
    while j < EDGE and cum[j] < want:
        j += 1
    f = (want - cum[j - 1]) / max(cum[j] - cum[j - 1], 1e-6)
    x = edge_pts[j - 1][0] + (edge_pts[j][0] - edge_pts[j - 1][0]) * f + GOLDW * 0.85
    y = edge_pts[j - 1][1] + (edge_pts[j][1] - edge_pts[j - 1][1]) * f
    var = 1.0 + 0.09 * math.sin(m * 2.9)
    neck = FRINGE * 0.22
    tip = y + FRINGE * var
    nw = half * 0.34
    body = deep_g if m % 4 == 2 else GOLD
    polys.append(([(x - nw, y), (x + nw, y), (x + nw, y + neck), (x - nw, y + neck)], edge_g))
    polys.append(([(x - half * 0.52, y + neck), (x + half * 0.52, y + neck),
                   (x + half, tip), (x - half, tip)], body))
    polys.append(([(x - half * 0.52, y + neck), (x - half * 0.10, y + neck),
                   (x - half * 0.30, tip), (x - half, tip)], pale))
    polys.append(([(x - half, tip - FRINGE * 0.10), (x + half, tip - FRINGE * 0.10),
                   (x + half, tip), (x - half, tip)], edge_g))

# ── 綁帶本身：一條厚的紅絨捲帶 ──────────────────────────────────────
# 畫在褶和穗之後，所以它壓在布上面 —— 綁帶是繞在布外面的。
# 金色的細帶那一版讀起來是一根架在布前面的金屬桿；這是一捲布，所以它自己也要有
# 滾筒的明暗：上下兩緣暗、中間亮。
ty = yat(TIE)
tw = inner(TIE) + GOLDW * 1.2
SEG = 72
BANDS = 6
for i in range(SEG):
    a0, a1 = i / SEG, (i + 1) / SEG
    x0, x1 = tw * a0, tw * a1
    h0 = TIE_H * (0.66 + 0.34 * math.sin(math.pi * a0) ** 0.7)
    h1 = TIE_H * (0.66 + 0.34 * math.sin(math.pi * a1) ** 0.7)
    # 外端（a=0）是繞到布後面去的，所以暗；往結的方向才轉亮。
    depth = 0.34 + 0.66 * ((a0 + a1) * 0.5) ** 0.7
    for b in range(BANDS):
        q0, q1 = b / BANDS, (b + 1) / BANDS
        roll = math.sin(math.pi * ((q0 + q1) * 0.5) ** 0.78) ** 0.9
        col = velvet((0.26 + 1.34 * roll) * depth)
        polys.append(([(x0, ty - h0 * 0.5 + h0 * q0), (x1, ty - h1 * 0.5 + h1 * q0),
                       (x1, ty - h1 * 0.5 + h1 * q1), (x0, ty - h0 * 0.5 + h0 * q1)], col))

# 結：綁帶在內緣打一個結，底下兩條垂尾。也是絨布。
kx = tw
kh = TIE_H * 0.92
for b in range(BANDS):
    q0, q1 = b / BANDS, (b + 1) / BANDS
    roll = math.sin(math.pi * ((q0 + q1) * 0.5) ** 0.7) ** 0.9
    col = velvet((0.30 + 1.42 * roll))
    polys.append(([(kx - kh * 0.62, ty - kh + kh * 2 * q0),
                   (kx + kh * 0.46, ty - kh * 0.86 + kh * 1.72 * q0),
                   (kx + kh * 0.46, ty - kh * 0.86 + kh * 1.72 * q1),
                   (kx - kh * 0.62, ty - kh + kh * 2 * q1)], col))
for s_side in (-1, 1):
    x0 = kx + kh * (0.05 + 0.26 * s_side)
    for b in range(BANDS):
        q0, q1 = b / BANDS, (b + 1) / BANDS
        roll = math.sin(math.pi * ((q0 + q1) * 0.5) ** 0.72) ** 0.9
        col = velvet((0.24 + 1.24 * roll) * (0.78 if s_side < 0 else 1.0))
        w0 = kh * 0.19
        w1 = kh * 0.27
        polys.append(([(x0 - w0 + 2 * w0 * q0, ty + kh * 0.55),
                       (x0 - w0 + 2 * w0 * q1, ty + kh * 0.55),
                       (x0 - w1 + 2 * w1 * q1, ty + kh * 2.1),
                       (x0 - w1 + 2 * w1 * q0, ty + kh * 2.1)], col))

# ── 上緣的木條 ──────────────────────────────────────────────────────
polys.append(([(0, 0), (W, 0), (W, BATTEN), (0, BATTEN)], (0.108, 0.062, 0.030)))
polys.append(([(0, 0), (W, 0), (W, BATTEN * 0.26), (0, BATTEN * 0.26)], (0.205, 0.128, 0.062)))

# ── 光柵化 ──────────────────────────────────────────────────────────
canvas = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
d = ImageDraw.Draw(canvas)
for quad, col in polys:
    d.polygon([(x * SS, y * SS) for x, y in quad],
              fill=tuple(int(clamp01(c) * 255) for c in col) + (255,))
tex = canvas.resize((W, H), Image.LANCZOS)

arr = np.asarray(tex).copy().astype(float)
# 重取樣會在邊緣留半調的 alpha；輪廓內補回實心。
inside = np.zeros(arr.shape[:2], dtype=bool)
for y in range(H):
    row = np.where(arr[y, :, 3] > 250)[0]
    if len(row):
        inside[y, row.min():row.max() + 1] = True
fix = inside & (arr[:, :, 3] < 255) & (arr[:, :, 3] > 40)
arr[:, :, 3][fix] = 255

# 下擺淡出：布往暗處退進去，而不是被一刀切在畫面底下。
t = (np.arange(H) / (H - 1.0) - FADE0) / max(FADE1 - FADE0, 1e-6)
ramp = np.clip(1.0 - t, 0.0, 1.0)
ramp = ramp * ramp * (3 - 2 * ramp)
arr[:, :, 3] *= ramp[:, None]

Image.fromarray(arr.clip(0, 255).astype(np.uint8)).save(OUT)
print("texture %d x %d  tie %.0f%%  pinch %.0f%%  fade %.0f->%.0f%%"
      % (W, H, TIE * 100, PINCH * 100, FADE0 * 100, FADE1 * 100))
print(OUT)
