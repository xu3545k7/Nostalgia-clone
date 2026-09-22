using UnityEngine;

/// <summary>
/// The Elder Futhark drawn at runtime instead of shipped as art.
/// </summary>
/// <remarks>
/// Every glyph is a handful of straight strokes, so an authored sheet would be
/// 24 hand-traced files to keep in sync with one stroke-weight decision. Here a
/// glyph is its segment list, and weight, glow falloff and resolution are three
/// numbers shared by all of them.
///
/// The alpha channel carries the whole shape: a bright core inside the stroke
/// and a soft falloff outside it, which is what <c>RuneGlyph.shader</c> reads to
/// separate the pale core from the saturated halo. RGB is left white, for the
/// same reason the magic circle ignores its own texture RGB.
/// </remarks>
public static class RuneGlyphs
{
    public const int Count = 24;

    private const int CellSize = 96;
    private const int Columns = 6;
    private const int Rows = 4;

    /// <summary>Stroke half-width, as a fraction of the cell.</summary>
    private const float StrokeHalfWidth = 0.030f;
    /// <summary>How far the glow reaches past the stroke, as a fraction of the cell.</summary>
    private const float GlowRadius = 0.075f;

    private static Sprite[] sprites;
    private static Texture2D atlas;

    // Normalised strokes as x0,y0,x1,y1 quads, origin bottom-left, drawn inside
    // x 0.2..0.8 and y 0.05..0.95. Order follows the Elder Futhark: Fehu, Uruz,
    // Thurisaz, Ansuz, Raidho, Kaunan, Gebo, Wunjo, Hagalaz, Naudiz, Isaz, Jera,
    // Eihwaz, Perth, Algiz, Sowilo, Tiwaz, Berkanan, Ehwaz, Mannaz, Laguz,
    // Ingwaz, Othala, Dagaz.
    private static readonly float[][] Strokes =
    {
        new[] { 0.35f, 0.05f, 0.35f, 0.95f, 0.35f, 0.90f, 0.74f, 0.68f, 0.35f, 0.60f, 0.74f, 0.38f },
        new[] { 0.28f, 0.05f, 0.28f, 0.95f, 0.28f, 0.95f, 0.72f, 0.74f, 0.72f, 0.74f, 0.72f, 0.05f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.32f, 0.74f, 0.68f, 0.52f, 0.68f, 0.52f, 0.32f, 0.30f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.32f, 0.95f, 0.70f, 0.72f, 0.32f, 0.62f, 0.70f, 0.39f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.32f, 0.95f, 0.68f, 0.78f, 0.68f, 0.78f, 0.32f, 0.55f, 0.32f, 0.55f, 0.70f, 0.05f },
        new[] { 0.68f, 0.95f, 0.30f, 0.50f, 0.30f, 0.50f, 0.68f, 0.05f },
        new[] { 0.24f, 0.95f, 0.76f, 0.05f, 0.76f, 0.95f, 0.24f, 0.05f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.32f, 0.95f, 0.70f, 0.77f, 0.70f, 0.77f, 0.32f, 0.56f },
        new[] { 0.27f, 0.05f, 0.27f, 0.95f, 0.73f, 0.05f, 0.73f, 0.95f, 0.27f, 0.62f, 0.73f, 0.40f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.16f, 0.34f, 0.80f, 0.68f },
        new[] { 0.50f, 0.05f, 0.50f, 0.95f },
        new[] { 0.68f, 0.95f, 0.30f, 0.73f, 0.30f, 0.73f, 0.68f, 0.48f, 0.68f, 0.48f, 0.30f, 0.24f, 0.30f, 0.24f, 0.66f, 0.05f },
        new[] { 0.42f, 0.14f, 0.42f, 0.86f, 0.42f, 0.86f, 0.70f, 0.95f, 0.42f, 0.14f, 0.16f, 0.05f },
        new[] { 0.68f, 0.95f, 0.32f, 0.76f, 0.32f, 0.76f, 0.32f, 0.24f, 0.32f, 0.24f, 0.68f, 0.05f },
        new[] { 0.50f, 0.05f, 0.50f, 0.95f, 0.50f, 0.68f, 0.22f, 0.95f, 0.50f, 0.68f, 0.78f, 0.95f },
        new[] { 0.70f, 0.95f, 0.34f, 0.74f, 0.34f, 0.74f, 0.66f, 0.42f, 0.66f, 0.42f, 0.30f, 0.05f },
        new[] { 0.50f, 0.05f, 0.50f, 0.95f, 0.50f, 0.95f, 0.26f, 0.70f, 0.50f, 0.95f, 0.74f, 0.70f },
        new[] { 0.32f, 0.05f, 0.32f, 0.95f, 0.32f, 0.95f, 0.68f, 0.78f, 0.68f, 0.78f, 0.32f, 0.53f, 0.32f, 0.53f, 0.68f, 0.30f, 0.68f, 0.30f, 0.32f, 0.07f },
        new[] { 0.26f, 0.05f, 0.26f, 0.95f, 0.74f, 0.05f, 0.74f, 0.95f, 0.26f, 0.95f, 0.50f, 0.58f, 0.50f, 0.58f, 0.74f, 0.95f },
        new[] { 0.24f, 0.05f, 0.24f, 0.95f, 0.76f, 0.05f, 0.76f, 0.95f, 0.24f, 0.95f, 0.76f, 0.52f, 0.76f, 0.95f, 0.24f, 0.52f },
        new[] { 0.36f, 0.05f, 0.36f, 0.95f, 0.36f, 0.95f, 0.70f, 0.71f },
        new[] { 0.50f, 0.95f, 0.78f, 0.50f, 0.78f, 0.50f, 0.50f, 0.05f, 0.50f, 0.05f, 0.22f, 0.50f, 0.22f, 0.50f, 0.50f, 0.95f },
        new[] { 0.50f, 0.95f, 0.76f, 0.62f, 0.76f, 0.62f, 0.50f, 0.30f, 0.50f, 0.30f, 0.24f, 0.62f, 0.24f, 0.62f, 0.50f, 0.95f, 0.50f, 0.30f, 0.80f, 0.05f, 0.50f, 0.30f, 0.20f, 0.05f },
        new[] { 0.24f, 0.05f, 0.24f, 0.95f, 0.76f, 0.05f, 0.76f, 0.95f, 0.24f, 0.95f, 0.76f, 0.05f, 0.24f, 0.05f, 0.76f, 0.95f },
    };

    /// <summary>Builds the atlas on first use and hands back one sprite per rune.</summary>
    public static Sprite[] GetSprites()
    {
        if (sprites != null && sprites.Length == Count && sprites[0] != null) return sprites;

        // Mip-mapped: the glyphs are drawn small, and thin strokes minified
        // without mips sparkle as the runes drift and turn -- the same shimmer
        // the hold magic circle's textures had before mipmaps were enabled on
        // them. The cells keep a transparent margin, so the bleed between
        // neighbours at the coarse levels stays under the glow's own falloff.
        atlas = new Texture2D(Columns * CellSize, Rows * CellSize, TextureFormat.RGBA32, true, true)
        {
            name = "Rune Glyph Atlas (Runtime)",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 2,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color32[atlas.width * atlas.height];
        var clear = new Color32(255, 255, 255, 0);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        for (int rune = 0; rune < Count; rune++)
        {
            RasterizeGlyph(pixels, atlas.width, CellX(rune), CellY(rune), Strokes[rune]);
        }

        atlas.SetPixels32(pixels);
        atlas.Apply(true, true);

        sprites = new Sprite[Count];
        for (int rune = 0; rune < Count; rune++)
        {
            sprites[rune] = Sprite.Create(atlas,
                new Rect(CellX(rune), CellY(rune), CellSize, CellSize),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprites[rune].name = "Rune " + (rune + 1);
        }
        return sprites;
    }

    public static void Release()
    {
        sprites = null;
        if (atlas == null) return;
        Object.Destroy(atlas);
        atlas = null;
    }

    private static int CellX(int rune) => (rune % Columns) * CellSize;

    private static int CellY(int rune) => (Rows - 1 - rune / Columns) * CellSize;

    private static void RasterizeGlyph(Color32[] pixels, int atlasWidth, int cellX, int cellY,
        float[] strokes)
    {
        for (int py = 0; py < CellSize; py++)
        {
            float ny = (py + 0.5f) / CellSize;
            for (int px = 0; px < CellSize; px++)
            {
                float nx = (px + 0.5f) / CellSize;

                float nearest = float.MaxValue;
                for (int s = 0; s + 3 < strokes.Length; s += 4)
                {
                    float distance = DistanceToSegment(nx, ny,
                        strokes[s], strokes[s + 1], strokes[s + 2], strokes[s + 3]);
                    if (distance < nearest) nearest = distance;
                }

                // Solid inside the stroke, then a falloff reaching further than
                // the stroke is wide. The shader turns the bright part into the
                // pale core and the falloff into the coloured halo.
                float core = 1f - SmoothEdge(StrokeHalfWidth * 0.6f, StrokeHalfWidth, nearest);
                float glow = 1f - SmoothEdge(StrokeHalfWidth, StrokeHalfWidth + GlowRadius, nearest);
                float alpha = Mathf.Clamp01(Mathf.Max(core, glow * glow * 0.62f));
                if (alpha <= 0.002f) continue;

                pixels[(cellY + py) * atlasWidth + (cellX + px)] =
                    new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
    }

    /// <summary>
    /// The shader-style edge function: 0 below <paramref name="edge0"/>, 1 above
    /// <paramref name="edge1"/>, smoothed between.
    /// </summary>
    /// <remarks>
    /// Not <c>Mathf.SmoothStep</c>. That one interpolates *between* its first
    /// two arguments and returns a value in that range, so feeding it stroke
    /// widths returns ~0.03 everywhere and every glyph rasterises as a solid
    /// opaque square.
    /// </remarks>
    private static float SmoothEdge(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax;
        float aby = by - ay;
        float lengthSquared = Mathf.Max(1e-6f, abx * abx + aby * aby);
        float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / lengthSquared);
        float dx = px - (ax + abx * t);
        float dy = py - (ay + aby * t);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
