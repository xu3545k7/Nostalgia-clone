using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole chart at a glance: six measures on a radar, each scaled so that
/// the marked ring is the level at which that measure is worth naming.
/// </summary>
/// <remarks>
/// **Why the outer ring means something.** A radar normalised to "the largest
/// value in this chart" makes every chart look equally extreme, and one
/// normalised to an invented maximum makes the shape meaningless. The rings
/// here are multiples of the vocabulary's own thresholds, which are the 80th
/// percentile of the library -- so a vertex on the bright ring says "this chart
/// earns that word", and the same shape means the same thing on every song.
///
/// **Why six axes and not nine.** Leaps and octaves are the same question asked
/// twice, as are runs, arpeggios and trills. Given a spoke each they dilute one
/// another and the polygon collapses towards the centre, so each group
/// contributes its strongest member on one spoke instead.
/// </remarks>
public sealed class ChartRadarGraphic : MaskableGraphic
{
    private const int Axes = 6;
    private const float LabelReach = 1.20f;

    private readonly List<ChartAnalysisVocabulary.RadarAxis> traits =
        new List<ChartAnalysisVocabulary.RadarAxis>(Axes);
    private readonly float[] scores = new float[Axes];
    private readonly float[] peakScores = new float[Axes];
    private readonly float[] quietScores = new float[Axes];
    private bool hasPeak;
    private bool hasQuiet;

    [Tooltip("最難的十二秒。")]
    [SerializeField] private Color peakColour = new Color(0.93f, 0.31f, 0.26f, 1f);
    [Tooltip("最輕的十二秒。")]
    [SerializeField] private Color quietColour = new Color(0.36f, 0.60f, 0.93f, 1f);
    private TextMeshProUGUI[] labels;
    private TextMeshProUGUI[] legendLabels;
    private bool hasData;

    [Tooltip("這張雷達圖的墨色。網、填色、標籤都從它推出來。")]
    [SerializeField] private Color ink = new Color(0.98f, 0.82f, 0.48f, 1f);

    /// <summary>
    /// The one colour the whole chart is derived from.
    /// </summary>
    /// <remarks>
    /// The radar appears on the dark hover card and again on the score book's
    /// cream paper. Six separate colour fields would have to be set correctly in
    /// two places; one ink cannot be set half way.
    /// </remarks>
    public Color Ink
    {
        get => ink;
        set
        {
            if (ink == value) return;
            ink = value;
            LayOutLabels();
            SetVerticesDirty();
        }
    }

    public void SetData(ChartAnalysis analysis)
    {
        traits.Clear();
        ChartAnalysisVocabulary.CollectRadar(analysis, traits);
        hasData = traits.Count == Axes;
        if (hasData)
            for (int i = 0; i < Axes; i++)
                scores[i] = traits[i].Fraction;

        hasPeak = hasData && Fill(analysis?.peakSection, peakScores);
        hasQuiet = hasData && Fill(analysis?.quietSection, quietScores);

        LayOutLabels();
        SetVerticesDirty();
    }

    /// <summary>Plots one section through the same axes as the chart itself.</summary>
    private bool Fill(ChartAnalysis section, float[] into)
    {
        if (section == null) return false;

        var axes = new List<ChartAnalysisVocabulary.RadarAxis>(Axes);
        ChartAnalysisVocabulary.CollectRadar(section, axes);
        if (axes.Count != Axes) return false;

        for (int i = 0; i < Axes; i++) into[i] = axes[i].Fraction;
        return true;
    }

    public void Clear()
    {
        hasData = false;
        hasPeak = false;
        hasQuiet = false;
        if (labels != null)
            foreach (TextMeshProUGUI label in labels)
                if (label != null) label.gameObject.SetActive(false);
        if (legendLabels != null)
            foreach (TextMeshProUGUI label in legendLabels)
                if (label != null) label.gameObject.SetActive(false);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasData) return;

        Measure(out Vector2 centre, out float radius, out float legendY);
        if (radius < 12f) return;
        Color web = new Color(ink.r, ink.g, ink.b, 0.24f);
        Color mark = new Color(ink.r, ink.g, ink.b, 0.60f);

        // 三圈各自是曲庫的中位數、第 80、第 95 百分位。第 80 那圈畫得亮 ——
        // 它就是標籤列會把這個詞說出來的那條線。
        DrawRing(vh, centre, radius * ChartAnalysisVocabulary.RadarAxis.MedianRadius, 1f, web);
        DrawRing(vh, centre, radius * ChartAnalysisVocabulary.RadarAxis.NotableRadius, 1.4f, mark);
        DrawRing(vh, centre, radius, 1f, web);

        for (int i = 0; i < Axes; i++)
            AddLine(vh, centre, centre + Direction(i) * radius, 1f, web);

        // 三層：最輕的段落在最底，整首在中間，最難的段落在最上面。中心一律透明，
        // 所以每一層只在自己的外緣附近有顏色，疊起來還讀得出彼此。
        if (hasQuiet) DrawLayer(vh, centre, radius, quietScores, quietColour, 0.22f, 1.4f);
        DrawLayer(vh, centre, radius, scores, ink, 0.20f, 1.8f);
        if (hasPeak) DrawLayer(vh, centre, radius, peakScores, peakColour, 0.26f, 1.4f);

        if (HasLegend) DrawLegendRules(vh, legendY);
    }

    /// <summary>The short coloured rule in front of each key caption.</summary>
    private void DrawLegendRules(VertexHelper vh, float legendY)
    {
        Rect r = rectTransform.rect;
        int shown = 1 + (hasQuiet ? 1 : 0) + (hasPeak ? 1 : 0);
        int placed = 0;
        for (int slot = 0; slot < 3; slot++)
        {
            if (slot == 0 && !hasQuiet) continue;
            if (slot == 2 && !hasPeak) continue;

            float slotWidth = r.width / shown;
            float slotCentre = r.xMin + slotWidth * (placed + 0.5f);
            Color colour = LegendColour(slot);
            AddLine(vh, new Vector2(slotCentre - 20f, legendY),
                new Vector2(slotCentre - 6f, legendY), 2f,
                new Color(colour.r, colour.g, colour.b, 0.92f));
            placed++;
        }
    }

    /// <summary>
    /// One polygon, drawn as a fan whose centre vertex is fully transparent.
    /// </summary>
    /// <remarks>
    /// The layers overlap by design -- the busiest twelve seconds contains the
    /// average -- so a solid fill would leave only the topmost one visible. A
    /// transparent centre turns each into a band that fades inwards, and three
    /// of them read as three shapes rather than as one.
    /// </remarks>
    private void DrawLayer(VertexHelper vh, Vector2 centre, float radius, float[] values,
        Color colour, float fillAlpha, float edgeWidth)
    {
        Color hollow = new Color(colour.r, colour.g, colour.b, 0f);
        Color fill = new Color(colour.r, colour.g, colour.b, fillAlpha);
        Color edge = new Color(colour.r, colour.g, colour.b, 0.92f);

        var points = new Vector2[Axes];
        for (int i = 0; i < Axes; i++)
            points[i] = centre + Direction(i) * (radius * values[i]);

        int first = vh.currentVertCount;
        AddVertex(vh, centre, hollow);
        for (int i = 0; i < Axes; i++) AddVertex(vh, points[i], fill);
        for (int i = 0; i < Axes; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % Axes);

        for (int i = 0; i < Axes; i++)
            AddLine(vh, points[i], points[(i + 1) % Axes], edgeWidth, edge);

        // 頂點的點：落在很低的軸還看得出它在那裡，不會被壓成一條線。
        for (int i = 0; i < Axes; i++)
            AddDiamond(vh, points[i], 2.4f, edge);
    }

    /// <summary>Straight up for the first axis, then clockwise.</summary>
    private static Vector2 Direction(int index)
    {
        float angle = Mathf.PI * 0.5f - index / (float)Axes * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private void LayOutLabels()
    {
        if (!hasData)
        {
            Clear();
            return;
        }

        if (labels == null) labels = new TextMeshProUGUI[Axes];
        Measure(out Vector2 centre, out float radius, out float legendY);

        for (int i = 0; i < Axes; i++)
        {
            if (labels[i] == null) labels[i] = CreateLabel("Axis" + i);
            TextMeshProUGUI label = labels[i];
            label.gameObject.SetActive(true);
            label.text = traits[i].name;
            // 達到門檻的軸點亮，其餘留在暗處 —— 和標籤列說的是同一件事。
            label.color = traits[i].Notable
                ? new Color(ink.r, ink.g, ink.b, 1f)
                : new Color(ink.r, ink.g, ink.b, 0.48f);
            ClassicalBookUITheme.ApplyLocalizedFont(label);
            label.rectTransform.anchoredPosition = centre + Direction(i) * (radius * LabelReach);
        }

        LayOutLegend(legendY);
    }

    /// <summary>
    /// Where the web sits and how much room the key underneath it takes.
    /// </summary>
    /// <remarks>
    /// Both the mesh and the labels need these three numbers, and they have to
    /// be the same three: computed twice, a change to the radius moved the web
    /// without moving its captions.
    /// </remarks>
    private void Measure(out Vector2 centre, out float radius, out float legendY)
    {
        Rect r = rectTransform.rect;
        float reserved = HasLegend ? 20f : 0f;
        centre = new Vector2(r.center.x, r.center.y + reserved * 0.5f);
        radius = Mathf.Min(r.width, r.height - reserved) * 0.5f - 16f;
        legendY = r.yMin + 10f;
    }

    private bool HasLegend => hasData && (hasPeak || hasQuiet);

    /// <summary>
    /// The key: which colour is the lightest stretch, the whole chart and the
    /// heaviest.
    /// </summary>
    /// <remarks>
    /// Three overlapping outlines are unreadable without it -- red over gold
    /// over blue says nothing on its own -- and the entries are built from the
    /// same flags that decide whether each layer is drawn, so the key can never
    /// name a shape that is not there.
    /// </remarks>
    private void LayOutLegend(float legendY)
    {
        if (legendLabels == null) legendLabels = new TextMeshProUGUI[3];
        Rect r = rectTransform.rect;

        int shown = 0;
        for (int slot = 0; slot < 3; slot++)
        {
            if (slot == 0 && !hasQuiet) continue;
            if (slot == 2 && !hasPeak) continue;
            shown++;
        }

        int placed = 0;
        for (int slot = 0; slot < 3; slot++)
        {
            bool visible = HasLegend && (slot == 1 || (slot == 0 && hasQuiet) || (slot == 2 && hasPeak));
            if (legendLabels[slot] == null)
            {
                if (!visible) continue;
                legendLabels[slot] = CreateLabel("Legend" + slot);
                legendLabels[slot].fontSize = 9.5f;
                legendLabels[slot].rectTransform.sizeDelta = new Vector2(40f, 13f);
            }

            legendLabels[slot].gameObject.SetActive(visible);
            if (!visible) continue;

            float slotWidth = r.width / shown;
            float slotCentre = r.xMin + slotWidth * (placed + 0.5f);
            legendLabels[slot].text = LegendText(slot);
            legendLabels[slot].color = LegendColour(slot);
            ClassicalBookUITheme.ApplyLocalizedFont(legendLabels[slot]);
            legendLabels[slot].rectTransform.anchoredPosition =
                new Vector2(slotCentre + 6f, legendY);
            placed++;
        }
    }

    private string LegendText(int slot)
    {
        if (slot == 0) return Localize.T("最輕", "最轻", "Lightest");
        if (slot == 2) return Localize.T("最重", "最重", "Heaviest");
        return Localize.T("平均", "平均", "Average");
    }

    private Color LegendColour(int slot)
    {
        if (slot == 0) return quietColour;
        if (slot == 2) return peakColour;
        return ink;
    }

    private TextMeshProUGUI CreateLabel(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(rectTransform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(52f, 15f);

        var label = go.GetComponent<TextMeshProUGUI>();
        label.fontSize = 10.5f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    // ------------------------------------------------------------ drawing --

    private static void DrawRing(VertexHelper vh, Vector2 centre, float radius,
        float thickness, Color colour)
    {
        for (int i = 0; i < Axes; i++)
            AddLine(vh, centre + Direction(i) * radius,
                centre + Direction((i + 1) % Axes) * radius, thickness, colour);
    }

    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color colour)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1e-4f) return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);

        int first = vh.currentVertCount;
        AddVertex(vh, from - normal, colour);
        AddVertex(vh, from + normal, colour);
        AddVertex(vh, to + normal, colour);
        AddVertex(vh, to - normal, colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddDiamond(VertexHelper vh, Vector2 centre, float size, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, new Vector2(centre.x, centre.y + size), colour);
        AddVertex(vh, new Vector2(centre.x + size, centre.y), colour);
        AddVertex(vh, new Vector2(centre.x, centre.y - size), colour);
        AddVertex(vh, new Vector2(centre.x - size, centre.y), colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
