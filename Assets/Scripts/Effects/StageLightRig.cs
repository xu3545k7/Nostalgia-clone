using UnityEngine;
using UnityEngine.UI;

/// <summary>One fixture and the beam it throws, in units of half the frame height.</summary>
/// <remarks>
/// Half-heights rather than a normalised 0..1 box: the same number then means
/// the same distance horizontally and vertically, so an angle survives being
/// scaled into any consumer's units. x simply runs to +/-aspect.
/// </remarks>
public struct StageBeam
{
    public Vector2 apex;
    public float tiltDegrees;
    public float length;
    public float topRadius;
    public float baseRadius;
    public float lampRadius;
    public float strength;
}

/// <summary>
/// The one description of where the stage lights hang, shared by the thing that
/// draws the beams and the thing that fills them with dust.
/// </summary>
/// <remarks>
/// The beams live on the UI backdrop and the motes are a 3D particle system on
/// the camera plane -- two different spaces. Because a camera-plane rectangle
/// at any depth is the frame, the same half-height coordinates land in the same
/// place in both, so "the dust is inside the beam" is arithmetic rather than a
/// coincidence that has to be re-tuned whenever either side moves.
/// </remarks>
public static class StageLightLayout
{
    public const int Count = 11;

    /// <summary>
    /// A row of narrow fixtures across the top, fanning outwards.
    /// </summary>
    /// <remarks>
    /// What makes a lighting rig read is many narrow beams at clearly different
    /// angles -- a few wide cones aimed straight down is a floodlight, not a
    /// stage. Everything is jittered: a perfectly symmetric fan of identical
    /// beams looks like a diagram of a rig rather than a rig.
    /// </remarks>
    public static StageBeam[] Build(float aspect)
    {
        var beams = new StageBeam[Count];
        for (int i = 0; i < Count; i++)
        {
            float across = i / (Count - 1f) * 2f - 1f;          // -1 左，+1 右
            float jitter = Wobble(i, 7) - 0.5f;
            float tilt = across * 27f + jitter * 5f;            // 往外掃
            float length = 1.85f / Mathf.Cos(tilt * Mathf.Deg2Rad);
            float top = 0.010f;
            float spread = 4.5f + Wobble(i, 19) * 2.5f;         // 半開角，度

            beams[i] = new StageBeam
            {
                apex = new Vector2(across * 0.80f * aspect + jitter * aspect * 0.03f,
                                   0.74f + Wobble(i, 13) * 0.10f),
                tiltDegrees = tilt,
                length = length,
                topRadius = top,
                // 底寬由**開角**決定，不是直接給一個底半徑。給底半徑的話，那個
                // 寬度只在光柱的盡頭才達到，而盡頭在畫面底下、被軌道擋著 ——
                // 看得到的上半段永遠只開了一小部分，所以怎麼加寬都還是一條線。
                baseRadius = top + length * Mathf.Tan(spread * Mathf.Deg2Rad),
                lampRadius = 0.026f + Wobble(i, 23) * 0.010f,
                strength = 0.62f + Wobble(i, 29) * 0.38f,
            };
        }

        return beams;
    }

    public static float Wobble(int index, int salt)
    {
        float v = Mathf.Sin((index + 1) * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }
}

/// <summary>
/// Draws the stage beams and their fixtures on the gameplay backdrop.
/// </summary>
/// <remarks>
/// **Why this is UI and not a mesh in the world.** It was a mesh on the camera
/// plane at the far clip, sharing space with the dust. Whether that lands in
/// front of or behind the backdrop canvas depends on the canvas render mode and
/// its plane distance, and the answer changed the moment the material stopped
/// writing depth -- opaque bars covered the backdrop, a proper blended beam
/// vanished behind it. This project has lost the same argument before with the
/// judgment-line echoes. Drawn as a sibling of the hall, the order is settled by
/// hierarchy and cannot be undone by a material change.
///
/// **Why the falloff is a texture.** Vertex colours can only ramp linearly, so
/// a beam built that way has two straight ramps meeting at a crease down its
/// spine. A Gaussian sampled from a texture has no crease, and it costs one
/// quad per beam instead of a fan of them.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class StageLightRig : MaskableGraphic
{
    private const string ObjectName = "~StageLightRig";
    private const int Resolution = 128;

    // 圖集的兩塊。各留一點邊，免得雙線性取樣把另一半吃進來。
    private const float BeamU0 = 0.004f;
    private const float BeamU1 = 0.496f;
    private const float LampU0 = 0.504f;
    private const float LampU1 = 0.996f;

    [Tooltip("燈光的顏色。暖黃。")]
    [SerializeField] private Color lampColour = new Color(1f, 0.80f, 0.42f, 1f);
    [Tooltip("調性色混進燈光的比例。")]
    [SerializeField, Range(0f, 1f)] private float tintBlend = 0.18f;
    [Tooltip("光柱最亮處的濃度。背景要讓路給譜面。")]
    [SerializeField, Range(0f, 1f)] private float beamOpacity = 0.30f;
    [Tooltip("燈口的濃度。小而亮。")]
    [SerializeField, Range(0f, 1f)] private float lampOpacity = 0.85f;

    private static Texture2D profileTexture;
    private Color tint = Color.white;

    public override Texture mainTexture => EnsureProfile();

    /// <summary>Installs the rig immediately in front of the hall.</summary>
    public static StageLightRig Attach(Transform hall)
    {
        if (hall == null || hall.parent == null) return null;

        Transform existing = hall.parent.Find(ObjectName);
        if (existing != null) return existing.GetComponent<StageLightRig>();

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 會在第一次重建時炸掉。
        var rigObject = new GameObject(ObjectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(StageLightRig));
        rigObject.layer = hall.gameObject.layer;

        RectTransform rect = rigObject.GetComponent<RectTransform>();
        rect.SetParent(hall.parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetSiblingIndex(hall.GetSiblingIndex() + 1);

        StageLightRig rig = rigObject.GetComponent<StageLightRig>();
        rig.raycastTarget = false;
        return rig;
    }

    /// <summary>Harmony colour. The lamps take a little of it, like the gilding.</summary>
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
        if (r.width <= 1f || r.height <= 1f) return;

        float halfHeight = r.height * 0.5f;
        Color warm = Color.Lerp(lampColour, tint, tintBlend);

        foreach (StageBeam beam in StageLightLayout.Build(r.width / r.height))
        {
            float radians = beam.tiltDegrees * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
            Vector2 across = new Vector2(-axis.y, axis.x);

            Vector2 apex = beam.apex * halfHeight;
            Vector2 foot = apex + axis * (beam.length * halfHeight);
            Vector2 top = across * (beam.topRadius * halfHeight);
            Vector2 bottom = across * (beam.baseRadius * halfHeight);

            AddQuad(vh, apex - top, apex + top, foot + bottom, foot - bottom,
                new Vector2(BeamU0, 0f), new Vector2(BeamU1, 0f),
                new Vector2(BeamU1, 1f), new Vector2(BeamU0, 1f),
                new Color(warm.r, warm.g, warm.b, beam.strength * beamOpacity));

            // 燈口本身：小而亮。沒有這一塊，光柱看起來像從畫面外憑空冒出來的。
            float lamp = beam.lampRadius * halfHeight;
            Vector2 hub = apex + axis * (lamp * 0.25f);
            AddQuad(vh,
                hub - across * lamp - axis * lamp, hub + across * lamp - axis * lamp,
                hub + across * lamp + axis * lamp, hub - across * lamp + axis * lamp,
                new Vector2(LampU0, 0f), new Vector2(LampU1, 0f),
                new Vector2(LampU1, 1f), new Vector2(LampU0, 1f),
                new Color(warm.r, warm.g, warm.b, Mathf.Min(1f, beam.strength * lampOpacity)));
        }
    }

    /// <summary>
    /// One atlas: the beam cross-section on the left, the fixture on the right.
    /// </summary>
    /// <remarks>
    /// Both halves fall to zero at the seam between them, so bilinear filtering
    /// has nothing to bleed across, and the whole rig stays one draw call.
    /// </remarks>
    private static Texture2D EnsureProfile()
    {
        if (profileTexture != null) return profileTexture;

        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            name = "StageLightProfile",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[Resolution * Resolution];
        for (int y = 0; y < Resolution; y++)
        {
            float v = (y + 0.5f) / Resolution;
            for (int x = 0; x < Resolution; x++)
            {
                float u = (x + 0.5f) / Resolution;
                float a;
                if (u < 0.5f)
                {
                    // 橫向：高斯，乘一個窗讓它在邊緣真的到 0，中間補一條細的芯。
                    float d = u / 0.5f * 2f - 1f;
                    float window = Mathf.Max(0f, 1f - d * d);
                    float cross = Mathf.Exp(-d * d * 3.0f) * window
                                  + Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(d)), 8f) * 0.35f;

                    // 縱向：燈口最亮，但煙霧是滿場的，所以底下不會完全消失 ——
                    // 掉太快會讓光柱看起來只是燈口的一團暈。最後一段才收乾淨。
                    float along = Mathf.Lerp(0.32f, 1f, Mathf.Pow(1f - v, 1.3f))
                                  * Mathf.SmoothStep(0f, 0.12f, 1f - v);
                    a = cross * along;
                }
                else
                {
                    float dx = (u - 0.5f) / 0.5f * 2f - 1f;
                    float dy = v * 2f - 1f;
                    float falloff = Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = falloff * falloff * falloff * 0.8f + Mathf.Pow(falloff, 14f) * 0.7f;
                }

                pixels[y * Resolution + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        profileTexture = texture;
        return profileTexture;
    }

    private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, Color colour)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, uv0, colour);
        AddVertex(vh, p1, uv1, colour);
        AddVertex(vh, p2, uv2, colour);
        AddVertex(vh, p3, uv3, colour);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
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
