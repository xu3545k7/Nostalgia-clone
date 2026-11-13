using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 改一階（或一級）：等級帶、門檻、以及那幾格各要考哪一首。
/// </summary>
/// <remarks>
/// **為什麼是一列一張卡，不是一整頁表格。** 十八列每列六到八個欄位，排成表格是一百
/// 多個控制項擠在一頁上，而作者一次只會改一列。書留著當清單（它本來就是），改的時候
/// 才開一張卡 —— 卡上只有這一列的東西，每個欄位都放得下它的單位和它的現值。
///
/// **為什麼是加減鈕不是輸入框。** 這裡每一個數字都是小整數（等級 1–20、門檻 0–100、
/// 容錯率 0.1–10%），而輸入框要處理空字串、負號、全形數字、以及「打到一半的值算不
/// 算數」。加減鈕沒有中間狀態，按一下就是一個合法值，也不必再寫驗證。
///
/// **為什麼改完是釘住而不是直接寫進考卷。** 曲目存在 <see cref="SyllabusOverrides"/>
/// 的釘住清單裡，抽課時先放釘住的、再補剩下的（見 <see cref="SyllabusCourses"/>）。
/// 直接寫進考卷的話，曲庫一變、或者別的地方重編一次，改過的東西就沒了；而且「釘住
/// 兩格同一首歌」這種事會靜靜地讓那一列再也湊不滿。
///
/// **為什麼「還原」是刪掉整列的覆寫而不是把值填回預設。** 填回預設的話那一列仍然是
/// 「被改過的」，日後調整內建階梯時它不會跟著走。刪掉之後它才真的回到自動。
/// </remarks>
public sealed class SyllabusRowEditor : MonoBehaviour
{
    private static SyllabusRowEditor instance;

    private const float CardWidth = 900f;
    private const float CardHeight = 560f;

    private static readonly Color Paper = new Color(0.93f, 0.89f, 0.74f, 1f);
    private static readonly Color Ink = new Color(0.22f, 0.115f, 0.055f, 1f);
    private static readonly Color InkSoft = new Color(0.36f, 0.24f, 0.13f, 1f);
    private static readonly Color InkFaint = new Color(0.50f, 0.38f, 0.25f, 1f);
    private static readonly Color Vermilion = new Color(0.62f, 0.17f, 0.11f, 1f);

    private bool recital;
    private int index;
    private RectTransform card;

    /// <summary>開一張卡改某一階／某一級。</summary>
    public static void Open(bool recital, int index)
    {
        if (instance != null) Destroy(instance.gameObject);
        var root = new GameObject("SyllabusRowEditor");
        instance = root.AddComponent<SyllabusRowEditor>();
        instance.recital = recital;
        instance.index = index;
        instance.Build();
    }

    public static bool IsOpen => instance != null;

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 比考程書高一層：它是從書上開出來的。
        canvas.sortingOrder = 26100;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        Image shade = Img(transform, "Shade", new Color(0f, 0f, 0f, 0.55f));
        Stretch(shade.rectTransform);
        var dismiss = shade.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(Close);

        card = Rect(transform, "Card", new Vector2(CardWidth, CardHeight), Vector2.zero);
        Image face = card.gameObject.AddComponent<Image>();
        face.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        face.type = Image.Type.Tiled;
        face.color = Paper;
        face.raycastTarget = true;          // 卡自己擋住射線，點卡面不會關掉
        PageRuleGraphic rule = PageRuleGraphic.Attach(card, 17,
            new Color(0.22f, 0.115f, 0.05f, 0.85f), 0.030f, 3.4f, true, 1);
        if (rule != null) rule.Wear = 0.22f;

        Rebuild();
    }

    private void Rebuild()
    {
        // OnEnable（訂閱）比 Build（建卡）早一步，中間來一發 Changed 就會踩到 null。
        if (card == null) return;
        for (int i = card.childCount - 1; i >= 0; i--)
        {
            Transform child = card.GetChild(i);
            if (child.name != "Body") continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        RectTransform body = Rect(card, "Body", Vector2.zero, Vector2.zero);
        Stretch(body);
        if (recital) BuildGrade(body);
        else BuildStage(body);

        // 底下兩顆：還原這一列、完成。
        Button reset = Btn(body, Localize.S("還原自動編定"), new Vector2(0f, 0f), new Vector2(44f, 34f),
            new Vector2(220f, 52f));
        reset.onClick.AddListener(() =>
        {
            SyllabusOverrides.ResetRow(recital, index);
            UiSfx.Back();
            Notify();
            Rebuild();
        });
        SetInteractable(reset, SyllabusOverrides.IsTweaked(recital, index));

        Button done = Btn(body, Localize.S("完成"), new Vector2(1f, 0f), new Vector2(-44f, 34f),
            new Vector2(180f, 52f));
        done.onClick.AddListener(Close);
    }

    // ── 技階 ──────────────────────────────────────────────────────────

    private void BuildStage(RectTransform body)
    {
        SyllabusLadder.Stage stage = SyllabusLadder.StageAt(index);
        if (stage == null) return;

        Head(body,
            Localize.SF("技階　{0}階　{1}", SyllabusProgress.Numeral(index), stage.Name),
            Localize.SF("照「{0}」{1}的三首排", SyllabusLadder.MetricName(stage.Key),
                Localize.S(stage.Ascending ? "最低" : "最高")));

        int low = SyllabusOverrides.LowOf(stage);
        int high = SyllabusOverrides.HighOf(stage);
        Field(body, Localize.S("等級帶"), 150f);
        Stepper(body, 200f, 150f, 120f, $"Lv{low}", () => Band(low - 1, high), () => Band(low + 1, high));
        Label(body, "–", 356f, 150f, 30f, 26f, InkSoft, TextAlignmentOptions.Center);
        Stepper(body, 396f, 150f, 120f, high >= 99 ? Localize.S("以上") : $"Lv{high}",
            () => Band(low, high - 1), () => Band(low, high + 1));

        float rate = SyllabusOverrides.RateOf(stage);
        SyllabusCourse course = SyllabusProgress.CourseFor(index);
        int allowance = SyllabusCourses.AllowanceFor(SongSelectionManager.Instance, course);
        Field(body, Localize.S("容錯率"), 214f);
        Stepper(body, 200f, 214f, 150f, (rate * 100f).ToString("0.0") + " %",
            () => SyllabusOverrides.SetRate(index, rate - 0.001f),
            () => SyllabusOverrides.SetRate(index, rate + 0.001f));
        // 比率本身沒有意義，點數才有 —— 三首譜的音符數決定它換算出多少條命。
        Label(body, course != null && course.charts.Count > 0
                ? Localize.SF("目前 {0} 點", allowance) : "—",
            376f, 218f, 200f, 20f, InkFaint, TextAlignmentOptions.MidlineLeft);

        Slots(body, 300f, SyllabusLadder.CourseSize,
            new[] { Localize.S("第一曲"), Localize.S("第二曲"), Localize.S("第三曲") }, course?.charts);
    }

    // ── 檢定 ──────────────────────────────────────────────────────────

    private void BuildGrade(RectTransform body)
    {
        SyllabusLadder.Grade grade = SyllabusLadder.GradeAt(index);
        if (grade == null) return;

        Head(body, Localize.SF("檢定　{0}級", SyllabusProgress.Numeral(index)),
            grade.RequiredStage > 0
                ? Localize.SF("先修 技階{0}階",
                    SyllabusProgress.Numeral(grade.RequiredStage))
                : Localize.S("免先修"));

        int low = SyllabusOverrides.LowOf(grade);
        int high = SyllabusOverrides.HighOf(grade);
        Field(body, Localize.S("等級帶"), 138f);
        Stepper(body, 200f, 138f, 120f, $"Lv{low}", () => Band(low - 1, high), () => Band(low + 1, high));
        Label(body, "–", 356f, 138f, 30f, 26f, InkSoft, TextAlignmentOptions.Center);
        Stepper(body, 396f, 138f, 120f, high >= 99 ? Localize.S("以上") : $"Lv{high}",
            () => Band(low, high - 1), () => Band(low, high + 1));

        SyllabusLadder.Demand demand = SyllabusOverrides.DemandOf(grade);
        var marks = new[]
        {
            demand.Completeness, demand.Melody, demand.Mistakes, demand.Expression, demand.Average
        };
        Field(body, Localize.S("門檻"), 202f);
        // 0 = 這一級不看這一項。五格排兩列，順序和評審報告一致。
        string[] names = { Localize.S("完整"), Localize.S("旋律"), Localize.S("失誤"), Localize.S("情感"), Localize.S("平均") };
        for (int i = 0; i < 5; i++)
        {
            int slot = i;
            float x = 200f + (i % 3) * 230f;
            float y = 202f + (i / 3) * 56f;
            Label(body, names[i], x, y + 6f, 60f, 20f, InkFaint, TextAlignmentOptions.MidlineLeft);
            Stepper(body, x + 62f, y, 140f, marks[i] <= 0f ? Localize.S("不看") : ((int)marks[i]).ToString(),
                () => Mark(marks, slot, -5f), () => Mark(marks, slot, 5f));
        }

        bool needs = SyllabusOverrides.NeedsExpressionOf(grade);
        Button toggle = Btn(body, needs ? Localize.S("✓ 需表情資料") : Localize.S("　 需表情資料"), new Vector2(0f, 1f),
            new Vector2(662f, -258f), new Vector2(200f, 46f));
        toggle.onClick.AddListener(() =>
        {
            SyllabusOverrides.SetExpression(index, !needs);
            UiSfx.Select();
            Notify();
            Rebuild();
        });

        SyllabusPaper paper = SyllabusProgress.PaperFor(index);
        Slots(body, 330f, SyllabusLadder.PaperSize, new[] { Localize.S("技巧曲"), Localize.S("歌唱曲") }, paper?.charts);
    }

    private void Band(int low, int high)
    {
        SyllabusOverrides.SetBand(recital, index, Mathf.Clamp(low, 1, 99), Mathf.Clamp(high, 1, 99));
        UiSfx.Select();
        Notify();
        Rebuild();
    }

    private void Mark(float[] marks, int slot, float delta)
    {
        var next = (float[])marks.Clone();
        next[slot] = Mathf.Clamp(next[slot] + delta, 0f, 100f);
        SyllabusOverrides.SetDemand(index, next);
        UiSfx.Select();
        Notify();
        Rebuild();
    }

    // ── 曲目那幾格 ────────────────────────────────────────────────────

    private void Slots(RectTransform body, float top, int count, string[] names, List<string> charts)
    {
        Field(body, Localize.S("曲目"), top);
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            float y = top + i * 56f;
            Label(body, names[i], 200f, y + 8f, 80f, 20f, InkFaint, TextAlignmentOptions.MidlineLeft);

            string chart = charts != null && slot < charts.Count ? charts[slot] : string.Empty;
            string pin = SyllabusOverrides.PinOf(recital, index, slot);
            bool pinned = !string.IsNullOrEmpty(pin);
            // 釘住的格子印你釘的那一首，不是考卷上的那一首。兩者平常一樣，但這一列
            // 要是因為曲庫湊不滿而整個「尚未開課」，考卷是空的 —— 那時候還印空的話，
            // 你剛剛明明選了一首歌，畫面卻說沒有。
            if (pinned) chart = pin;

            Button pick = Btn(body, NameOf(chart), new Vector2(0f, 1f),
                new Vector2(284f, -y), new Vector2(430f, 46f));
            TextMeshProUGUI label = pick.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                // 曲名是使用者曲庫裡的字，可能是日文。
                ClassicalBookUITheme.ApplyContentFont(label);
                label.color = pinned ? Ink : InkSoft;
                label.fontStyle = pinned ? FontStyles.Bold : FontStyles.Normal;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.offsetMin = new Vector2(12f, label.rectTransform.offsetMin.y);
            }
            pick.onClick.AddListener(() => SyllabusSongPicker.Open(chosen =>
            {
                SyllabusOverrides.SetPin(recital, index, slot, chosen);
                Notify();
                Rebuild();
            }));

            // 釘住的格子旁邊有一顆放開；沒釘的那幾格印「自動」而不是一顆按不下去的鈕。
            if (pinned)
            {
                Button release = Btn(body, Localize.S("放開"), new Vector2(0f, 1f),
                    new Vector2(726f, -y), new Vector2(110f, 46f));
                release.onClick.AddListener(() =>
                {
                    SyllabusOverrides.SetPin(recital, index, slot, string.Empty);
                    UiSfx.Back();
                    Notify();
                    Rebuild();
                });
            }
            else
            {
                Label(body, Localize.S("自動"), 738f, y + 12f, 90f, 20f, InkFaint, TextAlignmentOptions.MidlineLeft);
            }
        }
    }

    private static string NameOf(string chart)
    {
        if (string.IsNullOrEmpty(chart)) return Localize.S("（尚未編定）");
        SongSelectionManager.SongOption option =
            SyllabusCourses.Resolve(SongSelectionManager.Instance, chart);
        if (option == null) return Localize.S("（此譜已不在曲庫）");
        string name = string.IsNullOrWhiteSpace(option.displayName) ? chart : option.displayName;
        return $"{name}　Lv{option.difficultyLevel}";
    }

    /// <summary>
    /// 改完之後卡上的曲名不會自己變 —— 那要等重編。
    /// </summary>
    /// <remarks>
    /// 改一個覆寫只動到 <see cref="SyllabusOverrides"/>；真正決定「這一格考哪一首」
    /// 的是抽課引擎，而它是考程書在跑的（見 <see cref="SyllabusBookOverlay"/>）。所
    /// 以這裡不自己重編，也不用回呼去戳誰：重編完 <c>SyllabusProgress.Changed</c> 會
    /// 響，這張卡跟著重畫。少了這一條，放開一格之後那一格會一直印著剛剛被放開的那
    /// 首歌，直到關掉再開。
    /// </remarks>
    private void Notify() { }

    private void OnEnable() => SyllabusProgress.Changed += Rebuild;

    private void OnDisable() => SyllabusProgress.Changed -= Rebuild;

    private void Close()
    {
        if (instance == this) instance = null;
        UiSfx.Back();
        Destroy(gameObject);
    }

    private void Update()
    {
        if (SyllabusSongPicker.IsOpen) return;
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    // ── 版面小工具 ────────────────────────────────────────────────────

    private void Head(RectTransform body, string title, string note)
    {
        Label(body, title, 44f, 36f, 640f, 34f, Ink, TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        Label(body, note, 44f, 78f, 640f, 22f, InkFaint, TextAlignmentOptions.MidlineLeft);
        if (SyllabusOverrides.IsTweaked(recital, index))
            Label(body, Localize.S("已手改"), -44f, 40f, 160f, 24f, Vermilion, TextAlignmentOptions.MidlineRight,
                FontStyles.Bold, true);
    }

    private void Field(RectTransform body, string name, float top) =>
        Label(body, name, 44f, top + 10f, 140f, 24f, InkSoft, TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

    /// <summary>減、值、加。值本身不能按 —— 它是顯示，不是輸入。</summary>
    private void Stepper(RectTransform body, float x, float y, float width, string value,
        Action back, Action forward)
    {
        Button minus = Btn(body, "−", new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(42f, 40f));
        minus.onClick.AddListener(() => back());
        Label(body, value, x + 46f, y + 8f, width - 92f, 24f, Ink, TextAlignmentOptions.Center,
            FontStyles.Bold);
        Button plus = Btn(body, "＋", new Vector2(0f, 1f),
            new Vector2(x + width - 42f, -y), new Vector2(42f, 40f));
        plus.onClick.AddListener(() => forward());
    }

    private TextMeshProUGUI Label(RectTransform body, string text, float x, float y, float width,
        float size, Color colour, TextAlignmentOptions alignment,
        FontStyles style = FontStyles.Normal, bool fromRight = false)
    {
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = body.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(body, false);
        rect.anchorMin = rect.anchorMax = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.pivot = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, size + 8f);

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

    private Button Btn(RectTransform body, string text, Vector2 corner, Vector2 offset, Vector2 size)
    {
        var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        buttonObject.layer = body.gameObject.layer;
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(body, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        // 和結算頁那幾顆同一套：印在紙上的一個框，不是一塊壓在紙上的深色方塊。底圖
        // 很淡但不是全透明 —— Button 的 highlighted/pressed 是乘在 targetGraphic 的
        // 顏色上的，全透明就沒有東西可乘。
        Image face = buttonObject.GetComponent<Image>();
        face.color = new Color(0.42f, 0.27f, 0.11f, 0.10f);
        PageRuleGraphic rule = PageRuleGraphic.Attach(rect, 41,
            new Color(0.62f, 0.44f, 0.18f, 0.80f), 0.070f, 2.2f, true, 0);
        if (rule != null) rule.Wear = 0.18f;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(2.1f, 1.85f, 1.35f, 1.9f);
        colours.pressedColor = new Color(1.4f, 1.15f, 0.80f, 2.6f);
        button.colors = colours;

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = body.gameObject.layer;
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 2f);
        labelRect.offsetMax = new Vector2(-4f, -2f);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 20f;
        label.color = Ink;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return button;
    }

    private static void SetInteractable(Button button, bool on)
    {
        button.interactable = on;
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = on ? Ink : InkFaint;
    }

    private static Image Img(Transform parent, string name, Color colour)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = parent.gameObject.layer;
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = colour;
        return image;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 offset)
    {
        var rectObject = new GameObject(name, typeof(RectTransform));
        rectObject.layer = parent.gameObject.layer;
        var rect = rectObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
