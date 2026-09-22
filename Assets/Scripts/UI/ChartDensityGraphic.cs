using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The note-density profile of one chart, drawn as stacked per-hand columns.
/// </summary>
/// <remarks>
/// Procedural rather than a sprite because the shape is different for every
/// chart, and because it has to stay sharp at any Canvas Scaler resolution --
/// the same reason <see cref="BookmarkGraphic"/> is built this way.
/// </remarks>
[AddComponentMenu("UI/Chart Density Graphic")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChartDensityGraphic : MaskableGraphic
{
    // hand 0 is the right hand and is drawn red everywhere else in the game.
    [SerializeField] private Color rightHandColour = new Color(0.66f, 0.39f, 0.37f, 0.84f);
    [SerializeField] private Color leftHandColour = new Color(0.38f, 0.49f, 0.62f, 0.84f);
    [SerializeField] private Color baselineColour = new Color(0.48f, 0.36f, 0.23f, 0.25f);
    [SerializeField, Range(0f, 0.5f)] private float columnGap = 0.22f;

    private float[] left;
    private float[] right;
    private float peak;

    public void SetData(float[] leftDensity, float[] rightDensity, float peakTotal)
    {
        left = leftDensity;
        right = rightDensity;
        peak = Mathf.Max(0.001f, peakTotal);
        Repaint();
    }

    /// <summary>
    /// Marks the mesh dirty and builds it on the spot.
    ///
    /// SetVerticesDirty alone was not enough: the diagnostic showed SetData
    /// running with good data on an active graphic while OnPopulateMesh was
    /// never called even once, so the queued rebuild was being dropped
    /// somewhere between the registry and the canvas. Building here as well
    /// costs one mesh generation and does not depend on that path at all.
    /// </summary>
    private void Repaint()
    {
        SetVerticesDirty();
        if (isActiveAndEnabled) UpdateGeometry();
    }

    public void Clear()
    {
        left = null;
        right = null;
        Repaint();
    }

    /// <summary>
    /// A graphic that is enabled after its data was set has to rebuild, otherwise
    /// SetVerticesDirty made while it was inactive is dropped on the floor.
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        Repaint();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        // rectTransform.rect rather than GetPixelAdjustedRect(): the pixel
        // adjustment needs a resolved canvas and returns an empty rect before
        // the first layout pass.
        Rect r = rectTransform.rect;

        if (r.width <= 1f || r.height <= 1f) return;

        // A baseline is drawn even with no data so the panel does not look broken
        // while the chart is still being read.
        AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMin + 1.5f, baselineColour);
        if (left == null || right == null || left.Length == 0) return;

        int columns = Mathf.Min(left.Length, right.Length);
        float step = r.width / columns;
        float inset = step * columnGap * 0.5f;
        float usableHeight = r.height - 2f;

        for (int i = 0; i < columns; i++)
        {
            float x0 = r.xMin + step * i + inset;
            float x1 = r.xMin + step * (i + 1) - inset;
            if (x1 - x0 < 0.5f) x1 = x0 + 0.5f;

            float leftHeight = left[i] / peak * usableHeight;
            float rightHeight = right[i] / peak * usableHeight;
            float y = r.yMin + 1.5f;

            // Left hand sits on the floor, right hand stacks on top of it, so the
            // column total is the combined density and the split is readable.
            if (leftHeight > 0.2f)
            {
                AddRoundedRect(vh, x0, y, x1, y + leftHeight, leftHandColour);
                y += leftHeight;
            }
            if (rightHeight > 0.2f) AddRoundedRect(vh, x0, y, x1, y + rightHeight, rightHandColour);
        }
    }

    private static void AddRoundedRect(VertexHelper vh, float x0, float y0, float x1, float y1,
        Color colour)
    {
        float width = x1 - x0;
        float height = y1 - y0;
        float radius = Mathf.Min(width * 0.42f, height * 0.32f);
        if (radius < 0.6f)
        {
            AddQuad(vh, x0, y0, x1, y1, colour);
            return;
        }

        const int cornerSegments = 3;
        int centreIndex = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = colour;
        vertex.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        vh.AddVert(vertex);

        Vector2[] centres =
        {
            new Vector2(x1 - radius, y0 + radius),
            new Vector2(x1 - radius, y1 - radius),
            new Vector2(x0 + radius, y1 - radius),
            new Vector2(x0 + radius, y0 + radius)
        };
        float[] starts = { -90f, 0f, 90f, 180f };
        int firstPerimeter = vh.currentVertCount;
        for (int corner = 0; corner < 4; corner++)
        {
            for (int segment = 0; segment <= cornerSegments; segment++)
            {
                float angle = (starts[corner] + 90f * segment / cornerSegments) * Mathf.Deg2Rad;
                vertex.position = centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vh.AddVert(vertex);
            }
        }

        int perimeterCount = 4 * (cornerSegments + 1);
        for (int i = 0; i < perimeterCount; i++)
        {
            int next = (i + 1) % perimeterCount;
            vh.AddTriangle(centreIndex, firstPerimeter + i, firstPerimeter + next);
        }
    }

    private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
    {
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = colour;

        vertex.position = new Vector3(x0, y0); vh.AddVert(vertex);
        vertex.position = new Vector3(x0, y1); vh.AddVert(vertex);
        vertex.position = new Vector3(x1, y1); vh.AddVert(vertex);
        vertex.position = new Vector3(x1, y0); vh.AddVert(vertex);

        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }
}
