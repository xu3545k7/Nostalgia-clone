using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 設定畫面的材質和零件：按鈕、分類框、開關、圖示。
/// </summary>
/// <remarks>
/// **為什麼另起一套，不沿用 <see cref="ClassicalBookUITheme"/> 的銅牌。** 銅牌是
/// 為選歌頁那幾顆大按鈕畫的：20 像素的線腳、溝槽、四角鉚釘。縮到設定列上 40 像
/// 素高的時候，線腳本身就佔掉一半，字盤只剩一條縫，看起來是一團金色而不是按鈕。
///
/// 這裡的材質反過來：**邊只有一條細金線**，受光的上緣亮、背光的下緣暗；字盤是
/// 一層漆面的漸層，上緣內側留一道很淡的反光。細節少，所以縮小不會糊；而那道受
/// 光方向和選歌頁的銅牌是同一個光源，兩頁放在一起仍然是同一個世界的東西。
///
/// 所有貼圖都是程式產生的，產生一次就快取 —— 不依賴任何匯入的圖檔，也就不會因
/// 為某張圖的匯入設定不對而整組變形。
/// </remarks>
public static class SettingsUiKit
{
    public enum Tone
    {
        /// <summary>一般按鈕：深色漆面。</summary>
        Lacquer,
        /// <summary>主要動作：酒紅漆面、亮金邊。一個畫面只該有一顆。</summary>
        Accent,
        /// <summary>列上的小按鈕：半透明，不和數值搶視線。</summary>
        Quiet,
    }

    public static readonly Color PageColour = new Color(0.030f, 0.020f, 0.015f, 0.985f);
    public static readonly Color TextColour = new Color(0.94f, 0.89f, 0.78f, 1f);
    public static readonly Color MutedText = new Color(0.66f, 0.58f, 0.47f, 1f);
    public static readonly Color Gold = new Color(0.80f, 0.61f, 0.27f, 1f);
    public static readonly Color GoldBright = new Color(1.00f, 0.84f, 0.50f, 1f);

    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    // ------------------------------------------------------------------
    // 貼圖
    // ------------------------------------------------------------------

    public static Sprite ButtonSprite(Tone tone)
    {
        switch (tone)
        {
            case Tone.Accent:
                return Rounded("SettingsButtonAccent", 64, 11f, 18,
                    new Color(0.50f, 0.13f, 0.09f, 0.98f), new Color(0.24f, 0.05f, 0.04f, 0.98f),
                    1.6f, new Color(1.00f, 0.84f, 0.48f, 1f), new Color(0.60f, 0.42f, 0.18f, 1f),
                    0f, Color.clear, 0.15f);
            case Tone.Quiet:
                return Rounded("SettingsButtonQuiet", 64, 10f, 18,
                    new Color(0.17f, 0.115f, 0.08f, 0.80f), new Color(0.095f, 0.065f, 0.045f, 0.80f),
                    1.2f, new Color(0.84f, 0.65f, 0.32f, 0.62f), new Color(0.45f, 0.33f, 0.17f, 0.45f),
                    0f, Color.clear, 0.07f);
            default:
                return Rounded("SettingsButtonLacquer", 64, 11f, 18,
                    new Color(0.24f, 0.16f, 0.11f, 0.97f), new Color(0.11f, 0.07f, 0.05f, 0.97f),
                    1.6f, new Color(0.94f, 0.75f, 0.40f, 0.95f), new Color(0.46f, 0.33f, 0.16f, 0.90f),
                    0f, Color.clear, 0.11f);
        }
    }

    /// <summary>
    /// 分類框：半透明深底、一條受光的細金邊、內側再一道很淡的線。
    /// </summary>
    /// <remarks>
    /// 雙線是古典版面的框法 —— 單一條線圍起來的是「一塊區域」，雙線圍起來的才
    /// 是「一個欄目」。內線刻意壓到 16% 的不透明度，它要被感覺到，而不是被看到。
    /// </remarks>
    public static Sprite FrameSprite()
    {
        return Rounded("SettingsFrame", 96, 14f, 30,
            new Color(0.080f, 0.054f, 0.038f, 0.93f), new Color(0.062f, 0.042f, 0.030f, 0.93f),
            1.5f, new Color(0.86f, 0.66f, 0.30f, 0.78f), new Color(0.50f, 0.37f, 0.18f, 0.55f),
            6f, new Color(0.80f, 0.61f, 0.27f, 0.16f), 0.05f);
    }

    /// <summary>沒有邊的暗色底板。側欄用：框在裡面的分類框身上，外面再框一次會變成框中框。</summary>
    public static Sprite SoftPanelSprite()
    {
        return Rounded("SettingsSoftPanel", 96, 18f, 30,
            new Color(0.030f, 0.020f, 0.015f, 0.82f), new Color(0.022f, 0.015f, 0.011f, 0.86f),
            0f, Color.clear, Color.clear, 0f, Color.clear, 0f);
    }

    public static Sprite PillSprite(bool on)
    {
        return on
            ? Rounded("SettingsPillOn", 64, 31.5f, 31,
                new Color(0.86f, 0.66f, 0.30f, 1f), new Color(0.60f, 0.43f, 0.17f, 1f),
                1.3f, new Color(1.00f, 0.88f, 0.58f, 1f), new Color(0.46f, 0.32f, 0.12f, 1f),
                0f, Color.clear, 0.12f)
            : Rounded("SettingsPillOff", 64, 31.5f, 31,
                new Color(0.075f, 0.050f, 0.036f, 0.95f), new Color(0.12f, 0.08f, 0.06f, 0.95f),
                1.3f, new Color(0.80f, 0.61f, 0.27f, 0.55f), new Color(0.46f, 0.34f, 0.17f, 0.45f),
                0f, Color.clear, 0f);
    }

    public static Sprite KnobSprite()
    {
        return Rounded("SettingsKnob", 64, 31.5f, 0,
            new Color(1.00f, 0.96f, 0.87f, 1f), new Color(0.80f, 0.72f, 0.58f, 1f),
            1.2f, new Color(0.55f, 0.42f, 0.24f, 0.9f), new Color(0.30f, 0.21f, 0.11f, 0.9f),
            0f, Color.clear, 0.10f);
    }

    /// <summary>
    /// 按鈕外圍的光暈：只在形狀**外面**，裡面是空的。
    /// </summary>
    /// <remarks>
    /// 光暈掛在按鈕底下當子物件，子物件畫在父物件之上 —— 內部不挖空的話，滑鼠一
    /// 移上去整顆按鈕會被蓋成一片金色。
    /// </remarks>
    public static Sprite GlowSprite()
    {
        const string key = "SettingsGlow";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int size = 64;
        const float inset = 16f;
        const float radius = 10f;
        const float sigma = 5.5f;
        var texture = NewTexture(key, size);
        var pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + 0.5f - half, y + 0.5f - half, half - inset, radius);
                float a = d <= 0f ? 0f : Mathf.Exp(-(d * d) / (2f * sigma * sigma));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return Store(key, texture, 24);
    }

    /// <summary>橫向的細線，兩端淡出（centre）或只往右淡出（left）。</summary>
    public static Sprite RuleSprite(bool fadeBothEnds)
    {
        string key = fadeBothEnds ? "SettingsRuleCentre" : "SettingsRuleLeft";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int width = 256;
        const int height = 4;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = key,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float a = fadeBothEnds
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.22f))
                  * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.22f))
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.85f));
            for (int y = 0; y < height; y++)
                pixels[y * width + x] = new Color(1f, 1f, 1f, a);
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = key;
        Cache[key] = sprite;
        return sprite;
    }

    /// <summary>四角壓暗。整頁的底色只有一個顏色的話，看起來是「沒有畫」而不是「暗的房間」。</summary>
    public static Sprite VignetteSprite()
    {
        const string key = "SettingsVignette";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int size = 128;
        var texture = NewTexture(key, size);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f);
                pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.25f, r)));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = key;
        Cache[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// 圓角矩形的九宮格貼圖：上下漸層的面、受光方向決定亮暗的邊、內側一道線。
    /// </summary>
    /// <remarks>
    /// 距離場算邊緣，所以圓角有反鋸齒，放大縮小都不會出現階梯。
    ///
    /// 透明像素的 RGB 填成邊的顏色而不是黑色：雙線性取樣會把邊緣和旁邊的透明像素
    /// 混在一起，透明處是黑的話，每一顆按鈕外圈都會多一道髒髒的暗邊。
    /// </remarks>
    private static Sprite Rounded(string key, int size, float radius, int border,
        Color fillTop, Color fillBottom, float rim, Color rimLit, Color rimShade,
        float innerRule, Color innerRuleColour, float highlight)
    {
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var texture = NewTexture(key, size);
        var pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            Color fill = Color.Lerp(fillBottom, fillTop, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.92f, v)));
            Color edge = rim > 0f
                ? Color.Lerp(rimShade, rimLit, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.88f, v)))
                : fill;

            for (int x = 0; x < size; x++)
            {
                float d = RoundedDistance(x + 0.5f - half, y + 0.5f - half, half, radius);
                float coverage = Mathf.Clamp01(0.5f - d);
                if (coverage <= 0f)
                {
                    pixels[y * size + x] = new Color(edge.r, edge.g, edge.b, 0f);
                    continue;
                }

                float depth = -d;
                Color c = rim > 0f
                    ? Color.Lerp(edge, fill, Mathf.Clamp01(depth - rim + 0.5f))
                    : fill;

                if (innerRule > 0f)
                {
                    float k = Mathf.Clamp01(1f - Mathf.Abs(depth - innerRule)) * innerRuleColour.a;
                    c.r = Mathf.Lerp(c.r, innerRuleColour.r, k);
                    c.g = Mathf.Lerp(c.g, innerRuleColour.g, k);
                    c.b = Mathf.Lerp(c.b, innerRuleColour.b, k);
                }

                if (highlight > 0f)
                {
                    // 上緣內側那道反光。只在上半部，往下淡掉。
                    float k = Mathf.Clamp01(1f - Mathf.Abs(depth - (rim + 1.2f)))
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, v));
                    c.r = Mathf.Clamp01(c.r + highlight * k);
                    c.g = Mathf.Clamp01(c.g + highlight * k);
                    c.b = Mathf.Clamp01(c.b + highlight * k * 0.85f);
                }

                c.a *= coverage;
                pixels[y * size + x] = c;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return Store(key, texture, border);
    }

    /// <summary>點到圓角矩形邊緣的有號距離，裡面是負的。</summary>
    private static float RoundedDistance(float px, float py, float halfExtent, float radius)
    {
        float qx = Mathf.Abs(px) - (halfExtent - radius);
        float qy = Mathf.Abs(py) - (halfExtent - radius);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static Texture2D NewTexture(string key, int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = key,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
    }

    private static Sprite Store(string key, Texture2D texture, int border)
    {
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        sprite.name = key;
        Cache[key] = sprite;
        return sprite;
    }

    // ------------------------------------------------------------------
    // 物件
    // ------------------------------------------------------------------

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
        float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static Image AddImage(GameObject target, Sprite sprite, Color colour, bool raycast)
    {
        Image image = target.GetComponent<Image>();
        if (image == null) image = target.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        image.color = colour;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI CreateLabel(Transform parent, string name, float size, Color colour,
        TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
    {
        RectTransform rect = CreateRect(name, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.fontStyle = style;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }

    /// <summary>設文字，只在真的變了的時候才重設字型。</summary>
    public static void SetText(TextMeshProUGUI label, string text)
    {
        if (label == null) return;
        text ??= string.Empty;
        if (label.text == text) return;
        label.text = text;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
    }

    /// <summary>
    /// 一顆按鈕：底板、上面一層反光、外圍光暈，再加圖示和（或）文字。
    /// </summary>
    public static Button CreateButton(Transform parent, string name, Tone tone,
        SettingsGlyph.Shape? glyphShape, string caption, float fontSize, Vector2 size,
        out TextMeshProUGUI label, out SettingsGlyph glyph)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.sizeDelta = size;
        Image plate = AddImage(rect.gameObject, ButtonSprite(tone), Color.white, true);

        Shadow shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, tone == Tone.Quiet ? 0.28f : 0.5f);
        shadow.effectDistance = new Vector2(0f, -3f);

        RectTransform sheenRect = CreateRect("Sheen", rect);
        Stretch(sheenRect);
        Image sheen = AddImage(sheenRect.gameObject, ButtonSprite(tone), new Color(1f, 0.93f, 0.78f, 0f), false);

        RectTransform glowRect = CreateRect("Glow", rect);
        Stretch(glowRect, -14f, -14f, -14f, -14f);
        Image glow = AddImage(glowRect.gameObject, GlowSprite(), new Color(Gold.r, Gold.g, Gold.b, 0f), false);

        Color ink = tone == Tone.Accent ? new Color(1f, 0.95f, 0.86f, 1f) : TextColour;

        glyph = null;
        if (glyphShape.HasValue)
        {
            RectTransform glyphRect = CreateRect("Glyph", rect);
            glyph = glyphRect.gameObject.AddComponent<SettingsGlyph>();
            glyph.Kind = glyphShape.Value;
            glyph.color = tone == Tone.Accent ? GoldBright : new Color(0.93f, 0.80f, 0.55f, 1f);
            glyph.raycastTarget = false;
        }

        label = null;
        if (!string.IsNullOrEmpty(caption))
        {
            label = CreateLabel(rect, "Label", fontSize, ink, TextAlignmentOptions.Center, FontStyles.Bold);
            label.characterSpacing = 1.5f;
            // 英文字串通常比中文長一倍（「譜面預覽」vs「Chart Preview」）。按鈕寬度
            // 照中文訂，英文就讓字自己縮一點，而不是被截成「Chart Pre…」。
            label.enableAutoSizing = true;
            label.fontSizeMin = fontSize * 0.72f;
            label.fontSizeMax = fontSize;
            SetText(label, caption);
        }

        LayoutButtonContent(rect, glyph, label);

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.None;
        var feel = rect.gameObject.AddComponent<SettingsButtonFeel>();
        feel.Configure(button, sheen, glow, glyph, label);
        return button;
    }

    /// <summary>圖示靠左、文字填滿其餘；只有圖示就置中。</summary>
    public static void LayoutButtonContent(RectTransform button, SettingsGlyph glyph, TextMeshProUGUI label)
    {
        if (button == null) return;
        float height = Mathf.Max(20f, button.sizeDelta.y);
        if (glyph != null)
        {
            RectTransform g = glyph.rectTransform;
            float s = Mathf.Round(height * 0.40f);
            if (label == null)
            {
                Place(g, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s, s));
            }
            else
            {
                Place(g, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(s, s));
            }
        }
        if (label != null)
        {
            Stretch(label.rectTransform, glyph != null ? 18f + height * 0.40f + 6f : 12f, 2f, 14f, 2f);
        }
    }
}

/// <summary>
/// 設定畫面用的線條圖示。直接畫網格，不依賴字型裡有沒有那個符號。
/// </summary>
/// <remarks>
/// 「‹ › − ▶」這幾個字元在繁中、簡中、英文三套字型裡不一定都有，缺字時 TMP 會
/// 畫出一個方框。圖示用幾何畫，任何語言下都是同一個形狀、同一個粗細。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SettingsGlyph : MaskableGraphic
{
    public enum Shape { Minus, Plus, ChevronLeft, ChevronRight, Back, Play, Diamond, Sliders }

    [SerializeField] private Shape shape;
    [SerializeField] private float stroke = 2.6f;

    public Shape Kind
    {
        get => shape;
        set
        {
            if (shape == value) return;
            shape = value;
            SetVerticesDirty();
        }
    }

    public float Stroke
    {
        get => stroke;
        set
        {
            stroke = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float h = Mathf.Min(r.width, r.height) * 0.5f;
        if (h <= 1f) return;
        Vector2 c = r.center;
        float w = Mathf.Max(1.4f, stroke);

        switch (shape)
        {
            case Shape.Minus:
                Line(vh, c + new Vector2(-h * 0.62f, 0f), c + new Vector2(h * 0.62f, 0f), w);
                break;
            case Shape.Plus:
                Line(vh, c + new Vector2(-h * 0.62f, 0f), c + new Vector2(h * 0.62f, 0f), w);
                Line(vh, c + new Vector2(0f, -h * 0.62f), c + new Vector2(0f, h * 0.62f), w);
                break;
            case Shape.ChevronLeft:
                Line(vh, c + new Vector2(h * 0.22f, h * 0.58f), c + new Vector2(-h * 0.30f, 0f), w);
                Line(vh, c + new Vector2(-h * 0.30f, 0f), c + new Vector2(h * 0.22f, -h * 0.58f), w);
                break;
            case Shape.ChevronRight:
                Line(vh, c + new Vector2(-h * 0.22f, h * 0.58f), c + new Vector2(h * 0.30f, 0f), w);
                Line(vh, c + new Vector2(h * 0.30f, 0f), c + new Vector2(-h * 0.22f, -h * 0.58f), w);
                break;
            case Shape.Back:
                Line(vh, c + new Vector2(h * 0.70f, 0f), c + new Vector2(-h * 0.62f, 0f), w);
                Line(vh, c + new Vector2(-h * 0.62f, 0f), c + new Vector2(-h * 0.10f, h * 0.50f), w);
                Line(vh, c + new Vector2(-h * 0.62f, 0f), c + new Vector2(-h * 0.10f, -h * 0.50f), w);
                break;
            case Shape.Play:
                Triangle(vh, c + new Vector2(-h * 0.42f, h * 0.62f), c + new Vector2(h * 0.62f, 0f),
                    c + new Vector2(-h * 0.42f, -h * 0.62f));
                break;
            case Shape.Diamond:
                Quad(vh, c + new Vector2(0f, h), c + new Vector2(h, 0f), c + new Vector2(0f, -h), c + new Vector2(-h, 0f));
                break;
            case Shape.Sliders:
                float[] knobs = { 0.68f, 0.30f, 0.56f };
                for (int i = 0; i < knobs.Length; i++)
                {
                    float y = c.y + Mathf.Lerp(h * 0.62f, -h * 0.62f, i / 2f);
                    Line(vh, new Vector2(c.x - h * 0.80f, y), new Vector2(c.x + h * 0.80f, y), Mathf.Max(1.2f, w * 0.55f));
                    float kx = c.x + Mathf.Lerp(-h * 0.62f, h * 0.62f, knobs[i]);
                    Quad(vh, new Vector2(kx - h * 0.14f, y - h * 0.22f), new Vector2(kx - h * 0.14f, y + h * 0.22f),
                        new Vector2(kx + h * 0.14f, y + h * 0.22f), new Vector2(kx + h * 0.14f, y - h * 0.22f));
                }
                break;
        }
    }

    private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        Vector2 n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        // 兩端各延長半個線寬，折角處兩段才會接上，不會在尖端缺一個口。
        a -= dir * (width * 0.5f);
        b += dir * (width * 0.5f);
        Quad(vh, a - n, a + n, b + n, b - n);
    }

    private void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        int start = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = p0; vh.AddVert(v);
        v.position = p1; vh.AddVert(v);
        v.position = p2; vh.AddVert(v);
        v.position = p3; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    private void Triangle(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        int start = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = p0; vh.AddVert(v);
        v.position = p1; vh.AddVert(v);
        v.position = p2; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}

/// <summary>
/// 按鈕的手感：滑上去亮起來、外圍起一圈金光、按下去往下沉一點。
/// </summary>
/// <remarks>
/// 不用 Button 內建的 ColorTint：它是把整張底圖乘上一個顏色，而頂點顏色在 UI 裡
/// 會被夾在 1 以內 —— 「變亮」只能做成「其他狀態先變暗」，平常的樣子就被迫是灰
/// 的。這裡改成在底板上疊一層透明度會變的反光，平常就是原色，滑上去才加亮。
/// </remarks>
public sealed class SettingsButtonFeel : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Button button;
    private Image sheen;
    private Image glow;
    private Graphic glyph;
    private TextMeshProUGUI label;
    private Color glyphRest;
    private Color labelRest;

    private float hover;
    private float press;
    private float dim = -1f;
    private bool pointerInside;
    private bool pointerDown;

    public void Configure(Button target, Image sheenImage, Image glowImage, Graphic glyphGraphic,
        TextMeshProUGUI labelText)
    {
        button = target;
        sheen = sheenImage;
        glow = glowImage;
        glyph = glyphGraphic;
        label = labelText;
        glyphRest = glyph != null ? glyph.color : Color.white;
        labelRest = label != null ? label.color : Color.white;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;
    public void OnPointerExit(PointerEventData eventData) => pointerInside = false;
    public void OnPointerDown(PointerEventData eventData) => pointerDown = true;
    public void OnPointerUp(PointerEventData eventData) => pointerDown = false;

    private void OnDisable()
    {
        pointerInside = false;
        pointerDown = false;
        hover = 0f;
        press = 0f;
        dim = -1f;
        Apply();
    }

    private void Update()
    {
        bool usable = button == null || button.interactable;
        float wantHover = usable && pointerInside ? 1f : 0f;
        float wantPress = usable && pointerDown && pointerInside ? 1f : 0f;
        float wantDim = usable ? 0f : 1f;
        if (Mathf.Approximately(hover, wantHover) && Mathf.Approximately(press, wantPress)
            && Mathf.Approximately(dim, wantDim)) return;

        float dt = Time.unscaledDeltaTime;
        hover = Mathf.MoveTowards(hover, wantHover, dt / 0.12f);
        press = Mathf.MoveTowards(press, wantPress, dt / 0.05f);
        dim = dim < 0f ? wantDim : Mathf.MoveTowards(dim, wantDim, dt / 0.10f);
        Apply();
    }

    private void Apply()
    {
        float d = Mathf.Max(0f, dim);
        if (sheen != null) sheen.color = new Color(1f, 0.93f, 0.78f, 0.13f * hover);
        if (glow != null)
            glow.color = new Color(SettingsUiKit.Gold.r, SettingsUiKit.Gold.g, SettingsUiKit.Gold.b, 0.50f * hover);
        transform.localScale = Vector3.one * (1f - 0.04f * press);

        float fade = Mathf.Lerp(1f, 0.32f, d);
        if (glyph != null)
        {
            Color lit = Color.Lerp(glyphRest, SettingsUiKit.GoldBright, hover * 0.7f);
            lit.a = glyphRest.a * fade;
            glyph.color = lit;
        }
        if (label != null)
        {
            Color lit = Color.Lerp(labelRest, Color.white, hover * 0.6f);
            lit.a = labelRest.a * fade;
            label.color = lit;
        }
    }
}

/// <summary>開關：一條膠囊軌道，一顆會滑動的鈕。</summary>
public sealed class SettingsSwitch : MonoBehaviour
{
    private Image onFill;
    private RectTransform knob;
    private float travel;
    private float state;
    private float target;

    public void Configure(Image fill, RectTransform knobRect, float travelDistance)
    {
        onFill = fill;
        knob = knobRect;
        travel = travelDistance;
        Apply();
    }

    public void Set(bool on, bool instant)
    {
        target = on ? 1f : 0f;
        if (instant) state = target;
        Apply();
    }

    private void OnDisable()
    {
        state = target;
        Apply();
    }

    private void Update()
    {
        if (Mathf.Approximately(state, target)) return;
        state = Mathf.MoveTowards(state, target, Time.unscaledDeltaTime / 0.14f);
        Apply();
    }

    private void Apply()
    {
        float eased = state * state * (3f - 2f * state);
        if (onFill != null) onFill.color = new Color(1f, 1f, 1f, eased);
        if (knob != null) knob.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, eased), 0f);
    }
}

/// <summary>整列的滑過提示：一層很淡的金色底。</summary>
public sealed class SettingsRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Image background;
    private float state;
    private bool inside;

    public void Configure(Image image)
    {
        background = image;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) => inside = true;
    public void OnPointerExit(PointerEventData eventData) => inside = false;

    private void OnDisable()
    {
        inside = false;
        state = 0f;
        Apply();
    }

    private void Update()
    {
        float want = inside ? 1f : 0f;
        if (Mathf.Approximately(state, want)) return;
        state = Mathf.MoveTowards(state, want, Time.unscaledDeltaTime / 0.10f);
        Apply();
    }

    private void Apply()
    {
        if (background != null) background.color = new Color(1f, 0.82f, 0.50f, 0.065f * state);
    }
}
