using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A gilt picture frame around the song library, made only of ornament: a dense
/// cartouche in each corner and a scroll motif repeated along the edges between
/// them, close enough together that the border is the shape they make.
/// </summary>
/// <remarks>
/// **Why it is laid out in edge coordinates.** Every element is authored once in
/// (u, v) -- distance along an edge from its corner, and distance inwards from
/// that edge -- and then mapped onto each of the eight half-edges. A frame is
/// mirror-symmetric about both axes, so authoring one eighth and mapping it is
/// both less code and the only way the four corners are guaranteed to match.
/// The mapping carries a handedness: mirroring reverses which way a curl turns,
/// so every curvature is multiplied by the determinant of the mapping, or half
/// the frame curls the wrong way and the symmetry breaks.
///
/// **Why repetition rather than long strokes.** The first attempt grew a few
/// long vines from the corners, and long strokes read as four arcs across the
/// picture, not as a border however tightly they were clamped. Ornament reads as
/// a frame because a small motif repeats at a steady pitch and thickens into the
/// corners.
///
/// **Why there is no rule drawn along the edges.** A pair of plain lines does
/// give the border its continuity, and that is exactly the problem: what shows
/// then is a rectangle with decoration lying on it. The motifs are pitched so
/// each one reaches its neighbours instead, and the outline is what they add up
/// to -- so the frame is the ornament rather than a frame plus ornament.
///
/// **Why the strokes are volutes, not arcs.** A constant curvature is a circle,
/// and a circle reads as a hoop. The curvature here grows along the stroke, so
/// each one winds inwards -- which is what a scroll is.
///
/// Everything sits inside a band a tenth of the short side deep, and nothing
/// animates: the middle of the frame belongs to the carousel.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SelectionFiligreeGraphic : MaskableGraphic
{
    private const int StrokeSteps = 18;

    [Tooltip("紋樣的主色。象牙金。")]
    [SerializeField] private Color inkColour = new Color(0.91f, 0.85f, 0.68f, 1f);
    [Tooltip("偶爾偏過去的第二色。藕紫。")]
    [SerializeField] private Color accentColour = new Color(0.82f, 0.52f, 0.68f, 1f);
    [Tooltip("偶爾偏過去的第三色。銅青。")]
    [SerializeField] private Color patinaColour = new Color(0.48f, 0.76f, 0.66f, 1f);
    [Tooltip("難度色混進來的比例。0 = 永遠是金色。")]
    [SerializeField, Range(0f, 1f)] private float difficultyBlend = 0.14f;
    [SerializeField, Range(0f, 0.6f)] private float opacity = 0.24f;
    [Tooltip("框寬，佔畫面短邊的比例。所有紋樣都待在這條帶子裡。")]
    [SerializeField, Range(0.03f, 0.3f)] private float band = 0.10f;
    [Tooltip("邊上重複幾組母題（每半邊）。密到彼此相接，框才是紋樣圍出來的。")]
    [SerializeField, Range(1, 16)] private int motifsPerHalf = 9;
    [Tooltip("投影往右下偏移幾像素。0 關掉。")]
    [SerializeField, Range(0f, 6f)] private float shadowOffset = 2.0f;
    [Tooltip("投影相對線條的濃度。")]
    [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.6f;
    [Tooltip("受光面與背光面的明暗差。這是立體感的來源。")]
    [SerializeField, Range(0f, 1f)] private float relief = 0.45f;

    private Color tint = Color.white;

    /// <summary>The shared ornament brush, carrying this frame's shadow/relief.</summary>
    private OrnamentPen Pen => new OrnamentPen
    {
        shadowOffset = shadowOffset,
        shadowStrength = shadowStrength,
        relief = relief,
    };

    /// <summary>Maps one half-edge's (along, inward) coordinates onto the rect.</summary>
    public Color Tint
    {
        get => tint;
        set
        {
            if (tint == value) return;
            tint = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 8f || r.height <= 8f) return;

        Color ink = Color.Lerp(inkColour, tint, difficultyBlend);
        ink.a = opacity;

        float unit = Mathf.Min(r.width, r.height) * band;
        float inset = unit * 0.16f;

        Vector2 topLeft = new Vector2(r.xMin + inset, r.yMax - inset);
        Vector2 topRight = new Vector2(r.xMax - inset, r.yMax - inset);
        Vector2 bottomLeft = new Vector2(r.xMin + inset, r.yMin + inset);
        Vector2 bottomRight = new Vector2(r.xMax - inset, r.yMin + inset);

        // 八個半邊。每個角被兩個半邊共用，角上的卷飾只由水平那一個負責畫。
        OrnamentEdge[] edges =
        {
            new OrnamentEdge(topLeft, Vector2.right, Vector2.down),
            new OrnamentEdge(topRight, Vector2.left, Vector2.down),
            new OrnamentEdge(bottomLeft, Vector2.right, Vector2.up),
            new OrnamentEdge(bottomRight, Vector2.left, Vector2.up),
            new OrnamentEdge(topLeft, Vector2.down, Vector2.right),
            new OrnamentEdge(bottomLeft, Vector2.up, Vector2.right),
            new OrnamentEdge(topRight, Vector2.down, Vector2.left),
            new OrnamentEdge(bottomRight, Vector2.up, Vector2.left),
        };

        float halfWidth = r.width * 0.5f - inset;
        float halfHeight = r.height * 0.5f - inset;

        for (int i = 0; i < edges.Length; i++)
        {
            OrnamentEdge edge = edges[i];
            float half = i < 4 ? halfWidth : halfHeight;
            if (i < 4) Cartouche(vh, edge, unit, ink, i);
            Band(vh, edge, unit, half, ink, i);
        }
    }

    /// <summary>
    /// The dense group in a corner: three volutes of falling size sharing a
    /// centre, plus a diagonal sweep across the mitre.
    /// </summary>
    private void Cartouche(VertexHelper vh, OrnamentEdge edge, float unit, Color ink, int seed)
    {
        // 主卷：從角落沿著對角線出發，越捲越緊。
        Volute(vh, edge, 0.30f, 0.30f, 42f, 3.2f, 2.6f, 0.075f, unit, ink, seed * 13 + 1);
        Volute(vh, edge, 0.30f, 0.30f, 128f, -3.0f, 1.9f, 0.062f, unit, ink, seed * 13 + 2);
        Volute(vh, edge, 1.05f, 0.62f, -18f, 4.4f, 1.3f, 0.050f, unit, ink, seed * 13 + 3);
        Volute(vh, edge, 0.62f, 1.05f, 72f, -4.4f, 1.3f, 0.050f, unit, ink, seed * 13 + 4);

        // 兩支小的往邊上帶，讓角落和邊上的母題接得起來。
        Volute(vh, edge, 1.55f, 0.30f, 8f, 5.5f, 0.85f, 0.040f, unit, ink, seed * 13 + 5);
        Volute(vh, edge, 0.30f, 1.55f, 82f, -5.5f, 0.85f, 0.040f, unit, ink, seed * 13 + 6);
    }

    /// <summary>The motif repeated along an edge between the corners.</summary>
    private void Band(VertexHelper vh, OrnamentEdge edge, float unit, float half, Color ink, int seed)
    {
        float start = unit * 2.1f;
        float finish = half - unit * 0.15f;
        if (finish <= start) return;

        int count = Mathf.Max(1, motifsPerHalf);
        float pitch = (finish - start) / count;
        if (pitch < unit * 0.5f)
        {
            count = Mathf.Max(1, Mathf.FloorToInt((finish - start) / (unit * 0.5f)));
            pitch = (finish - start) / count;
        }

        // 母題的尺寸跟著間距走，這樣不管排幾組，相鄰的卷都會搭到一起 —— 邊界的
        // 連續性是它們接出來的，不是另外畫一條線。
        float reachUnits = pitch / unit * 0.62f;
        for (int i = 0; i < count; i++)
        {
            float u = start + pitch * (i + 0.5f);
            // 一組是兩個反向的卷，接成一個 S —— 單一方向的卷排一整排會像齒輪。
            Volute(vh, edge, u - pitch * 0.30f, 0.42f, 18f, 4.6f, reachUnits, 0.036f,
                unit, ink, seed * 31 + i * 3 + 1);
            Volute(vh, edge, u + pitch * 0.30f, 0.42f, 162f, -4.6f, reachUnits, 0.036f,
                unit, ink, seed * 31 + i * 3 + 2);
            // 中間一小片往內的葉，把兩個卷的接縫蓋掉。
            Volute(vh, edge, u, 0.30f, 90f, 3.4f, reachUnits * 0.55f, 0.030f,
                unit, ink, seed * 31 + i * 3 + 3);
        }
    }

    /// <summary>
    /// One scroll: a stroke whose curvature grows as it goes, so it winds in.
    /// </summary>
    /// <remarks>
    /// Lengths, widths and positions all arrive in multiples of the frame band,
    /// so the whole ornament scales with the window instead of with the pixel.
    /// </remarks>
    private void Volute(VertexHelper vh, OrnamentEdge edge, float u, float v, float degrees,
        float curl, float length, float width, float unit, Color ink, int seed)
    {
        Vector2 from = edge.At(u * unit, v * unit);
        float heading = edge.Heading(degrees);
        Pen.Paint(vh, from, heading, length * unit, width * unit,
            curl * edge.handed, 0.09f, 0.85f, StrandColour(seed, ink), StrokeSteps);
    }

    /// <summary>
    /// Most strands are ivory; a few drift towards one of the two accents.
    /// </summary>
    /// <remarks>
    /// Chosen per stroke rather than per edge, so the variety shows up inside one
    /// cluster. Giving every stroke its own hue turns ornament into confetti, so
    /// the shift is mostly small.
    /// </remarks>
    private Color StrandColour(int seed, Color ink)
    {
        Color accent = OrnamentPen.Wobble(seed, 37) > 0.5f ? accentColour : patinaColour;
        Color mixed = Color.Lerp(ink, accent, OrnamentPen.Wobble(seed, 31) * 0.85f);
        mixed.a = ink.a;
        return mixed;
    }
}
