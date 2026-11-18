# -*- coding: utf-8 -*-
"""斷奏箭頭（StaccatoMark）換色。

**只換填色，不動金邊。** 金色的框是這個遊戲從簾幕、書板到樂譜共用的語彙，箭頭跟著
用是對的；不顯眼的是中間那塊填色 —— 它和右手音符、和整個深紅色的舞台是同一個色相。

原圖的色相分得很乾淨（量過）：
    0–20°   紅色填充   94187 px   S 0.79  V 0.45–0.66
    40–50°  金邊       22086 px   S 0.76  V 0.81
中間 20–40° 是兩者之間的抗鋸齒。所以用色相當遮罩就能把填色挑出來，形狀、明暗、
高光全部原封不動地保留。

用法：
    python tools/make_staccato_mark.py            # 套用定案的顏色
    python tools/make_staccato_mark.py --sheet    # 只產生候選比較圖，不覆蓋素材
"""
import argparse
import os

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "Assets", "Resources", "graphic")
SRC = os.path.join(ROOT, "tools", "art_src")

# 填色遮罩：**用「不是金色」來定義填色**，不是用「是紅色」。
#
# 紅色那張的填色在 0–20，藍色那張在 210–230 —— 兩張唯一的共通點是金邊都在 40–50。
# 照紅色去挑的遮罩在藍色那張會一個像素都挑不到（第一版就是這樣）。
GOLD_HUE = 45.0      # 金邊的色相中心
GOLD_CORE = 6.0      # 離中心這麼近，全算金邊
GOLD_SPAN = 17.0     # 離中心這麼遠，完全不算金邊

# 明度抬升的下限：暗處要留著，見 recolour 裡的說明。
DARK_KEEP = 0.30
DARK_FULL = 0.52

# 候選。(名稱, 目標色相, 明度倍率, 彩度倍率, 明度抬升)
#
# **明度抬升和明度倍率是兩件事。** 倍率是等比放大，暗的地方放大後還是相對暗 ——
# 原本的填色 V 只有 0.45–0.66，光靠倍率會把亮處夾到 1 而暗處還在半腰，結果是一塊
# 對比被壓扁的色塊。抬升是把整段往 1 推（`lerp(v, 1, lift)`），暗處拉得比亮處多，
# 這才是「淡」該有的樣子：顏色退到高明度、低彩度那一區。
#
# 淡色一定要同時降彩度。只抬明度不降彩度會變成「螢光色」，那和這個遊戲的玻璃＋金
# 的質感不相容。
CANDIDATES = {
    # 淡紫（第一版）。使用者的評語是「有點突兀」—— 整塊填色是同一個高明度的淡紫，
    # 和金框之間沒有過渡，像貼上去的一塊色紙。
    "lilac":      dict(label="淡紫",        hue=272.0, v=1.12, s=0.46, lift=0.52),
    # 定案：底色調深（彩度拉回、抬升收斂），再加「越靠外框越深」的漸層。
    "lilac_deep": dict(label="淡紫·外深",  hue=272.0, v=1.06, s=0.70, lift=0.30,
                       edge_v=0.46, edge_s=1.30),
    "lilac_deeper": dict(label="淡紫·更深", hue=272.0, v=1.00, s=0.84, lift=0.20,
                         edge_v=0.38, edge_s=1.35),
    "lilac_pale": dict(label="淡紫·更淡",  hue=272.0, v=1.14, s=0.34, lift=0.64),
    "orchid":     dict(label="淡紫·偏粉",  hue=288.0, v=1.12, s=0.48, lift=0.52),
    "spring":     dict(label="春綠·平",    hue=145.0, v=1.42, s=1.00, lift=0.00),
    # 定案：綠，但深度處理和淡紫那一版完全一樣（同樣的 v/s/lift/edge）。
    # 使用者要的是「換回綠色，深度依樣」—— 所以只換色相，其餘五個參數原封不動。
    "spring_deep": dict(label="春綠·外深",  hue=145.0, v=1.06, s=0.70, lift=0.30,
                        edge_v=0.46, edge_s=1.30),
    # 漸層方向翻過來：**最內部最深，越往外越淺**（edge_v > 1 就是往外提亮）。
    # 同時底色再壓深一階：彩度拉高、抬升收掉。
    "spring_core":  dict(label="春綠·內深", hue=145.0, v=0.88, s=1.05, lift=0.10,
                         edge_v=1.85, edge_s=0.58),
    "spring_core2": dict(label="春綠·內更深", hue=145.0, v=0.78, s=1.18, lift=0.04,
                         edge_v=2.15, edge_s=0.48),
    # 整體提亮，但**保留內深外淺的方向**（edge_v 仍然大於 1）。
    # 深和暗是兩件事：深＝彩度高，暗＝明度低。要的是前者，不是後者。
    "spring_lit":  dict(label="春綠·亮",   hue=145.0, v=1.22, s=0.96, lift=0.26,
                        edge_v=1.70, edge_s=0.62),
    "spring_lit2": dict(label="春綠·更亮", hue=145.0, v=1.40, s=0.88, lift=0.38,
                        edge_v=1.55, edge_s=0.68),
    # 照使用者給的參考圖量出來的目標：色相 117.9、彩度 1.000、明度 0.910
    # （平均 RGB 0.059/0.872/0.030）。那張圖的綠幾乎是**單一色**，四分位只差 0.3 度。
    #
    # 彩度給 1.35 是為了把原圖 0.79 的彩度推到夾滿 1.0；edge_s 設 1.0 表示往外
    # **不退彩度** —— 參考色整片都是飽和的，只留明度上的內深外淺。
    "ref_green":  dict(label="參考綠",   hue=118.0, v=1.13, s=1.35, lift=0.44,
                       edge_v=1.25, edge_s=1.00),
    # 完全平塗版：沒有內外漸層，最貼近參考圖，但會失去立體感。
    "ref_flat":   dict(label="參考綠·平", hue=118.0, v=1.18, s=1.35, lift=0.45),
    # 比參考綠再深一階。彩度已經夾在 1.0 了，所以「更深」只能往明度走。
    "ref_deep":   dict(label="參考綠·深", hue=118.0, v=0.94, s=1.35, lift=0.30,
                       edge_v=1.25, edge_s=1.00),
    # 定案：改回紅／藍（各自保留原色相），但淡一點、亮一點。
    # 彩度 0.78→約 0.47，明度 0.65→約 0.88，並保留內深外淺。
    "hand_pale":  dict(label="手色·淡亮",  hue=None,  v=1.10, s=0.60, lift=0.42,
                       edge_v=1.25, edge_s=1.00),
    # 再淡一階：彩度 0.78→約 0.30，明度 0.65→約 0.93。
    "hand_paler": dict(label="手色·更淡",  hue=None,  v=1.06, s=0.38, lift=0.60,
                       edge_v=1.20, edge_s=1.00),
    "hand_palest": dict(label="手色·最淡", hue=None,  v=1.04, s=0.26, lift=0.70,
                        edge_v=1.15, edge_s=1.00),
    # 定案：海洋藍（左手）／紅酒紅（右手）。
    #
    # 兩手的**色相不同**，所以參數要分開給 —— hands 裡的那一份會蓋掉外層。
    # 色相取自這兩個顏色的慣例值：海洋藍 ≈ 200（偏青的藍）、酒紅 ≈ 348（偏紫的紅）。
    #
    # 明度沒有照「酒紅很暗」去壓：遊戲管線實測會把明度砍掉快三分之二
    # （貼圖 0.918 → 畫面 0.341），照色票的 0.45 下去，畫面上就剩 0.15 ＝ 黑的。
    # 這裡留在 0.72–0.78，讓它在畫面上落到酒紅該有的濃度。
    "ocean_wine": dict(label="海洋藍／酒紅", hue=None, v=1.00, s=1.00, lift=0.30,
                       edge_v=1.22, edge_s=1.00,
                       hands={
                           "Red":  dict(hue=348.0, s=0.96, lift=0.18),
                           "Blue": dict(hue=200.0, s=1.10, lift=0.40),
                       }),
    "current":    dict(label="現狀",        hue=None,  v=1.00, s=1.00, lift=0.00),
}

CHOSEN = "ocean_wine"


def rgb_to_hsv(rgb):
    mx = rgb.max(-1)
    mn = rgb.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-9
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    idx = nz & (mx == r)
    h[idx] = (((g - b) / np.where(d == 0, 1, d))[idx] % 6.0) * 60.0
    idx = nz & (mx == g) & (mx != r)
    h[idx] = (((b - r) / np.where(d == 0, 1, d))[idx] + 2.0) * 60.0
    idx = nz & (mx == b) & (mx != r) & (mx != g)
    h[idx] = (((r - g) / np.where(d == 0, 1, d))[idx] + 4.0) * 60.0
    s = np.where(mx > 1e-9, d / np.maximum(mx, 1e-9), 0.0)
    return h % 360.0, s, mx


def hsv_to_rgb(h, s, v):
    h = (h % 360.0) / 60.0
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    out = np.zeros(h.shape + (3,), dtype=float)
    for k, (R, G, B) in enumerate([(v, t, p), (q, v, p), (p, v, t),
                                   (p, q, v), (t, p, v), (v, p, q)]):
        m = i == k
        out[m] = np.stack([R, G, B], -1)[m]
    return out


def recolour(src, target_h, v_gain, s_gain, v_lift=0.0,
             edge_v=1.0, edge_s=1.0):
    """只把填色換成 target_h。target_h 為 None 就原樣回傳。"""
    a = np.asarray(src.convert("RGBA")).astype(float) / 255.0
    rgb, alpha = a[..., :3], a[..., 3]
    h, s, v = rgb_to_hsv(rgb)

    # 色相是環狀的，所以要用繞一圈的距離，不能直接相減。
    dh = np.abs(((h - GOLD_HUE + 180.0) % 360.0) - 180.0)
    gold = np.clip((GOLD_SPAN - dh) / (GOLD_SPAN - GOLD_CORE), 0.0, 1.0)
    # 1 = 填色，0 = 金邊。過渡帶留著，換色之後邊緣才不會長出一圈硬邊。
    w = 1.0 - gold
    # 灰階的地方不要動：那是白色高光和近黑的外框，它們沒有色相可言。
    w = w * np.clip((s - 0.12) / 0.18, 0.0, 1.0)

    # ── 越靠外框越深 ────────────────────────────────────────────
    #
    # 「深度」用填色遮罩的模糊當代理：模糊之後，深在填色內部的像素還是接近 1，
    # 靠近金框的像素會被外面的 0 拉下來。沒有 scipy 可以做距離轉換，而這個代理
    # 對「一條有寬度的色帶」來說夠準，而且本來就是平滑的 —— 距離轉換反而要再
    # 模糊一次才不會出現等高線。
    if edge_v != 1.0 or edge_s != 1.0:
        side = max(src.size)
        blur = Image.fromarray((np.clip(w, 0, 1) * 255).astype(np.uint8))
        blur = blur.filter(ImageFilter.GaussianBlur(side * 0.055))
        depth = np.asarray(blur).astype(float) / 255.0
        # 重新拉伸到 0..1：模糊之後的值擠在中段，直接用的話漸層會很弱。
        depth = np.clip((depth - 0.30) / 0.45, 0.0, 1.0)
        depth = depth * depth * (3.0 - 2.0 * depth)
    else:
        depth = np.ones_like(w)
    edge = 1.0 - depth                      # 0 = 深處，1 = 貼著外框

    # target_h 給 None ＝ 保留原本的色相，只調彩度和明度。
    # 「改回紅藍但淡一點亮一點」就是這一種：形狀、色相都不動，只換濃淡。
    out_h = h if target_h is None else np.where(w > 0.0, np.full_like(h, target_h), h)
    # **抬升不能套在暗的地方。**
    #
    # 金圈外面那一圈近黑的暗邊，作用是把箭頭和底下那塊會發光的玻璃音符分開（音符
    # 是加法混色＋HDR＋bloom，沒有暗邊的話箭頭的輪廓會被光吃掉）。等比抬升對暗處
    # 拉得最多 —— 量過：V 0.25 的那 16000 多個像素會被抬到 0.72，那圈暗邊就等於
    # 被拆掉了。填色底部的陰影同理，那是給形狀立體感的。
    #
    # 所以抬升照明度自己加權：V 低於 DARK_KEEP 完全不抬，高於 DARK_FULL 才全抬。
    lift_w = np.clip((v - DARK_KEEP) / (DARK_FULL - DARK_KEEP), 0.0, 1.0)
    lifted = v + (1.0 - v) * v_lift * lift_w
    v_mul = v_gain * (1.0 + (edge_v - 1.0) * edge)
    s_mul = s_gain * (1.0 + (edge_s - 1.0) * edge)
    out_v = (v + (lifted * v_mul - v) * w)
    out_s = s * (1.0 + (s_mul - 1.0) * w)
    out = hsv_to_rgb(out_h, np.clip(out_s, 0, 1), np.clip(out_v, 0, 1))
    # 只在權重內混，權重外完全保留原像素。
    out = rgb * (1.0 - w)[..., None] + out * w[..., None]
    merged = np.concatenate([out, alpha[..., None]], -1)
    return Image.fromarray((merged * 255).clip(0, 255).astype(np.uint8))


def stage(width, height):
    """一塊有代表性的背景：深紅舞台 + 一條會發光的右手音符。"""
    y = np.linspace(0, 1, height)[:, None]
    bg = np.zeros((height, width, 3))
    bg[..., 0] = 0.20 - 0.09 * y
    bg[..., 1] = 0.018 - 0.008 * y
    bg[..., 2] = 0.042 - 0.018 * y
    img = Image.fromarray((bg * 255).clip(0, 255).astype(np.uint8)).convert("RGBA")

    note = Image.new("RGBA", (int(width * 0.74), int(height * 0.17)), (0, 0, 0, 0))
    na = np.zeros((note.height, note.width, 4))
    ny = np.linspace(0, 1, note.height)[:, None]
    core = np.exp(-((ny - 0.42) / 0.26) ** 2)
    na[..., 0] = 0.98
    na[..., 1] = 0.30 + 0.62 * core
    na[..., 2] = 0.34 + 0.52 * core
    na[..., 3] = 0.92
    note = Image.fromarray((na * 255).clip(0, 255).astype(np.uint8))
    img.paste(note, ((width - note.width) // 2, int(height * 0.66)), note)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", action="store_true", help="只產生候選比較圖")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    # **一定要從未經處理的原圖出發，不能拿上一次的輸出當來源。**
    #
    # 這支會覆蓋 Assets 底下的素材，所以第二次執行時那裡放的已經是上一次染好的圖。
    # 再染一次等於把色相位移、明度倍率、抬升全部疊第二層 —— 同樣的參數會得到完全
    # 不同的結果，而且每多跑一次就再偏一點。原圖存在 tools/art_src/，和產生器放在
    # 一起，換顏色隨時可以重來。
    sources = {
        "Red": Image.open(os.path.join(SRC, "StaccatoMark_Red.png")),
        "Blue": Image.open(os.path.join(SRC, "StaccatoMark_Blue.png")),
    }
    src = sources["Red"]        # 比較圖只畫右手那張

    if args.sheet:
        keys = ["current", "hand_pale", "hand_paler", "hand_palest"]
        cw, ch = 300, 330
        sheet = Image.new("RGBA", (cw * len(keys), ch), (14, 14, 16, 255))
        for i, k in enumerate(keys):
            c = CANDIDATES[k]
            mark = recolour(src, c['hue'], c['v'], c['s'], c['lift'],
                            c.get('edge_v', 1.0), c.get('edge_s', 1.0))
            mark = mark.resize((int(cw * 0.62), int(cw * 0.62 * mark.height / mark.width)),
                               Image.LANCZOS)
            cell = stage(cw, ch)
            cell.paste(mark, ((cw - mark.width) // 2, int(ch * 0.30)), mark)
            sheet.paste(cell, (i * cw, 0))
        out = args.out or os.path.join(ROOT, "tools", "staccato_mark_candidates.png")
        sheet.convert("RGB").save(out)
        print(out)
        print("由左至右：" + "、".join(CANDIDATES[k]["label"] for k in keys))
        return

    c = CANDIDATES[CHOSEN]

    # **兩隻手同一色，所以兩個檔案從同一張來源產生，像素完全一致。**
    #
    # 這是刻意的：箭頭要變成一個「符號」—— 眼睛只要學會「這個顏色＝斷奏」。手別由
    # 音符本身負責，不需要箭頭再講一次；原本那個紅／藍把顏色花在一個你已經知道的
    # 資訊上，等於沒有替「這顆是斷奏」出到力。
    #
    # 兩張原圖的 alpha 完全相同（量過：最大差 0），只有填色不同，所以拿紅的當來源
    # 不會改到形狀。檔名維持 _Red／_Blue：程式和 Note.prefab 都照這兩個名字找，改
    # 名字等於多動兩個地方而沒有換到任何東西。
    # **兩手分開處理，各自從自己的原圖出發。**
    # 同色的版本可以共用一張來源，手色不行 —— 紅的色相 6、藍的 217.5，
    # 拿同一張去染只會得到兩張一樣的圖。
    for hand, source in sources.items():
        # hands 裡那一份蓋掉外層：兩手的色相不同時，共用一組參數做不出來。
        p = dict(c)
        p.update((c.get("hands") or {}).get(hand, {}))
        tinted = recolour(source, p['hue'], p['v'], p['s'], p['lift'],
                          p.get('edge_v', 1.0), p.get('edge_s', 1.0))
        tinted.save(os.path.join(ART, "StaccatoMark_%s.png" % hand))
    print("StaccatoMark_{Red,Blue}.png -> %s（兩手同色）" % c["label"])


if __name__ == "__main__":
    main()
