using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ornamental border around the gameplay song card, themed on the chart's
/// difficulty colour and getting more elaborate the harder the chart is.
/// </summary>
/// <remarks>
/// **Why it is a sibling of the card, not a child.** The card carries a
/// <see cref="RectMask2D"/>, so anything parented to it is clipped at the paper
/// edge -- which is exactly where the ornament has to sit. It is placed next to
/// the card instead and given the same rect plus a margin.
///
/// **Why the tiers are drawn, not authored.** Four difficulty bands each want a
/// different amount of gilding on a card whose size is decided at runtime.
/// Sprites would need four sets of nine-slices and would still not let the
/// corner scrollwork grow with the tier, so every element is a few quads
/// generated from the same rect.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DifficultyFrameGraphic : MaskableGraphic
{
    private int level = 1;
    private Color? overrideInk;

    /// <summary>
    /// Forces the frame's colour instead of taking it from the difficulty.
    /// </summary>
    /// <remarks>
    /// The gameplay card wants the chart's own colour; a gilt frame around a
    /// panel wants gold whatever is inside it. One nullable override beats a
    /// second copy of the drawing code.
    /// </remarks>
    public Color? OverrideInk
    {
        get => overrideInk;
        set
        {
            overrideInk = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Chart level. Decides both the colour and how much ornament.</summary>
    public int Level
    {
        get => level;
        set
        {
            if (level == value) return;
            level = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Builds the frame next to <paramref name="card"/> and returns it.
    /// </summary>
    public static DifficultyFrameGraphic Attach(RectTransform card, float margin)
    {
        if (card == null || card.parent == null) return null;

        Transform existing = card.parent.Find("DifficultyFrame");
        if (existing != null) return existing.GetComponent<DifficultyFrameGraphic>();

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var frameObject = new GameObject("DifficultyFrame", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(DifficultyFrameGraphic));
        frameObject.layer = card.gameObject.layer;

        RectTransform rect = frameObject.GetComponent<RectTransform>();
        rect.SetParent(card.parent, false);
        rect.anchorMin = card.anchorMin;
        rect.anchorMax = card.anchorMax;
        rect.pivot = card.pivot;
        rect.anchoredPosition = card.anchoredPosition + new Vector2(-margin, margin);
        rect.sizeDelta = card.sizeDelta + new Vector2(margin * 2f, margin * 2f);
        rect.SetSiblingIndex(card.GetSiblingIndex() + 1);

        DifficultyFrameGraphic frame = frameObject.GetComponent<DifficultyFrameGraphic>();
        frame.raycastTarget = false;
        return frame;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 4f || r.height <= 4f) return;

        Color theme = overrideInk ?? DifficultyVisualPalette.ForLevel(level);
        Color ink = new Color(theme.r * 0.55f, theme.g * 0.55f, theme.b * 0.55f, 0.95f);
        Color gilt = Color.Lerp(theme, new Color(1f, 0.93f, 0.72f, 1f), 0.45f);
        gilt.a = 0.95f;

        int tier = Tier(level);

        // 每一階都疊在前一階上面，而不是各畫各的 —— 難度愈高就是「同一個框再加東西」，
        // 這樣四階看起來才是一套，不是四個不同的框。
        DrawRule(vh, r, 0f, 2.4f, ink);
        DrawCornerStuds(vh, r, 3.5f, gilt);

        if (tier >= 1)
        {
            DrawRule(vh, r, 6f, 1.2f, gilt);
            DrawCornerBrackets(vh, r, 22f, 2.2f, gilt);
        }

        if (tier >= 2)
        {
            DrawCornerScrolls(vh, r, 26f, 10f, 2.0f, gilt);
            DrawEdgeFleurons(vh, r, 7f, gilt);
        }

        if (tier >= 3)
        {
            DrawRule(vh, r, 10.5f, 0.9f, ink);
            DrawDentils(vh, r, 13f, 3.4f, gilt);
            DrawCornerFinials(vh, r, 9f, theme);
        }
    }

    /// <summary>Four bands, matching the palette's own thresholds.</summary>
    private static int Tier(int level)
    {
        if (level >= 13) return 3;
        if (level >= 10) return 2;
        if (level >= 7) return 1;
        return 0;
    }

    // ------------------------------------------------------------- elements --

    /// <summary>A rectangular rule inset from the frame edge.</summary>
    private static void DrawRule(VertexHelper vh, Rect r, float inset, float thickness, Color colour)
    {
        float x0 = r.xMin + inset, x1 = r.xMax - inset;
        float y0 = r.yMin + inset, y1 = r.yMax - inset;
        if (x1 - x0 < thickness * 2f || y1 - y0 < thickness * 2f) return;

        AddRect(vh, x0, y1 - thickness, x1, y1, colour);
        AddRect(vh, x0, y0, x1, y0 + thickness, colour);
        AddRect(vh, x0, y0, x0 + thickness, y1, colour);
        AddRect(vh, x1 - thickness, y0, x1, y1, colour);
    }

    /// <summary>A small square at each corner: the plainest tier still needs a stop.</summary>
    private static void DrawCornerStuds(VertexHelper vh, Rect r, float size, Color colour)
    {
        foreach (Vector2 corner in Corners(r, 1.5f))
            AddRect(vh, corner.x - size * 0.5f, corner.y - size * 0.5f,
                corner.x + size * 0.5f, corner.y + size * 0.5f, colour);
    }

    /// <summary>An L at each corner, reading as a mitred joint.</summary>
    private static void DrawCornerBrackets(VertexHelper vh, Rect r, float arm, float thickness, Color colour)
    {
        float x0 = r.xMin + 6f, x1 = r.xMax - 6f;
        float y0 = r.yMin + 6f, y1 = r.yMax - 6f;
        if (x1 - x0 < arm * 2.5f || y1 - y0 < arm * 2.5f) return;

        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? 1f : -1f;
            float sy = i < 2 ? 1f : -1f;
            float cx = sx > 0f ? x0 : x1;
            float cy = sy > 0f ? y1 : y0;
            float ix = cx + sx * 5f;
            float iy = cy - sy * 5f;

            AddRect(vh, Mathf.Min(ix, ix + sx * arm), Mathf.Min(iy, iy - sy * thickness),
                Mathf.Max(ix, ix + sx * arm), Mathf.Max(iy, iy - sy * thickness), colour);
            AddRect(vh, Mathf.Min(ix, ix + sx * thickness), Mathf.Min(iy, iy - sy * arm),
                Mathf.Max(ix, ix + sx * thickness), Mathf.Max(iy, iy - sy * arm), colour);
        }
    }

    /// <summary>
    /// A quarter scroll curling inwards from each corner.
    /// </summary>
    /// <remarks>
    /// A plain arc reads as a rounded corner; the radius has to shrink along the
    /// sweep for it to read as scrollwork, which is why this is a spiral.
    /// </remarks>
    private static void DrawCornerScrolls(VertexHelper vh, Rect r, float outer, float inner,
        float thickness, Color colour)
    {
        if (r.width < outer * 4f || r.height < outer * 4f) return;

        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? 1f : -1f;
            float sy = i < 2 ? 1f : -1f;
            Vector2 pivot = new Vector2(
                sx > 0f ? r.xMin + outer + 8f : r.xMax - outer - 8f,
                sy > 0f ? r.yMax - outer - 8f : r.yMin + outer + 8f);

            // 從指向角落的方向起卷，往內收 270 度。
            float start = Mathf.Atan2(-sy, -sx) * Mathf.Rad2Deg;
            AddSpiral(vh, pivot, outer, inner, start, sx * sy > 0f ? 265f : -265f,
                thickness, colour);
        }
    }

    /// <summary>A diamond with two beads at the middle of every edge.</summary>
    private static void DrawEdgeFleurons(VertexHelper vh, Rect r, float size, Color colour)
    {
        Vector2 centre = r.center;
        Vector2[] spots =
        {
            new Vector2(centre.x, r.yMax - 6f),
            new Vector2(centre.x, r.yMin + 6f),
            new Vector2(r.xMin + 6f, centre.y),
            new Vector2(r.xMax - 6f, centre.y),
        };

        bool[] horizontal = { true, true, false, false };
        for (int i = 0; i < spots.Length; i++)
        {
            AddDiamond(vh, spots[i], size, colour);
            Vector2 step = horizontal[i] ? new Vector2(size * 1.9f, 0f) : new Vector2(0f, size * 1.9f);
            AddDiamond(vh, spots[i] - step, size * 0.42f, colour);
            AddDiamond(vh, spots[i] + step, size * 0.42f, colour);
        }
    }

    /// <summary>A repeating tooth course just inside the outer rule.</summary>
    private static void DrawDentils(VertexHelper vh, Rect r, float spacing, float size, Color colour)
    {
        float x0 = r.xMin + 13f, x1 = r.xMax - 13f;
        float y0 = r.yMin + 13f, y1 = r.yMax - 13f;
        if (x1 - x0 < spacing * 3f || y1 - y0 < spacing * 3f) return;

        // 從中央往兩側排，這樣兩端的留白是對稱的；從一端排起會在另一端切半顆。
        for (float x = (x0 + x1) * 0.5f; x <= x1; x += spacing)
        {
            float mirror = x0 + x1 - x;
            AddRect(vh, x - size * 0.5f, y1 - size, x + size * 0.5f, y1, colour);
            AddRect(vh, x - size * 0.5f, y0, x + size * 0.5f, y0 + size, colour);
            if (mirror < x - 0.5f)
            {
                AddRect(vh, mirror - size * 0.5f, y1 - size, mirror + size * 0.5f, y1, colour);
                AddRect(vh, mirror - size * 0.5f, y0, mirror + size * 0.5f, y0 + size, colour);
            }
        }

        for (float y = (y0 + y1) * 0.5f; y <= y1; y += spacing)
        {
            float mirror = y0 + y1 - y;
            AddRect(vh, x0, y - size * 0.5f, x0 + size, y + size * 0.5f, colour);
            AddRect(vh, x1 - size, y - size * 0.5f, x1, y + size * 0.5f, colour);
            if (mirror < y - 0.5f)
            {
                AddRect(vh, x0, mirror - size * 0.5f, x0 + size, mirror + size * 0.5f, colour);
                AddRect(vh, x1 - size, mirror - size * 0.5f, x1, mirror + size * 0.5f, colour);
            }
        }
    }

    /// <summary>A jewel sitting on each corner of the outermost rule.</summary>
    private static void DrawCornerFinials(VertexHelper vh, Rect r, float size, Color colour)
    {
        Color core = new Color(colour.r, colour.g, colour.b, 0.95f);
        Color halo = new Color(colour.r, colour.g, colour.b, 0.28f);
        foreach (Vector2 corner in Corners(r, 1.5f))
        {
            AddDiamond(vh, corner, size * 1.8f, halo);
            AddDiamond(vh, corner, size, core);
        }
    }

    // ------------------------------------------------------------ primitives --

    private static Vector2[] Corners(Rect r, float inset)
    {
        return new[]
        {
            new Vector2(r.xMin + inset, r.yMax - inset),
            new Vector2(r.xMax - inset, r.yMax - inset),
            new Vector2(r.xMin + inset, r.yMin + inset),
            new Vector2(r.xMax - inset, r.yMin + inset),
        };
    }

    /// <summary>A stroked spiral: the radius shrinks as the sweep advances.</summary>
    private static void AddSpiral(VertexHelper vh, Vector2 centre, float outer, float inner,
        float startDegrees, float sweepDegrees, float thickness, Color colour)
    {
        const int Steps = 18;
        int first = vh.currentVertCount;
        for (int i = 0; i <= Steps; i++)
        {
            float t = i / (float)Steps;
            float angle = (startDegrees + sweepDegrees * t) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(outer, inner, t);
            Vector2 unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // 尾端收細，卷起來才有粗細變化，不是一條等寬的彎管。
            float half = thickness * (1f - t * 0.55f) * 0.5f;
            AddVertex(vh, centre + unit * (radius - half), colour);
            AddVertex(vh, centre + unit * (radius + half), colour);
        }

        for (int i = 0; i < Steps; i++)
        {
            int a = first + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a + 3, a + 2, a);
        }
    }

    private static void AddDiamond(VertexHelper vh, Vector2 centre, float size, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, new Vector2(centre.x, centre.y + size), colour);
        AddVertex(vh, new Vector2(centre.x + size * 0.62f, centre.y), colour);
        AddVertex(vh, new Vector2(centre.x, centre.y - size), colour);
        AddVertex(vh, new Vector2(centre.x - size * 0.62f, centre.y), colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddRect(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, new Vector2(x0, y0), colour);
        AddVertex(vh, new Vector2(x0, y1), colour);
        AddVertex(vh, new Vector2(x1, y1), colour);
        AddVertex(vh, new Vector2(x1, y0), colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
