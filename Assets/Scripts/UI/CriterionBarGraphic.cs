using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One examiner's mark, drawn as a graduated scale rather than a progress bar.
/// </summary>
/// <remarks>
/// **Why a scale and not a bar.** A filled rectangle in a hollow rectangle is the
/// one shape on this page that could only have come from a piece of software.
/// Everything around it -- the rules, the page frame, the grade's border -- is
/// printed, worn and lit; a flat block of colour beside them reads as a sticker.
/// A ruled scale with an inked hairline, graduations at the quarters and a gilt
/// index says the same number and belongs to the paper.
///
/// **Why the graduations are not decoration.** A bare bar answers "how full" but
/// never "how full out of what" -- you cannot see 75 on it, and 75 is the mark
/// that matters here, because it is the pass. The tick at the halfway point and
/// the taller ones at the ends give the eye something to measure against, so a
/// mark can be read as *nearly there* or *well past* without reading the number.
///
/// **Why the index lozenge.** The band alone ends in a butt edge whose exact
/// position is hard to fix against a busy ground. A small diamond sitting on the
/// end is a point, and a point can be located precisely -- the same reason an
/// instrument has a needle and not just a coloured column.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CriterionBarGraphic : MaskableGraphic
{
    [SerializeField, Range(0f, 1f)] private float value01;
    [SerializeField] private Color fill = new Color(0.76f, 0.57f, 0.25f, 1f);
    [SerializeField] private Color ink = new Color(0.24f, 0.17f, 0.09f, 1f);
    [SerializeField] private int seed = 3;

    /// <summary>Sets the mark (0..1) and the colour it is earned in.</summary>
    public void Set(float value, Color colour)
    {
        value01 = Mathf.Clamp01(value);
        fill = colour;
        SetVerticesDirty();
    }

    public void Engrave(Color inkColour, int inkSeed)
    {
        ink = inkColour;
        seed = inkSeed;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 12f || r.height < 5f) return;

        OrnamentPen pen = OrnamentPen.Default;
        float mid = r.center.y;
        float left = r.xMin;
        float span = r.width;

        // 尺身。比成績的帶子細，而且是墨色不是金色 —— 刻度是紙上印好的，
        // 成績是後來填上去的，兩者不該是同一種東西。
        Color rule = new Color(ink.r, ink.g, ink.b, ink.a * 0.74f);
        pen.WornLine(vh, new Vector2(left, mid), 0f, span, 1.4f, rule, seed, 0.20f);

        for (int i = 0; i <= 4; i++)
        {
            // 兩端最長、中間次之、四分之一處最短：這樣不用數也知道哪一格是一半。
            float height = r.height * (i == 0 || i == 4 ? 0.90f : i == 2 ? 0.62f : 0.38f);
            float x = Mathf.Lerp(left, r.xMax, i * 0.25f);
            pen.WornLine(vh, new Vector2(x, mid - height * 0.5f), 90f, height, 1.4f, rule,
                seed + 7 * (i + 1), 0.12f);
        }

        if (value01 <= 0.001f) return;

        float end = left + span * value01;
        float band = r.height * 0.46f;
        if (end - left > band * 0.5f)
        {
            var path = new List<Vector2> { new Vector2(left, mid), new Vector2(end, mid) };
            pen.Ribbon(vh, path, band, fill, 0.9f);
        }

        Lozenge(vh, new Vector2(end, mid), r.height * 0.86f, fill, ink);
    }

    /// <summary>The index: a small lit diamond, bright above and deep below.</summary>
    private static void Lozenge(VertexHelper vh, Vector2 centre, float size, Color face, Color edge)
    {
        float half = size * 0.5f;
        float waist = half * 0.58f;
        int start = vh.currentVertCount;

        OrnamentPen.AddVertex(vh, centre + new Vector2(0f, half), Color.Lerp(face, Color.white, 0.62f));
        OrnamentPen.AddVertex(vh, centre + new Vector2(waist, 0f), face);
        OrnamentPen.AddVertex(vh, centre + new Vector2(0f, -half), Color.Lerp(face, edge, 0.55f));
        OrnamentPen.AddVertex(vh, centre + new Vector2(-waist, 0f), face);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
