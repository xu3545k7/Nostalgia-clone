using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tempo across a chart, drawn as a chart rather than a line: the levels the
/// piece holds are labelled down the left edge, a dashed rule runs from each
/// label to where that level begins, and every change is marked.
/// </summary>
/// <remarks>
/// **Why the scale is not zero-based.** Most charts hold one tempo for minutes
/// and then move it a few percent. Drawn from zero, every one of them is a flat
/// line across the top and the change -- the only thing this graph is for --
/// disappears. The range is the chart's own span, padded, with a floor on how
/// narrow it can get so a chart that really is constant does not have its
/// rounding noise magnified into a mountain range.
///
/// **Why plateaus rather than every bucket.** Forty-eight labels is not a chart,
/// it is a table. The curve is grouped into the levels it actually rests at, and
/// only those get a label and a rule; a gradual ramp is marked at both ends
/// instead, because that is the pair of numbers a player needs.
///
/// **Why the practice scale is applied here.** The player adjusts the tempo by
/// naming a new top speed, and has to see immediately what that does to the rest
/// of the piece. The curve is stored raw and multiplied at draw time, so the
/// labels follow the stepper without anything being recomputed.
/// </remarks>
public sealed class ChartBpmGraphic : MaskableGraphic
{
    private const float MinimumSpan = 8f;
    private const int MaxLevels = 4;

    [Tooltip("線的顏色。")]
    [SerializeField] private Color lineColour = new Color(0.42f, 0.62f, 0.86f, 1f);
    [SerializeField, Range(0.5f, 6f)] private float thickness = 2f;
    [Tooltip("刻度虛線的濃度。")]
    [SerializeField, Range(0f, 1f)] private float guideOpacity = 0.35f;
    [Tooltip("左邊留給刻度標籤的寬度（像素）。")]
    [SerializeField, Range(20f, 80f)] private float axisWidth = 38f;

    private readonly float[] curve = new float[ChartAnalysis.BucketCount];
    private readonly List<Plateau> plateaus = new List<Plateau>(8);
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>(MaxLevels);
    private bool hasData;
    private float scale = 1f;
    private float low;
    private float high;

    /// <summary>One stretch the tempo rests at, in bucket indices.</summary>
    private readonly struct Plateau
    {
        public readonly int from;
        public readonly int to;
        public readonly float bpm;

        public Plateau(int from, int to, float bpm)
        {
            this.from = from;
            this.to = to;
            this.bpm = bpm;
        }

        public int Length => to - from + 1;
    }

    /// <summary>The chart's own top and bottom tempo, before any adjustment.</summary>
    public float OriginalHigh { get; private set; }
    public float OriginalLow { get; private set; }
    public bool HasData => hasData;

    /// <summary>
    /// Multiplies every tempo shown. 1 is the chart as written.
    /// </summary>
    public float Scale
    {
        get => scale;
        set
        {
            float clamped = Mathf.Max(0.05f, value);
            if (Mathf.Approximately(scale, clamped)) return;
            scale = clamped;
            Rescale();
            SetVerticesDirty();
        }
    }

    public void SetData(ChartAnalysis analysis)
    {
        hasData = analysis != null && analysis.HasBpmCurve;
        if (!hasData)
        {
            Clear();
            return;
        }

        OriginalHigh = float.MinValue;
        OriginalLow = float.MaxValue;
        for (int i = 0; i < curve.Length; i++)
        {
            curve[i] = analysis.bpmCurve[i];
            if (curve[i] <= 0.01f) continue;
            OriginalLow = Mathf.Min(OriginalLow, curve[i]);
            OriginalHigh = Mathf.Max(OriginalHigh, curve[i]);
        }

        FindPlateaus();
        Rescale();
        SetVerticesDirty();
    }

    public void Clear()
    {
        hasData = false;
        plateaus.Clear();
        foreach (TextMeshProUGUI label in labels)
            if (label != null) label.gameObject.SetActive(false);
        SetVerticesDirty();
    }

    /// <summary>The tempo range as it will be played, for a caption to report.</summary>
    public bool TryGetRange(out float lowest, out float highest)
    {
        lowest = OriginalLow * scale;
        highest = OriginalHigh * scale;
        return hasData;
    }

    /// <summary>
    /// Groups the curve into the levels it rests at.
    /// </summary>
    /// <remarks>
    /// A bucket joins the current group while it stays within a tolerance of the
    /// group's own average, so a slow ramp breaks into short groups and a held
    /// tempo stays one long one. The tolerance is a fraction of the chart's
    /// range rather than a fixed number of beats: three bpm is nothing in a
    /// piece that swings by eighty and everything in one that swings by five.
    /// </remarks>
    private void FindPlateaus()
    {
        plateaus.Clear();
        float tolerance = Mathf.Max(0.4f, (OriginalHigh - OriginalLow) * 0.06f);

        int start = -1;
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < curve.Length; i++)
        {
            if (curve[i] <= 0.01f) continue;
            if (start < 0)
            {
                start = i;
                sum = curve[i];
                count = 1;
                continue;
            }

            float average = sum / count;
            if (Mathf.Abs(curve[i] - average) <= tolerance)
            {
                sum += curve[i];
                count++;
                continue;
            }

            plateaus.Add(new Plateau(start, i - 1, sum / count));
            start = i;
            sum = curve[i];
            count = 1;
        }

        if (start >= 0) plateaus.Add(new Plateau(start, curve.Length - 1, sum / count));
    }

    private void Rescale()
    {
        low = OriginalLow * scale;
        high = OriginalHigh * scale;
        if (high <= low)
        {
            high = low + MinimumSpan;
        }
        else if (high - low < MinimumSpan)
        {
            float middle = (low + high) * 0.5f;
            low = middle - MinimumSpan * 0.5f;
            high = middle + MinimumSpan * 0.5f;
        }

        // 上下各留一成，線才不會貼著框走。
        float pad = (high - low) * 0.10f;
        low -= pad;
        high += pad;
        LayOutLabels();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasData) return;

        Rect r = rectTransform.rect;
        if (r.width <= axisWidth + 8f || r.height <= 8f) return;

        Color guide = new Color(lineColour.r, lineColour.g, lineColour.b, guideOpacity);
        Color faint = new Color(lineColour.r, lineColour.g, lineColour.b, guideOpacity * 0.55f);

        // 每一段平台一條水平虛線，從左邊的刻度拉到它開始的位置 —— 那條線就是
        // 「這個標示對應到這裡」的意思。
        foreach (Plateau plateau in Ranked())
        {
            float y = PlaceY(r, plateau.bpm * scale);
            Dashed(vh, new Vector2(r.xMin + axisWidth * 0.86f, y),
                new Vector2(PlaceX(r, plateau.from), y), 1f, guide);
        }

        // 變化的地方一條垂直虛線。漸變的段落頭尾各一條，因為那是要讀的兩個數字。
        for (int i = 1; i < plateaus.Count; i++)
        {
            float x = PlaceX(r, plateaus[i].from);
            Dashed(vh, new Vector2(x, r.yMin), new Vector2(x, r.yMax), 1f, faint);
        }

        int previous = -1;
        for (int i = 0; i < curve.Length; i++)
        {
            if (curve[i] <= 0.01f) continue;
            if (previous >= 0) AddSegment(vh, r, previous, i);
            previous = i;
        }
    }

    /// <summary>The longest plateaus first, so the labels go to the levels that matter.</summary>
    private IEnumerable<Plateau> Ranked()
    {
        if (plateaus.Count <= MaxLevels) return plateaus;
        var sorted = new List<Plateau>(plateaus);
        sorted.Sort((a, b) => b.Length.CompareTo(a.Length));
        return sorted.GetRange(0, MaxLevels);
    }

    private void LayOutLabels()
    {
        if (!hasData) return;
        Rect r = rectTransform.rect;

        int index = 0;
        foreach (Plateau plateau in Ranked())
        {
            while (labels.Count <= index) labels.Add(CreateLabel());
            TextMeshProUGUI label = labels[index];
            label.gameObject.SetActive(true);
            label.text = Mathf.RoundToInt(plateau.bpm * scale).ToString();
            label.rectTransform.anchoredPosition =
                new Vector2(r.xMin + axisWidth * 0.5f, PlaceY(r, plateau.bpm * scale));
            index++;
        }

        for (int i = index; i < labels.Count; i++)
            if (labels[i] != null) labels[i].gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateLabel()
    {
        var go = new GameObject("BpmTick", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(rectTransform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(axisWidth, 13f);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.fontSize = 11f;
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.color = new Color(lineColour.r, lineColour.g, lineColour.b, 0.95f);
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private void AddSegment(VertexHelper vh, Rect r, int fromIndex, int toIndex)
    {
        Vector2 a = new Vector2(PlaceX(r, fromIndex), PlaceY(r, curve[fromIndex] * scale));
        Vector2 b = new Vector2(PlaceX(r, toIndex), PlaceY(r, curve[toIndex] * scale));
        Vector2 direction = b - a;
        if (direction.sqrMagnitude < 1e-4f) return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        Color clear = new Color(lineColour.r, lineColour.g, lineColour.b, 0f);

        // 兩側淡出。這條線很細又常常是斜的，硬邊的階梯比線本身還顯眼。
        int first = vh.currentVertCount;
        AddVertex(vh, a - normal, clear);
        AddVertex(vh, a, lineColour);
        AddVertex(vh, b, lineColour);
        AddVertex(vh, b - normal, clear);
        AddVertex(vh, a + normal, clear);
        AddVertex(vh, b + normal, clear);

        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
        vh.AddTriangle(first + 1, first + 4, first + 5);
        vh.AddTriangle(first + 5, first + 2, first + 1);
    }

    /// <summary>A run of short segments with gaps between them.</summary>
    private static void Dashed(VertexHelper vh, Vector2 from, Vector2 to, float width, Color colour)
    {
        const float Dash = 4f;
        const float Gap = 3f;
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 1f) return;

        Vector2 step = delta / length;
        Vector2 normal = new Vector2(-step.y, step.x) * (width * 0.5f);
        for (float at = 0f; at < length; at += Dash + Gap)
        {
            Vector2 a = from + step * at;
            Vector2 b = from + step * Mathf.Min(length, at + Dash);
            int first = vh.currentVertCount;
            AddVertex(vh, a - normal, colour);
            AddVertex(vh, a + normal, colour);
            AddVertex(vh, b + normal, colour);
            AddVertex(vh, b - normal, colour);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }
    }

    private float PlaceX(Rect r, int index)
    {
        float u = index / (ChartAnalysis.BucketCount - 1f);
        return Mathf.Lerp(r.xMin + axisWidth, r.xMax, u);
    }

    private float PlaceY(Rect r, float bpm)
    {
        return Mathf.Lerp(r.yMin, r.yMax, Mathf.InverseLerp(low, high, bpm));
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
