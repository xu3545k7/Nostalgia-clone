using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The card that floats out of a difficulty bookmark: what that chart is made
/// of, and what kind of difficulty it is.
/// </summary>
/// <remarks>
/// Lives on the focused song card so it inherits the card's pose, and covers the
/// cover art rather than spilling over the neighbouring card -- the bookmark is a
/// tab, so opening it should reveal the page it marks.
/// </remarks>
public sealed class ChartAnalysisPanel
{
    // 右邊多出來的 142px 是雷達圖那一欄。文字欄維持原來的寬度，所以既有的
    // 換行和字級都不用重新排。
    private const float PanelWidth = 486f;
    private const float TextRightEdge = -154f;
    private const float PanelHeight = 246f;
    private const float BookmarkGap = 12f;
    private const float FadeSpeed = 7f;

    private static readonly string[] PitchNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    private readonly StringBuilder builder = new StringBuilder(256);

    private RectTransform root;
    private RectTransform card;
    private CanvasGroup group;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI statsLabel;
    private TextMeshProUGUI tagLabel;
    private ChartDensityGraphic graph;
    private ChartRadarGraphic radar;
    private bool wantVisible;

    public bool IsBuilt => root != null;
    /// <summary>The chart the panel is currently showing, so a late analysis can be discarded.</summary>
    public string ShownChartFileName { get; private set; }

    // ---- construction -----------------------------------------------------

    public void Build(RectTransform songCard)
    {
        if (songCard == null || IsBuilt) return;
        card = songCard;

        root = CreateChild(card, "ChartAnalysisPanel");
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        // The panel starts at the bookmark's right edge and grows to the right.
        root.pivot = new Vector2(0f, 0.5f);
        root.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var background = root.gameObject.AddComponent<Image>();
        background.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        background.type = Image.Type.Tiled;
        background.color = new Color(0.12f, 0.055f, 0.032f, 0.985f);
        background.raycastTarget = false;
        var outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(ClassicalBookUITheme.Gold.r, ClassicalBookUITheme.Gold.g,
            ClassicalBookUITheme.Gold.b, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        var shadow = root.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(9f, -9f);

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        // Purely informational, so it must never eat a click meant for a bookmark.
        group.blocksRaycasts = false;
        group.interactable = false;

        titleLabel = AddLabel("Title", new Vector2(12f, -10f), new Vector2(TextRightEdge, -36f),
            19f, FontStyles.Bold, ClassicalBookUITheme.Gold, TextAlignmentOptions.Left);

        RectTransform graphRoot = CreateChild(root, "Density");
        graphRoot.anchorMin = new Vector2(0f, 1f);
        graphRoot.anchorMax = new Vector2(1f, 1f);
        graphRoot.pivot = new Vector2(0.5f, 1f);
        graphRoot.offsetMin = new Vector2(12f, 0f);
        graphRoot.offsetMax = new Vector2(TextRightEdge, 0f);
        graphRoot.anchoredPosition = new Vector2(0f, -178f);
        graphRoot.sizeDelta = new Vector2(12f + TextRightEdge, 60f);
        // Graphic.UpdateGeometry() requires this to exist before the Graphic is
        // enabled. AddComponent<ChartDensityGraphic>() invokes OnEnable
        // immediately, so adding it afterwards is already too late.
        graphRoot.gameObject.AddComponent<CanvasRenderer>();
        graph = graphRoot.gameObject.AddComponent<ChartDensityGraphic>();
        graph.raycastTarget = false;

        // Four lines at 14.5pt need ~78px. The first draft gave them 62 and
        // let TMP overflow, which drew the range line on top of the tags.
        // Five lines at 14.5pt need ~98px. The first draft gave four lines 62 and
        // let TMP overflow, which drew the range line on top of the tags.
        statsLabel = AddLabel("Stats", new Vector2(12f, -40f), new Vector2(TextRightEdge, -142f),
            14.5f, FontStyles.Normal, ClassicalBookUITheme.ParchmentLight, TextAlignmentOptions.TopLeft);
        statsLabel.lineSpacing = 0f;

        tagLabel = AddLabel("Tags", new Vector2(12f, -144f), new Vector2(TextRightEdge, -172f),
            15f, FontStyles.Bold, new Color(0.91f, 0.77f, 0.46f, 1f), TextAlignmentOptions.Left);

        RectTransform radarRoot = CreateChild(root, "Radar");
        radarRoot.anchorMin = new Vector2(1f, 1f);
        radarRoot.anchorMax = new Vector2(1f, 1f);
        radarRoot.pivot = new Vector2(1f, 1f);
        radarRoot.anchoredPosition = new Vector2(-8f, -28f);
        radarRoot.sizeDelta = new Vector2(140f, 196f);
        // 同上：Graphic 一被啟用就要有 CanvasRenderer，AddComponent 之後才補就晚了。
        radarRoot.gameObject.AddComponent<CanvasRenderer>();
        radar = radarRoot.gameObject.AddComponent<ChartRadarGraphic>();
        radar.raycastTarget = false;

        root.gameObject.SetActive(false);
    }

    private TextMeshProUGUI AddLabel(string name, Vector2 topLeft, Vector2 bottomRight,
        float size, FontStyles style, Color colour, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateChild(root, name);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(topLeft.x, bottomRight.y);
        rect.offsetMax = new Vector2(bottomRight.x, topLeft.y);

        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = size;
        label.fontStyle = style;
        label.color = colour;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform CreateChild(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    // ---- presentation -----------------------------------------------------

    /// <summary>Opens the panel next to a bookmark while its chart is still being read.</summary>
    public void ShowLoading(string title, Color titleColour, string chartFileName, RectTransform bookmark)
    {
        if (!IsBuilt) return;
        ShownChartFileName = chartFileName;
        wantVisible = true;
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();

        titleLabel.color = titleColour;
        titleLabel.text = title ?? string.Empty;
        ClassicalBookUITheme.ApplyLocalizedFont(titleLabel);
        statsLabel.text = Localize.T("讀取譜面中…", "读取谱面中…", "Reading chart…");
        ClassicalBookUITheme.ApplyLocalizedFont(statsLabel);
        tagLabel.text = string.Empty;
        graph.Clear();
        radar.Clear();

        AlignTo(bookmark);
    }

    /// <summary>Places the panel immediately to the right of its difficulty bookmark.</summary>
    private void AlignTo(RectTransform bookmark)
    {
        float y = 0f;
        float x = card != null ? card.rect.xMax + BookmarkGap : BookmarkGap;
        if (bookmark != null && card != null)
        {
            var corners = new Vector3[4];
            bookmark.GetWorldCorners(corners);
            Vector3 centre = card.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);
            float right = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
                right = Mathf.Max(right, card.InverseTransformPoint(corners[i]).x);
            x = right + BookmarkGap;
            y = centre.y;
        }
        float limit = Mathf.Max(0f, card.rect.height * 0.5f - PanelHeight * 0.5f - 12f);
        root.anchoredPosition = new Vector2(x, Mathf.Clamp(y, -limit, limit));
    }

    public void SetAnalysis(ChartAnalysis analysis)
    {
        if (!IsBuilt || analysis == null) return;

        graph.SetData(analysis.leftDensity, analysis.rightDensity, analysis.peakBucketTotal);
        radar.SetData(analysis);

        builder.Clear();
        builder.Append(Localize.T("音符", "音符", "Notes")).Append("  ")
            .Append(analysis.noteCount.ToString("N0"));
        if (analysis.holdCount > 0)
            builder.Append("    ").Append(Localize.T("長押", "长按", "Holds")).Append(' ')
                .Append(analysis.holdCount.ToString("N0"));
        builder.Append('\n');

        builder.Append(Localize.T("密度", "密度", "Density")).Append("  ")
            .Append(analysis.averageDensity.ToString("0.0")).Append("/s    ")
            .Append(Localize.T("峰值", "峰值", "Peak")).Append(' ')
            .Append(analysis.peakDensity.ToString("0")).Append("/s\n");

        builder.Append(Localize.T("和弦", "和弦", "Chord")).Append("  ")
            .Append(analysis.NotesPerOnset.ToString("0.00")).Append(' ')
            .Append(Localize.T("音", "音", "notes")).Append("    ")
            .Append(Localize.T("右", "右", "R")).Append(' ')
            .Append((analysis.RightHandRatio * 100f).ToString("0")).Append("% / ")
            .Append(Localize.T("左", "左", "L")).Append(' ')
            .Append(((1f - analysis.RightHandRatio) * 100f).ToString("0")).Append('%');

        // Plenty of converted charts carry no pitch at all, and an empty range
        // line reads as a bug rather than as missing data.
        if (analysis.HasPitchData)
            builder.Append('\n').Append(Localize.T("音域", "音域", "Range")).Append("  ")
                .Append(DescribePitch(analysis.lowestPitch)).Append(" – ")
                .Append(DescribePitch(analysis.highestPitch));

        AppendTechniqueLine(builder, analysis);

        statsLabel.text = builder.ToString();
        ClassicalBookUITheme.ApplyLocalizedFont(statsLabel);

        tagLabel.text = ChartAnalysisVocabulary.DescribeCharacter(analysis);
        ClassicalBookUITheme.ApplyLocalizedFont(tagLabel);
    }

    /// <summary>
    /// Shows the three strongest techniques with their numbers, whether or not
    /// they clear the threshold: "no leaps at all" is information too.
    /// </summary>
    private static void AppendTechniqueLine(StringBuilder target, ChartAnalysis analysis)
    {
        if (!analysis.hasTechniqueData) return;
        target.Append('\n').Append(Localize.T("技法", "技法", "Technique")).Append("  ")
            .Append(ChartAnalysisVocabulary.DescribeTechniques(analysis));
    }

    private static string DescribePitch(int midi)
    {
        if (midi <= 0) return "—";
        return PitchNames[midi % 12] + (midi / 12 - 1).ToString();
    }

    /// <summary>Shown when the chart file cannot be read, so the panel never sticks on "reading".</summary>
    public void SetUnavailable()
    {
        if (!IsBuilt) return;
        graph.Clear();
        radar.Clear();
        statsLabel.text = Localize.T("讀不到譜面", "读不到谱面", "Chart unavailable");
        ClassicalBookUITheme.ApplyLocalizedFont(statsLabel);
        tagLabel.text = string.Empty;
    }

    /// <summary>
    /// Closes the panel.  Pass <paramref name="immediate"/> when the fade will
    /// not get to run -- starting a song stops the selection screen updating, so
    /// a fading panel would otherwise still be there on the way back.
    /// </summary>
    public void Hide(bool immediate = false)
    {
        wantVisible = false;
        ShownChartFileName = null;
        if (!immediate || !IsBuilt) return;
        if (group != null) group.alpha = 0f;
        root.gameObject.SetActive(false);
    }

    /// <summary>Fades the panel and retires it once it is fully out of the way.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsBuilt || group == null) return;
        if (!wantVisible && !root.gameObject.activeSelf) return;

        float target = wantVisible ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, FadeSpeed * deltaTime);
        if (!wantVisible && group.alpha <= 0.001f) root.gameObject.SetActive(false);
    }
}
