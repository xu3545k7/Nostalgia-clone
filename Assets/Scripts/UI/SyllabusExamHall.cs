using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 技階考場：進來先看清楚這一階要彈哪三首、什麼難度、多少容錯。
/// </summary>
/// <remarks>
/// **為什麼考場是一張榜，不是又一本書。** 考程書（<see cref="SyllabusBookOverlay"/>）
/// 是帳本 —— 兩條階梯十八列全部攤開，刻意不能按。考場只講一件事：<b>下一階的那三
/// 首</b>。把它做成第二本書，玩家得在十八列裡自己找到「我現在要考哪一階」，而那正是
/// 書已經做過的事。一張榜只印一階，進來就看到要彈什麼。
///
/// **為什麼曲目在應試之前就全部攤開。** 三首的順序就是建議的應試順序。玩家要決定
/// 「現在考還是再練」，就得先看到三首各是什麼難度、多長 —— 藏起來只會讓人開了考試才
/// 發現第三首是自己沒看過的譜。
///
/// **逐曲應試，可以重測。** 每一列都是一顆開始鈕，打完回來再打下一首；重打同一首只會
/// 把成績往上換（<see cref="SyllabusProgress.RecordAttempt"/> 取最好的一次）。這繞開了
/// 「一局連開三首」的整套生命週期重建 —— 和檢定用送審制繞過去是同一個手法。
///
/// **三首各自都要到及第線才授階**，不是看平均 —— 一首打得極好不該能括兩首沒彈好的。
/// 榜上印的達成率是三首最佳成績的平均，那是進度計，不是授階條件。
/// </remarks>
public sealed class SyllabusExamHall : MonoBehaviour
{
    private static SyllabusExamHall instance;

    public static bool IsOpen => instance != null;

    private const float BoardWidth = 900f;
    private const float BoardHeight = 1010f;
    /// <summary>一列曲目的高度。三列加上題頭與頁腳剛好吃滿榜面。</summary>
    private const float RowHeight = 118f;

    private static readonly Color Ink = new Color(0.22f, 0.115f, 0.055f, 1f);
    private static readonly Color InkSoft = new Color(0.34f, 0.22f, 0.12f, 1f);
    private static readonly Color InkFaint = new Color(0.46f, 0.34f, 0.22f, 1f);
    private static readonly Color Vermilion = new Color(0.62f, 0.17f, 0.11f, 1f);

    private CanvasGroup canvasGroup;
    private RectTransform board;
    private RectTransform sheet;
    private SongSelectionManager manager;

    /// <summary>這一張榜講哪一條階梯。</summary>
    private bool grading;

    /// <summary>技階考場。</summary>
    public static void Open() => Open(false);

    /// <summary>檢定考場。</summary>
    public static void OpenGrade() => Open(true);

    /// <remarks>
    /// 兩條階梯共用同一張榜，因為玩家要做的事一模一樣：看清楚這一關要彈哪幾首、什麼
    /// 難度、現在打到幾分，然後點一首去打。只有標題、要求那一行和幾首不同。各做一張
    /// 只會得到兩張看起來不太一樣的榜。
    /// </remarks>
    private static void Open(bool grade)
    {
        if (instance != null) Destroy(instance.gameObject);
        var root = new GameObject("SyllabusExamHall");
        instance = root.AddComponent<SyllabusExamHall>();
        instance.grading = grade;
        instance.Build();
    }

    private void Build()
    {
        manager = SongSelectionManager.Instance;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 和考程書同一層：兩者是同一組東西，而且不會同時開著。
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
        var shadeButton = shade.gameObject.AddComponent<Button>();
        shadeButton.transition = Selectable.Transition.None;
        shadeButton.onClick.AddListener(Close);

        RectTransform shadow = AddRect(transform, "BoardShadow",
            new Vector2(BoardWidth + 26f, BoardHeight + 18f), new Vector2(10f, -14f));
        Image shadowImage = shadow.gameObject.AddComponent<Image>();
        shadowImage.color = new Color(0f, 0f, 0f, 0.72f);
        shadowImage.sprite = SettingsUiKit.PlainRoundedSprite(14f);
        shadowImage.type = Image.Type.Sliced;
        shadowImage.pixelsPerUnitMultiplier = 1f;
        shadowImage.raycastTarget = false;

        // **和考程書同一套做法。** 這張榜是那本書的一葉，不是另一種傢俱：書板
        // （斜面＋金邊＋紋理）、書葉、金尺規，三件都用書已經解掉的那幾個元件。
        // 原本是一塊平色木板貼一張平色紙，在這個專案裡看起來像沒做完。
        board = AddRect(transform, "ExamBoard", new Vector2(BoardWidth, BoardHeight), Vector2.zero);
        Image leather = board.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.ApplyDarkSurface(leather,
            new Color(0.105f, 0.038f, 0.025f, 1f), ClassicalBookUITheme.Surface.Leather, 2.5f);
        // 榜自己擋射線：關閉鈕鋪滿整個畫面，不擋的話點在榜上也會關掉。
        leather.raycastTarget = true;

        BookBoardGraphic boards = BookBoardGraphic.Attach(board, 29);
        if (boards != null)
        {
            boards.Profile = BookShape.Profile.Volume;
            boards.Set(new Color(0.115f, 0.045f, 0.028f, 1f),
                new Color(0.135f, 0.055f, 0.033f, 1f),
                new Color(0.84f, 0.78f, 0.62f, 1f), ClassicalBookUITheme.Gold);
            Sprite boardGrain = ClassicalBookUITheme.GetBoardTextureSprite();
            if (boardGrain != null) boards.Grain(boardGrain.texture, boardGrain.texture.width);
        }
        AgedPaperGraphic boardWear = AgedPaperGraphic.Attach(board, 29);
        if (boardWear != null)
        {
            boardWear.Shape(BookShape.Profile.Volume);
            boardWear.Stain = new Color(0.36f, 0.22f, 0.13f, 1f);
            boardWear.Strength = 0.6f;
            boardWear.EdgeDepth = 0.055f;
            boardWear.Mottle = 0.3f;
            boardWear.Foxing = 0;
        }

        // 書葉。下緣留一條腰帶給底下那顆牌，和考程書一樣。
        Image paper = AddImage(board, "Sheet", new Color(0.93f, 0.89f, 0.74f, 1f));
        paper.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        paper.type = Image.Type.Tiled;
        paper.raycastTarget = false;
        sheet = paper.rectTransform;
        sheet.anchorMin = new Vector2(0.042f, 0.088f);
        sheet.anchorMax = new Vector2(0.958f, 0.962f);
        sheet.offsetMin = sheet.offsetMax = Vector2.zero;

        // **一張單葉，不是一本裝訂好的書。** <c>Bound</c> 預設是 true，那會畫出書脊、
        // 書溝和整疊紙 —— 深色的那一套，整張蓋在紙上。考程書的 AddPage 就是先關掉它。
        BookBoardGraphic leaf = BookBoardGraphic.Attach(sheet, 41);
        if (leaf != null)
        {
            leaf.Bound = false;
            leaf.Profile = BookShape.Profile.Page;
            Color tone = new Color(0.93f, 0.89f, 0.74f, 1f);
            leaf.Set(tone, tone, tone, ClassicalBookUITheme.Gold);
            Sprite fibre = ClassicalBookUITheme.GetPaperTextureSprite();
            if (fibre != null) leaf.Grain(fibre.texture, fibre.texture.width);
        }
        AgedPaperGraphic wear = AgedPaperGraphic.Attach(sheet, 41, 1);
        if (wear != null)
        {
            wear.Shape(BookShape.Profile.Page);
            wear.Stain = new Color(0.30f, 0.175f, 0.085f, 1f);
            wear.Strength = 0.88f;
            wear.EdgeDepth = 0.075f;
            wear.Mottle = 0.24f;
            wear.Foxing = 6;
        }
        PageRuleGraphic rule = PageRuleGraphic.Attach(sheet, 41,
            new Color(0.22f, 0.115f, 0.05f, 0.90f), 0.040f, 4.4f, true, 2);
        if (rule != null)
        {
            rule.Wear = 0.26f;
            rule.Shape(BookShape.Profile.Page);
        }

        Rebuild();
        StartCoroutine(FadeIn());
        SyllabusProgress.Changed += Rebuild;
    }

    private void OnDestroy()
    {
        SyllabusProgress.Changed -= Rebuild;
        if (instance == this) instance = null;
    }

    private void Close()
    {
        UiSfx.Close();
        if (instance == this) instance = null;
        Destroy(gameObject);
    }

    private System.Collections.IEnumerator FadeIn()
    {
        float at = 0f;
        while (at < 1f)
        {
            at += Time.unscaledDeltaTime * 6f;
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Clamp01(at);
            yield return null;
        }
    }

    // ------------------------------------------------------------------

    private void Rebuild()
    {
        if (sheet == null) return;
        // **裝飾層要留著。** 這幾個元件是在 Build 裡裝上的，每次重畫全部刪掉
        // 再裝一次是白做工；更糟的是 <c>BookBoardGraphic.Attach</c> 會把宿主 Image 的
        // alpha 設成 0 並接手繪製 —— 把它刪掉之後，畫紙的東西沒了，宿主又是透明的，
        // 整張書葉就消失，看到的是後面的書板。
        //
        // 這個守衛原本寫成「只留 AgedPaperGraphic」，因為那時候裝飾只有一個。改成認
        // 名字開頭的 '~'（這幾個 Attach 建的子物件都是這個約定），以後再加就不會再踩。
        for (int i = sheet.childCount - 1; i >= 0; i--)
        {
            Transform child = sheet.GetChild(i);
            if (!child.name.StartsWith("~")) Destroy(child.gameObject);
        }

        float y = -46f;
        TextMeshProUGUI title = AddText(sheet, grading ? Localize.S("檢　定　考　場") : Localize.S("技　階　考　場"),
            42f, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(760f, 56f));

        // 右上角壓一枚記號：技階是黃銅階數牌、檢定是朱印 —— **和入口那一枚是同一個**。
        // 螢幕靠它自己表明身分，玩家不必讀標題就知道按進來的是哪一條階梯。
        Stamp(y + 6f);

        y -= 64f;
        Rule(y);
        y -= 26f;

        if (grading) RebuildGrade(y);
        else RebuildStage(y);
    }

    private void RebuildStage(float y)
    {
        int next = SyllabusProgress.Stage + 1;
        SyllabusLadder.Stage stage = SyllabusLadder.StageAt(next);
        if (stage == null)
        {
            Notice(y - 60f, Localize.S("十階已畢。考程到此為止。"), Ink, 28f);
            Footer(null);
            return;
        }

        SyllabusCourse course = SyllabusProgress.CourseFor(next);
        string band = SyllabusLadder.BandText(SyllabusOverrides.LowOf(stage),
            SyllabusOverrides.HighOf(stage));
        y = Heading(y, Localize.SF("第 {0} 階　{1}", Numeral(next), stage.Name),
            Localize.SF("{0}　依「{1}」取三首", band, SyllabusLadder.MetricName(stage.Key)));

        if (course?.charts == null || course.charts.Count < SyllabusLadder.CourseSize)
        {
            Notice(y - 40f, Localize.SF("曲庫不足：這一階需要 {0} 的三首不同曲目。", band),
                Vermilion, 24f);
            Footer(null);
            return;
        }

        int totalNotes = 0;
        for (int i = 0; i < course.charts.Count && i < SyllabusLadder.CourseSize; i++)
        {
            totalNotes += Row(y, i, course.charts[i],
                SyllabusProgress.AttemptOf(course, i), course.passed, null);
            y -= RowHeight;
        }

        y -= 10f;
        Rule(y);
        y -= 30f;
        y = Summary(y, SyllabusProgress.AchievementOf(course), course.passed,
            Localize.SF("授階條件：三首各自達 {0:0}%", Judgment.RecitalAssessment.PassMark),
            course.passed ? Localize.S("已授階") : null, totalNotes);

        if (course.widened > 0)
            y = Aside(y, Localize.SF("等級帶往外放寬 {0} 級才湊滿三首", course.widened),
                InkFaint);
        if (course.belowSignificance)
            y = Aside(y, Localize.S("本階曲庫未達顯著，已取最接近的三首"), Vermilion);
        if (!string.IsNullOrEmpty(course.lastAttempt))
            Aside(y, Localize.SF("上次：{0}", course.lastAttempt), InkFaint);

        Footer(course.passed
            ? Localize.S("本階已授。過了的課永久凍結，它就是你過的那門課的紀錄。")
            : Localize.S("點一列就開那一首。打完回來再打下一首；重打同一首只會把成績往上換。"));
    }

    /// <remarks>
    /// 檢定和技階的差別在**要求**不在流程：級與級的不同是「多一項從選修變必修」，所以
    /// 這裡印的是那四項門檻而不是技法名。先修階不夠時照樣印得出來、也照樣打得了 ——
    /// 擋的是授級不是應考，那一局是真的彈過的。
    /// </remarks>
    private void RebuildGrade(float y)
    {
        int next = SyllabusProgress.Grade + 1;
        SyllabusLadder.Grade grade = SyllabusLadder.GradeAt(next);
        if (grade == null)
        {
            Notice(y - 60f, Localize.S("八級已畢。考程到此為止。"), Ink, 28f);
            Footer(null);
            return;
        }

        SyllabusPaper paper = SyllabusProgress.PaperFor(next);
        string band = SyllabusLadder.BandText(SyllabusOverrides.LowOf(grade),
            SyllabusOverrides.HighOf(grade));
        SyllabusLadder.Demand demand = SyllabusOverrides.DemandOf(grade);

        var need = new StringBuilder();
        for (int c = 0; c < 4; c++)
        {
            float bar = demand.For(c);
            if (bar <= 0f) continue;
            if (need.Length > 0) need.Append("　");
            need.Append(SyllabusLadder.CriterionShortName(c)).Append($" {bar:0}");
        }
        if (demand.Average > 0f)
            need.Append(Localize.SF("　平均 {0:0}", demand.Average));
        y = Heading(y, Localize.SF("第 {0} 級", Numeral(next)), $"{band}　{need}");

        if (paper?.charts == null || paper.charts.Count < SyllabusLadder.PaperSize)
        {
            Notice(y - 40f, Localize.SF("曲庫不足：這一級需要 {0} 的兩首不同曲目。", band),
                Vermilion, 24f);
            Footer(null);
            return;
        }

        int totalNotes = 0;
        for (int i = 0; i < paper.charts.Count && i < SyllabusLadder.PaperSize; i++)
        {
            SyllabusSubmission held = paper.submissions != null && i < paper.submissions.Count
                ? paper.submissions[i] : null;
            totalNotes += Row(y, i, paper.charts[i], held, paper.awarded,
                i == 0 ? Localize.S("技巧曲") : Localize.S("歌唱曲"));
            y -= RowHeight;
        }

        y -= 10f;
        Rule(y);
        y -= 30f;
        y = Summary(y, SyllabusProgress.AchievementOf(paper), paper.awarded,
            Localize.S("每一首都要達到，兩首平均也要達到"),
            paper.awarded
                ? Localize.SF("已授　{0}", SyllabusLadder.SealText(paper.seal))
                : null, totalNotes);

        if (SyllabusProgress.Stage < grade.RequiredStage)
            y = Aside(y, Localize.SF("需先取得技階 {0} 階才會授級（照樣可以先打）",
                Numeral(grade.RequiredStage)), Vermilion);
        if (grade.NeedsExpressionData)
            y = Aside(y, Localize.S("本級要求力度與踏板都評得到"), InkFaint);
        if (paper.widened > 0)
            Aside(y, Localize.SF("等級帶往外放寬 {0} 級才湊滿兩首", paper.widened), InkFaint);

        Footer(paper.awarded
            ? Localize.S("本級已授。印只往上蓋，回頭重考不會把它弄掉。")
            : Localize.S("點一列就開那一首。打完在結算頁按「送審」，送審只會把成績往上換。"));
    }

    private float Heading(float y, string line, string sub)
    {
        TextMeshProUGUI heading = AddText(sheet, line, 32f, Ink, FontStyles.Bold,
            TextAlignmentOptions.Center);
        Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(760f, 44f));
        y -= 44f;
        TextMeshProUGUI note = AddText(sheet, sub, 20f, InkFaint, FontStyles.Italic,
            TextAlignmentOptions.Center);
        Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(780f, 30f));
        return y - 46f;
    }

    private float Summary(float y, float achieved, bool done, string rule, string badge, int notes)
    {
        var line = new StringBuilder();
        line.Append(Localize.SF("達成率 {0:0.#}%　{1}", achieved, rule));
        if (!string.IsNullOrEmpty(badge)) line.Append($"　{badge}");
        TextMeshProUGUI text = AddText(sheet, line.ToString(), 21f,
            done ? Vermilion : InkSoft, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(820f, 30f));
        y -= 32f;
        if (notes > 0) y = Aside(y, Localize.SF("合計 {0} 顆音符", notes), InkFaint);
        return y;
    }

    private float Aside(float y, string message, Color colour)
    {
        TextMeshProUGUI text = AddText(sheet, message, 19f, colour, FontStyles.Italic,
            TextAlignmentOptions.Center);
        Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(800f, 26f));
        return y - 26f;
    }

    /// <summary>
    /// 右上角那枚記號。技階用階數牌、檢定用朱印。
    /// </summary>
    private void Stamp(float y)
    {
        RectTransform mark = AddRect(sheet, "Stamp", new Vector2(64f, 64f), Vector2.zero);
        Place(mark, new Vector2(1f, 1f), new Vector2(-34f, y), new Vector2(64f, 64f));
        mark.gameObject.AddComponent<CanvasRenderer>();
        if (grading)
        {
            var seal = mark.gameObject.AddComponent<GradeSealGraphic>();
            seal.Grade = SyllabusProgress.Grade + 1;
            seal.Seal = SyllabusLadder.SealRank(SyllabusProgress.Seal);
            seal.raycastTarget = false;
            TextMeshProUGUI digit = AddText(mark,
                (SyllabusProgress.Grade + 1).ToString(), 24f,
                new Color(1f, 0.95f, 0.90f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(digit.rectTransform, 8f);
        }
        else
        {
            var tablet = mark.gameObject.AddComponent<RomanTabletGraphic>();
            tablet.Numeral = SyllabusProgress.Stage + 1;
            tablet.raycastTarget = false;
        }
    }

    /// <summary>一列曲目，整列可以按。回傳這份譜的音符數。</summary>
    private int Row(float y, int order, string chartFileName, SyllabusSubmission best,
        bool frozen, string role)
    {
        RectTransform row = AddRect(sheet, "Row" + order, new Vector2(760f, RowHeight - 14f),
            Vector2.zero);
        Place(row, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(760f, RowHeight - 14f));

        // **列不是一塊色。** 底色只留到剛好分得出列，分界交給底下那條金尺規和兩端的
        // 菱點 —— 一塊 7% 的色塊讀起來是表格，而這是一張榜。
        Image bed = AddRounded(row, "Bed", new Color(0.52f, 0.38f, 0.20f, 0.035f), 12f);
        Stretch(bed.rectTransform);
        bed.raycastTarget = true;

        Image hair = AddRounded(row, "Hairline", new Color(0.62f, 0.45f, 0.19f, 0.45f), 1f);
        Place(hair.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 2f),
            new Vector2(700f, 2f));
        hair.raycastTarget = false;
        Pip(row, -352f);
        Pip(row, 352f);

        SongSelectionManager.SongOption option = manager != null
            ? SyllabusCourses.Resolve(manager, chartFileName)
            : null;
        ChartAnalysis analysis = ChartAnalysisCache.GetImmediate(chartFileName);

        TextMeshProUGUI numeral = AddText(row, Numeral(order + 1), 30f, Vermilion,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Place(numeral.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f, 0f),
            new Vector2(54f, 44f));

        // 譜被刪掉或改名了就印檔名 —— 看得出是哪一份不見了，比一列空白好。
        string name = option != null && !string.IsNullOrWhiteSpace(option.displayName)
            ? option.displayName : chartFileName;
        // **曲名放不下就讓它走過去。** 裁成「エンドマークに希望と…」會讓開頭相同的兩
        // 首看起來一樣，而玩家要靠這一行決定要彈哪一首。
        TextMarquee marquee = TextMarquee.Attach(row, new Vector2(0f, 1f),
            new Vector2(76f, -14f), new Vector2(392f, 36f));
        marquee.Label.fontSize = 26f;
        marquee.Label.color = Ink;
        marquee.Label.fontStyle = FontStyles.Bold;
        marquee.SetText(name);

        string by = option != null && !string.IsNullOrWhiteSpace(option.author)
            ? option.author : string.Empty;
        if (!string.IsNullOrEmpty(role))
            by = string.IsNullOrEmpty(by) ? role : Localize.SF("{0}・{1}", role, by);
        TextMarquee under = TextMarquee.Attach(row, new Vector2(0f, 1f),
            new Vector2(76f, -52f), new Vector2(392f, 26f));
        under.Label.fontSize = 18f;
        under.Label.color = InkFaint;
        under.Label.fontStyle = FontStyles.Italic;
        under.SetText(by);

        if (option != null)
        {
            // 難度徽章：和遊玩畫面那張曲目資訊卡同一塊牌。難度名縮成一個字母 ——
            // 整塊牌的顏色已經答完「哪一階難度」，字母只是把它釘死。
            Color tint = ClassicalBookUITheme.GetDifficultyColour(
                option.difficultyName, option.difficultyLevel);
            RectTransform badge = AddRect(row, "Badge", new Vector2(74f, 32f), Vector2.zero);
            Place(badge, new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(74f, 32f));
            badge.gameObject.AddComponent<CanvasRenderer>();
            var art = badge.gameObject.AddComponent<DifficultyBadgeGraphic>();
            art.Ink = tint;
            art.raycastTarget = false;
            TextMeshProUGUI mark = AddText(badge,
                $"{Initial(option.difficultyName)} {option.difficultyLevel}", 19f,
                new Color(1f, 0.98f, 0.96f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(mark.rectTransform, 2f);
        }

        if (analysis != null && analysis.noteCount > 0)
        {
            int seconds = Mathf.Max(0, Mathf.RoundToInt(analysis.lengthSeconds));
            TextMeshProUGUI stats = AddText(row,
                Localize.SF("{0}′{1:00}″　{2} 音", seconds / 60, seconds % 60,
                analysis.noteCount), 18f, InkFaint,
                FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            Place(stats.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -52f),
                new Vector2(250f, 26f));
        }

        Slot(row, best);

        if (option != null && !frozen)
        {
            var play = row.gameObject.AddComponent<Button>();
            play.targetGraphic = bed;
            play.transition = Selectable.Transition.ColorTint;
            ColorBlock colours = play.colors;
            colours.normalColor = Color.white;
            // 底色只有 3.5% 不透明，提亮倍率要夠大才看得出滑過。
            colours.highlightedColor = new Color(4.2f, 3.4f, 2.4f, 1f);
            colours.pressedColor = new Color(0.8f, 0.72f, 0.6f, 1f);
            colours.fadeDuration = 0.08f;
            play.colors = colours;
            play.onClick.AddListener(() => Play(option));
        }

        return analysis != null ? analysis.noteCount : 0;
    }

    /// <summary>難度名縮成一個字母。</summary>
    /// <remarks>
    /// 徽章只有 74 寬，塞不下「Master piano solo」這種名字，而整塊牌的顏色本來就已經
    /// 答完「這是哪一階難度」—— 字母是給色盲和截圖用的保險，不是主要資訊。
    /// 認不出來就取第一個字，至少不會變成空白。
    /// </remarks>
    private static string Initial(string difficultyName)
    {
        if (string.IsNullOrWhiteSpace(difficultyName)) return "?";
        string trimmed = difficultyName.Trim();
        switch (char.ToUpperInvariant(trimmed[0]))
        {
            case 'N': return "N";
            case 'H': return "H";
            case 'E': return "E";
            case 'R': return "R";
            case 'M': return "M";
            case 'O': return "O";
            default: return trimmed.Substring(0, 1).ToUpperInvariant();
        }
    }

    /// <summary>尺規兩端的小菱點。</summary>
    private void Pip(RectTransform row, float x)
    {
        // 半徑等於半邊長 = 一個圓點。尺規兩端是點，不是小方塊。
        Image pip = AddRounded(row, "Pip", new Color(0.70f, 0.52f, 0.22f, 0.85f), 3f);
        Place(pip.rectTransform, new Vector2(0.5f, 0f), new Vector2(x, 1f), new Vector2(6f, 6f));
        pip.raycastTarget = false;
    }

    /// <summary>
    /// 成績：刻在紙上的一道槽，填到哪就亮到哪。
    /// </summary>
    /// <remarks>
    /// 空的時候也看得出「這裡該有東西」—— 一行「未演奏」的灰字飄在中間只是缺字，
    /// 一道空的槽是還沒填。及第線刻一道痕，不用讀數字就知道差多少。
    /// </remarks>
    private void Slot(RectTransform row, SyllabusSubmission best)
    {
        const float SlotWidth = 118f;
        RectTransform slot = AddRect(row, "Slot", new Vector2(SlotWidth, 16f), Vector2.zero);
        Place(slot, new Vector2(0f, 0.5f), new Vector2(486f, 0f), new Vector2(SlotWidth, 16f));

        // 槽做成膠囊：半徑就是半個高度。
        Image groove = AddRounded(slot, "Groove", new Color(0.46f, 0.34f, 0.18f, 0.22f), 8f);
        Stretch(groove.rectTransform);
        groove.raycastTarget = false;

        float bar = Judgment.RecitalAssessment.PassMark;
        if (best != null)
        {
            float fill = Mathf.Clamp01(best.Average / 100f);
            bool cleared = best.Average >= bar;
            Image done = AddRounded(slot, "Fill",
                cleared ? new Color(0.62f, 0.17f, 0.11f, 0.85f)
                        : new Color(0.52f, 0.40f, 0.22f, 0.70f), 8f);
            done.rectTransform.anchorMin = new Vector2(0f, 0f);
            done.rectTransform.anchorMax = new Vector2(fill, 1f);
            done.rectTransform.offsetMin = done.rectTransform.offsetMax = Vector2.zero;
            done.raycastTarget = false;
        }

        // 及第線。槽上刻一道，不用讀數字就知道差多少。
        Image notch = AddImage(slot, "PassMark", new Color(0.30f, 0.18f, 0.08f, 0.75f));
        notch.rectTransform.anchorMin = new Vector2(bar / 100f, -0.35f);
        notch.rectTransform.anchorMax = new Vector2(bar / 100f, 1.35f);
        notch.rectTransform.offsetMin = new Vector2(-1f, 0f);
        notch.rectTransform.offsetMax = new Vector2(1f, 0f);
        notch.raycastTarget = false;

        TextMeshProUGUI caption = AddText(row,
            best == null ? Localize.S("未演奏") : $"{best.Average:0.#}%",
            best == null ? 15f : 17f,
            best == null ? InkFaint
                : (best.Average >= bar ? Vermilion : InkSoft),
            best == null ? FontStyles.Italic : FontStyles.Bold,
            TextAlignmentOptions.Center);
        Place(caption.rectTransform, new Vector2(0f, 0.5f), new Vector2(486f, -20f),
            new Vector2(SlotWidth, 22f));
    }

    /// <summary>開這一首。打完回來再進考場打下一首。</summary>
    /// <remarks>
    /// 考試要在演奏會模式下打 —— 練習可以改速度、背景音樂關著，那和別人考的不是同一
    /// 張譜。玩家從練習室進來時多半還開著練習模式，所以這裡替他關掉而不是擋下來。
    /// </remarks>
    private void Play(SongSelectionManager.SongOption option)
    {
        if (manager == null || option == null) return;
        var settings = SettingsManager.Instance;
        if (settings != null && settings.PracticeMode) settings.SetPracticeMode(false);
        UiSfx.Select();
        if (instance == this) instance = null;
        Destroy(gameObject);
        manager.StartSongOption(option);
    }

    /// <summary>底下那一條：怎麼考，以及翻開帳本。</summary>
    private void Footer(string hint)
    {
        TextMeshProUGUI how = AddText(sheet, hint ?? Localize.S("課目尚未編定。"), 19f, InkFaint,
            FontStyles.Italic, TextAlignmentOptions.Center);
        Place(how.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 62f),
            new Vector2(820f, 26f));

        // 榜只講下一關，整本帳在書裡。從這裡翻得過去，不必退回頂欄換按另一枚記號。
        RectTransform plate = AddRect(sheet, "ToBook", new Vector2(220f, 46f), Vector2.zero);
        Place(plate, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(220f, 46f));
        // 皮面是貼圖，所以用遮罩裁圓角，不是拿一塊平色蓋上去。
        RectTransform inside = SettingsUiKit.RoundCorners(plate, 10f);
        Image face = AddImage(inside, "Face", Color.white);
        ClassicalBookUITheme.ApplyDarkSurface(face, new Color(0.19f, 0.09f, 0.055f, 1f),
            ClassicalBookUITheme.Surface.Leather, 1.4f);
        Stretch(face.rectTransform);
        TextMeshProUGUI caption = AddText(inside, Localize.S("翻開考程"), 21f,
            new Color(0.92f, 0.86f, 0.72f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(caption.rectTransform);

        var button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.highlightedColor = new Color(1.2f, 1.14f, 1.05f, 1f);
        colours.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        button.onClick.AddListener(() =>
        {
            UiSfx.Open();
            if (instance == this) instance = null;
            Destroy(gameObject);
            SyllabusBookOverlay.Open();
        });
    }

    // ------------------------------------------------------------------

    private void Rule(float y)
    {
        Image rule = AddRounded(sheet, "Rule", new Color(0.42f, 0.28f, 0.16f, 0.45f), 1.5f);
        Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(740f, 3f));
        rule.raycastTarget = false;
    }

    private void Notice(float y, string message, Color colour, float size)
    {
        TextMeshProUGUI text = AddText(sheet, message, size, colour, FontStyles.Italic,
            TextAlignmentOptions.Center);
        Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(760f, 40f));
    }

    private static string Numeral(int value)
    {
        switch (value)
        {
            case 1: return Localize.S("一");
            case 2: return Localize.S("二");
            case 3: return Localize.S("三");
            case 4: return Localize.S("四");
            case 5: return Localize.S("五");
            case 6: return Localize.S("六");
            case 7: return Localize.S("七");
            case 8: return Localize.S("八");
            case 9: return Localize.S("九");
            case 10: return Localize.S("十");
            default: return value.ToString();
        }
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

    /// <summary>
    /// 一塊圓角的色。這張榜上的方塊一律走這裡。
    /// </summary>
    /// <remarks>
    /// 直角的色塊在這個畫面上讀起來是「UI 元件」，而周圍的東西 —— 書板、書葉、印、
    /// 菱點 —— 沒有一個是直角的。形狀不一致比顏色不一致更容易被看出來。
    /// </remarks>
    private static Image AddRounded(Transform parent, string name, Color colour, float radius)
    {
        Image image = AddImage(parent, name, colour);
        image.sprite = SettingsUiKit.PlainRoundedSprite(radius);
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
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
        // 框高小於字級 1.5 倍時 TMP 會整段不畫（見 TMP 文字消失）。這裡的列高是算出
        // 來的，常常比字級的 1.5 倍矮。
        text.overflowMode = TextOverflowModes.Overflow;
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
