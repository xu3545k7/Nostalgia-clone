using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 攤開的考程：左頁檢定八級，右頁技階十階。
/// </summary>
/// <remarks>
/// **為什麼兩條階梯共用一本書。** 它們住在兩個房間（技階在練習室、檢定在演奏廳），
/// 而這正是問題：玩家不該為了知道「我在哪」先學會這套系統的房間配置。一本書把兩條
/// 都攤開，而每一列印的是<b>要求</b>加上你的印記 —— 要求不需要任何紀錄就讀得懂，所
/// 以零紀錄的玩家翻開它看到的是一整本課表，不是十八個破折號。
///
/// **為什麼每一列都印真的曲名。** 整本考程在第一次進畫面時就編定完並且寫檔（見
/// <see cref="SyllabusCourses"/>），所以一個還沒打過任何一首的玩家眼前是 46 個他自
/// 己曲庫裡的曲名。這是這套系統對空狀態最大的一筆，而抽課引擎反正得寫，這個效果不
/// 加錢。
///
/// **為什麼列不能按，底下卻有三顆按鈕。** 每一列是帳不是控制台 —— 按一列會讓人以為
/// 按了就開始考試，而實際要彈哪一首是考場的事。但「帳本沒有任何出口」比那更糟：從演奏
/// 廳的牌子翻開這本書之後就是死路，玩家看完十八列卻不知道下一步在哪。所以底下有兩條明
/// 確的路通往兩座考場，按鈕上寫的就是它會做的事。
/// </remarks>
public sealed class SyllabusBookOverlay : MonoBehaviour
{
    private static SyllabusBookOverlay instance;

    private const float BookWidth = 1680f;
    private const float BookHeight = 900f;

    private static readonly Color Ink = new Color(0.22f, 0.115f, 0.055f, 1f);
    private static readonly Color InkSoft = new Color(0.34f, 0.22f, 0.12f, 1f);
    private static readonly Color InkFaint = new Color(0.46f, 0.34f, 0.22f, 1f);
    /// <summary>朱批：已經拿到手的那些印。這本書上唯一的第二種顏色。</summary>
    private static readonly Color Vermilion = new Color(0.62f, 0.17f, 0.11f, 1f);

    private CanvasGroup canvasGroup;
    private RectTransform book;
    private RectTransform leftPage;
    private RectTransform rightPage;
    private TextMeshProUGUI notice;
    private SongSelectionManager manager;
    private TextMeshProUGUI editLabel;
    private Coroutine redrafting;

    /// <summary>
    /// 編輯模式開著時每一列都可以按，按了開那一列的編輯卡。
    /// </summary>
    /// <remarks>
    /// **為什麼要一個開關，不是隨時可按。** 考程的覆寫是<b>全機共用</b>的（見
    /// <see cref="SyllabusOverrides"/>）—— 一個只是來看自己考到幾級的人，不該有機會
    /// 一個手滑就改掉所有人的考題。一顆明確的「編輯考程」把「看」和「改」分開，代價
    /// 是作者每次要多按一下。
    /// </remarks>
    private bool editing;

    public static void Open()
    {
        if (instance != null) Destroy(instance.gameObject);
        var root = new GameObject("SyllabusBookOverlay");
        instance = root.AddComponent<SyllabusBookOverlay>();
        instance.Build();
    }

    private void Build()
    {
        manager = SongSelectionManager.Instance;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 和曲譜書同一層：它們是同一種東西，不該互相蓋。
        canvas.sortingOrder = 26000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        Image shade = AddImage(transform, "RoomShade", new Color(0.008f, 0.006f, 0.008f, 0.92f));
        Stretch(shade.rectTransform);
        // 點暗處關書。這是唯一一個可以按的地方。
        var shadeButton = shade.gameObject.AddComponent<Button>();
        shadeButton.transition = Selectable.Transition.None;
        shadeButton.onClick.AddListener(Close);

        RectTransform shadow = AddRect(transform, "BookShadow",
            new Vector2(BookWidth + 30f, BookHeight + 20f), new Vector2(12f, -16f));
        Image shadowImage = shadow.gameObject.AddComponent<Image>();
        shadowImage.color = new Color(0f, 0f, 0f, 0.72f);
        shadowImage.raycastTarget = false;

        book = AddRect(transform, "OpenSyllabus", new Vector2(BookWidth, BookHeight), Vector2.zero);
        Image leather = book.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.ApplyDarkSurface(leather,
            new Color(0.105f, 0.038f, 0.025f, 1f), ClassicalBookUITheme.Surface.Leather, 2.5f);
        // 書自己擋住射線：暗處的那顆關閉鈕鋪滿整個畫面，書不擋的話點在書上也會關掉。
        leather.raycastTarget = true;

        // 書板與兩頁的做法抄自 SongScoreBookOverlay —— 那裡已經解掉了「攤開的書」的
        // 造型（板子比書芯大一圈、訂口平翻口有弧、兩頁不同種子），重做一遍只會得到
        // 一本看起來不太一樣的書。
        BookBoardGraphic boards = BookBoardGraphic.Attach(book, 7);
        if (boards != null)
        {
            boards.Profile = BookShape.Profile.Volume;
            boards.Set(new Color(0.115f, 0.045f, 0.028f, 1f),
                new Color(0.135f, 0.055f, 0.033f, 1f),
                new Color(0.84f, 0.78f, 0.62f, 1f), ClassicalBookUITheme.Gold);
            Sprite boardGrain = ClassicalBookUITheme.GetBoardTextureSprite();
            if (boardGrain != null) boards.Grain(boardGrain.texture, boardGrain.texture.width);
        }
        AgedPaperGraphic boardWear = AgedPaperGraphic.Attach(book, 7);
        if (boardWear != null)
        {
            boardWear.Shape(BookShape.Profile.Volume);
            boardWear.Stain = new Color(0.36f, 0.22f, 0.13f, 1f);
            boardWear.Strength = 0.62f;
            boardWear.EdgeDepth = 0.055f;
            boardWear.Mottle = 0.32f;
            boardWear.Foxing = 0;
        }

        // 兩頁的下緣收到 0.135，空出一條約 120px 的腰帶給底下那三顆牼。
        // 舊的 0.052 只剩 47px，而牼本身就 46px 高 —— 所以它們是踩在書緣上、
        // 上半被頁面壓著，在畫面上就是「看不到」。
        leftPage = AddPage("LeftPage", new Vector2(0.034f, 0.135f), new Vector2(0.489f, 0.948f));
        rightPage = AddPage("RightPage", new Vector2(0.511f, 0.135f), new Vector2(0.966f, 0.948f));
        Image spine = AddImage(book, "BookSpine", new Color(0.28f, 0.15f, 0.065f, 0.78f));
        spine.rectTransform.anchorMin = new Vector2(0.497f, 0.128f);
        spine.rectTransform.anchorMax = new Vector2(0.503f, 0.955f);
        spine.rectTransform.offsetMin = spine.rectTransform.offsetMax = Vector2.zero;
        spine.raycastTarget = false;

        notice = AddText(book, string.Empty, 20f, InkFaint, FontStyles.Italic,
            TextAlignmentOptions.Center);
        Place(notice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(900f, 28f));

        Button edit = BuildPlate("EditToggle", Localize.S("編輯考程"), new Vector2(1f, 0f),
            new Vector2(-42f, 30f), new Vector2(212f, 50f), null);
        editLabel = edit.GetComponentInChildren<TextMeshProUGUI>();
        edit.onClick.AddListener(() =>
        {
            editing = !editing;
            UiSfx.Select();
            if (editLabel != null) editLabel.text = editing ? Localize.S("完成編輯") : Localize.S("編輯考程");
            Rebuild();
        });

        // 左頁是檢定、右頁是技階，所以兩顆門各站自己那一頁的下緣。
        BuildPlate("ToGradeHall", Localize.S("檢定考場"), new Vector2(0f, 0f),
            new Vector2(42f, 30f), new Vector2(212f, 50f),
            () => { Close(); SyllabusExamHall.OpenGrade(); });
        BuildPlate("ToStageHall", Localize.S("技階考場"), new Vector2(0.5f, 0f),
            new Vector2(146f, 30f), new Vector2(212f, 50f),
            () => { Close(); SyllabusExamHall.Open(); });

        Rebuild();
        StartCoroutine(FadeIn());
        SyllabusOverrides.Changed += OnOverridesChanged;

        // 曲庫變了（或這是第一次）就先編定，編好再重畫一次。
        //
        // 選曲畫面在載完曲庫時就已經開始編了，所以翻開書的時候通常已經有東西。真的
        // 撞上進行中的那一次要用**等**的，不能再叫一次 DraftRoutine —— 它會被自己的
        // 旗標擋掉並立刻回報「沒做」，於是提示被清掉、書卻還是空的。
        if (manager != null && SyllabusCourses.NeedsDraft(manager))
        {
            notice.text = Localize.S("編定考程中…");
            StartCoroutine(SyllabusCourses.Drafting ? AwaitDraft() : Draft());
        }
    }

    private IEnumerator Draft()
    {
        yield return SyllabusCourses.DraftRoutine(manager, null);
        notice.text = string.Empty;
        Rebuild();
    }

    private IEnumerator AwaitDraft()
    {
        while (SyllabusCourses.Drafting) yield return null;
        notice.text = string.Empty;
        Rebuild();
    }

    private void Rebuild()
    {
        BuildPage(leftPage, true);
        BuildPage(rightPage, false);
    }

    /// <summary>
    /// 一頁：抬頭、一行現況，然後每一階（或每一級）一列。
    /// </summary>
    /// <remarks>
    /// 列高是算出來的不是寫死的：左頁八列、右頁十列，共用同一份版面預算，這樣兩頁的
    /// 上下緣才對得齊 —— 寫死行高的那一版，十列那一頁一定先撞到版框。
    /// </remarks>
    private void BuildPage(RectTransform page, bool recital)
    {
        for (int i = page.childCount - 1; i >= 0; i--)
        {
            Transform child = page.GetChild(i);
            if (child.name != "Row" && child.name != "Head") continue;
            // 先停用再刪：Destroy 要到這一幀結束才生效，而新的列是現在就加上去的
            // —— 不停用的話會閃一幀兩份重疊的字。
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        int count = recital ? SyllabusLadder.Grades.Length : SyllabusLadder.Stages.Length;
        // 左右用比例內縮（版框是照比例畫的），上下用絕對值 —— 版框到第一行字的距離
        // 是排版的事，不該隨書的高度變。
        const float Inset = 0.075f;
        const float PageTop = 46f;
        const float PageBottom = 40f;
        const float HeadHeight = 96f;
        float pageHeight = BookHeight * 0.896f;
        float rowHeight = (pageHeight - PageTop - PageBottom - HeadHeight) / count;

        RectTransform head = AddRect(page, "Head", Vector2.zero, Vector2.zero);
        Span(head, Inset, HeadHeight, PageTop);
        TextMeshProUGUI heading = AddText(head, recital ? Localize.S("檢定") : Localize.S("技階"), 34f, Ink,
            FontStyles.Bold, TextAlignmentOptions.TopLeft);
        Stretch(heading.rectTransform);
        heading.characterSpacing = 6f;

        TextMeshProUGUI state = AddText(head, SyllabusProgress.Headline(recital), 24f, Vermilion,
            FontStyles.Bold, TextAlignmentOptions.TopRight);
        Stretch(state.rectTransform);

        TextMeshProUGUI next = AddText(head, SyllabusProgress.NextStep(recital), 17f, InkSoft,
            FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        Stretch(next.rectTransform);

        for (int i = 0; i < count; i++)
        {
            RectTransform row = AddRect(page, "Row", Vector2.zero, Vector2.zero);
            Span(row, Inset, rowHeight, PageTop + HeadHeight + i * rowHeight);
            if (editing) MakeEditable(row, recital, i + 1);
            if (recital) BuildGradeRow(row, SyllabusLadder.Grades[i], rowHeight);
            else BuildStageRow(row, SyllabusLadder.Stages[i], rowHeight);
        }
    }

    /// <summary>編輯模式下的一列：淡淡的底 + 整列可按。手改過的那幾列底色深一點。</summary>
    private static void MakeEditable(RectTransform row, bool recital, int index)
    {
        Image face = row.gameObject.AddComponent<Image>();
        bool tweaked = SyllabusOverrides.IsTweaked(recital, index);
        face.color = tweaked
            ? new Color(0.62f, 0.17f, 0.11f, 0.10f)
            : new Color(0.42f, 0.27f, 0.11f, 0.06f);
        Button button = row.gameObject.AddComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(2.2f, 1.9f, 1.4f, 2.0f);
        colours.pressedColor = new Color(1.5f, 1.2f, 0.85f, 2.8f);
        button.colors = colours;
        button.onClick.AddListener(() =>
        {
            UiSfx.Open();
            SyllabusRowEditor.Open(recital, index);
        });
    }

    private void BuildStageRow(RectTransform row, SyllabusLadder.Stage stage, float height)
    {
        SyllabusCourse course = SyllabusProgress.CourseFor(stage.Index);
        bool passed = course != null && course.passed;
        bool open = SyllabusProgress.Stage + 1 >= stage.Index;
        Color ink = passed ? Ink : open ? InkSoft : InkFaint;

        TextMeshProUGUI title = AddText(row,
            Localize.SF("{0}階　{1}", SyllabusProgress.Numeral(stage.Index), stage.Name),
            24f, ink,
            passed ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.TopLeft);
        Stretch(title.rectTransform);

        // 放寬與「未達顯著」是關於這一**階**的事實，不是關於那三首歌，所以它們放在
        // 條件欄不是曲名欄 —— 掛在曲名後面的話，日文長曲名一擠就先把它們切掉。
        string right = SyllabusLadder.BandText(SyllabusOverrides.LowOf(stage),
            SyllabusOverrides.HighOf(stage));
        if (course != null && course.charts.Count == SyllabusLadder.CourseSize)
        {
            right += Localize.SF("　{0} 容錯", SyllabusCourses.AllowanceFor(manager, course));
            if (course.widened > 0) right += $"　±{course.widened}";
            if (course.belowSignificance) right += Localize.S("　曲庫未達顯著");
        }
        TextMeshProUGUI meta = AddText(row, right, 17f, InkFaint, FontStyles.Normal,
            TextAlignmentOptions.TopRight);
        Stretch(meta.rectTransform);

        // 朱印蓋在最右邊，和印記那一欄對齊 —— 它是這一列唯一的顏色。
        if (passed)
        {
            TextMeshProUGUI seal = AddText(row, Localize.S("已敘"), 22f, Vermilion, FontStyles.Bold,
                TextAlignmentOptions.BottomRight);
            Stretch(seal.rectTransform);
        }

        TextMeshProUGUI songs = AddText(row, DescribeCourse(course, stage), 16f,
            passed ? InkSoft : InkFaint, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        // 曲名是使用者曲庫裡的字，可能是日文。
        ClassicalBookUITheme.ApplyContentFont(songs);
        // Ellipsis 不是 Overflow：三個日文長曲名加起來超過 530 寬，Overflow 會讓它
        // 直接畫到右邊的印記上、甚至畫出紙外。這個框高（列高的一半）比字級的 1.5 倍
        // 高，所以不會踩到 TMP「框太矮就整段不畫」那個坑。
        songs.overflowMode = TextOverflowModes.Ellipsis;
        BottomSpan(songs.rectTransform, height, 120f);
    }

    private void BuildGradeRow(RectTransform row, SyllabusLadder.Grade grade, float height)
    {
        SyllabusPaper paper = SyllabusProgress.PaperFor(grade.Index);
        bool awarded = paper != null && paper.awarded;
        bool open = SyllabusProgress.Grade + 1 >= grade.Index;
        Color ink = awarded ? Ink : open ? InkSoft : InkFaint;

        TextMeshProUGUI title = AddText(row,
            Localize.SF("{0}級", SyllabusProgress.Numeral(grade.Index)), 24f, ink,
            awarded ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.TopLeft);
        Stretch(title.rectTransform);

        TextMeshProUGUI meta = AddText(row, DescribeDemand(grade), 16f, InkFaint,
            FontStyles.Normal, TextAlignmentOptions.TopRight);
        Stretch(meta.rectTransform);

        if (awarded && !string.IsNullOrEmpty(paper.seal))
        {
            TextMeshProUGUI seal = AddText(row, paper.seal, 22f, Vermilion, FontStyles.Bold,
                TextAlignmentOptions.BottomRight);
            Stretch(seal.rectTransform);
        }
        else if (paper != null && SyllabusProgress.FilledSlots(paper) > 0)
        {
            TextMeshProUGUI pending = AddText(row,
                Localize.SF("已送審 {0} / {1}", SyllabusProgress.FilledSlots(paper),
                    SyllabusLadder.PaperSize),
                16f, InkSoft, FontStyles.Normal, TextAlignmentOptions.BottomRight);
            Stretch(pending.rectTransform);
        }

        TextMeshProUGUI songs = AddText(row, DescribePaper(paper, grade), 16f,
            awarded ? InkSoft : InkFaint, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        ClassicalBookUITheme.ApplyContentFont(songs);
        songs.overflowMode = TextOverflowModes.Ellipsis;
        BottomSpan(songs.rectTransform, height, 150f);
    }

    /// <summary>
    /// 一門課的三首。抽不出來就印<b>缺什麼</b>，不是印「尚無資料」。
    /// </summary>
    private string DescribeCourse(SyllabusCourse course, SyllabusLadder.Stage stage)
    {
        if (course == null || course.charts.Count < SyllabusLadder.CourseSize)
            return Localize.SF("尚未開課：需 {0} 的三首不同曲目",
                SyllabusLadder.BandText(SyllabusOverrides.LowOf(stage),
                    SyllabusOverrides.HighOf(stage)));

        var text = new StringBuilder();
        for (int i = 0; i < course.charts.Count; i++)
        {
            if (i > 0) text.Append(Localize.S("・"));
            text.Append(NameOf(course.charts[i]));
        }
        return text.ToString();
    }

    private string DescribePaper(SyllabusPaper paper, SyllabusLadder.Grade grade)
    {
        if (paper == null || paper.charts.Count < SyllabusLadder.PaperSize)
            return Localize.SF("尚未開考：需 {0} 的兩首不同曲目",
                SyllabusLadder.BandText(SyllabusOverrides.LowOf(grade),
                    SyllabusOverrides.HighOf(grade)));

        string tech = NameOf(paper.charts[0]);
        string sing = paper.charts.Count > 1 ? NameOf(paper.charts[1]) : "—";
        string body = Localize.SF("技巧 {0}　歌唱 {1}", tech, sing);
        if (paper.widened > 0) body += Localize.SF("（已放寬 ±{0}）", paper.widened);
        return body;
    }

    /// <summary>這一級多要求了什麼。級與級的差別是「多一項從選修變必修」。</summary>
    private static string DescribeDemand(SyllabusLadder.Grade grade)
    {
        SyllabusLadder.Demand demand = SyllabusOverrides.DemandOf(grade);
        var text = new StringBuilder();
        for (int c = 0; c < 4; c++)
        {
            float need = demand.For(c);
            if (need <= 0f) continue;
            if (text.Length > 0) text.Append("　");
            text.Append(SyllabusLadder.CriterionShortName(c)).Append(' ').Append((int)need);
        }
        if (demand.Average > 0f)
            text.Append(Localize.SF("　平均 {0}", (int)demand.Average));
        if (SyllabusOverrides.NeedsExpressionOf(grade)) text.Append(Localize.S("　需表情資料"));
        if (grade.RequiredStage > 0)
            text.Append(Localize.SF("　先修 {0}階",
                SyllabusProgress.Numeral(grade.RequiredStage)));
        return text.ToString();
    }

    private string NameOf(string chartFileName)
    {
        SongSelectionManager.SongOption option = SyllabusCourses.Resolve(manager, chartFileName);
        if (option == null) return Localize.S("（此譜已不在曲庫）");
        return string.IsNullOrWhiteSpace(option.displayName) ? chartFileName : option.displayName;
    }

    /// <summary>
    /// 書開著的時候，底下的畫面不該再吃鍵盤。
    /// </summary>
    /// <remarks>
    /// 練習室和選曲畫面都用這種旗標擋住自己的 <c>HandleKeyboard</c>（曲譜書有同一個
    /// <c>IsOpen</c>）。不擋的話，在書上按 Esc 會**同時**關掉書並且把底下的畫面也切
    /// 走 —— 而按 Enter 會直接開始遊戲，書還整片蓋在最上面。
    /// </remarks>
    public static bool IsOpen => instance != null;

    private void Update()
    {
        // 上面還蓋著東西就讓它自己處理 Esc —— 不擋的話按一次會把三層一起關掉。
        if (SyllabusRowEditor.IsOpen || SyllabusSongPicker.IsOpen) return;
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    /// <summary>
    /// 改了覆寫之後整本要重編，因為「這一格考哪一首」是抽課引擎算出來的。
    /// </summary>
    /// <remarks>
    /// 重編是整本重來而不是只重算那一列：檢定的考卷會避開已經被用掉的曲目，所以改
    /// 三階會影響到後面每一格。第一次之後分析檔都在
    /// <see cref="ChartAnalysisCache"/> 裡了，重編不再碰磁碟。
    /// </remarks>
    private void OnOverridesChanged()
    {
        if (redrafting != null) StopCoroutine(redrafting);
        redrafting = StartCoroutine(RedraftThenRebuild());
    }

    private IEnumerator RedraftThenRebuild()
    {
        while (SyllabusCourses.Drafting) yield return null;
        if (manager != null && SyllabusCourses.NeedsDraft(manager))
            yield return SyllabusCourses.DraftRoutine(manager, null);
        redrafting = null;
        Rebuild();
    }

    /// <summary>書緣上的一顆牌：淡底、金線、刻字。</summary>
    private Button BuildPlate(string name, string caption, Vector2 corner, Vector2 offset,
        Vector2 size, System.Action onClick)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = book.gameObject.layer;
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(book, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        // **這三顆牼坐在書板上，不是紙頁上。** 舊的做法是 12% 透明的底配墨色字
        // （和頁上那些字同一個 <see cref="Ink"/>），而書板是深褐色的皮 —— 深配深，
        // 在畫面上幾乎看不見。改成銃牼：實心的皮面、金線、米白刻字，和考場底下那顆
        // 「翻開考程」是同一種東西。
        // 面是**有貼圖的**皮，所以圓角走遮罩：疊一塊平色圓角會把皮紋整片蓋掉。
        RectTransform inside = SettingsUiKit.RoundCorners(rect, 10f);
        Image face = AddImage(inside, "Face", Color.white);
        ClassicalBookUITheme.ApplyDarkSurface(face, new Color(0.205f, 0.095f, 0.055f, 1f),
            ClassicalBookUITheme.Surface.Leather, 1.4f);
        face.rectTransform.anchorMin = Vector2.zero;
        face.rectTransform.anchorMax = Vector2.one;
        face.rectTransform.offsetMin = face.rectTransform.offsetMax = Vector2.zero;
        PageRuleGraphic rule = PageRuleGraphic.Attach(inside, 53,
            new Color(0.82f, 0.63f, 0.28f, 0.95f), 0.075f, 2.2f, true, 0);
        if (rule != null) rule.Wear = 0.12f;

        var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        label.layer = book.gameObject.layer;
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(inside, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 2f);
        labelRect.offsetMax = new Vector2(-4f, -2f);
        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        text.text = caption;
        text.fontSize = 20f;
        text.color = new Color(0.94f, 0.88f, 0.74f, 1f);
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(text);

        Button button = buttonObject.GetComponent<Button>();
        // 染色要染看得見的那一層。遮罩圖不畫，染它等於什麼都沒做。
        button.targetGraphic = face;
        // 底色現在是實心的，提亮倍率要跟著收 —— 舊的 2.1× 是配 12% 透明底算的，
        // 直接留著會爆白。
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.22f, 1.16f, 1.06f, 1f);
        colours.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
        button.colors = colours;
        if (onClick != null)
            button.onClick.AddListener(() => { UiSfx.Select(); onClick(); });
        return button;
    }

    private void OnDestroy() => SyllabusOverrides.Changed -= OnOverridesChanged;

    private void Close()
    {
        UiSfx.Back();
        if (instance == this) instance = null;
        Destroy(gameObject);
    }

    private IEnumerator FadeIn()
    {
        const float duration = 0.18f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            canvasGroup.alpha = Mathf.Clamp01(t / duration);
            book.localScale = Vector3.one * Mathf.Lerp(0.972f, 1f, Mathf.Clamp01(t / duration));
            yield return null;
        }
        canvasGroup.alpha = 1f;
        book.localScale = Vector3.one;
    }

    // ── 版面小工具 ────────────────────────────────────────────────────

    private RectTransform AddPage(string name, Vector2 min, Vector2 max)
    {
        Image page = AddImage(book, name, new Color(0.93f, 0.89f, 0.74f, 1f));
        page.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        page.type = Image.Type.Tiled;
        page.raycastTarget = false;
        page.rectTransform.anchorMin = min;
        page.rectTransform.anchorMax = max;
        page.rectTransform.offsetMin = Vector2.zero;
        page.rectTransform.offsetMax = Vector2.zero;

        int seed = name == "LeftPage" ? 23 : 61;
        BookShape.Profile sheet = name == "LeftPage"
            ? BookShape.Profile.Page.Mirrored()
            : BookShape.Profile.Page;

        BookBoardGraphic leaf = BookBoardGraphic.Attach(page.rectTransform, seed);
        if (leaf != null)
        {
            leaf.Bound = false;
            leaf.Profile = sheet;
            Color paper = new Color(0.93f, 0.89f, 0.74f, 1f);
            leaf.Set(paper, paper, paper, ClassicalBookUITheme.Gold);
            Sprite fibre = ClassicalBookUITheme.GetPaperTextureSprite();
            if (fibre != null) leaf.Grain(fibre.texture, fibre.texture.width);
        }
        AgedPaperGraphic wear = AgedPaperGraphic.Attach(page.rectTransform, seed, 1);
        if (wear != null)
        {
            wear.Shape(sheet);
            wear.Stain = new Color(0.30f, 0.175f, 0.085f, 1f);
            wear.Strength = 0.88f;
            wear.EdgeDepth = 0.075f;
            wear.Mottle = 0.24f;
            wear.Foxing = 6;
        }
        PageRuleGraphic rule = PageRuleGraphic.Attach(page.rectTransform, seed,
            new Color(0.22f, 0.115f, 0.05f, 0.90f), 0.040f, 4.4f, true, 2);
        if (rule != null)
        {
            rule.Wear = 0.26f;
            rule.Shape(sheet);
        }
        return page.rectTransform;
    }

    /// <summary>
    /// 一列的下半：左邊到列尾，右邊讓出 <paramref name="reserve"/> 給印記那一欄。
    /// </summary>
    /// <remarks>
    /// 用 anchor 撐開而不是 sizeDelta：列寬是錨點算出來的，這裡拿不到它的實際值，寫
    /// 死一個數字在窄一點的書上就會壓到右邊的印。
    /// </remarks>
    private static void BottomSpan(RectTransform rect, float rowHeight, float reserve)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.offsetMin = new Vector2(0f, 2f);
        rect.offsetMax = new Vector2(-reserve, 2f + rowHeight * 0.5f);
    }

    /// <summary>一列：左右貼著版框內側，上緣離頁頂 <paramref name="top"/>。</summary>
    private static void Span(RectTransform rect, float inset, float height, float top)
    {
        rect.anchorMin = new Vector2(inset, 1f);
        rect.anchorMax = new Vector2(1f - inset, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -(top + height));
        rect.offsetMax = new Vector2(0f, -top);
    }

    private static Image AddImage(Transform parent, string name, Color colour)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = parent.gameObject.layer;
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = colour;
        return image;
    }

    private static RectTransform AddRect(Transform parent, string name, Vector2 size, Vector2 offset)
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

    private static void Place(RectTransform rect, Vector2 corner, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    private static TextMeshProUGUI AddText(Transform parent, string value, float size, Color colour,
        FontStyles style, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        // TMP 在「框高 < 字級 × 1.5」時整段不畫（Truncate），而這本書的列高是算出來
        // 的，一定比字級的 1.5 倍矮。
        text.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(text);
        return text;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
