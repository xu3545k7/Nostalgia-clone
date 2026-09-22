using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A horizontal ribbon/bookmark with a V-shaped cut at its trailing edge.
/// Kept procedural so it remains sharp at every Canvas Scaler resolution.
/// </summary>
[AddComponentMenu("UI/Bookmark Graphic")]
public sealed class BookmarkGraphic : MaskableGraphic
{
    [SerializeField, Min(2f)] private float notchDepth = 22f;

    public float NotchDepth
    {
        get => notchDepth;
        set
        {
            notchDepth = Mathf.Max(2f, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float cutX = Mathf.Max(r.xMin, r.xMax - Mathf.Min(notchDepth, r.width * 0.35f));
        float middleY = r.center.y;

        AddVertex(vh, new Vector2(r.xMin, r.yMin), new Vector2(0f, 0f));
        AddVertex(vh, new Vector2(r.xMax, r.yMin), new Vector2(1f, 0f));
        AddVertex(vh, new Vector2(cutX, middleY), new Vector2(0.9f, 0.5f));
        AddVertex(vh, new Vector2(r.xMax, r.yMax), new Vector2(1f, 1f));
        AddVertex(vh, new Vector2(r.xMin, r.yMax), new Vector2(0f, 1f));

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 4);
        vh.AddTriangle(2, 3, 4);
    }

    private void AddVertex(VertexHelper vh, Vector2 position, Vector2 uv)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = uv;
        vh.AddVert(vertex);
    }
}
