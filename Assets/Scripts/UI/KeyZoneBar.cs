using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 畫面最下方那一條：把 MIDI 鍵盤切成幾個區塊，告訴玩家每一段鍵按下去會做什麼。
/// </summary>
/// <remarks>
/// **為什麼光是由下往上。** 這一條代表的是玩家面前那台實體鍵盤 —— 光從畫面下緣
/// 升起來，讀起來就是「從鍵盤那邊照上來的」。由上往下的光會變成一塊掛在畫面上的
/// 招牌，和手上的東西斷開。
///
/// **為什麼字是白的、而且只有字是白的。** 區塊的顏色負責分段，文字負責說是什麼。
/// 兩件事分開之後，顏色可以隨便換而不影響可讀性；文字跟著區塊變色的話，暗色區塊
/// 上的字就會先消失。
///
/// **為什麼區塊是資料不是寫死的。** 瀏覽和設定是兩套分法（三塊 vs 四塊），而且
/// 之後還會有別的畫面要用。區塊定義從外面餵進來，這個元件只負責畫和亮。
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public sealed class KeyZoneBar : MonoBehaviour
{
    /// <summary>一個區塊。</summary>
    public struct Zone
    {
        /// <summary>這一塊涵蓋的鍵位（含頭含尾，0–27）。</summary>
        public int FirstKey;
        public int LastKey;
        /// <summary>白字。</summary>
        public string Label;
        /// <summary>由下往上那道光的顏色。</summary>
        public Color Glow;

        public Zone(int firstKey, int lastKey, string label, Color glow)
        {
            FirstKey = firstKey;
            LastKey = lastKey;
            Label = label;
            Glow = glow;
        }
    }

    /// <summary>整條的高度。</summary>
    private const float BarHeight = 92f;

    /// <summary>沒被按的時候那道光有多亮。</summary>
    private const float IdleStrength = 0.34f;

    /// <summary>按著的時候有多亮。</summary>
    private const float PressedStrength = 1f;

    /// <summary>亮度追上去的速度（每秒）。瞬間切換會閃，太慢會感覺鍵盤沒反應。</summary>
    private const float StrengthLerpPerSecond = 9f;

    private readonly List<ZoneVisual> visuals = new List<ZoneVisual>(6);

    private sealed class ZoneVisual
    {
        public Zone Data;
        public KeyZoneGlow Glow;
        public TextMeshProUGUI Label;
        public float Strength;
        public float Target;
    }

    /// <summary>整條佔掉畫面底部多高。各畫面要照這個數字把自己縮短。</summary>
    public static float ReservedHeight => BarHeight;

    private static KeyZoneBar instance;

    /// <summary>
    /// 取得（必要時建立）唯一那一條。
    /// </summary>
    /// <remarks>
    /// **它有自己的覆蓋層 canvas，不住在任何畫面裡。** 第一版掛在選曲面板底下，
    /// 結果一打開設定、那個面板被 SetActive(false)，條就跟著消失 —— 而設定正是
    /// 最需要它的地方。它說明的是「現在手按下去會發生什麼」，那件事不屬於任何
    /// 一個畫面，所以它不該是任何一個畫面的子物件。
    ///
    /// 排序 31500：在開場字卡（31000）之上，但在布幕（32500）之下 —— 轉場的時候
    /// 它應該和別的東西一起被蓋住。
    /// </remarks>
    public static KeyZoneBar Ensure()
    {
        if (instance != null) return instance;

        var root = new GameObject("KeyZoneBarOverlay");
        DontDestroyOnLoad(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 31500;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560f, 1440f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var go = new GameObject("~KeyZoneBar", typeof(RectTransform));
        go.layer = root.layer;
        go.transform.SetParent(root.transform, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = new Vector2(0f, BarHeight);

        instance = go.AddComponent<KeyZoneBar>();
        return instance;
    }

    /// <summary>整條顯示或收起來。沒有鍵盤可用的畫面就收起來。</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    /// <summary>換一組區塊。內容一樣就什麼都不做，免得每幀重建。</summary>
    public void SetZones(IList<Zone> zones)
    {
        if (zones == null) return;
        if (SameAsCurrent(zones)) return;

        for (int i = 0; i < visuals.Count; i++)
        {
            if (visuals[i].Glow != null) Destroy(visuals[i].Glow.gameObject);
        }
        visuals.Clear();

        RectTransform rect = (RectTransform)transform;
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            // 區塊在畫面上的位置照**鍵位**分，不是照等分 —— 玩家對照的是手底下的鍵，
            // 所以第 0 鍵在最左、第 27 鍵在最右，中間按比例攤開。
            float x0 = zone.FirstKey / (float)KeyCount;
            float x1 = (zone.LastKey + 1) / (float)KeyCount;

            var go = new GameObject("Zone" + i, typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(rect, false);
            RectTransform zr = (RectTransform)go.transform;
            zr.anchorMin = new Vector2(x0, 0f);
            zr.anchorMax = new Vector2(x1, 1f);
            zr.offsetMin = Vector2.zero;
            zr.offsetMax = Vector2.zero;

            KeyZoneGlow glow = go.AddComponent<KeyZoneGlow>();
            glow.raycastTarget = false;
            glow.color = zone.Glow;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.layer = gameObject.layer;
            labelGo.transform.SetParent(zr, false);
            RectTransform lr = (RectTransform)labelGo.transform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            // 字帶要夠高：TMP 的 Truncate 在框高小於字級 1.5 倍時會整段不畫
            // （見 [[tmp-text-vanishes]]）。這裡雖然設了 Overflow，留夠空間仍然
            // 是對的 —— 字被框切掉和字沒畫出來一樣難查。
            //
            // **開了自動縮字之後，高度還要再多留。** 自動縮字是「塞不進 rect 就
            // 降字級」，而它**寬高都看**：6..70 之前這個框高 52，日文字型的行高是
            // 1.485 em（源真ゴシック，繁簡韓那三份是 1.448），34 級就是 50.5 ——
            // 只剩 1.5px。那樣每一塊的字都會為了高度而縮一兩級，而字級是對的、
            // 寬度也放得下，根本看不出來是誰在縮。中心點維持在 38（對齊方式是
            // 置中），所以加高不動字的位置。
            lr.offsetMin = new Vector2(4f, 6f);
            lr.offsetMax = new Vector2(-4f, 70f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = zone.Label;
            // 這是站在幾公尺外、手放在鍵盤上的時候要讀的字，不是湊近看的介面文字。
            ClassicalBookUITheme.StyleText(label, Color.white, 34f, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // Truncate 在框高小於字級 1.5 倍時整段不畫，見 [[tmp-text-vanishes]]。
            label.overflowMode = TextOverflowModes.Overflow;
            // **一塊的寬度是鍵位分的，字的長度是語言分的，兩邊對不上。**
            // 「設定（長按）」那一塊只有三個鍵寬（2560 的 3/28，扣掉邊距剩 266），
            // 而同一句話在各語言量起來是：繁簡 218、日「設定（長押し）」255、
            // 韓「설정（길게 누르기）」338、英「SETTINGS (HOLD)」354。
            // 「分類（長按）」更糟，日文是「ジャンル（長押し）」327。
            // NoWrap + Overflow 代表超出去的字直接畫到隔壁那一塊上面 ——
            // 兩塊的字疊在一起，比截斷還難讀。
            //
            // 所以讓字自己縮。最小 22 是照最壞的 ×1.33 再留餘裕（×1.55）訂的：
            // 自動縮字只在放不下時才動，放得下的語言仍然是 34 級，不會為了
            // 遷就最長的那一種語言把每一塊的字都做小。
            label.enableAutoSizing = true;
            label.fontSizeMin = 22f;
            label.fontSizeMax = 34f;
            label.raycastTarget = false;
            ClassicalBookUITheme.ApplyLocalizedFont(label);

            // **描邊要排在字型指派之後，而且要包起來。**
            // TMP 的 outlineWidth setter 會直接去改 fontMaterial；字型資產還沒解析
            // 好的時候那是 null，整個 setter 就 NullReferenceException —— 而這段跑在
            // Update 裡，等於每一幀炸一次。描邊只是讓白字在亮色區塊上站得住，
            // 沒有它字照樣讀得到，所以它不值得把整個畫面拖下水。
            try
            {
                if (label.fontSharedMaterial != null)
                {
                    label.outlineWidth = 0.14f;
                    label.outlineColor = new Color(0.03f, 0.015f, 0.01f, 0.95f);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("KeyZoneBar: 描邊設定失敗，略過。" + e.Message);
            }

            visuals.Add(new ZoneVisual
            {
                Data = zone,
                Glow = glow,
                Label = label,
                Strength = IdleStrength,
                Target = IdleStrength,
            });
        }
    }

    /// <summary>鍵盤上一共幾個鍵位。和 MidiNoteToKeyMapper 的表同寬。</summary>
    public const int KeyCount = 28;

    /// <summary>
    /// 哪些鍵現在按著。每幀餵進來，<c>null</c> 代表全部放開。
    /// </summary>
    public void SetPressedKeys(Func<int, bool> isKeyPressed)
    {
        for (int i = 0; i < visuals.Count; i++)
        {
            bool lit = false;
            if (isKeyPressed != null)
            {
                for (int k = visuals[i].Data.FirstKey; k <= visuals[i].Data.LastKey; k++)
                {
                    if (isKeyPressed(k)) { lit = true; break; }
                }
            }
            visuals[i].Target = lit ? PressedStrength : IdleStrength;
        }
    }

    private void Update()
    {
        float step = Time.unscaledDeltaTime * StrengthLerpPerSecond;
        for (int i = 0; i < visuals.Count; i++)
        {
            ZoneVisual v = visuals[i];
            if (Mathf.Abs(v.Strength - v.Target) < 0.001f) continue;
            v.Strength = Mathf.MoveTowards(v.Strength, v.Target, step);
            if (v.Glow != null) v.Glow.SetStrength(v.Strength);
        }
    }

    private bool SameAsCurrent(IList<Zone> zones)
    {
        if (visuals.Count != zones.Count) return false;
        for (int i = 0; i < zones.Count; i++)
        {
            Zone a = visuals[i].Data;
            Zone b = zones[i];
            if (a.FirstKey != b.FirstKey || a.LastKey != b.LastKey || a.Label != b.Label) return false;
        }
        return true;
    }
}

/// <summary>
/// 一塊區域的光：下緣最亮，往上淡出。
/// </summary>
/// <remarks>
/// 用 <see cref="MaskableGraphic"/> 自己填頂點色，不用貼圖也不用 shader ——
/// 這是一個純粹的垂直漸層，一張貼圖或一支 shader 都只是把同樣的兩個顏色放到
/// 別的地方去管。中間多切幾段是因為頂點色是線性插值的，而視覺上想要的是
/// 「貼著下緣很亮、很快淡掉」那種指數型的衰減。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KeyZoneGlow : MaskableGraphic
{
    /// <summary>垂直切幾段。線性插值做不出指數衰減，靠分段逼近。</summary>
    private const int Bands = 10;

    /// <summary>衰減的陡峭程度。越大代表光越貼著下緣。</summary>
    private const float Falloff = 2.6f;

    /// <summary>左右邊緣各留幾成給淡出，免得相鄰兩塊之間出現一條硬邊。</summary>
    private const float EdgeFadeShare = 0.06f;

    private float strength = 0.34f;

    public void SetStrength(float value)
    {
        strength = Mathf.Clamp01(value);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;

        // 左右的淡出用三段欄位：左緣、本體、右緣。相鄰兩塊的光因此互相化開，
        // 中間不會出現一條接縫 —— 接縫會讓人以為那是一條分隔線而不是兩塊光。
        float fade = Mathf.Max(1f, r.width * EdgeFadeShare);
        float[] xs = { r.xMin, r.xMin + fade, r.xMax - fade, r.xMax };
        float[] xw = { 0f, 1f, 1f, 0f };
        if (xs[1] > xs[2])
        {
            xs = new[] { r.xMin, r.center.x, r.center.x, r.xMax };
            xw = new[] { 0f, 1f, 1f, 0f };
        }

        for (int b = 0; b < Bands; b++)
        {
            float t0 = b / (float)Bands;
            float t1 = (b + 1) / (float)Bands;
            float a0 = Mathf.Pow(1f - t0, Falloff) * strength;
            float a1 = Mathf.Pow(1f - t1, Falloff) * strength;
            float y0 = r.yMin + r.height * t0;
            float y1 = r.yMin + r.height * t1;

            for (int c = 0; c < 3; c++)
            {
                float xa = xs[c], xb = xs[c + 1];
                if (xb - xa <= 0.01f) continue;
                float wa = xw[c], wb = xw[c + 1];
                int baseIndex = vh.currentVertCount;
                vh.AddVert(new Vector3(xa, y0), Tint(a0 * wa), Vector2.zero);
                vh.AddVert(new Vector3(xb, y0), Tint(a0 * wb), Vector2.zero);
                vh.AddVert(new Vector3(xb, y1), Tint(a1 * wb), Vector2.zero);
                vh.AddVert(new Vector3(xa, y1), Tint(a1 * wa), Vector2.zero);
                vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
                vh.AddTriangle(baseIndex + 2, baseIndex + 3, baseIndex);
            }
        }
    }

    private Color32 Tint(float alpha)
    {
        Color c = color;
        c.a = Mathf.Clamp01(alpha);
        return c;
    }
}
