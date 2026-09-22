using UnityEngine;

/// <summary>
/// The crystal that marks a note's middle key in recital mode, and the pieces
/// it breaks into.
/// </summary>
/// <remarks>
/// **Why a marker at all.** Recital mode pays for hitting the middle key of a
/// note, but a note three lanes wide has no middle *drawn* on it -- the art is
/// one continuous plate. Asking a player to aim at a lane they have to count out
/// while the note is falling is asking them to do arithmetic, not to play. The
/// crystal puts the target where the eye already is.
///
/// **Why it takes the hand's colour.** Everything else that belongs to a note is
/// red for the right hand and blue for the left, and a stone that ignored that
/// would be the one thing on the note not saying which hand plays it. Both are
/// pushed towards violet so they still read as the same family as the PRECISE
/// judgment and the seal's violet rim -- a plain red stone would just look like
/// part of the note art.
///
/// **Why the stone fills most of the texture.** The first version put the stone
/// at 60% and spent the rest on halo, so every size the caller asked for came out
/// nearly half as big as intended. <see cref="StoneFill"/> is published so the
/// caller can size the *stone*, not the sprite.
/// </remarks>
public static class CentreCrystal
{
    private const int Size = 256;

    /// <summary>
    /// How much of the sprite's half-extent the stone itself covers; the rest is
    /// halo. Multiply a wanted stone size by 1/this to get the sprite size.
    /// </summary>
    public const float StoneFill = 0.84f;

    private static readonly Vector2 Light = new Vector2(-0.55f, 0.84f).normalized;

    private static readonly Sprite[] stones = new Sprite[2];
    private static readonly Sprite[] shards = new Sprite[2];
    private static Sprite cross;

    /// <summary>Deep and pale ends of a hand's colour ramp.</summary>
    private static void Palette(bool rightHand, out Color deep, out Color pale, out Color rim)
    {
        if (rightHand)
        {
            // 偏紫的紅：紅是右手的顏色，紫是「精準」這件事的顏色。
            deep = new Color(0.46f, 0.07f, 0.30f, 1f);
            pale = new Color(1f, 0.62f, 0.82f, 1f);
            rim = new Color(0.22f, 0.03f, 0.14f, 1f);
        }
        else
        {
            deep = new Color(0.17f, 0.13f, 0.62f, 1f);
            pale = new Color(0.70f, 0.80f, 1f, 1f);
            rim = new Color(0.07f, 0.05f, 0.30f, 1f);
        }
    }

    /// <summary>
    /// The stone: a six-sided cut with a pale heart, in the hand's colour.
    /// </summary>
    /// <remarks>
    /// What makes it read as cut and not as a coloured hexagon is three things
    /// on top of the per-facet shading: the seams where facets meet, drawn as
    /// thin bright lines from the centre out; the girdle where the heart meets
    /// the outer facets; and one hard glint on the facet that faces the light.
    /// Each is a couple of lines of arithmetic and each is worth more than
    /// another gradient.
    /// </remarks>
    public static Sprite Stone(bool rightHand)
    {
        int index = rightHand ? 1 : 0;
        if (stones[index] != null) return stones[index];

        Palette(rightHand, out Color deep, out Color pale, out Color edge);
        var pixels = new Color32[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size * 2f - 1f;
                float v = (y + 0.5f) / Size * 2f - 1f;
                float d = Hex(u, v) / StoneFill;

                Color colour;
                if (d <= 1f)
                {
                    // 這一點屬於哪一個切面，由方位角決定；每一面朝著不同方向，
                    // 和光比對出來的明暗就會一面一面跳。
                    float angle = Mathf.Atan2(v, u);
                    float sector = (angle + Mathf.PI) / (Mathf.PI / 3f);
                    int facet = Mathf.Clamp(Mathf.FloorToInt(sector), 0, 5);
                    float mid = -Mathf.PI + (facet + 0.5f) * (Mathf.PI / 3f);
                    Vector2 facing = new Vector2(Mathf.Cos(mid), Mathf.Sin(mid));
                    float lit = Vector2.Dot(facing, Light) * 0.5f + 0.5f;
                    colour = Color.Lerp(deep, pale, lit * lit);

                    // 稜線：兩個切面交界的那一條。真正的切割石頭是靠這些線讀出來的。
                    float seam = Mathf.Abs(sector - Mathf.Floor(sector) - 0.5f) * 2f;
                    float seamGlow = Mathf.Clamp01((seam - 0.86f) / 0.14f) * Mathf.Clamp01(d / 0.30f);
                    colour = Color.Lerp(colour, Color.Lerp(pale, Color.white, 0.6f), seamGlow * 0.55f);

                    // 心：淺色的內石，自己也有六個面，但對比小得多。
                    float core = d / 0.50f;
                    if (core <= 1f)
                    {
                        Color heart = Color.Lerp(Color.Lerp(pale, Color.white, 0.55f),
                            Color.white, lit);
                        colour = Color.Lerp(colour, heart, Mathf.Clamp01((1f - core) / 0.26f));
                    }

                    // 腰稜：心和外圈交界的一圈亮。
                    float girdle = Mathf.Clamp01(1f - Mathf.Abs(d - 0.50f) / 0.055f);
                    colour = Color.Lerp(colour, Color.white, girdle * 0.5f);

                    // 一顆硬的高光，落在朝著光的那一面上。
                    Vector2 spot = Light * 0.46f;
                    float glint = Mathf.Clamp01(1f - (new Vector2(u, v) - spot).magnitude / 0.17f);
                    colour = Color.Lerp(colour, Color.white, glint * glint * 0.85f);

                    // 外緣收一圈暗，石頭才有輪廓。
                    colour = Color.Lerp(colour, edge, Mathf.Clamp01((d - 0.86f) / 0.14f) * 0.92f);
                    colour.a = 1f;
                }
                else
                {
                    float halo = Mathf.Clamp01(1f - (d - 1f) / 0.30f);
                    Color glow = Color.Lerp(deep, pale, 0.45f);
                    colour = new Color(glow.r, glow.g, glow.b, halo * halo * 0.62f);
                }

                pixels[y * Size + x] = colour;
            }
        }

        stones[index] = Build(pixels, rightHand ? "Centre Crystal R" : "Centre Crystal L");
        return stones[index];
    }

    /// <summary>
    /// A piece of the broken crystal: a diamond, pointing the way it flies.
    /// </summary>
    /// <remarks>
    /// A diamond and not the hexagon it came from. A hexagon is a whole stone --
    /// four whole stones flying apart reads as four new objects appearing, not
    /// as one breaking. A wedge with a point is unmistakably a *piece of*
    /// something, and pointing it along its travel makes it look thrown.
    /// </remarks>
    public static Sprite Shard(bool rightHand)
    {
        int index = rightHand ? 1 : 0;
        if (shards[index] != null) return shards[index];

        Palette(rightHand, out Color deep, out Color pale, out Color edge);
        var pixels = new Color32[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size * 2f - 1f;
                float v = (y + 0.5f) / Size * 2f - 1f;
                // 菱形：|u|/寬 + |v|/高 = 1。橫向收窄，尖端才明顯。
                float d = (Mathf.Abs(u) / 0.62f + Mathf.Abs(v)) / StoneFill;

                Color colour;
                if (d <= 1f)
                {
                    Vector2 facing = new Vector2(Mathf.Sign(u) * 0.85f, Mathf.Sign(v) * 0.53f);
                    float lit = Vector2.Dot(facing.normalized, Light) * 0.5f + 0.5f;
                    colour = Color.Lerp(deep, Color.Lerp(pale, Color.white, 0.25f), lit * lit);
                    // 中脊：從尖到尖那一條亮線，碎片才有厚度。
                    float ridge = Mathf.Clamp01(1f - Mathf.Abs(u) / 0.16f);
                    colour = Color.Lerp(colour, Color.white, ridge * ridge * 0.6f);
                    colour = Color.Lerp(colour, edge, Mathf.Clamp01((d - 0.82f) / 0.18f) * 0.85f);
                    colour.a = 1f;
                }
                else
                {
                    float halo = Mathf.Clamp01(1f - (d - 1f) / 0.35f);
                    Color glow = Color.Lerp(deep, pale, 0.55f);
                    colour = new Color(glow.r, glow.g, glow.b, halo * halo * 0.6f);
                }

                pixels[y * Size + x] = colour;
            }
        }

        shards[index] = Build(pixels, rightHand ? "Crystal Shard R" : "Crystal Shard L");
        return shards[index];
    }

    /// <summary>
    /// The flash left where the crystal was: a four-pointed star of light.
    /// </summary>
    /// <remarks>
    /// Arms rather than a disc, and white rather than either hand's colour. A
    /// round flash says "something glowed here"; a cross says "something broke
    /// apart along these axes", which is what the shards then do. It is white
    /// because it is the light of the break, not the material -- the colour is
    /// carried out by the pieces.
    /// </remarks>
    public static Sprite Cross
    {
        get
        {
            if (cross != null) return cross;

            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    // 一條臂 = 沿著它的方向長、垂直方向窄。兩條相加就是十字。
                    float horizontal = Mathf.Max(0f, 1f - Mathf.Abs(u)) * Mathf.Max(0f, 1f - Mathf.Abs(v) * 9f);
                    float vertical = Mathf.Max(0f, 1f - Mathf.Abs(v)) * Mathf.Max(0f, 1f - Mathf.Abs(u) * 9f);
                    float core = Mathf.Max(0f, 1f - Mathf.Sqrt(u * u + v * v) * 4.5f);
                    float glow = Mathf.Clamp01(horizontal + vertical + core * 1.4f);

                    Color colour = Color.Lerp(new Color(0.72f, 0.62f, 1f, 1f),
                        new Color(1f, 0.98f, 1f, 1f), glow * glow);
                    colour.a = glow * glow;
                    pixels[y * Size + x] = colour;
                }
            }

            cross = Build(pixels, "Centre Crystal Flash");
            return cross;
        }
    }

    private static Sprite Build(Color32[] pixels, string name)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    /// <summary>Distance to a pointy-top hexagon's edge: 1 exactly on it.</summary>
    private static float Hex(float u, float v)
    {
        float best = 0f;
        for (int i = 0; i < 6; i++)
        {
            float a = (90f + i * 60f) * Mathf.Deg2Rad;
            best = Mathf.Max(best, u * Mathf.Cos(a) + v * Mathf.Sin(a));
        }
        // 六個半平面的最大值是內接圓的量法，除以 cos(30°) 才是到頂點為 1。
        return best / 0.8660254f;
    }
}
