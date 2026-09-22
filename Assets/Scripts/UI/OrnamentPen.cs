using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One edge of a rectangular ornament, in the ornament's own coordinates.
/// </summary>
/// <remarks>
/// Everything an ornament draws is placed as (u, v): u runs along the edge from
/// its corner, v runs inwards. The same motif can then be written once and
/// mapped onto all eight half-edges of a frame, and the handedness sign keeps a
/// scroll curling *into* the frame on every one of them rather than mirroring
/// into a comma on half of them.
/// </remarks>
public readonly struct OrnamentEdge
{
    public readonly Vector2 origin;
    public readonly Vector2 along;
    public readonly Vector2 inward;

    /// <summary>+1 or -1: which way a positive curvature turns on this edge.</summary>
    public readonly float handed;

    public OrnamentEdge(Vector2 origin, Vector2 along, Vector2 inward)
    {
        this.origin = origin;
        this.along = along;
        this.inward = inward;
        handed = Mathf.Sign(along.x * inward.y - along.y * inward.x);
        if (handed == 0f) handed = 1f;
    }

    public Vector2 At(float u, float v) => origin + along * u + inward * v;

    /// <summary>An angle measured from the edge, in world degrees.</summary>
    public float Heading(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 direction = along * Mathf.Cos(radians) + inward * Mathf.Sin(radians);
        return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
    }
}

/// <summary>
/// The brush every drawn ornament in this project uses: a tapering, winding,
/// feathered and lit stroke, with its own drop shadow.
/// </summary>
/// <remarks>
/// **Why one pen.** The look here was tuned over many rounds -- soft edges so a
/// diagonal hairline does not staircase, a relief highlight that travels around
/// a scroll, a shadow so the gilding lifts off the leather. Two copies of that
/// drift apart, and then the same ornament reads differently in two places on
/// the same screen. The selection frame and the book gilding both draw with
/// this, so they are the same hand.
/// </remarks>
public struct OrnamentPen
{
    /// <summary>How far the drop shadow is offset, in pixels. 0 disables it.</summary>
    public float shadowOffset;
    /// <summary>Shadow alpha as a fraction of the stroke's own.</summary>
    public float shadowStrength;
    /// <summary>How hard the light/dark sides of a stroke are pushed apart.</summary>
    public float relief;

    public static OrnamentPen Default => new OrnamentPen
    {
        shadowOffset = 2.0f,
        shadowStrength = 0.6f,
        relief = 0.45f,
    };

    /// <summary>
    /// Draws one stroke twice: its shadow, then the lit line itself.
    /// </summary>
    public void Paint(VertexHelper vh, Vector2 from, float angle, float length, float width,
        float curve, float growth, float taper, Color colour, int steps)
    {
        if (length < 1.5f || width < 0.2f) return;

        // 投影先畫。同一條路徑往右下偏一點、略粗、近黑，紋樣才會離開背景浮起來。
        if (shadowOffset > 0.01f && shadowStrength > 0.01f)
        {
            Vector2 drop = new Vector2(shadowOffset, -shadowOffset);
            Stroke(vh, from + drop, angle, length, width * 1.3f, curve, growth, taper,
                new Color(0.04f, 0.02f, 0.02f, colour.a * shadowStrength), false, steps);
        }

        Stroke(vh, from, angle, length, width, curve, growth, taper, colour, true, steps);
    }

    /// <summary>
    /// A rule as a hand and a few centuries would leave it: never quite
    /// straight, thicker here than there, and rubbed away in places.
    /// </summary>
    /// <remarks>
    /// **Why not one straight stroke.** A rule of constant width running exactly
    /// along a rect edge is the single thing that gives a drawn ornament away as
    /// drawn -- nothing printed on paper in 1890 and handled since is that even.
    /// The line is laid as a chain of short segments whose ends drift off the
    /// axis by a fraction of the line's own width, so it wavers at a scale you
    /// feel rather than see.
    ///
    /// **Why some segments are missing.** Gilding rubs off the high points
    /// first, and printer's ink fails where the paper was already low. The gaps
    /// are what make the rule look pressed into the page instead of drawn over
    /// it. They fall on a fixed hash of the seed, so a given panel wears the
    /// same way every time it is rebuilt.
    /// </remarks>
    /// <param name="wear">0 = an unbroken rule, 1 = badly rubbed.</param>
    public void WornLine(VertexHelper vh, Vector2 from, float angle, float length, float width,
        Color colour, int seed, float wear)
    {
        if (length < 2f || width < 0.15f) return;

        int segments = Mathf.Clamp(Mathf.RoundToInt(length / 24f), 3, 40);
        float step = length / segments;
        float radians = angle * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 normal = new Vector2(-direction.y, direction.x);
        float drift = width * 0.85f;
        float threshold = 1f - Mathf.Clamp01(wear) * 0.42f;

        for (int i = 0; i < segments; i++)
        {
            float roll = Wobble(seed * 131 + i, 7);
            // 磨掉的那幾段直接不畫。
            if (roll > threshold) continue;

            Vector2 a = from + direction * (step * i)
                        + normal * (Wobble(seed * 131 + i, 23) - 0.5f) * drift;
            Vector2 b = from + direction * (step * (i + 1))
                        + normal * (Wobble(seed * 131 + i + 1, 23) - 0.5f) * drift;
            Vector2 span = b - a;
            float segmentLength = span.magnitude;
            if (segmentLength < 0.5f) continue;

            float segmentAngle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;
            Color faded = colour;
            // 沒斷的地方也不是一樣濃。越接近磨掉的門檻越淡，斷點才不會突兀。
            faded.a = colour.a * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(roll / Mathf.Max(0.01f, threshold)));

            // 頭尾各多畫一點點，段和段之間才接得起來。
            Paint(vh, a - span.normalized * (width * 0.4f), segmentAngle,
                segmentLength + width * 0.8f, width * (0.72f + 0.55f * Wobble(seed * 131 + i, 61)),
                0f, 0f, 0f, faded, 2);
        }
    }

    /// <summary>
    /// The same worn rule, but following a path instead of a straight axis.
    /// </summary>
    /// <remarks>
    /// A book's edges are curved, so the rules on it are curved too -- a
    /// straight fillet inside a bowed board crosses its own edge, which reads
    /// worse than having no fillet at all. The path arrives already bent by
    /// <see cref="BookShape"/>; this only has to wander, thin and break along it.
    /// </remarks>
    public void WornPath(VertexHelper vh, System.Collections.Generic.IList<Vector2> path,
        float width, Color colour, int seed, float wear)
    {
        if (path == null || path.Count < 2 || width < 0.15f) return;
        float threshold = 1f - Mathf.Clamp01(wear) * 0.42f;
        float drift = width * 0.85f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            float roll = Wobble(seed * 131 + i, 7);
            if (roll > threshold) continue;

            Vector2 span = path[i + 1] - path[i];
            float length = span.magnitude;
            if (length < 0.5f) continue;
            Vector2 direction = span / length;
            Vector2 normal = new Vector2(-direction.y, direction.x);

            Vector2 a = path[i] + normal * (Wobble(seed * 131 + i, 23) - 0.5f) * drift;
            Vector2 b = path[i + 1] + normal * (Wobble(seed * 131 + i + 1, 23) - 0.5f) * drift;
            Vector2 leg = b - a;
            float legLength = leg.magnitude;
            if (legLength < 0.5f) continue;

            Color faded = colour;
            faded.a = colour.a * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(roll / Mathf.Max(0.01f, threshold)));

            Paint(vh, a - leg / legLength * (width * 0.4f),
                Mathf.Atan2(leg.y, leg.x) * Mathf.Rad2Deg, legLength + width * 0.8f,
                width * (0.72f + 0.55f * Wobble(seed * 131 + i, 61)), 0f, 0f, 0f, faded, 2);
        }
    }

    /// <summary>
    /// A worn rule round all four sides of a book, on the book's own curve.
    /// </summary>
    /// <param name="inset">How far in the rule sits, in unit-square fractions.</param>
    public void WornBookFrame(VertexHelper vh, Rect r, BookShape.Profile profile, float insetU,
        float insetV, float width, Color colour, int seed, float wear, int segments = 22)
    {
        if (insetU >= 0.45f || insetV >= 0.45f) return;
        float farU = 1f - insetU;
        float farV = 1f - insetV;
        var path = new System.Collections.Generic.List<Vector2>(segments * 4 + 4);

        for (int i = 0; i <= segments; i++)
            path.Add(BookShape.Map(r, Mathf.Lerp(insetU, farU, i / (float)segments), farV, profile));
        for (int i = 1; i <= segments; i++)
            path.Add(BookShape.Map(r, farU, Mathf.Lerp(farV, insetV, i / (float)segments), profile));
        for (int i = 1; i <= segments; i++)
            path.Add(BookShape.Map(r, Mathf.Lerp(farU, insetU, i / (float)segments), insetV, profile));
        for (int i = 1; i <= segments; i++)
            path.Add(BookShape.Map(r, insetU, Mathf.Lerp(insetV, farV, i / (float)segments), profile));
        path.Add(path[0]);

        WornPath(vh, path, width, colour, seed, wear);
    }

    /// <summary>Lays a worn rule round all four sides of a rect.</summary>
    public void WornFrame(VertexHelper vh, Rect r, float inset, float width, Color colour,
        int seed, float wear)
    {
        float x0 = r.xMin + inset, x1 = r.xMax - inset;
        float y0 = r.yMin + inset, y1 = r.yMax - inset;
        if (x1 - x0 < width * 4f || y1 - y0 < width * 4f) return;

        WornLine(vh, new Vector2(x0, y1), 0f, x1 - x0, width, colour, seed + 1, wear);
        WornLine(vh, new Vector2(x0, y0), 0f, x1 - x0, width, colour, seed + 2, wear);
        WornLine(vh, new Vector2(x0, y0), 90f, y1 - y0, width, colour, seed + 3, wear);
        WornLine(vh, new Vector2(x1, y0), 90f, y1 - y0, width, colour, seed + 4, wear);
    }

    /// <summary>
    /// Lays one tapering, winding stroke.
    /// </summary>
    /// <remarks>
    /// Four vertices per step: transparent, core, core, transparent. Both sides
    /// of the line are gradients, so there is no hard edge to alias -- on a thin
    /// stroke running diagonally, the staircase along a hard edge is more obvious
    /// than the line itself.
    ///
    /// When <paramref name="shaded"/> the two core vertices are toned apart by
    /// which way the stroke's own normal faces the light, so the line is bright
    /// on one side and dark on the other. The normal turns as the stroke winds,
    /// so the highlight travels around the scroll the way it would on a moulding.
    /// </remarks>
    public void Stroke(VertexHelper vh, Vector2 from, float angle, float length, float width,
        float curve, float growth, float taper, Color colour, bool shaded, int steps)
    {
        if (steps < 1) steps = 1;
        // 光從左上來。方向是定的，所以整個框的受光面自動一致。
        Vector2 light = new Vector2(-0.55f, 0.84f);

        Vector2 point = from;
        float heading = angle;
        int first = vh.currentVertCount;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float half = width * (1f - t * taper) * 0.5f;
            float core = half * 0.34f;
            float radians = heading * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 unit = new Vector2(-direction.y, direction.x);

            float facing = shaded ? Vector2.Dot(unit, light) : 0f;
            Color plus = Tone(colour, facing);
            Color minus = Tone(colour, -facing);

            AddVertex(vh, point - unit * half, Transparent(minus));
            AddVertex(vh, point - unit * core, minus);
            AddVertex(vh, point + unit * core, plus);
            AddVertex(vh, point + unit * half, Transparent(plus));

            // 最後一圈頂點畫完就停，否則回報的終點會比實際畫到的地方多一段。
            if (i < steps)
            {
                point += direction * (length / steps);
                // 曲率隨著行進變大，走出來的是渦線而不是圓弧。
                heading += curve * (1f + i * growth);
            }
        }

        for (int i = 0; i < steps; i++)
        {
            int a = first + i * 4;
            int b = a + 4;
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(a + k, a + k + 1, b + k + 1);
                vh.AddTriangle(b + k + 1, b + k, a + k);
            }
        }
    }

    /// <summary>
    /// Brightens towards white on the lit side, darkens on the other.
    /// </summary>
    /// <remarks>
    /// The alpha moves with the tone, not just the colour. At a low alpha nothing
    /// reads: the highlight is a slightly paler grey and the shaded side is black
    /// at a tenth, which over a dark room is nothing at all.
    /// </remarks>
    /// <summary>
    /// Lays a continuous ribbon along a path: soft on both sides, lit across.
    /// </summary>
    /// <remarks>
    /// Four vertices per point -- transparent, core, core, transparent -- stitched
    /// into three bands. Both edges are gradients, so a curve only a few pixels
    /// wide has no staircase on it; on a curve this matters more than on a
    /// straight rule, because every part of it meets the pixel grid at a
    /// different angle and a hard edge would shimmer differently along its
    /// length.
    ///
    /// The core is toned by how the local normal faces the light, so the band is
    /// bright on one flank and deep on the other, and the highlight travels round
    /// a curve. That is the whole difference between a gold line and a gilt
    /// moulding: gilding is read from the way light runs along it.
    ///
    /// Use this rather than <see cref="WornPath"/> wherever the path turns inside
    /// a few pixels. WornPath jitters and drops each short segment on its own,
    /// which reads as a hand-drawn rule on a long straight line and as noise on
    /// anything tightly curved.
    /// </remarks>
    public void Ribbon(VertexHelper vh, System.Collections.Generic.IList<Vector2> path,
        float width, Color colour, float reliefAmount)
    {
        if (path == null || path.Count < 2 || width < 0.2f) return;

        float half = width * 0.5f;
        float core = half * 0.55f;
        Vector2 light = new Vector2(-0.42f, 0.91f).normalized;
        Color deep = new Color(colour.r * 0.42f, colour.g * 0.34f, colour.b * 0.26f, colour.a);
        Color pale = Color.Lerp(colour, Color.white, 0.62f);
        int start = vh.currentVertCount;

        for (int i = 0; i < path.Count; i++)
        {
            Vector2 behind = path[Mathf.Max(0, i - 1)];
            Vector2 ahead = path[Mathf.Min(path.Count - 1, i + 1)];
            Vector2 tangent = ahead - behind;
            if (tangent.sqrMagnitude < 1e-6f) tangent = Vector2.right;
            tangent.Normalize();
            Vector2 normal = new Vector2(-tangent.y, tangent.x);

            float lit = Vector2.Dot(normal, light) * 0.5f + 0.5f;
            Color outer = Color.Lerp(colour, pale, lit * reliefAmount);
            Color inner = Color.Lerp(colour, deep, (1f - lit) * reliefAmount);

            Vector2 point = path[i];
            AddVertex(vh, point + normal * half, Transparent(outer));
            AddVertex(vh, point + normal * core, outer);
            AddVertex(vh, point - normal * core, inner);
            AddVertex(vh, point - normal * half, Transparent(inner));
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            int a = start + i * 4;
            int b = a + 4;
            for (int band = 0; band < 3; band++)
            {
                vh.AddTriangle(a + band, a + band + 1, b + band + 1);
                vh.AddTriangle(a + band, b + band + 1, b + band);
            }
        }
    }

    public Color Tone(Color colour, float amount)
    {
        float k = Mathf.Clamp(amount, -1f, 1f);
        Color target = k > 0f ? Color.white : new Color(0.08f, 0.04f, 0.04f, 1f);
        Color mixed = Color.Lerp(colour, target, Mathf.Abs(k) * relief);
        mixed.a = Mathf.Clamp01(colour.a * (1f + k * 0.75f));
        return mixed;
    }

    public static Color Transparent(Color colour)
    {
        return new Color(colour.r, colour.g, colour.b, 0f);
    }

    /// <summary>A stable pseudo-random in [0,1) for a motif index.</summary>
    public static float Wobble(int index, int salt)
    {
        float v = Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    public static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
