# -*- coding: utf-8 -*-
"""照參考圖（images/40.png）重烘簷幕。

和前一版的差別：
  · 配色改成參考圖量出來的緋紅（綠通道幾乎是 0），不再往燈色混而變成磚褐。
  · 五片垂幔改四片，褶從 6 道加到 12 道。
  · 角落的放射扇換成**兩側垂下來的側幕**，這才是參考圖裡那兩塊。
  · 頂帶的四分之一圓下沉關掉 —— 側幕接手填滿兩個角。
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw
import swag

SS = 4
VARIANT = sys.argv[1] if len(sys.argv) > 1 else ""
FINAL = r"D:\Nostalgia\Nostalgia-clone\Assets\Resources\UI\ProsceniumValance.png"
OUT = FINAL if not VARIANT else ("bake2_%s.png" % VARIANT)

CANVAS_W, CANVAS_H = 2560.0, 1440.0
RECT_W, RECT_H = CANVAS_W * 1.02, CANVAS_H * 1.02
UNIT = min(RECT_W, RECT_H) * 0.10

# ── 參考圖的天鵝絨 ──────────────────────────────────────────────────
BODY = (0.218, 0.014, 0.044)        # 56, 4, 11 —— 比參考圖更暗，色相不動
LAMP = (1.00, 0.88, 0.66)


def velvet(value, base=BODY):
    """0 是褶谷，1 是布本色，>1 是褶脊。褶脊留住飽和度，不往燈色跑。"""
    valley = tuple(c * 0.20 for c in base)
    crest = swag.mix(tuple(min(1.0, c * 2.55) for c in base), LAMP, 0.10)
    if value <= 1.0:
        return swag.mix(valley, base, swag.clamp01(value))
    return swag.mix(base, crest, swag.clamp01(value - 1.0))


swag.velvet_colour = lambda value, base: velvet(value)
swag.MOROCCO = swag.BURGUNDY = BODY
swag.GOLD = (0.878, 0.802, 0.596)   # 224,205,152 —— 參考圖的淡金偏奶油
swag.RECT_W, swag.HALFW = RECT_W, RECT_W * 0.5
swag.VISIBLE = swag.HALFW          # 垂幔跨滿整個 rect，兩端壓在側幕上面
swag.CX, swag.RECT_TOP, swag.UNIT = swag.HALFW, 0.0, UNIT
swag.S, swag.PX0, swag.PY0 = 1.0, 0.0, 0.0

P = dict(swag.P)
P.update(velvet=1.0, swags=7, folds=11, sub=9, seg=72, gather_sub=16,
         spread=0.34, drop=2.59, corner=1.04, sag_floor=0.26,
         sag_round=0.88, swag_var=0.085, gold_on=1,
         # 褶往下越來越寬（>1）。0.52 是反過來的，讀起來就是等寬的橫條紋。
         fold_bias=1.45, floor_taper=1.7, fold_var=0.22,
         # 亮線往弧心集中，兩端的抓點壓暗 —— 布在那裡被擠成一把。
         pinch=0.44, layer=0.30, peak=0.66, lit=0.74, floor=0.20,
         fillet=0.0, fan_on=0,
         gold=UNIT * 0.072,
         fringe=0.086, fringe_pitch=0.086, fringe_w=0.050, cut=UNIT * 0.016, shadow=UNIT * 0.42, alpha=1.0)

polys, gathers, top_at, sag_at = swag.build(P)

# ── 側幕 ────────────────────────────────────────────────────────────
LEG_TOP = UNIT * 3.05          # 頂端寬（貼著簾桿最寬）
LEG_BOT = UNIT * 1.30          # 底端寬（往外收，布倒向兩側）
LEG_H = UNIT * 5.60            # 垂多長
LEG_PLEATS = 9
LEG_ROWS = 40
legs = []


def leg_width(t):
    """上寬下窄。

    內緣往下往外斜，整片布就讀成被撥到兩側、掛在那裡往外垂；上窄下寬的那一版
    是一塊垂直掛著的板子，和台口的關係講不清楚。
    """
    taper = t ** 1.15
    return (LEG_TOP + (LEG_BOT - LEG_TOP) * taper) * (1.0 - 0.05 * math.sin(math.pi * t * 0.8))


def leg_bottom(u):
    """下襬往內側收得高一些，外側垂到底。"""
    return LEG_H * (0.80 + 0.20 * (1.0 - u) ** 0.9)


for side in (-1.0, 1.0):
    edge = swag.CX + side * swag.HALFW
    for k in range(LEG_PLEATS):
        u0, u1 = k / LEG_PLEATS, (k + 1) / LEG_PLEATS
        um = (u0 + u1) * 0.5
        # 一道褶是一個圓滾；亮線偏外側，光從台口外面來。
        roll = math.sin(math.pi * ((um - u0 * 0) % 1.0)) if False else None
        for m in range(6):
            v0, v1 = u0 + (u1 - u0) * m / 6.0, u0 + (u1 - u0) * (m + 1) / 6.0
            vm = (v0 + v1) * 0.5
            local = (vm - u0) / (u1 - u0)
            shade = math.sin(math.pi * local ** 0.68) ** 0.85
            deep = 1.0 - 0.34 * abs(2 * um - 1) ** 1.4      # 靠內側壓暗
            bottom = min(leg_bottom(v0), leg_bottom(v1))
            for i in range(LEG_ROWS):
                t0, t1 = i / LEG_ROWS, (i + 1) / LEG_ROWS
                y0, y1 = bottom * t0, bottom * t1
                fall = 1.0 - 0.30 * ((t0 + t1) * 0.5) ** 1.5   # 越下面越暗
                value = (0.22 + 1.34 * shade) * deep * fall
                q = [(edge - side * v0 * leg_width(t0), y0),
                     (edge - side * v1 * leg_width(t0), y0),
                     (edge - side * v1 * leg_width(t1), y1),
                     (edge - side * v0 * leg_width(t1), y1)]
                legs.append((q, velvet(value)))

    # 側幕內緣的金邊：沿著斜下去的那條邊走。切口和影子往內（房間那一側）。
    ROWS = LEG_ROWS * 3
    g, cut, sh_len = P['gold'], P['cut'], P['shadow']
    sh = tuple(c * 0.42 for c in (0.075, 0.04, 0.025))
    inner_bottom = leg_bottom(1.0)
    for i in range(ROWS):
        t0, t1 = i / float(ROWS), (i + 1) / float(ROWS)
        y0, y1 = inner_bottom * t0, inner_bottom * t1
        x0 = edge - side * leg_width(t0)
        x1 = edge - side * leg_width(t1)
        # 亮度跟著高度走：上面受光，往下收進暗處。
        lit = swag.clamp01(1.0 - ((t0 + t1) * 0.5) ** 0.85)
        col = swag.mix(swag.mix(swag.GOLD, (0.075, 0.04, 0.025), 0.50), swag.GOLD,
                       swag.clamp01(lit ** 1.4))
        a0, a1 = x0 - side * g, x1 - side * g
        legs.append(([(x0, y0), (x1, y1), (a1, y1), (a0, y0)], col))
        c0, c1 = a0 - side * cut, a1 - side * cut
        legs.append(([(a0, y0), (a1, y1), (c1, y1), (c0, y0)],
                     swag.mix(swag.mix(swag.GOLD, sh, 0.55), sh, 0.5), P['shadow_a']))
        d0, d1 = c0 - side * sh_len, c1 - side * sh_len
        legs.append(([(c0, y0), (c1, y1), (d1, y1), (d0, y0)], sh, P['shadow_a'], True))

polys = legs + polys      # 側幕在下，垂幔蓋在它上面

# ── 畫布 ────────────────────────────────────────────────────────────
ymax = max(y for item in polys for _, y in item[0])
ymax = max(ymax, max(top_at(g) + sag_at(g) * 0.46 for g in (gathers[1], gathers[-2]))
           + UNIT * (0.30 + 0.20 + 0.46))
W, H = int(round(RECT_W)), int(math.ceil(ymax + 8))
print("texture %d x %d  (unit %.2f)" % (W, H, UNIT))


def rgb(c):
    return tuple(int(swag.clamp01(v) * 255) for v in c)


def pts(q):
    return [(x * SS, y * SS) for x, y in q]


canvas = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
overlay = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
dc, do = ImageDraw.Draw(canvas), ImageDraw.Draw(overlay)
for item in polys:
    if len(item) == 2:
        dc.polygon(pts(item[0]), fill=rgb(item[1]) + (255,))
    elif len(item) == 3:
        do.polygon(pts(item[0]), fill=rgb(item[1]) + (int(255 * swag.clamp01(item[2])),))
    else:
        q, a = pts(item[0]), swag.clamp01(item[2])
        for k in range(24):
            t0, t1 = k / 24.0, (k + 1) / 24.0
            s = [(swag.lerp(q[0][0], q[3][0], t0), swag.lerp(q[0][1], q[3][1], t0)),
                 (swag.lerp(q[1][0], q[2][0], t0), swag.lerp(q[1][1], q[2][1], t0)),
                 (swag.lerp(q[1][0], q[2][0], t1), swag.lerp(q[1][1], q[2][1], t1)),
                 (swag.lerp(q[0][0], q[3][0], t1), swag.lerp(q[0][1], q[3][1], t1))]
            do.polygon(s, fill=rgb(item[1]) + (int(255 * a * (1 - (t0 + t1) * 0.5)),))

# 切口和影子只畫在背後沒有布的地方 —— 交疊處才不會把輪廓透出來。
cov = np.asarray(canvas)[:, :, 3] > 8
ov = np.asarray(overlay).copy()
ov[:, :, 3][cov] = 0
canvas = Image.alpha_composite(canvas, Image.fromarray(ov))

# 參考圖沒有垂飾。要加回來就把這兩行解開。
# dt = ImageDraw.Draw(canvas)
# for gx in (gathers[1], gathers[-2]):
#     swag.tassel(dt, gx, top_at(gx) + sag_at(gx) * 0.46, SS)

# 側幕的下襬淡出，不要硬切也不要斜切 —— 布往暗處退進去。
fade0, fade1 = LEG_H * 0.62, LEG_H
big = np.asarray(canvas).copy().astype(float)
rows = np.arange(big.shape[0]) / float(SS)
ramp = np.clip((fade1 - rows) / max(fade1 - fade0, 1.0), 0.0, 1.0)
ramp = ramp * ramp * (3 - 2 * ramp)
big[:, :, 3] *= ramp[:, None]
canvas = Image.fromarray(big.clip(0, 255).astype(np.uint8))

tex = canvas.resize((W, H), Image.LANCZOS)
arr = np.asarray(tex).copy()
inside = np.zeros(arr.shape[:2], dtype=bool)
for x in range(W):
    col = np.where(arr[:, x, 3] > 250)[0]
    if len(col):
        inside[col.min():col.max() + 1, x] = True
# 輪廓內半調的 alpha 是重取樣的假影，補回實心。側幕之間的空隙不在輪廓內。
fix = inside & (arr[:, :, 3] < 255) & (arr[:, :, 3] > 40)
arr[:, :, 3][fix] = 255
tex = Image.fromarray(arr)
if os.path.dirname(OUT):
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
tex.save(OUT)
print(u"補實心 %d 像素" % int(fix.sum()))
print(OUT)
