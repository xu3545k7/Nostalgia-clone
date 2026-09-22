using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-built backdrop for the song library: a neutral classical room,
/// difficulty-coloured stage light and a small amount of illuminated dust.
/// </summary>
public sealed class SongSelectionAtmosphere : MonoBehaviour
{
    private SelectionScoreGraphic score;
    private SelectionFiligreeGraphic filigree;
    private SelectionHallLightGraphic hallLight;
    private SelectionSpotlightGraphic spotlight;
    private SelectionSmokeGraphic smoke;
    private SelectionDustGraphic dust;
    private RectTransform atmosphereRoot;
    private AspectFillRawImage roomImage;
    private Texture2D classicalRoomTexture;
    private Texture2D practiceRoomTexture;
    private bool practiceRoomChecked;
    private RectTransform canvasRoot;
    private readonly Vector3[] canvasCorners = new Vector3[4];
    private Color currentColour = DifficultyVisualPalette.ForLevel(1);
    private Color targetColour = DifficultyVisualPalette.ForLevel(1);

    public static SongSelectionAtmosphere Attach(GameObject panel)
    {
        if (panel == null) return null;
        Transform existing = panel.transform.Find("SongSelectionAtmosphere");
        if (existing != null) return existing.GetComponent<SongSelectionAtmosphere>();

        var rootObject = new GameObject("SongSelectionAtmosphere", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(AspectFillRawImage), typeof(SongSelectionAtmosphere));
        rootObject.layer = panel.layer;
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(panel.transform, false);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        // Cover the old paper-book backdrop but stay behind every interactive
        // carousel element already authored in the panel.
        Transform oldBackdrop = panel.transform.Find("ClassicalBookBackdrop");
        root.SetSiblingIndex(oldBackdrop != null ? oldBackdrop.GetSiblingIndex() + 1 : 0);

        AspectFillRawImage room = rootObject.GetComponent<AspectFillRawImage>();
        room.texture = Resources.Load<Texture2D>("UI/SongSelectionClassicalRoom");
        room.color = Color.white;
        room.raycastTarget = false;
        room.UpdateUVRect();

        SongSelectionAtmosphere atmosphere = rootObject.GetComponent<SongSelectionAtmosphere>();
        atmosphere.atmosphereRoot = root;
        atmosphere.roomImage = room;
        atmosphere.classicalRoomTexture = room.texture as Texture2D;
        Canvas canvas = panel.GetComponentInParent<Canvas>();
        atmosphere.canvasRoot = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        atmosphere.FitToRootCanvas();
        atmosphere.BuildLayers(root);
        atmosphere.SetDifficulty(1, true);
        return atmosphere;
    }

    private void BuildLayers(RectTransform root)
    {
        // 先加的排在後面（畫得比較底層）：譜紙貼在牆上，光打在它上面，塵埃最前。
        score = AddLayer<SelectionScoreGraphic>(root, "DriftingScore");
        filigree = AddLayer<SelectionFiligreeGraphic>(root, "CornerFiligree");
        hallLight = AddLayer<SelectionHallLightGraphic>(root, "HallLight");
        spotlight = AddLayer<SelectionSpotlightGraphic>(root, "DifficultySpotlight");
        smoke = AddLayer<SelectionSmokeGraphic>(root, "GildedSmoke");
        dust = AddLayer<SelectionDustGraphic>(root, "FloatingDust");
    }

    private static T AddLayer<T>(RectTransform parent, string name) where T : Graphic
    {
        var layerObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        layerObject.layer = parent.gameObject.layer;
        RectTransform rect = layerObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        T graphic = layerObject.GetComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    /// <summary>
    /// Swaps the room behind the library for the one that matches the mode.
    /// </summary>
    /// <remarks>
    /// **Why a photograph and not a drawing.** A drawn room was tried three
    /// times -- correct perspective, photographic value structure, light pools,
    /// contact shadows, floor reflections and film grain -- and it still sat
    /// next to a photograph looking like a diagram. Nothing short of an actual
    /// photograph matches a photograph, and the normal-mode room already is one,
    /// so this is one too. Drop a rehearsal-room picture at
    /// <c>Assets/Resources/UI/PracticeRoom.png</c> and it appears here.
    ///
    /// **Why it falls back silently.** Until that file exists the library simply
    /// keeps the room it already had. A missing decoration is not worth a broken
    /// screen, and the mode's actual behaviour does not depend on it.
    /// </remarks>
    public void SetRecitalRoom(bool recital)
    {
        if (roomImage == null) return;
        if (!practiceRoomChecked)
        {
            practiceRoomChecked = true;
            practiceRoomTexture = Resources.Load<Texture2D>("UI/PracticeRoom");
        }

        Texture2D wanted = recital && practiceRoomTexture != null
            ? practiceRoomTexture
            : classicalRoomTexture;
        if (wanted == null || roomImage.texture == wanted) return;
        roomImage.texture = wanted;
        roomImage.UpdateUVRect();
    }

    public void SetDifficulty(int level, bool immediate = false)
    {
        SetDifficulty(null, level, immediate);
    }

    /// <summary>
    /// 難度名有的話照名字上色（Normal 綠 / Hard 黃 / Expert 紅 / 其他紫）；
    /// 游標停在整首歌上、沒有指定某一個難度時，才退回照等級。
    /// </summary>
    public void SetDifficulty(string difficultyName, int level, bool immediate = false)
    {
        targetColour = string.IsNullOrWhiteSpace(difficultyName)
            ? DifficultyVisualPalette.ForLevel(level)
            : DifficultyVisualPalette.For(difficultyName, level);
        if (immediate) currentColour = targetColour;
        ApplyColour();
    }

    private void Update()
    {
        float blend = 1f - Mathf.Exp(-3.4f * Time.unscaledDeltaTime);
        currentColour = Color.Lerp(currentColour, targetColour, blend);
        if (score != null) score.Advance(Time.unscaledDeltaTime);
        if (hallLight != null) hallLight.Advance(Time.unscaledDeltaTime);
        if (smoke != null) smoke.Advance(Time.unscaledDeltaTime);
        ApplyColour();
    }

    private void LateUpdate()
    {
        // SelectionPanel is intentionally constrained to a square by an
        // AspectRatioFitter. The backdrop is its child so it follows panel
        // visibility, but its rect must follow the root canvas instead.
        FitToRootCanvas();
    }

    private void FitToRootCanvas()
    {
        if (atmosphereRoot == null || canvasRoot == null || atmosphereRoot.parent == null) return;
        canvasRoot.GetWorldCorners(canvasCorners);
        Transform parent = atmosphereRoot.parent;
        Vector3 min = parent.InverseTransformPoint(canvasCorners[0]);
        Vector3 max = min;
        for (int i = 1; i < canvasCorners.Length; i++)
        {
            Vector3 local = parent.InverseTransformPoint(canvasCorners[i]);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }

        atmosphereRoot.anchoredPosition = (Vector2)((min + max) * 0.5f);
        // Two percent overscan prevents fractional CanvasScaler edge seams.
        atmosphereRoot.sizeDelta = (Vector2)(max - min) * 1.02f;
    }

    private void ApplyColour()
    {
        if (score != null) score.Tint = currentColour;
        if (filigree != null) filigree.Tint = currentColour;
        if (hallLight != null) hallLight.Tint = currentColour;
        if (smoke != null) smoke.Tint = currentColour;
        if (spotlight != null) spotlight.Tint = currentColour;
        if (dust != null) dust.Tint = currentColour;
    }
}

/// <summary>One shared difficulty palette for book captions and room light.</summary>
/// <summary>
/// 難度的顏色。**照難度名稱**：Normal 綠、Hard 黃、Expert 紅，其餘（Real、
/// Master…）紫。
/// </summary>
/// <remarks>
/// 照名字而不是照等級，是因為同一個名字在不同曲子上的等級差很多——簡單曲的
/// Expert 可能只有 Lv.9、難曲的 Hard 就有 Lv.12。照等級上色的話同一階難度會
/// 在不同曲子間變色，看不出「這是第幾階」。
///
/// 這裡是全專案唯一一份難度配色。以前有三份各自為政（這個、
/// ClassicalBookUITheme、SongDifficultyStrip），改一份另外兩份不會跟著動——
/// 曲目詳情頁那張清單就是這樣一直維持舊的等級配色。
/// </remarks>
public static class DifficultyVisualPalette
{
    public static readonly Color Green = new Color(0.34f, 0.70f, 0.43f, 1f);
    public static readonly Color Yellow = new Color(0.90f, 0.68f, 0.22f, 1f);
    public static readonly Color Red = new Color(0.82f, 0.27f, 0.25f, 1f);
    public static readonly Color Violet = new Color(0.66f, 0.37f, 0.86f, 1f);

    public static Color For(string difficultyName, int level)
    {
        string name = (difficultyName ?? string.Empty).Trim();
        if (name.Length == 0) return ForLevel(level);
        if (name.Equals("Normal", System.StringComparison.OrdinalIgnoreCase)) return Green;
        if (name.Equals("Hard", System.StringComparison.OrdinalIgnoreCase)) return Yellow;
        if (name.Equals("Expert", System.StringComparison.OrdinalIgnoreCase)) return Red;
        return Violet;
    }

    /// <summary>沒有難度名可用時的退路（氣氛光之類只拿得到等級的地方）。</summary>
    public static Color ForLevel(int level)
    {
        if (level >= 13) return Violet;
        if (level >= 10) return Red;
        if (level >= 7) return Yellow;
        return Green;
    }
}

/// <summary>RawImage equivalent of CSS background-size: cover.</summary>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class AspectFillRawImage : RawImage
{
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        UpdateUVRect();
    }

    public void UpdateUVRect()
    {
        if (texture == null || rectTransform == null || rectTransform.rect.height <= 0.01f) return;
        float textureAspect = texture.width / (float)Mathf.Max(1, texture.height);
        float rectAspect = rectTransform.rect.width / rectTransform.rect.height;
        if (rectAspect > textureAspect)
        {
            float visibleHeight = textureAspect / rectAspect;
            uvRect = new Rect(0f, (1f - visibleHeight) * 0.5f, 1f, visibleHeight);
        }
        else
        {
            float visibleWidth = rectAspect / textureAspect;
            uvRect = new Rect((1f - visibleWidth) * 0.5f, 0f, visibleWidth, 1f);
        }
    }
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class SelectionSpotlightGraphic : MaskableGraphic
{
    private Color tint = Color.white;

    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        DrawCone(vh, r, 0.42f, 0.12f);
        DrawCone(vh, r, 0.29f, 0.16f);
        DrawCone(vh, r, 0.17f, 0.13f);
    }

    private void DrawCone(VertexHelper vh, Rect r, float bottomWidth, float strength)
    {
        const int bands = 12;
        for (int i = 0; i < bands; i++)
        {
            float t0 = i / (float)bands;
            float t1 = (i + 1) / (float)bands;
            float y0 = Mathf.Lerp(r.yMax, r.yMin + r.height * 0.05f, t0);
            float y1 = Mathf.Lerp(r.yMax, r.yMin + r.height * 0.05f, t1);
            float half0 = r.width * Mathf.Lerp(0.018f, bottomWidth, t0) * 0.5f;
            float half1 = r.width * Mathf.Lerp(0.018f, bottomWidth, t1) * 0.5f;
            float a0 = Mathf.Sin(t0 * Mathf.PI) * strength;
            float a1 = Mathf.Sin(t1 * Mathf.PI) * strength;
            AddSoftBand(vh, y0, y1, half0, half1, a0, a1);
        }
    }

    private void AddSoftBand(VertexHelper vh, float y0, float y1, float half0, float half1,
        float alpha0, float alpha1)
    {
        Color clear = new Color(tint.r, tint.g, tint.b, 0f);
        Color centre0 = new Color(tint.r, tint.g, tint.b, alpha0);
        Color centre1 = new Color(tint.r, tint.g, tint.b, alpha1);
        AddQuad(vh, new Vector2(-half0, y0), new Vector2(0f, y0),
            new Vector2(0f, y1), new Vector2(-half1, y1), clear, centre0, centre1, clear);
        AddQuad(vh, new Vector2(0f, y0), new Vector2(half0, y0),
            new Vector2(half1, y1), new Vector2(0f, y1), centre0, clear, clear, centre1);
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Color c0, Color c1, Color c2, Color c3)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, c0);
        AddVertex(vh, p1, c1);
        AddVertex(vh, p2, c2);
        AddVertex(vh, p3, c3);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}

/// <summary>
/// The room around the light: raking side beams, a warm bounce off the stage
/// floor, a darkened ceiling and a few wall sconces, all breathing slowly.
/// </summary>
/// <remarks>
/// The difficulty spotlight is one cone down the middle, which reads as a lamp
/// rather than as a hall.  What makes a concert hall legible is everything
/// around that cone: light arriving from the sides, bouncing off the boards,
/// and dying out towards the ceiling.  Kept far below the spotlight in strength
/// so the carousel stays the brightest thing on screen.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class SelectionHallLightGraphic : MaskableGraphic
{
    private const int SconcesPerSide = 3;

    [Tooltip("燈的本色。鎢絲燈的暖白，難度色只摻一點。")]
    [SerializeField] private Color lampColour = new Color(1f, 0.88f, 0.66f, 1f);
    [SerializeField, Range(0f, 1f)] private float difficultyBlend = 0.3f;
    [Tooltip("兩道從上方斜射下來、在中間交叉的光柱。")]
    [SerializeField, Range(0f, 0.2f)] private float rakeStrength = 0.03f;
    [Tooltip("舞台地板反上來的暖光。")]
    [SerializeField, Range(0f, 0.2f)] private float bounceStrength = 0.05f;
    [Tooltip("上方壓暗的程度，廳堂的景深靠這個。")]
    [SerializeField, Range(0f, 0.4f)] private float ceilingShade = 0.15f;
    [Tooltip("兩側牆上壁燈的光暈。")]
    [SerializeField, Range(0f, 0.3f)] private float sconceStrength = 0.06f;

    private Color tint = Color.white;
    private float time;

    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Breathes the lamps. Driven by the atmosphere alongside the sheets.</summary>
    public void Advance(float deltaTime)
    {
        time += deltaTime;
        SetVerticesDirty();
        // 和譜紙同一個理由：這個專案的 canvas 重建佇列不可靠，當場建才會動。
        if (isActiveAndEnabled) UpdateGeometry();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        Color warm = Color.Lerp(lampColour, tint, difficultyBlend);
        Color shade = new Color(0.03f, 0.03f, 0.05f, 1f);

        // 天花板壓暗。廳堂的上半部本來就比舞台暗，少了這層就只是一張亮牆。
        AddVerticalBand(vh, r, r.yMax, r.yMax - r.height * 0.38f,
            new Color(shade.r, shade.g, shade.b, ceilingShade),
            new Color(shade.r, shade.g, shade.b, 0f));

        // 地板把光反上來，緩慢起伏。
        float bounce = bounceStrength * (0.85f + 0.15f * Mathf.Sin(time * 0.5f));
        AddVerticalBand(vh, r, r.yMin, r.yMin + r.height * 0.3f,
            new Color(warm.r, warm.g, warm.b, bounce),
            new Color(warm.r, warm.g, warm.b, 0f));

        // 兩道交叉的側光。呼吸相位錯開，否則看起來像同一顆燈在閃。
        AddRake(vh, r, -1f, 0.11f, warm);
        AddRake(vh, r, 1f, 2.3f, warm);

        // 牆上的壁燈，離舞台越遠越暗。
        for (int i = 0; i < SconcesPerSide; i++)
        {
            float fade = 1f - i * 0.28f;
            float y = r.yMax - r.height * (0.2f + i * 0.17f);
            for (float side = -1f; side <= 1f; side += 2f)
            {
                float flicker = 0.78f + 0.22f * Mathf.Sin(time * (1.1f + 0.31f * i) + side * 1.7f + i);
                AddHalo(vh, new Vector2(side * r.width * 0.43f, y), r.width * 0.05f,
                    warm, sconceStrength * fade * flicker);
            }
        }
    }

    /// <summary>A beam crossing the frame from one of the upper corners.</summary>
    private void AddRake(VertexHelper vh, Rect r, float side, float phase, Color warm)
    {
        const int Bands = 10;
        float peak = rakeStrength * (0.7f + 0.3f * Mathf.Sin(time * 0.42f + phase));
        if (peak <= 0.001f) return;

        Vector2 origin = new Vector2(side * r.width * 0.34f, r.yMax);
        Vector2 axis = new Vector2(-side * 0.42f, -1f).normalized;
        Vector2 across = new Vector2(-axis.y, axis.x);
        float length = r.height * 1.4f;

        for (int i = 0; i < Bands; i++)
        {
            float t0 = i / (float)Bands;
            float t1 = (i + 1) / (float)Bands;
            Vector2 c0 = origin + axis * (length * t0);
            Vector2 c1 = origin + axis * (length * t1);
            Vector2 e0 = across * (r.width * Mathf.Lerp(0.025f, 0.15f, t0));
            Vector2 e1 = across * (r.width * Mathf.Lerp(0.025f, 0.15f, t1));
            float a0 = peak * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t0 * 1.15f));
            float a1 = peak * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t1 * 1.15f));
            AddSoftBand(vh, c0, c1, e0, e1, warm, a0, a1);
        }
    }

    /// <summary>One slice of a beam: bright along its spine, gone at both edges.</summary>
    private static void AddSoftBand(VertexHelper vh, Vector2 c0, Vector2 c1,
        Vector2 e0, Vector2 e1, Color warm, float a0, float a1)
    {
        Color clear = new Color(warm.r, warm.g, warm.b, 0f);
        Color m0 = new Color(warm.r, warm.g, warm.b, a0);
        Color m1 = new Color(warm.r, warm.g, warm.b, a1);
        AddQuad(vh, c0 - e0, c0, c1, c1 - e1, clear, m0, m1, clear);
        AddQuad(vh, c0, c0 + e0, c1 + e1, c1, m0, clear, clear, m1);
    }

    /// <summary>A wall lamp: bright at the middle, nothing at the rim.</summary>
    private static void AddHalo(VertexHelper vh, Vector2 centre, float radius, Color warm, float alpha)
    {
        const int Segments = 14;
        Color clear = new Color(warm.r, warm.g, warm.b, 0f);
        int first = vh.currentVertCount;
        AddVertex(vh, centre, new Color(warm.r, warm.g, warm.b, alpha));
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            AddVertex(vh, centre + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 1.35f), clear);
        }
        for (int i = 0; i < Segments; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % Segments);
    }

    /// <summary>Full-width wash between two heights.</summary>
    private static void AddVerticalBand(VertexHelper vh, Rect r, float yNear, float yFar,
        Color near, Color far)
    {
        if (near.a <= 0.001f && far.a <= 0.001f) return;
        AddQuad(vh, new Vector2(r.xMin, yNear), new Vector2(r.xMax, yNear),
            new Vector2(r.xMax, yFar), new Vector2(r.xMin, yFar), near, near, far, far);
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Color c0, Color c1, Color c2, Color c3)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, c0);
        AddVertex(vh, p1, c1);
        AddVertex(vh, p2, c2);
        AddVertex(vh, p3, c3);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}

/// <summary>
/// Gold smoke made of drifting motes: every mote is a soft glow, carried by a
/// slowly turning vortex field, so the clouds stretch into curls on their own.
/// </summary>
/// <remarks>
/// This was a strip through the same field, which reads as a drawn line no
/// matter how thin it gets -- a line has an edge, and smoke does not.  Motes
/// give it back: each one is a two-ring falloff (bright core, wide faint halo),
/// so where they pile up the image thickens instead of getting a harder edge.
///
/// The structure comes from the motion, not from the placement.  Motes are born
/// in tight puffs; the vortex kernel is divergence-free, so the field preserves
/// area while it shears -- a compact puff is drawn out into a filament and
/// wrapped around a vortex, which is how real smoke gets its striations.  They
/// are integrated frame to frame rather than re-traced, so what you see is the
/// accumulated history of the flow.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class SelectionSmokeGraphic : MaskableGraphic
{
    private const int Motes = 4800;
    private const int Puffs = 12;
    private const int Vortices = 5;
    private const int GlowResolution = 64;

    [Tooltip("煙的顏色。金色為主，難度色只摻一點。")]
    [SerializeField] private Color smokeColour = new Color(0.95f, 0.82f, 0.5f, 1f);
    [SerializeField, Range(0f, 1f)] private float difficultyBlend = 0.12f;
    [Tooltip("一顆微粒的半寬，佔畫面高的比例。長度由這個乘上拉伸倍率。")]
    [SerializeField, Range(0.001f, 0.05f)] private float moteRadius = 0.0038f;
    [Tooltip("剛生出來時的長寬比。越大越像絲，越小越像點。")]
    [SerializeField, Range(1f, 12f)] private float streakAspect = 2.2f;
    [Tooltip("核心的亮度。外圈永遠淡出到 0。")]
    [SerializeField, Range(0f, 0.5f)] private float opacity = 0.08f;
    [Tooltip("飄動的快慢，佔畫面高的比例／秒。")]
    [SerializeField, Range(0.01f, 0.4f)] private float driftSpeed = 0.08f;
    [Tooltip("整團打轉的快慢。")]
    [SerializeField, Range(0.05f, 1f)] private float churnSpeed = 0.22f;
    [Tooltip("一顆微粒活多久（秒）。夠久才被剪出絲狀結構。")]
    [SerializeField, Range(2f, 40f)] private float moteLifetime = 9f;

    private readonly Vector2[] position = new Vector2[Motes];
    private readonly float[] age = new float[Motes];
    private readonly float[] lifetime = new float[Motes];
    private readonly float[] scale = new float[Motes];
    private readonly Vector2[] heading = new Vector2[Motes];
    private readonly Vector2[] vortexCentre = new Vector2[Vortices];
    private readonly float[] vortexStrength = new float[Vortices];

    private static Texture2D glowTexture;

    private System.Random rng;
    private Color tint = Color.white;
    private float time;
    private bool seeded;

    /// <summary>
    /// The falloff lives in a generated sprite rather than in the mesh.
    /// </summary>
    /// <remarks>
    /// A mote used to be a triangle fan, which cost thirteen vertices to
    /// approximate a circle badly.  One textured quad costs four and gives a
    /// smooth radial profile for free, so the budget buys motes instead of
    /// segments -- which is the whole difference between a scattering of dots
    /// and something that reads as smoke.
    /// </remarks>
    public override Texture mainTexture => EnsureGlowTexture();

    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Carries every mote one step along the field.</summary>
    public void Advance(float deltaTime)
    {
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        time += deltaTime * churnSpeed;
        UpdateField(r);

        if (rng == null) rng = new System.Random(20260906);
        if (!seeded)
        {
            for (int i = 0; i < Motes; i++)
            {
                Spawn(i, r);
                // 第一批的年齡打散，否則整片會同時淡出再同時淡入。
                age[i] = (float)rng.NextDouble() * lifetime[i];
            }
            seeded = true;
        }

        float speed = r.height * driftSpeed;
        float core = r.height * 0.13f;
        float margin = r.height * 0.12f;
        for (int i = 0; i < Motes; i++)
        {
            age[i] += deltaTime;
            Vector2 velocity = Flow(position[i], core, speed);
            position[i] += velocity * deltaTime;
            // 微粒是沿著自己的流向拉長的，所以方向要留下來給繪製用。
            if (velocity.sqrMagnitude > 1e-4f) heading[i] = velocity.normalized;

            bool gone = age[i] >= lifetime[i]
                || position[i].x < r.xMin - margin || position[i].x > r.xMax + margin
                || position[i].y < r.yMin - margin || position[i].y > r.yMax + margin;
            if (gone) Spawn(i, r);
        }

        SetVerticesDirty();
        // 和譜紙同一個理由：這個專案的 canvas 重建佇列不可靠，當場建才會動。
        if (isActiveAndEnabled) UpdateGeometry();
    }

    /// <summary>Puts a mote back into one of the puffs, tightly.</summary>
    private void Spawn(int index, Rect r)
    {
        int puff = index % Puffs;
        float phase = Wobble(puff, 17) * 6.283f;
        Vector2 centre = new Vector2(
            Mathf.Sin(time * (0.13f + 0.04f * puff) + phase) * r.width * 0.42f,
            Mathf.Cos(time * (0.11f + 0.03f * puff) + phase * 1.6f) * r.height * 0.42f);

        // 生得緊，才有東西可以被剪開。散著生就只是一片均勻的雜訊。
        float angle = (float)rng.NextDouble() * 6.283f;
        // 生得很緊。一顆密實的結被剪開才會變成絲；生得鬆就只是一片雜訊。
        float radius = Mathf.Sqrt((float)rng.NextDouble()) * r.height * 0.007f;
        position[index] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        heading[index] = Vector2.up;
        age[index] = 0f;
        lifetime[index] = moteLifetime * (0.6f + (float)rng.NextDouble() * 0.8f);
        scale[index] = 0.55f + (float)rng.NextDouble() * 0.9f;
    }

    /// <summary>The vortices wander and change sign, so the curls never settle.</summary>
    private void UpdateField(Rect r)
    {
        for (int v = 0; v < Vortices; v++)
        {
            float ax = 0.31f + Wobble(v, 3) * 0.5f;
            float ay = 0.27f + Wobble(v, 9) * 0.45f;
            float phase = Wobble(v, 17) * 6.283f;
            vortexCentre[v] = new Vector2(
                Mathf.Sin(time * (0.21f + 0.07f * v) + phase) * r.width * ax * 0.5f,
                Mathf.Cos(time * (0.17f + 0.05f * v) + phase * 1.7f) * r.height * ay * 0.5f);
            vortexStrength[v] = (Wobble(v, 23) > 0.5f ? 1f : -1f)
                * (0.55f + Wobble(v, 29) * 0.75f)
                * Mathf.Sin(time * 0.13f + phase);
        }
    }

    /// <summary>
    /// Velocity at a point: every vortex contributes a circulation that falls off
    /// with distance, softened by a core radius so the centre does not blow up.
    /// </summary>
    private Vector2 Flow(Vector2 at, float core, float speed)
    {
        Vector2 velocity = Vector2.zero;
        float coreSquared = core * core;
        for (int v = 0; v < Vortices; v++)
        {
            Vector2 offset = at - vortexCentre[v];
            float falloff = vortexStrength[v] / (offset.sqrMagnitude + coreSquared);
            velocity.x += -offset.y * falloff;
            velocity.y += offset.x * falloff;
        }

        // 一點點的上飄，讓煙有方向感；再加一道很淺的波紋當細節。
        velocity = velocity * core + new Vector2(0f, 0.3f);
        velocity.x += Mathf.Sin(at.y * 0.017f + time * 0.6f) * 0.16f;
        velocity.y += Mathf.Sin(at.x * 0.014f - time * 0.5f) * 0.12f;
        return Vector2.ClampMagnitude(velocity, 1.6f) * speed;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f || !seeded) return;

        Color gold = Color.Lerp(smokeColour, tint, difficultyBlend);
        float radius = r.height * moteRadius;

        for (int i = 0; i < Motes; i++)
        {
            float life = lifetime[i] > 0.01f ? age[i] / lifetime[i] : 1f;
            if (life >= 1f) continue;

            // 密的時候亮，散掉就收乾淨 —— 對稱的淡進淡出會把攤平的尾巴留在畫面上，
            // 那正是看起來鬆散的原因。這裡改成短暫淡入、然後三次方掉到零。
            float rise = Mathf.Clamp01(life * 8f);
            float decay = 1f - life;
            float alpha = opacity * rise * decay * decay * decay;
            if (alpha <= 0.0015f) continue;

            // 越活越長、越活越細，就是一團煙被剪開的樣子。
            float half = radius * scale[i];
            Vector2 along = heading[i] * (half * (streakAspect + life * 5.5f));
            Vector2 across = new Vector2(-heading[i].y, heading[i].x) * (half / (1f + life * 1.2f));
            AddGlow(vh, position[i], along, across, gold, alpha);
        }
    }

    /// <summary>
    /// One mote: a single quad carrying the glow sprite, stretched along the
    /// flow so it reads as a strand rather than as a dot.
    /// </summary>
    private static void AddGlow(VertexHelper vh, Vector2 centre, Vector2 along, Vector2 across,
        Color gold, float alpha)
    {
        Color ink = new Color(gold.r, gold.g, gold.b, alpha);
        int first = vh.currentVertCount;
        AddVertex(vh, centre - along - across, new Vector2(0f, 0f), ink);
        AddVertex(vh, centre - along + across, new Vector2(0f, 1f), ink);
        AddVertex(vh, centre + along + across, new Vector2(1f, 1f), ink);
        AddVertex(vh, centre + along - across, new Vector2(1f, 0f), ink);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    /// <summary>
    /// A tight bright core sitting inside a much wider halo: the sum of a soft
    /// cubic falloff and a very sharp one.  A single falloff either reads as a
    /// hard dot or as a formless blur; the pair is what glows.
    /// </summary>
    private static Texture2D EnsureGlowTexture()
    {
        if (glowTexture != null) return glowTexture;

        var texture = new Texture2D(GlowResolution, GlowResolution, TextureFormat.RGBA32, false)
        {
            name = "SelectionSmokeGlow",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[GlowResolution * GlowResolution];
        float centre = (GlowResolution - 1) * 0.5f;
        for (int y = 0; y < GlowResolution; y++)
        {
            for (int x = 0; x < GlowResolution; x++)
            {
                float dx = (x - centre) / centre;
                float dy = (y - centre) / centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float falloff = Mathf.Clamp01(1f - distance);
                float halo = falloff * falloff * falloff;
                float core = Mathf.Pow(falloff, 11f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(halo * 0.7f + core * 0.55f) * 255f);
                pixels[y * GlowResolution + x] = new Color32(255, 255, 255, a);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        glowTexture = texture;
        return glowTexture;
    }

    private static float Wobble(int index, int salt)
    {
        float v = Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Vector2 uv, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.uv0 = uv;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class SelectionDustGraphic : MaskableGraphic
{
    private const int ParticleCount = 36;

    private struct Mote
    {
        public float x;
        public float y;
        public float size;
        public float speed;
        public float drift;
        public float phase;
        public float alpha;
    }

    private readonly Mote[] motes = new Mote[ParticleCount];
    private Color tint = Color.white;
    private float elapsed;
    private bool initialized;

    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            SetVerticesDirty();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        var random = new System.Random(194903);
        for (int i = 0; i < motes.Length; i++)
        {
            motes[i] = new Mote
            {
                x = 0.25f + (float)random.NextDouble() * 0.50f,
                y = (float)random.NextDouble(),
                size = 1.4f + (float)random.NextDouble() * 3.2f,
                speed = 0.010f + (float)random.NextDouble() * 0.018f,
                drift = 0.004f + (float)random.NextDouble() * 0.010f,
                phase = (float)random.NextDouble() * Mathf.PI * 2f,
                alpha = 0.14f + (float)random.NextDouble() * 0.24f
            };
        }
        initialized = true;
    }

    private void Update()
    {
        Initialize();
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        elapsed += dt;
        for (int i = 0; i < motes.Length; i++)
        {
            Mote mote = motes[i];
            mote.y += mote.speed * dt;
            if (mote.y > 1.04f) mote.y = -0.04f;
            motes[i] = mote;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Initialize();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        for (int i = 0; i < motes.Length; i++)
        {
            Mote mote = motes[i];
            float x = Mathf.Lerp(r.xMin, r.xMax, mote.x) +
                      Mathf.Sin(elapsed * 0.42f + mote.phase) * r.width * mote.drift;
            float y = Mathf.Lerp(r.yMin, r.yMax, mote.y);
            float beamFactor = 1f - Mathf.Clamp01(Mathf.Abs(x) / (r.width * 0.34f));
            Color colour = new Color(tint.r, tint.g, tint.b, mote.alpha * beamFactor);
            AddDiamond(vh, new Vector2(x, y), mote.size, colour);
        }
    }

    private static void AddDiamond(VertexHelper vh, Vector2 centre, float size, Color colour)
    {
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = colour;
        vertex.position = centre + Vector2.up * size; vh.AddVert(vertex);
        vertex.position = centre + Vector2.right * size * 0.65f; vh.AddVert(vertex);
        vertex.position = centre + Vector2.down * size; vh.AddVert(vertex);
        vertex.position = centre + Vector2.left * size * 0.65f; vh.AddVert(vertex);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }
}

/// <summary>
/// Sheets of manuscript drifting behind the library: ruled staves with a few
/// note heads riding along them, sliding slowly across the wall.
/// </summary>
/// <remarks>
/// Deliberately not real music.  This sits behind the song cards at a few
/// percent opacity, where a reader would only ever catch the texture of
/// notation, never read it -- and a page of the actual chart would cost a lookup
/// to say something nobody can see.  What matters here is that it moves, and
/// that it moves slowly enough to stay scenery.
///
/// Every sheet's layout comes from its index rather than from Random, so the
/// pattern is stable across rebuilds and identical on every machine; only the
/// scroll offset changes frame to frame.
/// </remarks>
internal sealed class SelectionScoreGraphic : MaskableGraphic
{
    private const int Sheets = 8;
    private const int LinesPerStaff = 5;
    private const int NotesPerStaff = 7;

    [Tooltip("紙墨的底色。淡金，像陳年的手抄譜。")]
    [SerializeField] private Color inkColour = new Color(0.86f, 0.75f, 0.51f, 1f);
    [Tooltip("難度色混進紙墨的比例。0 = 永遠是金色，1 = 完全跟著難度走。")]
    [SerializeField, Range(0f, 1f)] private float difficultyBlend = 0.18f;
    [SerializeField, Range(0f, 0.3f)] private float lineOpacity = 0.038f;
    [SerializeField, Range(0f, 0.4f)] private float noteOpacity = 0.058f;
    [SerializeField, Range(1f, 60f)] private float driftPixelsPerSecond = 11f;
    [Tooltip("紙張佔畫面寬的比例。小一點看起來離得遠。")]
    [SerializeField, Range(0.1f, 0.8f)] private float sheetWidthRatio = 0.24f;

    private Color tint = Color.white;
    private float offset;

    // OnPopulateMesh 不會重入，所以這幾條暫存共用一份就好。
    private readonly float[] noteDegree = new float[NotesPerStaff];
    private readonly int[] noteKind = new int[NotesPerStaff];
    private readonly bool[] noteStemUp = new bool[NotesPerStaff];
    private readonly float[] beamDegree = new float[NotesPerStaff];

    public Color Tint
    {
        get => tint;
        set
        {
            tint = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Moves the sheets along. Driven by the atmosphere's own update.</summary>
    public void Advance(float deltaTime)
    {
        offset += driftPixelsPerSecond * deltaTime;
        SetVerticesDirty();
        // 這個專案的 canvas 重建佇列不可靠，當場建才保證每幀都動。
        if (isActiveAndEnabled) UpdateGeometry();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        float sheetWidth = r.width * sheetWidthRatio;
        float span = r.width + sheetWidth;

        // 金色為主，難度色只摻一點 —— 紙是舊的，燈才是會變色的那個。
        Color ink = Color.Lerp(inkColour, tint, difficultyBlend);
        Color lineInk = new Color(ink.r, ink.g, ink.b, lineOpacity);
        Color noteInk = new Color(ink.r, ink.g, ink.b, noteOpacity);

        for (int sheet = 0; sheet < Sheets; sheet++)
        {
            // 每張紙的節奏都不同，才不會整片一起平移看起來像捲軸。
            float speed = 0.6f + Wobble(sheet, 11) * 0.9f;
            float x = r.xMax - Mathf.Repeat(offset * speed + sheet * span / Sheets, span);
            // 偏上方：曲卡佔著畫面中段，紙留在那條帶子之外才像貼在後牆上。
            float band = Wobble(sheet, 3);
            float y = r.yMin + r.height * (band < 0.5f ? 0.06f + band * 0.34f : 0.62f + (band - 0.5f) * 0.6f);
            float lineGap = 9f + Wobble(sheet, 7) * 6f;
            float tilt = (Wobble(sheet, 5) - 0.5f) * 7f;

            // 紙不是平的。兩個變形一起用：中段隆起，加上遠端收窄。
            // 只做傾斜的話每張紙還是一塊硬紙板 —— 平移過去的時候看得最清楚。
            float curl = (Wobble(sheet, 23) - 0.5f) * lineGap * 2.6f;
            float taper = 0.14f + Wobble(sheet, 29) * 0.26f;

            // 遠端的行距壓縮比例。垂直方向的尺寸都要乘它，否則音符會浮在紙外面。
            float Squeeze(float px)
            {
                float u = Mathf.Clamp01((px - x) / sheetWidth);
                return Mathf.Lerp(1f, 1f - taper, u);
            }

            // 把譜表座標壓到彎曲的紙面上。所有東西都走這一條路，紙才是同一張。
            Vector2 Bend(float px, float py)
            {
                float u = Mathf.Clamp01((px - x) / sheetWidth);
                float bow = Mathf.Sin(u * Mathf.PI) * curl;
                return new Vector2(px, y + bow + (py - y) * Mathf.Lerp(1f, 1f - taper, u));
            }

            // 五線改成折線。一整條直的 quad 沒辦法彎，而線正是最看得出紙面形狀的
            // 東西 —— 音符跟著彎但線是直的，看起來會像音符飄在紙上面。
            const int LineSegments = 14;
            for (int line = 0; line < LinesPerStaff; line++)
            {
                float ly = y + line * lineGap;
                for (int i = 0; i < LineSegments; i++)
                {
                    float x0 = x + sheetWidth * (i / (float)LineSegments);
                    float x1 = x + sheetWidth * ((i + 1) / (float)LineSegments);
                    Vector2 a = Bend(x0, ly);
                    Vector2 b = Bend(x1, ly);
                    Vector2 mid = (a + b) * 0.5f;
                    float dx = b.x - a.x;
                    float dy = b.y - a.y;
                    AddRotatedQuad(vh, mid,
                        new Vector2(Mathf.Sqrt(dx * dx + dy * dy) + 1f, 1.2f),
                        tilt + Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, lineInk);
                }
            }

            // 譜號位置的一道花體，讓每張紙有個起頭。
            float clefX = x + sheetWidth * 0.035f;
            AddRotatedQuad(vh, Bend(clefX, y + lineGap * 2f),
                new Vector2(2f, lineGap * 6.5f * Squeeze(clefX)), tilt, noteInk);

            // 連桿要先分組再畫。逐顆各自決定符桿方向的話，一組裡有高有低
            // 就會連出橫跨譜表的斜線 —— 真正的抄譜是一組共用一個方向、一條直線。
            int beamRemaining = 0;
            for (int note = 0; note < NotesPerStaff; note++)
            {
                // 音高落在譜表內外都有，看起來才像真的抄譜而不是一條直線。
                noteDegree[note] = Mathf.Floor(Wobble(sheet * 31 + note, 13) * 9f) - 1f;

                // 六種寫法輪著出現。單一種符頭重複七次就是網格，不是樂譜。
                int kind = Mathf.FloorToInt(Wobble(sheet * 17 + note, 29) * 6f);
                if (beamRemaining > 0) { kind = 4; beamRemaining--; }
                else if (kind == 4) beamRemaining = 1 + Mathf.FloorToInt(Wobble(sheet * 5 + note, 23) * 2f);

                noteKind[note] = kind;
                noteStemUp[note] = noteDegree[note] < 4f;    // 中線以下往上長
                beamDegree[note] = 0f;
            }

            ResolveBeamGroups();

            for (int note = 0; note < NotesPerStaff; note++)
            {
                float nx = x + sheetWidth * (0.12f + 0.82f * note / (NotesPerStaff - 1f));
                float ny = y + noteDegree[note] * lineGap * 0.5f;
                float squeeze = Squeeze(nx);
                Vector2 headRadius = new Vector2(lineGap * 0.58f, lineGap * 0.4f * squeeze);
                int kind = noteKind[note];

                if (kind == 5)
                {
                    // 休止符：兩段錯開的短橫，小尺寸下足以讀成「不是音符」。
                    AddRotatedQuad(vh, Bend(nx, y + lineGap * 2.4f),
                        new Vector2(lineGap * 0.9f, lineGap * 0.45f * squeeze), tilt, noteInk);
                    AddRotatedQuad(vh, Bend(nx + lineGap * 0.25f, y + lineGap * 1.7f),
                        new Vector2(lineGap * 0.9f, lineGap * 0.45f * squeeze), tilt, noteInk);
                    continue;
                }

                bool hollow = kind == 2 || kind == 3;   // 二分與全音符
                bool hasStem = kind != 3;
                bool flagged = kind == 1;
                bool beamed = kind == 4;
                bool stemUp = noteStemUp[note];

                // 符頭是往右上揚的橢圓，和實際的抄譜一樣。
                Vector2 head = Bend(nx, ny);
                if (hollow) AddEllipseRing(vh, head, headRadius, 1.5f, tilt + 16f, noteInk);
                else AddEllipse(vh, head, headRadius, tilt + 16f, noteInk);

                float stemX = nx + (stemUp ? lineGap * 0.58f : -lineGap * 0.58f);
                float tipY = beamed
                    ? y + beamDegree[note] * lineGap * 0.5f
                    : ny + (stemUp ? lineGap * 3f : -lineGap * 3f);
                Vector2 stemFoot = Bend(stemX, ny);
                Vector2 stemTip = Bend(stemX, tipY);

                if (hasStem)
                    AddRotatedQuad(vh, (stemFoot + stemTip) * 0.5f,
                        new Vector2(1.4f, Mathf.Abs(stemTip.y - stemFoot.y)), tilt, noteInk);

                if (flagged)
                    AddRotatedQuad(vh,
                        Bend(stemX + lineGap * 0.35f, tipY - (stemUp ? lineGap * 0.5f : -lineGap * 0.5f)),
                        new Vector2(lineGap * 0.9f, 1.6f), tilt + (stemUp ? -38f : 38f), noteInk);

                // 一組連桿共線 —— 但共的是譜表座標上的線，壓到紙面之後跟著紙一起彎。
                if (beamed && note > 0 && noteKind[note - 1] == 4)
                {
                    float px = x + sheetWidth * (0.12f + 0.82f * (note - 1) / (NotesPerStaff - 1f));
                    float pStemX = px + (noteStemUp[note - 1] ? lineGap * 0.58f : -lineGap * 0.58f);
                    Vector2 previous = Bend(pStemX, y + beamDegree[note - 1] * lineGap * 0.5f);
                    float dx = stemTip.x - previous.x;
                    float dy = stemTip.y - previous.y;
                    AddRotatedQuad(vh, (previous + stemTip) * 0.5f,
                        new Vector2(Mathf.Sqrt(dx * dx + dy * dy), 2.4f),
                        Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, noteInk);
                }

                // 附點：偶爾一顆，打破均勻感的最便宜方式。
                if (kind == 0 && Wobble(sheet * 7 + note, 41) > 0.72f)
                    AddRotatedQuad(vh, Bend(nx + lineGap * 0.85f, ny),
                        new Vector2(2.2f, 2.2f), 0f, noteInk);
            }
        }
    }

    /// <summary>
    /// Gives every beamed run one shared stem direction and one straight beam.
    /// </summary>
    /// <remarks>
    /// Each note used to choose its own stem direction from its own pitch, and
    /// the beam was then drawn between whatever tips came out -- so a group with
    /// one note above the middle line and one below produced a beam that dived
    /// clean through the staff.  Engraving decides the direction once per group,
    /// from the average pitch, and keeps the beam a straight line.
    /// </remarks>
    private void ResolveBeamGroups()
    {
        int note = 0;
        while (note < NotesPerStaff)
        {
            if (noteKind[note] != 4) { note++; continue; }

            int last = note;
            while (last + 1 < NotesPerStaff && noteKind[last + 1] == 4) last++;
            if (last == note) { noteKind[note] = 0; note++; continue; }   // 落單的連桿不成立

            float mean = 0f;
            for (int i = note; i <= last; i++) mean += noteDegree[i];
            mean /= last - note + 1;
            bool up = mean < 4f;
            float direction = up ? 1f : -1f;

            // 首尾拉一條線，限制斜率，再整條推開到蓋過每根符桿。
            float head = noteDegree[note] + direction * 6f;
            float tail = head + Mathf.Clamp(noteDegree[last] - noteDegree[note], -3f, 3f);
            float shift = 0f;
            for (int i = note; i <= last; i++)
            {
                float along = (i - note) / (float)(last - note);
                float needed = noteDegree[i] + direction * 5f - Mathf.Lerp(head, tail, along);
                shift = up ? Mathf.Max(shift, needed) : Mathf.Min(shift, needed);
            }

            for (int i = note; i <= last; i++)
            {
                float along = (i - note) / (float)(last - note);
                beamDegree[i] = Mathf.Lerp(head, tail, along) + shift;
                noteStemUp[i] = up;
            }

            note = last + 1;
        }
    }

    /// <summary>
    /// A note head as an actual ellipse, tilted the way engraving tilts them.
    /// </summary>
    /// <remarks>
    /// These were rotated rectangles, which at a glance read as tilted bricks --
    /// the one thing every reader knows about a note head is that it is round.
    /// Ten segments is plenty at this size and costs eleven vertices.
    /// </remarks>
    private static void AddEllipse(VertexHelper vh, Vector2 centre, Vector2 radius,
        float degrees, Color colour)
    {
        const int Segments = 10;
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));

        int centreIndex = vh.currentVertCount;
        AddVertex(vh, centre, colour);
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            AddVertex(vh, centre + right * (Mathf.Cos(a) * radius.x) + up * (Mathf.Sin(a) * radius.y), colour);
        }
        for (int i = 0; i < Segments; i++)
            vh.AddTriangle(centreIndex, centreIndex + 1 + i, centreIndex + 1 + (i + 1) % Segments);
    }

    /// <summary>An open head: a true ring between two ellipses, so the gap is real.</summary>
    private static void AddEllipseRing(VertexHelper vh, Vector2 centre, Vector2 radius,
        float thickness, float degrees, Color colour)
    {
        const int Segments = 10;
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        Vector2 inner = new Vector2(Mathf.Max(0.5f, radius.x - thickness),
                                    Mathf.Max(0.5f, radius.y - thickness));

        int first = vh.currentVertCount;
        for (int i = 0; i < Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            AddVertex(vh, centre + right * (c * radius.x) + up * (sn * radius.y), colour);
            AddVertex(vh, centre + right * (c * inner.x) + up * (sn * inner.y), colour);
        }
        for (int i = 0; i < Segments; i++)
        {
            int a0 = first + i * 2, b0 = first + ((i + 1) % Segments) * 2;
            vh.AddTriangle(a0, b0, b0 + 1);
            vh.AddTriangle(b0 + 1, a0 + 1, a0);
        }
    }

    /// <summary>A stable 0..1 from an index. Cheap, repeatable, and good enough for scenery.</summary>
    private static float Wobble(int index, int salt)
    {
        float v = Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    private static void AddRotatedQuad(VertexHelper vh, Vector2 centre, Vector2 size,
        float degrees, Color colour)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (size.x * 0.5f);
        Vector2 up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * (size.y * 0.5f);

        int index = vh.currentVertCount;
        AddVertex(vh, centre - right - up, colour);
        AddVertex(vh, centre + right - up, colour);
        AddVertex(vh, centre + right + up, colour);
        AddVertex(vh, centre - right + up, colour);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
