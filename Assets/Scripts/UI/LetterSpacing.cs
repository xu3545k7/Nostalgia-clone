using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracking for a legacy <see cref="Text"/>, which has no letter-spacing of its
/// own.
/// </summary>
/// <remarks>
/// **Why this exists.** TMP has `characterSpacing`; legacy Text has nothing. The
/// score is drawn with legacy Text on both screens on purpose -- some standalone
/// graphics backends produce an empty TMP mesh for that one large line -- so the
/// only way to open the digits up is to move the glyph quads after they are
/// built.
///
/// **Why it must sit above the Outline components.** Mesh effects run in
/// component order, and `Outline` multiplies the vertex stream by copying the
/// whole mesh once per offset. If this ran afterwards it would see those copies
/// as extra glyphs, and space them as though they were letters -- the outline
/// would walk away from the text. Add it before them.
/// </remarks>
[RequireComponent(typeof(Text))]
public sealed class LetterSpacing : BaseMeshEffect
{
    /// <summary>Extra pixels between one glyph and the next.</summary>
    [SerializeField] private float spacing = 5f;

    public float Spacing
    {
        get => spacing;
        set
        {
            if (Mathf.Approximately(spacing, value)) return;
            spacing = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    /// <summary>
    /// How far apart two glyph tops may be and still count as one line.
    /// </summary>
    /// <remarks>
    /// This was <c>Mathf.Approximately</c>, whose tolerance is about one part in
    /// a million -- far tighter than the rounding that separates two glyph tops
    /// that are meant to be level. When it split a line in two, each half was
    /// aligned to the margin **independently**, so on right-aligned text the
    /// halves piled up on top of each other: the digits appeared to collapse into
    /// a lump, and only for some values, because which digits round which way
    /// depends on the number. A pixel of slack is still an order of magnitude
    /// less than any real line gap.
    /// </remarks>
    private const float LineTolerance = 1f;

    private readonly List<UIVertex> stream = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh == null || Mathf.Approximately(spacing, 0f)) return;

        stream.Clear();
        vh.GetUIVertexStream(stream);
        int glyphs = stream.Count / 6;
        if (glyphs < 2 || stream.Count % 6 != 0) return;

        Text label = GetComponent<Text>();
        float pull = AlignmentPull(label != null ? label.alignment : TextAnchor.UpperLeft);

        // 一行一行處理。同一行的字上緣 y 相同 —— 這對每行字級一致的字串成立，
        // 而分數就是這樣（標籤一行、數字一行，各自單一字級）。
        int lineStart = 0;
        float lineTop = stream[0].position.y;
        for (int glyph = 1; glyph <= glyphs; glyph++)
        {
            bool last = glyph == glyphs;
            float top = last ? 0f : stream[glyph * 6].position.y;
            if (last || Mathf.Abs(top - lineTop) > LineTolerance)
            {
                SpaceLine(lineStart, glyph, pull);
                lineStart = glyph;
                lineTop = top;
            }
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(stream);
    }

    /// <summary>
    /// Spreads one line and puts it back where its alignment says it belongs.
    /// </summary>
    /// <remarks>
    /// Tracking makes a line wider, and the width it gains all appears on the
    /// right. Left-aligned that is correct; right-aligned the line would crawl
    /// off its margin, so the whole line is pulled back by everything it gained.
    /// </remarks>
    private void SpaceLine(int from, int to, float pull)
    {
        int count = to - from;
        if (count < 2) return;

        float origin = -spacing * (count - 1) * pull;
        for (int glyph = from; glyph < to; glyph++)
        {
            float shift = origin + spacing * (glyph - from);
            for (int corner = 0; corner < 6; corner++)
            {
                int index = glyph * 6 + corner;
                UIVertex vertex = stream[index];
                Vector3 position = vertex.position;
                position.x += shift;
                vertex.position = position;
                stream[index] = vertex;
            }
        }
    }

    /// <summary>0 keeps the left edge, 1 the right, 0.5 the centre.</summary>
    private static float AlignmentPull(TextAnchor alignment)
    {
        switch (alignment)
        {
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                return 1f;
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
                return 0.5f;
            default:
                return 0f;
        }
    }
}
