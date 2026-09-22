using UnityEngine;

/// <summary>
/// The shell drawn around a note to show how hard it is meant to be struck.
/// </summary>
/// <remarks>
/// **Why the shell sits outside the note and not over it.** A note's colour is
/// already spoken for: red is the right hand, blue is the left. Tinting the note
/// itself for dynamics puts two facts on one channel, and the case that breaks
/// is a soft note in the left hand -- a blue wash over a blue-violet note is
/// nothing at all. Outside the silhouette the shell is read against the dark
/// track instead, where both colours have somewhere to be seen.
///
/// **Why thickness carries the same fact as hue.** A note is read in peripheral
/// vision while the eye is somewhere else, and hue is the first thing peripheral
/// vision gives up. Loud is a thick warm band, soft is a thin cool one, so the
/// two are still told apart by the amount of light even when the colour is not
/// resolved.
///
/// **Why a generated ring mesh and not a sprite.** The note is a hexagon --
/// pointed left and right, measured off the art at a tip about half the
/// half-height long -- and a shell of a different shape reads as a sticker
/// behind the note rather than as light coming off it. A nine-slice sprite can
/// only ever be rectangular, and a stretched glow makes the band four times
/// thicker down the sides than across the top, because notes are several times
/// wider than they are tall. Twelve vertices solve both: the ring follows the
/// silhouette exactly, the band is a true uniform offset, and the gradient rides
/// on vertex colour with no texture at all.
/// </remarks>
public static class VelocityHalo
{
    /// <summary>Loud is hot, soft is cold -- the same pair the wash uses.</summary>
    /// <remarks>
    /// **Why not gold any more.** Gold put the loud shell in the pedal's family:
    /// the pedal's cue edge is amber at (1.02, 0.50, 0.12), and in peripheral
    /// vision a warm rim round a note and a warm bracket beside the track are the
    /// same signal. Dynamics and pedalling are two different instructions and a
    /// player should never have to work out which one lit up.
    ///
    /// So hue now says *which system*: red and blue are dynamics wherever they
    /// appear, amber is the pedal and nothing else. Within dynamics, red against
    /// blue says loud against soft. These are brighter than the floor wash's
    /// version of the same pair because a thin rim on a dark ground needs more
    /// than a broad field does to read as the same colour.
    /// </remarks>
    // 兩者都往外推：紅的把綠藍壓下去，藍的把紅壓下去。
    //
    // 原本是 (1, 0.34, 0.28) 和 (0.42, 0.68, 1) —— 兩個都帶著不少對方的成分，在
    // 一顆本來就有紅藍分手色的音符旁邊，那點差別讀不出來。殼要講的是「這顆該用
    // 多大力」，那是一個二選一的判斷，所以顏色也該是二選一的距離。
    public static readonly Color Strong = new Color(1f, 0.16f, 0.10f, 0.88f);
    public static readonly Color Weak = new Color(0.16f, 0.52f, 1f, 0.80f);

    /// <summary>
    /// How far the hexagon's point reaches, as a share of its half-height.
    /// </summary>
    /// <remarks>
    /// Measured off the note art rather than guessed: on Real_right_note the top
    /// edge is inset 15px each side over a 31px half-height, on right_note 10 of
    /// 18, on soft_note 15 of 26 -- 0.48, 0.56, 0.58. Half is the honest middle,
    /// and the shell only has to read as the same shape, not trace one frame of
    /// it exactly.
    /// </remarks>
    private const float TipPerHalfHeight = 0.52f;

    private static Material material;
    private static Material additive;

    /// <summary>The same flat unlit look, but adding light instead of covering.</summary>
    public static Material AdditiveMaterial
    {
        get
        {
            if (additive != null) return additive;
            Shader shader = Shader.Find("Nostalgia/FlatUnlitAdditive");
            if (shader == null) return null;
            additive = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return additive;
        }
    }

    /// <summary>The flat unlit material every shell shares.</summary>
    public static Material Material
    {
        get
        {
            if (material != null) return material;
            Shader shader = Shader.Find("Nostalgia/FlatUnlitVertexColour");
            if (shader == null) return null;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return material;
        }
    }

    /// <summary>
    /// Rewrites <paramref name="mesh"/> as a hexagonal ring hugging a note of
    /// this size, fading from <paramref name="tint"/> at the note's edge to
    /// nothing <paramref name="band"/> further out.
    /// </summary>
    /// <remarks>
    /// The outer ring is a true offset, not a scaled copy: each corner moves
    /// along the bisector of its two edges by band / cos(half the corner angle),
    /// which is what keeps the band the same width on the long flat sides and on
    /// the sharp points. Scaling instead would pinch the band to nothing at the
    /// tips, exactly where a hexagon is most recognisable.
    /// </remarks>
    public static void Build(Mesh mesh, float width, float height, float band, Color tint)
    {
        if (mesh == null) return;

        float halfWidth = Mathf.Max(0.0001f, width * 0.5f);
        float halfHeight = Mathf.Max(0.0001f, height * 0.5f);
        float tip = Mathf.Min(halfHeight * TipPerHalfHeight, halfWidth * 0.45f);
        float flat = halfWidth - tip;

        // 順時針，從左尖開始。和音符的圖同一個形狀：中線最寬，上下緣往內收。
        var inner = new Vector3[6];
        inner[0] = new Vector3(-halfWidth, 0f, 0f);
        inner[1] = new Vector3(-flat, halfHeight, 0f);
        inner[2] = new Vector3(flat, halfHeight, 0f);
        inner[3] = new Vector3(halfWidth, 0f, 0f);
        inner[4] = new Vector3(flat, -halfHeight, 0f);
        inner[5] = new Vector3(-flat, -halfHeight, 0f);

        var outer = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            Vector2 previous = inner[(i + 5) % 6];
            Vector2 here = inner[i];
            Vector2 next = inner[(i + 1) % 6];

            Vector2 n1 = OutwardNormal(previous, here);
            Vector2 n2 = OutwardNormal(here, next);
            Vector2 sum = n1 + n2;

            // 斜接：兩條邊各自外推 band 之後的交點。夾角越尖，頂點要推得越遠，
            // 分母就是那個補償 —— 少了它，尖端的帶子會被夾到幾乎沒有。
            float scale = 1f + Vector2.Dot(n1, n2);
            Vector2 offset = scale > 0.0001f ? sum * (band / scale) : n1 * band;
            outer[i] = here + offset;
        }

        // 三圈而不是兩圈。兩圈的話亮度是從音符邊緣一路直線掉到零，看起來像一片
        // 有邊界的色塊；中間多一圈維持滿亮，光才是「貼著音符最亮、往外散開」。
        const float CoreShare = 0.42f;
        var vertices = new Vector3[18];
        var colours = new Color[18];
        Color fade = new Color(tint.r, tint.g, tint.b, 0f);
        for (int i = 0; i < 6; i++)
        {
            vertices[i] = inner[i];
            colours[i] = tint;
            vertices[i + 6] = Vector3.Lerp(inner[i], outer[i], CoreShare);
            colours[i + 6] = tint;
            vertices[i + 12] = outer[i];
            colours[i + 12] = fade;
        }

        var triangles = new int[72];
        int t = 0;
        for (int ring = 0; ring < 2; ring++)
        {
            int a = ring * 6;
            int b = a + 6;
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                triangles[t++] = a + i;
                triangles[t++] = b + i;
                triangles[t++] = b + next;
                triangles[t++] = a + i;
                triangles[t++] = b + next;
                triangles[t++] = a + next;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colours;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Lays an open polyline as a flat ribbon of even thickness, lit along one
    /// flank and deep along the other.
    /// </summary>
    /// <remarks>
    /// Corners are mitered rather than butted, so a right angle keeps its full
    /// thickness through the turn instead of pinching -- on a bracket that pinch
    /// is the whole difference between a drawn mark and two lines that happen to
    /// meet. The shading is two colours on the two flanks, which is enough for a
    /// flat mesh to read as something with an edge rather than as a decal.
    /// </remarks>
    public static void BuildRibbon(Mesh mesh, Vector2[] path, float thickness, Color tint)
    {
        BuildRibbons(mesh, new[] { path }, thickness, tint);
    }

    /// <summary>Several ribbons in one mesh, so a mirrored pair costs one draw.</summary>
    public static void BuildRibbons(Mesh mesh, Vector2[][] paths, float thickness, Color tint)
    {
        if (mesh == null || paths == null) return;

        int points = 0;
        for (int p = 0; p < paths.Length; p++)
            if (paths[p] != null && paths[p].Length >= 2) points += paths[p].Length;
        if (points == 0) { mesh.Clear(); return; }

        var allVertices = new Vector3[points * 2];
        var allColours = new Color[points * 2];
        var allTriangles = new int[(points - paths.Length) * 6];
        int vertexAt = 0;
        int triangleAt = 0;

        for (int p = 0; p < paths.Length; p++)
        {
            Vector2[] path = paths[p];
            if (path == null || path.Length < 2) continue;
            int start = vertexAt;
            AppendRibbon(path, thickness, tint, allVertices, allColours, ref vertexAt);
            for (int i = 0; i < path.Length - 1; i++)
            {
                int a = start + i * 2;
                int b = a + 2;
                allTriangles[triangleAt++] = a;
                allTriangles[triangleAt++] = b;
                allTriangles[triangleAt++] = b + 1;
                allTriangles[triangleAt++] = a;
                allTriangles[triangleAt++] = b + 1;
                allTriangles[triangleAt++] = a + 1;
            }
        }

        mesh.Clear();
        mesh.vertices = allVertices;
        mesh.colors = allColours;
        mesh.triangles = allTriangles;
        mesh.RecalculateBounds();
    }

    private static void AppendRibbon(Vector2[] path, float thickness, Color tint,
        Vector3[] vertices, Color[] colours, ref int at)
    {

        float half = thickness * 0.5f;
        Color lit = Color.Lerp(tint, Color.white, 0.45f);
        Color deep = new Color(tint.r * 0.34f, tint.g * 0.32f, tint.b * 0.30f, tint.a);

        for (int i = 0; i < path.Length; i++)
        {
            Vector2 normal;
            if (i == 0) normal = Perpendicular(path[1] - path[0]);
            else if (i == path.Length - 1) normal = Perpendicular(path[i] - path[i - 1]);
            else
            {
                Vector2 n1 = Perpendicular(path[i] - path[i - 1]);
                Vector2 n2 = Perpendicular(path[i + 1] - path[i]);
                float miter = 1f + Vector2.Dot(n1, n2);
                normal = miter > 0.0001f ? (n1 + n2) / miter : n1;
            }

            vertices[at] = path[i] + normal * half;
            colours[at] = lit;
            at++;
            vertices[at] = path[i] - normal * half;
            colours[at] = deep;
            at++;
        }
    }

    private static Vector2 Perpendicular(Vector2 edge)
    {
        if (edge.sqrMagnitude < 1e-10f) return Vector2.up;
        edge.Normalize();
        return new Vector2(-edge.y, edge.x);
    }

    /// <summary>The outward unit normal of the edge a -> b, walking clockwise.</summary>
    private static Vector2 OutwardNormal(Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        if (edge.sqrMagnitude < 1e-10f) return Vector2.up;
        edge.Normalize();
        // 頂點是順時針排的（y 向上），順時針走的話**左**法線朝外。
        return new Vector2(-edge.y, edge.x);
    }
}
