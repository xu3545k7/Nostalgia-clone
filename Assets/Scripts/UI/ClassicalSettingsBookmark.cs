using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Positions and animates the song-select settings bookmark.</summary>
public sealed class ClassicalSettingsBookmark : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private static readonly Color RestPlate = new Color(0.16f, 0.07f, 0.035f, 0.98f);
    private static readonly Color LitPlate = new Color(0.31f, 0.155f, 0.055f, 0.995f);
    private static readonly Color RestGold = new Color(0.78f, 0.58f, 0.22f, 0.82f);
    private static readonly Color LitGold = new Color(1.00f, 0.80f, 0.34f, 1f);

    private RectTransform rect;
    private RectTransform canvasRoot;
    private Image plate;
    private Outline outline;
    private TextMeshProUGUI label;
    private TuningSlidersGraphic icon;
    private readonly Vector3[] canvasCorners = new Vector3[4];
    private float glow;
    private float targetGlow;

    public void Configure(Image buttonPlate, Outline buttonOutline,
        TextMeshProUGUI buttonLabel, TuningSlidersGraphic buttonIcon)
    {
        rect = transform as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        canvasRoot = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        plate = buttonPlate;
        outline = buttonOutline;
        label = buttonLabel;
        icon = buttonIcon;
        FitToCanvasCorner();
        ApplyVisuals();
    }

    public void OnPointerEnter(PointerEventData eventData) => targetGlow = 1f;
    public void OnPointerExit(PointerEventData eventData) => targetGlow = 0f;
    public void OnPointerDown(PointerEventData eventData) => targetGlow = 0.55f;
    public void OnPointerUp(PointerEventData eventData) => targetGlow = 1f;

    private void Update()
    {
        glow = Mathf.MoveTowards(glow, targetGlow, Time.unscaledDeltaTime / 0.18f);
        ApplyVisuals();
    }

    private void LateUpdate() => FitToCanvasCorner();

    private void FitToCanvasCorner()
    {
        if (rect == null || canvasRoot == null || rect.parent == null) return;
        canvasRoot.GetWorldCorners(canvasCorners);
        Vector3 topRight = rect.parent.InverseTransformPoint(canvasCorners[2]);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = (Vector2)topRight + new Vector2(-34f, -34f);
        rect.sizeDelta = new Vector2(188f, 56f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * Mathf.Lerp(1f, 1.025f, glow);
    }

    private void ApplyVisuals()
    {
        Color gold = Color.Lerp(RestGold, LitGold, glow);
        if (plate != null) plate.color = Color.Lerp(RestPlate, LitPlate, glow);
        if (outline != null)
        {
            outline.effectColor = gold;
            outline.effectDistance = Vector2.one * Mathf.Lerp(2f, 4f, glow);
        }
        if (label != null) label.color = Color.Lerp(ClassicalBookUITheme.ParchmentLight, Color.white, glow);
        if (icon != null) icon.Tint = gold;
    }
}

/// <summary>Small resolution-independent tuning-sliders symbol.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TuningSlidersGraphic : MaskableGraphic
{
    private Color tint = ClassicalBookUITheme.Gold;

    public Color Tint
    {
        get => tint;
        set { tint = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 1f || r.height <= 1f) return;
        float[] knobs = { 0.68f, 0.34f, 0.56f };
        for (int i = 0; i < knobs.Length; i++)
        {
            float y = Mathf.Lerp(r.yMax - 6f, r.yMin + 6f, i / 2f);
            AddRect(vh, r.xMin + 2f, y - 1.4f, r.xMax - 2f, y + 1.4f, tint);
            float knobX = Mathf.Lerp(r.xMin + 5f, r.xMax - 5f, knobs[i]);
            AddRect(vh, knobX - 3.6f, y - 5f, knobX + 3.6f, y + 5f, tint);
        }
    }

    private static void AddRect(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
    {
        int index = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = colour;
        vertex.position = new Vector2(x0, y0); vh.AddVert(vertex);
        vertex.position = new Vector2(x0, y1); vh.AddVert(vertex);
        vertex.position = new Vector2(x1, y1); vh.AddVert(vertex);
        vertex.position = new Vector2(x1, y0); vh.AddVert(vertex);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }
}
