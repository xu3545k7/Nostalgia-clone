using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選曲畫面左下角那一條檢定：一枚朱印、目前的級數與等第、下一步。按了翻開考程。
/// </summary>
/// <remarks>
/// **為什麼在左下，而且只有一行高。** 它以前是報頭那一排的第五顆按鈕，和「音樂類
/// 型」「‹ ›」「演奏會」擠在同一條線上 —— 但那一排講的是「你在挑哪一首歌」，而檢定
/// 講的是「你走到哪」。簾桿上線之後，「你是誰」整組搬到桿上，挑歌的東西留在簾下，
/// 而進度這種「隨時看得到、偶爾才點」的東西落到畫面最下緣的一條裡。一行的高度是刻
/// 意的：它要一直在，但不該佔位置。
///
/// **為什麼印章是主角。** 一行 64 高的條上，字和字之間沒有層級可言；一枚朱紅的圓印
/// 在一整排深銅裡是畫面上唯一的那個顏色，掃過去就找得到。級數和等第都畫在印上（見
/// <see cref="GradeSealGraphic"/>），所以那枚印本身就是狀態。
///
/// **它和右下那條共用同一支建構子**（<see cref="SelectionRail.Plate"/>）。兩條並排
/// 在最下緣，各自畫一份的話遲早會漂開。
/// </remarks>
public sealed class SyllabusPlaque : MonoBehaviour
{
    private const float Width = 536f;

    private static readonly Color Ink = new Color(0.941f, 0.894f, 0.800f, 1f);
    private static readonly Color InkDim = new Color(0.729f, 0.659f, 0.533f, 1f);

    private float textCaption;
    private float textBody;
    private float textLabel;

    private GradeSealGraphic sealArt;
    private TextMeshProUGUI sealDigit;
    private TextMeshProUGUI headline;
    private TextMeshProUGUI next;

    public static SyllabusPlaque Attach(RectTransform panel)
    {
        if (panel == null) return null;
        SyllabusPlaque existing = panel.GetComponentInChildren<SyllabusPlaque>(true);
        if (existing != null) return existing;

        var host = new GameObject("SyllabusEntry", typeof(RectTransform));
        host.layer = panel.gameObject.layer;
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        SyllabusPlaque entry = host.AddComponent<SyllabusPlaque>();
        entry.Build(rect);
        return entry;
    }

    private void Build(RectTransform root)
    {
        // 和右下那條、和練習室共用同一張字級表（見 ClassicalType）。
        textCaption = ClassicalType.On(this, ClassicalType.Caption);
        textBody = ClassicalType.On(this, ClassicalType.Body);
        textLabel = ClassicalType.On(this, ClassicalType.Label);

        // 放進頂部導覽列那一排，接在「前往練習模式」外側；右邊的評分明細對稱。
        // 寬度用 SelectionRail.NavPlaqueWidth 而不是自己的 Width —— 桿上的空檔比
        // 這塊牌原本的寬度窄，見那邊的說明。
        Button strip = SelectionRail.Plate(root, "Certification", new Vector2(0f, 1f),
            new Vector2(SelectionRail.NavPlaqueInset, -SelectionRail.NavPlaqueDrop),
            new Vector2(SelectionRail.NavPlaqueWidth, SelectionRail.FootHeight));
        strip.onClick.AddListener(() =>
        {
            UiSfx.Open();
            // 檢定住在演奏廳，所以這塊牌子直接開檢定考場；整本帳在考場底下的
            // 「翻開考程」後面。帳本不該是入口。
            SyllabusExamHall.OpenGrade();
        });
        RectTransform rect = strip.GetComponent<RectTransform>();

        var sealObject = new GameObject("Seal", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(GradeSealGraphic));
        sealObject.layer = root.gameObject.layer;
        var sealRect = sealObject.GetComponent<RectTransform>();
        sealRect.SetParent(rect, false);
        sealRect.anchorMin = sealRect.anchorMax = new Vector2(0f, 0.5f);
        sealRect.pivot = new Vector2(0f, 0.5f);
        sealRect.anchoredPosition = new Vector2(12f, 0f);
        sealRect.sizeDelta = new Vector2(44f, 44f);
        sealArt = sealObject.GetComponent<GradeSealGraphic>();
        sealArt.raycastTarget = false;

        sealDigit = Label(sealRect, "Digit", string.Empty, textBody,
            new Color(1f, 0.95f, 0.90f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        sealDigit.rectTransform.anchorMin = Vector2.zero;
        sealDigit.rectTransform.anchorMax = Vector2.one;
        sealDigit.rectTransform.offsetMin = new Vector2(4f, 4f);
        sealDigit.rectTransform.offsetMax = new Vector2(-4f, -4f);

        // 字級和右下那條同一套（見 SelectionRail 的字級表）。
        TextMeshProUGUI caption = Label(rect, "Caption", Localize.S("檢定"), textLabel, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(caption.rectTransform, new Vector2(0f, 0.5f), new Vector2(68f, 0f),
            new Vector2(90f, 36f));

        headline = Label(rect, "Headline", string.Empty, textBody, InkDim,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(headline.rectTransform, new Vector2(0f, 0.5f), new Vector2(166f, 0f),
            new Vector2(190f, 30f));

        next = Label(rect, "Next", string.Empty, textCaption, InkDim,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(next.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f),
            new Vector2(300f, 28f));

        Refresh();
        SyllabusProgress.Changed += Refresh;
    }

    private void OnDestroy() => SyllabusProgress.Changed -= Refresh;

    private void OnEnable() => Refresh();

    /// <summary>
    /// 這一條印的是演奏廳那一條刻度（檢定）。
    /// </summary>
    /// <remarks>
    /// 技階是練習室的事，那裡有自己的兩枚記號。按下去翻開的是同一本考程，兩條階梯
    /// 都在上面。
    /// </remarks>
    private void Refresh()
    {
        if (headline == null) return;
        int grade = SyllabusProgress.Grade;
        headline.text = SyllabusProgress.Headline(true);
        next.text = SyllabusProgress.NextStep(true);
        if (sealArt == null) return;
        sealArt.Grade = grade;
        sealArt.Seal = SyllabusLadder.SealRank(SyllabusProgress.Seal);
        sealDigit.text = grade > 0 ? grade.ToString() : string.Empty;
    }

    private static void Place(RectTransform rect, Vector2 corner, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, string text,
        float size, Color colour, TextAlignmentOptions alignment,
        FontStyles style = FontStyles.Normal)
    {
        var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = parent.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(200f, size + 10f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.fontStyle = style;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // TMP 在「框高 < 字級 × 1.5」時整段不畫。
        label.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }
}
