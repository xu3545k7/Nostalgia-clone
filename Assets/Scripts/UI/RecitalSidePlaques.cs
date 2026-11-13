using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 演奏會畫面左右下角的兩塊銘牌：左邊是演奏者本人，右邊是他的評分明細和上次挑戰。
/// </summary>
/// <remarks>
/// **為什麼這兩塊該存在。** 選曲面板原本被鎖成正方形，兩側各有 560 單位是純粹的
/// 房間照片 —— 44% 的畫面寬度不回答任何問題。這兩塊放的是整個遊戲裡唯一一份「我
/// 現在有多強」：<see cref="PlayerRating"/> 存了四項指標的最佳值、演出過幾首、上
/// 一次挑戰的結果，而在這之前沒有任何畫面讀它。
///
///   左：**演奏者是誰、他在哪個位置。** 頭像、演奏會刻度的評分、評語（初學／普通
///       ／精熟／名家）、離下一個評語還差多少、演出過幾首。
///   右：**這個評分是怎麼來的、上一局發生了什麼。** 四項指標各一條尺，加上上次挑
///       戰那一局的曲子、階層、等級、分數、多久以前，以及**它有沒有算進去**。
///
/// **為什麼兩邊都是演奏者。** 上一版左邊放的是曲子的規模（時長、音符數）。那是一
/// 份**作品**的資料，和右邊的**結果**是兩個主題，兩個角落因此各講一件事，中間夾著
/// 曲繪又是第三件。現在兩個角落是同一個主題的兩半：左邊是結論（一個數字、一個評
/// 語），右邊是證據（四條尺、最後一局）。曲子是誰，中央那張卡片從頭到尾都在講。
///
/// **為什麼只讀演奏會那一段刻度。** 見 <see cref="Band"/>。
///
/// **為什麼不是練習室那一套。** 分界線是一句話：演奏會回答「今晚演哪一首」，練習
/// 室回答「我要練什麼」。所以這裡印的是跨全庫的**結果**，不是雷達圖、密度曲線、難
/// 點區段 —— 那些今天就在練習室裡。只要這兩塊長出搜尋框、排序鈕，或開始逐段拆解
/// 一份譜，演奏會就變成練習室了。
///
/// **為什麼純顯示、不吃點擊。** 畫面左右下角是手指在平板上甩動的自然起點，而
/// `IsPointerOnCarouselSurface` 只要 raycast 命中任何 `Selectable` 就會放棄整個手
/// 勢。在那兩個角落放按鈕，等於把最順手的滑動區收掉。整塊牌（含每一個字、每一條
/// 尺、徽章）的 raycastTarget 都是關的。
/// </remarks>
internal sealed class RecitalSidePlaques : MonoBehaviour
{
    // ── 版面 ──────────────────────────────────────────────────────────
    //
    // 一塊牌 780 x 216。高度是被上面那排難度寶石逼出來的：側難度牌的下緣在畫面中
    // 心下方 461.2，16:9 的畫布半高是 720，所以底下這層架子只有 720−461.2−24 =
    // 234.8 單位。216 是塞得進去、又放得下「抬頭＋四列＋兩行」的最大值，剩下的
    // 18.8 是牌頂到寶石的縫。要再高就會壓到寶石 —— 那不是擁擠，是壞掉。
    private const float PlaqueWidth = 780f;
    private const float PlaqueHeight = 216f;

    /// <summary>牌面最窄能窄到哪。比這個還窄就整塊退場。</summary>
    /// <remarks>
    /// 上一版是 420，而 420 是「排得下四條尺」的下限，不是「讀得完一行字」的下限：
    /// 上次挑戰那兩行在 420 寬的框裡會縮到 16 級還是滿出牌外。真正會遇到的最窄是
    /// 5:4（畫布 2147 寬 → room 573），所以門檻訂在 520 —— 比它有得救，比它窄的
    /// （直立螢幕、21:9）本來就整塊退場。
    /// </remarks>
    private const float MinWidth = 520f;

    /// <summary>整塊能縮到多小。再小字就讀不動了，那時候才真的退場。</summary>
    private const float MinScale = 0.62f;
    private const float Margin = 24f;

    /// <summary>側難度牌的外緣。兩塊牌不能越過它，否則會疊在寶石上。</summary>
    private const float SideStripEdge = 476.5f;
    /// <summary>側難度牌的下緣（距畫面中心）。底下那層架子的高度是照它算的。</summary>
    private const float SideStripBottom = 461.2f;

    // ── 牌面內部的格線 ────────────────────────────────────────────────
    //
    // **為什麼現在是 20，不再是 38。** 上一版的牌面是平頂六角形，而它在內縮 14 的地
    // 方還畫了一圈**同樣是六角形**的細金線。那圈線的左頂點只有在牌面的半高處才在
    // x=14 —— 往上往下它都跟著斜邊往右退，在最上和最下那一行字的高度上退到 x≈35，
    // 所以內容從 26 開始的話，抬頭那幾個字會**騎在金線上**。
    //
    // 病根是那條**跑滿全高的斜邊**，不是內縮的算法：換成數學上完美的多邊形 offset
    // 只把那條線從 35.60 拉回 33.08，賺 2.5，Pad 還是得 37。現在輪廓換掉了 —— 左牌
    // 的造型只在外端那顆鼻子上、右牌是方切，兩塊的四條直邊上金線恆在 15.6 —— 所以
    // Pad 直接降到 20，離金線內緣還有 4.4 的縫。
    //
    // 這 18 單位是整件事裡最值錢的一筆：右牌的四條評分長條在 780 寬時從 518 變 554
    // （+7%），在 5:4 的 572.8 窄版時從 310.8 變 346.8（**+11.6%**）。
    private const float Pad = 20f;
    private const float CaptionColumn = 96f;   // 「上次挑戰」、四項的名字
    private const float ContentX = 124f;       // = Pad + CaptionColumn + 8
    private const float ValueColumn = 72f;     // 「4,280」放得下的最小寬度
    private const float MedallionSize = 110f;

    // ── 字級：四階 ────────────────────────────────────────────────────
    //
    // 這塊牌只有四種字：註記、等第字母、評語、評分。多一階就會出現「22 和 24 並排
    // 看不出是兩種東西，只看得出沒對齊」。
    private const float TextCaption = 22f;
    private const float TextBody = 26f;
    private const float TextLabel = 30f;
    private const float TextHero = 64f;

    // ── 顏色 ──────────────────────────────────────────────────────────
    private static readonly Color Gilt = new Color(0.92f, 0.78f, 0.44f, 1f);
    private static readonly Color Cream = new Color(0.94f, 0.90f, 0.80f, 1f);
    private static readonly Color CreamDim = new Color(0.86f, 0.80f, 0.68f, 0.82f);

    /// <summary>還沒到門檻、還沒計入的那一種紅。</summary>
    /// <remarks>
    /// 結算畫面用的是 (0.55, 0.22, 0.14) —— 那個值是對著淺色的紙調的。同一個紅放在
    /// 這塊近黑的牌面（0.10, 0.055, 0.035）上只比背景亮一點點，等於看不見。往上提
    /// 一級，色相不動，這樣兩個畫面講的還是同一件事。
    /// </remarks>
    private static readonly Color Shortfall = new Color(0.82f, 0.45f, 0.32f, 1f);

    /// <summary>這兩塊牌只講演奏會那一段刻度。</summary>
    /// <remarks>
    /// 轉盤本身就是演奏會的空間（練習模式在練習室，見 `EnterCurrentMode`），所以
    /// 這裡讀的是 recital 那一段。兩段刻度不能混：練習模式的旋律和表情有大量「沒被
    /// 要求就給滿分」，混在一起數字會無緣無故跳一千。
    ///
    /// 上次挑戰那一局**可能**是練習模式打的（PlayerRating 收每一局，包含練習）。
    /// 那一行因此會自己標出「練習」—— 不標的話，一個練習刻度的差距會被讀成演奏會
    /// 刻度的差距。
    /// </remarks>
    private const bool Band = true;

    /// <summary>這一段刻度整體往上多少。畫面上每一條門檻都照它算。</summary>
    /// <remarks>
    /// 門檻不寫死 4000 / 6000：刻度是使用者定的，而它只定在 `PlayerRating` 一個地方。
    /// </remarks>
    private static float Shift => Band ? PlayerRating.RecitalBand : 0f;

    private RectTransform canvasRoot;
    private CanvasGroup performerGroup;
    private CanvasGroup detailGroup;
    private RectTransform performerRect;
    private RectTransform detailRect;

    // 左牌
    private RectTransform medallionRect;
    private AspectFillRawImage portraitImage;
    private TextMeshProUGUI performerHeading;
    private TextMeshProUGUI performerName;
    private TextMeshProUGUI monogram;
    private TextMeshProUGUI ratingValue;
    private TextMeshProUGUI ratingTitle;
    private TextMeshProUGUI ratingNext;
    private TextMeshProUGUI playedCount;

    // 右牌
    private readonly TextMeshProUGUI[] criterionNames = new TextMeshProUGUI[4];
    private readonly TextMeshProUGUI[] criterionValues = new TextMeshProUGUI[4];
    private readonly CriterionBarGraphic[] criterionBars = new CriterionBarGraphic[4];
    private TextMeshProUGUI detailCeiling;
    private TextMeshProUGUI attemptGrade;
    private TextMeshProUGUI attemptLine;
    private TextMeshProUGUI attemptResult;

    private SongSelectionManager manager;

    /// <summary>上次數過的時候曲庫有幾首。−1 = 還沒數過。</summary>
    private int countedLibrary = -1;


    public static RecitalSidePlaques Attach(RectTransform panel, SongSelectionManager owner)
    {
        if (panel == null || owner == null) return null;
        var existing = panel.GetComponentInChildren<RecitalSidePlaques>(true);
        if (existing != null) return existing;

        var host = new GameObject("RecitalSidePlaques", typeof(RectTransform));
        host.layer = panel.gameObject.layer;
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var plaques = host.AddComponent<RecitalSidePlaques>();
        plaques.manager = owner;
        plaques.Build(rect);
        return plaques;
    }

    private void Build(RectTransform root)
    {
        canvasRoot = root;
        BuildPerformer(root);
        BuildDetail(root);
        RefreshRecord();
    }

    /// <summary>
    /// 左牌：演奏者。
    /// </summary>
    /// <remarks>
    /// 由上往下是一句話：**我是誰（抬頭）→ 我值多少（4,280）→ 那叫什麼（普通）→
    /// 還差多少（距精熟 1,720）→ 我來過幾次（已演出 37 / 344 首）**。頭像另立一欄
    /// 在左邊，因為它不是這句話裡的一個字，是講話的那個人。
    /// </remarks>
    private void BuildPerformer(RectTransform root)
    {
        // 牌面 W x 216，由上往下（數字是**字的上緣離牌頂多遠**）：
        //   18   抬頭「演奏者　演奏會評分」
        //   52   評分 4,280（64 級，字身大約到 100）
        //   112  評語「普通」　　　　　　　右緣：距精熟 1,720
        //   158  已演出 37 / 344 首
        // 字只能落在離牌頂 14~202 之間 —— 牌面內縮 14 的那圈金線上下兩段就在那裡。
        performerRect = MakePlaque(root, "Performer", new Vector2(0f, 0f), new Vector2(Margin, Margin), true);
        performerGroup = performerRect.gameObject.AddComponent<CanvasGroup>();

        medallionRect = MakeMedallion(performerRect);
        var editProfile = medallionRect.GetComponent<UnityEngine.UI.Button>();
        if (editProfile != null)
        {
            editProfile.onClick.AddListener(() =>
            {
                UiSfx.Open();
                PlayerProfileCard.Show(() =>
                {
                    SetPortrait(PlayerProfile.Portrait, true);
                    RefreshRecord();
                });
            });
        }

        float column = Pad + MedallionSize + 22f;   // 頭像右邊 22 留白後開始的文字欄

        // 抬頭那一行是**名字**，不是「演奏者」三個字。
        //
        // 這是這塊牌最初的漏洞：它叫「演奏者」，卻從頭到尾沒有寫過那個人是誰 ——
        // 抬頭是寫死的字串、頭像在沒有圖的時候是一枚空徽章，所以整塊讀起來是「一張
        // 還沒填的表」。使用者兩次回報「玩家不見了 / 儲存完沒出現在畫面上」都是同一
        // 件事，而上一次的處置是在左上角另外掛一塊徽章繞過去，沒有修根本。
        //
        // 刻度的小標（「演奏會評分」）退到同一行的右端：它是給那個大數字用的註記，
        // 不是這塊牌的標題。
        performerName = MakeLabel(performerRect, "Name", TextLabel, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 1f), new Vector2(column, -14f), new Vector2(300f, 34f));
        performerName.fontStyle = FontStyles.Bold;
        performerName.color = Cream;

        performerHeading = MakeLabel(performerRect, "Heading", TextCaption, TextAlignmentOptions.MidlineRight,
            new Vector2(1f, 1f), new Vector2(-Pad, -18f), new Vector2(180f, 26f));
        performerHeading.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        performerHeading.color = Gilt;
        SetText(performerHeading, Localize.S("演奏會評分"));

        // 大數字往左 4：64 級的數字左邊有側邊空隙，照框對齊的話它的字幹會比上面那
        // 行字往右縮一格，看起來像沒對齊。
        ratingValue = MakeLabel(performerRect, "Rating", TextHero, TextAlignmentOptions.TopLeft,
            new Vector2(0f, 1f), new Vector2(column - 4f, -50f), new Vector2(280f, 68f));
        ratingValue.fontStyle = FontStyles.Bold;

        ratingTitle = MakeLabel(performerRect, "Title", TextLabel, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 1f), new Vector2(column, -112f), new Vector2(104f, 30f));
        ratingTitle.fontStyle = FontStyles.Bold;

        // 「還差多少」貼右緣：它是這一行的另一端，靠在評語旁邊會被讀成評語的一部分。
        // 框高和評語一樣 30，兩個字才會落在同一條中線上。
        ratingNext = MakeLabel(performerRect, "Next", TextCaption, TextAlignmentOptions.MidlineRight,
            new Vector2(1f, 1f), new Vector2(-Pad, -112f), new Vector2(132f, 30f));
        ratingNext.color = CreamDim;

        playedCount = MakeSpanLabel(performerRect, "Played", TextCaption, TextAlignmentOptions.MidlineLeft,
            column, Pad, -156f, 26f);
        playedCount.color = CreamDim;
    }

    /// <summary>
    /// 右牌：評分明細和上次挑戰。
    /// </summary>
    /// <remarks>
    /// **為什麼四項要各一條尺。** 總評分是四項的等權平均，所以一個數字在動的時候，
    /// 「哪一項在動」是唯一還沒被回答的問題。四個 0–7000 的數字排成一欄會被讀成一
    /// 串統計；四條尺會被讀成四個高度，而比高度是看這一塊牌唯一會做的動作。
    ///
    /// **為什麼上次挑戰在分隔線下面。** 上面四條是**累積**（只加不減的最佳值），下
    /// 面兩行是**一次**（剛剛那一局）。兩者混在同一疊裡，那一局的分數會被當成第五
    /// 項指標。一條金線就夠把「長期」和「剛剛」分開。
    /// </remarks>
    private void BuildDetail(RectTransform root)
    {
        // 牌面 W x 216，由上往下：
        //   14        抬頭「評分明細」　　　　　　　右緣：滿分 7000
        //   42~141    四項 × 25 一列：名字（38）｜尺（142~右緣退 120）｜數字（貼右緣）
        //   147       金線
        //   153       上次挑戰　｜　曲名 · 階層 Lv.
        //   176       等第　　　｜　分數 · 多久以前 · 有沒有計入
        // 最下面那一行的字底在 199，而牌面內縮 14 的那圈金線在 200.4 —— 只剩 1.4。
        // 這一疊是滿的：再多一行就得把 216 加高，而 216 已經是架子的上限。
        detailRect = MakePlaque(root, "Detail", new Vector2(1f, 0f), new Vector2(-Margin, Margin), false);
        detailGroup = detailRect.gameObject.AddComponent<CanvasGroup>();

        TextMeshProUGUI heading = MakeLabel(detailRect, "Heading", TextCaption,
            TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(Pad, -14f),
            new Vector2(280f, 26f));
        heading.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        heading.color = Gilt;
        SetText(heading, Localize.S("評分明細"));

        // 滿分寫出來：四條尺沒有刻度標的話，「4,280」是高是低要靠猜。
        detailCeiling = MakeLabel(detailRect, "Ceiling", TextCaption, TextAlignmentOptions.MidlineRight,
            new Vector2(1f, 1f), new Vector2(-Pad, -14f), new Vector2(160f, 26f));
        detailCeiling.color = CreamDim;

        string[] names = PlayerRating.CriterionNames;
        for (int i = 0; i < 4; i++)
        {
            float top = -42f - i * 25f;

            criterionNames[i] = MakeLabel(detailRect, "Name" + i, TextCaption,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(Pad, top),
                new Vector2(CaptionColumn, 24f));
            criterionNames[i].color = Cream;
            SetText(criterionNames[i], names != null && i < names.Length ? names[i] : string.Empty);

            var barObject = new GameObject("Bar" + i, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(CriterionBarGraphic));
            barObject.layer = detailRect.gameObject.layer;
            var barRect = barObject.GetComponent<RectTransform>();
            barRect.SetParent(detailRect, false);
            // 尺身跟著牌面伸縮（兩端各釘在牌緣內側），不是每幀重算寬度 —— 牌面的
            // 寬度會隨畫面比例變，而 anchor 本來就是為這件事準備的。
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.offsetMin = new Vector2(ContentX, top - 19f);
            barRect.offsetMax = new Vector2(-(Pad + ValueColumn + 10f), top - 5f);
            criterionBars[i] = barObject.GetComponent<CriterionBarGraphic>();
            criterionBars[i].raycastTarget = false;
            // 尺身和刻度是「印在牌上」的，所以要用牌上讀得到的墨。結算畫面那組墨色
            // 是對著紙調的，放在這塊近黑的牌上整條尺會消失。
            criterionBars[i].Engrave(new Color(0.88f, 0.82f, 0.70f, 0.52f), 3 + i * 13);

            criterionValues[i] = MakeLabel(detailRect, "Value" + i, TextCaption,
                TextAlignmentOptions.MidlineRight, new Vector2(1f, 1f), new Vector2(-Pad, top),
                new Vector2(ValueColumn, 24f));
            criterionValues[i].fontStyle = FontStyles.Bold;
        }

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.layer = detailRect.gameObject.layer;
        var ruleRect = rule.GetComponent<RectTransform>();
        ruleRect.SetParent(detailRect, false);
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = new Vector2(1f, 1f);
        ruleRect.pivot = new Vector2(0f, 1f);
        ruleRect.offsetMin = new Vector2(Pad, -149f);
        ruleRect.offsetMax = new Vector2(-Pad, -147f);
        var ruleImage = rule.GetComponent<Image>();
        ruleImage.color = new Color(Gilt.r, Gilt.g, Gilt.b, 0.38f);
        ruleImage.raycastTarget = false;

        TextMeshProUGUI attemptCaption = MakeLabel(detailRect, "AttemptCaption", TextCaption,
            TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(Pad, -153f),
            new Vector2(CaptionColumn, 24f));
        attemptCaption.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        attemptCaption.color = Gilt;
        SetText(attemptCaption, Localize.S("上次挑戰"));

        // 曲名自己一個標籤：它是使用者曲庫裡的字，可能是日文，要走 ApplyContentFont
        // （不照介面語言換字型）。旁邊那幾個固定的中文詞走 ApplyLocalizedFont。混在
        // 同一個標籤裡的話，兩邊都只能挑一種字型，其中一種一定會出現空框。
        attemptLine = MakeSpanLabel(detailRect, "Attempt", TextCaption, TextAlignmentOptions.MidlineLeft,
            ContentX, Pad, -153f, 24f);
        Fit(attemptLine);

        // 等第字母貼著分數（右對齊在名字欄裡），才讀成「S 896,420」而不是一個孤零
        // 零的字母掛在左邊。
        attemptGrade = MakeLabel(detailRect, "Grade", TextBody, TextAlignmentOptions.MidlineRight,
            new Vector2(0f, 1f), new Vector2(Pad, -176f), new Vector2(CaptionColumn, 24f));
        attemptGrade.fontStyle = FontStyles.Bold;
        attemptGrade.color = Gilt;

        attemptResult = MakeSpanLabel(detailRect, "Result", TextCaption, TextAlignmentOptions.MidlineLeft,
            ContentX, Pad, -176f, 24f);
        attemptResult.color = CreamDim;
        Fit(attemptResult);
    }

    private static RectTransform MakePlaque(RectTransform parent, string name, Vector2 corner,
        Vector2 offset, bool left)
    {
        var plaqueObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(PlaqueGraphic));
        plaqueObject.layer = parent.gameObject.layer;
        var rect = plaqueObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(PlaqueWidth, PlaqueHeight);

        var graphic = PlaqueGraphic.Build(rect, left ? PlaqueShape.Bay : PlaqueShape.Card);
        // 兩塊牌不是同一個形狀 —— 它們在同一個畫面的左右下角同時出現，一模一樣的話
        // 讀起來是「同一個東西印了兩次」。左邊裝的是一張臉，所以它的外端是一顆半橢
        // 圓的鼻子，肖像就坐在裡面、和它同心；右邊裝的是四條量尺和八段文字，那是一
        // 張**印刷品**，裁刀沒有半徑，所以它方切，角上只斜掉 4。
        //
        // 兩塊的裝訂端（朝畫面中央那一側）都是方角，所以這一對讀起來是「一本攤開的
        // 書的兩葉，往外翻」。
        //
        // 共用的是金屬不是家具：同一個金色、同一組三層填色、同一條 14→15.6 的金線、
        // 同一圈 1 單位的羽化。
        // 鼻子的深度要等於肖像的中心 x，否則那個造型就開始花版面。
        if (left) graphic.Nose = Pad + MedallionSize * 0.5f;
        ClassicalBookUITheme.AddShadow(graphic.OutlineTarget, new Color(0f, 0f, 0f, 0.5f), new Vector2(9f, -9f));
        return rect;
    }

    /// <summary>頭像的位置：一枚空的刻紋徽章，同時是改名換頭像的入口。</summary>
    /// <remarks>
    /// **為什麼入口在這裡。** 左上角那塊 430x116 的徽章被拿掉了（它和這塊牌講的是同
    /// 一件事，而且它的錨點在左、分組鈕的錨點在中間，畫面一窄就一定疊在一起），所以
    /// 「改自己的資料」要有一個新家。點自己的臉去改自己的臉，比藏在設定第二十六列裡
    /// 好找 —— 而且設定頁的索引是寫死的，插一列會把後面每一個索引推掉。
    ///
    /// **只有肖像可以點，整塊牌不行。** `IsPointerOnCarouselSurface` 只要射線命中任
    /// 何 `Selectable` 就放棄整個手勢，而這塊牌在左下角、正好是手指開始甩轉盤的地
    /// 方。110x110 的一塊擋掉的手勢，遠少於 780x216。
    /// </remarks>
    private static RectTransform MakeMedallion(RectTransform parent)
    {
        var medallion = new GameObject("PortraitMedallion", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(PortraitMedallionGraphic), typeof(UnityEngine.UI.Button));
        medallion.layer = parent.gameObject.layer;
        var rect = medallion.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        // 縱向對著**文字那一疊**的中線（字從 -20 排到 -180，中心 -100），不是對著牌
        // 面的中線：牌面底下還留了 36 的邊，照牌面對齊的話徽章看起來會偏低。
        rect.anchoredPosition = new Vector2(Pad, -45f);
        rect.sizeDelta = new Vector2(MedallionSize, MedallionSize);
        var art = medallion.GetComponent<PortraitMedallionGraphic>();
        art.raycastTarget = true;
        // 沒有頭像的時候刻一個縮寫上去。一枚**空**徽章讀起來是「圖載不出來」，刻了
        // 字的讀起來是「這個人還沒放照片」—— 同樣沒有圖，一個是壞掉，一個是本人。
        var mono = new GameObject("Monogram", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        mono.layer = medallion.layer;
        var monoRect = (RectTransform)mono.transform;
        monoRect.SetParent(rect, false);
        monoRect.anchorMin = new Vector2(0.14f, 0.14f);
        monoRect.anchorMax = new Vector2(0.86f, 0.86f);
        monoRect.offsetMin = Vector2.zero;
        monoRect.offsetMax = Vector2.zero;
        var button = medallion.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = art;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        return rect;
    }

    /// <summary>固定寬度的字框，從一個角落量。</summary>
    private static TextMeshProUGUI MakeLabel(RectTransform parent, string name, float size,
        TextAlignmentOptions alignment, Vector2 corner, Vector2 offset, Vector2 box)
    {
        var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = parent.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = box;
        return Style(labelObject.GetComponent<TextMeshProUGUI>(), size, alignment);
    }

    /// <summary>跟著牌面伸縮的字框：左右各釘在牌緣內側。</summary>
    /// <remarks>
    /// 牌面的寬度會隨畫面比例在 520~780 之間變。用 anchor 撐開的框自己會跟著走，
    /// 用 sizeDelta 的框得在 LateUpdate 裡一個一個重算 —— 而漏掉一個是看不出來的，
    /// 它只是在某個比例的螢幕上默默地把字切掉。
    /// </remarks>
    private static TextMeshProUGUI MakeSpanLabel(RectTransform parent, string name, float size,
        TextAlignmentOptions alignment, float left, float right, float top, float height)
    {
        var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = parent.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, top - height);
        rect.offsetMax = new Vector2(-right, top);
        return Style(labelObject.GetComponent<TextMeshProUGUI>(), size, alignment);
    }

    private static TextMeshProUGUI Style(TextMeshProUGUI label, float size, TextAlignmentOptions alignment)
    {
        label.fontSize = size;
        label.alignment = alignment;
        label.color = Cream;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // TMP 在「框高 < 字級 × 1.5」時整段**不畫**（Truncate），而這塊牌的框高是照
        // 版面給的，一定比字級的 1.5 倍矮。Overflow 才畫得出來。
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }

    /// <summary>長度會變的那幾行：縮小一點，不要掉尾巴。</summary>
    /// <remarks>
    /// 下限 16 是照結算畫面那個 FitValue 的理由訂的：低於這個就不是「小一點」，是
    /// 「讀不到」。寧可讓極端的長度去撞版面被發現，也不要讓它安靜地縮成看不見。
    /// </remarks>
    private static void Fit(TextMeshProUGUI label)
    {
        if (label == null) return;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = TextCaption;
    }

    /// <summary>填字，然後照字的內容挑字型。</summary>
    /// <remarks>
    /// `ApplyLocalizedFont` 看的是 `text.text`，所以**一定要填完才叫** —— 先叫的話
    /// 它看到的是空字串，判不出有中日文，整行就留在沒有漢字的預設字型上（滿版空
    /// 框）。這一層包起來就是為了不會有人漏掉那個順序。
    /// </remarks>
    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label == null) return;
        label.text = value ?? string.Empty;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
    }

    /// <summary>曲名、作者這種曲庫裡的字：不照介面語言換字型。</summary>
    private static void SetContentText(TextMeshProUGUI label, string value)
    {
        if (label == null) return;
        label.text = value ?? string.Empty;
        ClassicalBookUITheme.ApplyContentFont(label);
    }

    /// <summary>
    /// 把真實的頭像掛進徽章裡。
    /// </summary>
    /// <remarks>
    /// 沒有頭像的時候徽章畫的是一個空的刻紋槽（那讀起來是「這裡留給頭像」，而一
    /// 個空方框讀起來是「圖載不出來」）。
    ///
    /// `circular` 給 true 表示那張圖**本身已經是圓的**（alpha 已經切好，見
    /// `PlayerProfile.SetPortrait`）—— 那就不必內接，直接填滿內壁，因為它的四個角
    /// 本來就是透明的。
    ///
    /// 金框已經是畫框了，圖只要填進內壁裡就好，不必再自己畫邊。填的是**橢圓的內接
    /// 矩形**（半徑各除以根號二），因為圖是方的、徽章是橢圓的 —— 照內壁的外接框填
    /// 的話四個角會戳出金框。要讓圖真的填滿整個橢圓，得換成一張已經切成圓形的貼
    /// 圖，或在徽章上掛一顆 `Mask`（它的網格 alpha 就是遮罩）；先不做，因為選哪一
    /// 種要看頭像從哪裡來。
    /// </remarks>
    public void SetPortrait(Texture portrait, bool circular = false)
    {
        if (medallionRect == null) return;
        if (portraitImage == null)
        {
            var portraitObject = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(AspectFillRawImage));
            portraitObject.layer = medallionRect.gameObject.layer;
            var rect = portraitObject.GetComponent<RectTransform>();
            rect.SetParent(medallionRect, false);
            // 方的圖要內接（半徑各除以根號二），已經切圓的圖直接貼著內壁。
            rect.anchorMin = circular ? new Vector2(0.085f, 0.085f) : new Vector2(0.257f, 0.224f);
            rect.anchorMax = circular ? new Vector2(0.915f, 0.915f) : new Vector2(0.743f, 0.776f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            portraitImage = portraitObject.GetComponent<AspectFillRawImage>();
            portraitImage.raycastTarget = false;
        }
        portraitImage.texture = portrait;
        portraitImage.enabled = portrait != null;
        portraitImage.UpdateUVRect();
    }

    private void OnEnable()
    {
        // 面板是 SetActive 開關的（ShowSelection / HideSelection），所以從遊戲或結算
        // 回來這裡一定會被呼叫一次 —— 評分就是在那之間變的。
        RefreshRecord();
    }

    /// <summary>
    /// 兩塊牌一起重讀。全庫掃一次，不進 Update。
    /// </summary>
    /// <remarks>
    /// 名字留著不改：唯一的呼叫端在 `SongSelectionManager.ShowSelection`，而那個檔
    /// 案不屬於這次的改動範圍。它現在做的事比名字大一點 —— 演奏者、四項指標、上次
    /// 挑戰全部在這裡填。
    /// </remarks>
    public void RefreshRecord()
    {
        RefreshPerformer();
        RefreshDetail();
    }

    private void RefreshPerformer()
    {
        if (ratingValue == null) return;

        // 名字和縮寫每次都重寫：改完資料回來、第一次建檔完成，都走這一條。
        if (performerName != null)
        {
            SetText(performerName, PlayerProfile.Name);
            ClassicalBookUITheme.ApplyLocalizedFont(performerName);
        }
        if (monogram == null && medallionRect != null)
        {
            Transform found = medallionRect.Find("Monogram");
            if (found != null) monogram = found.GetComponent<TextMeshProUGUI>();
            if (monogram != null)
            {
                monogram.fontSize = 44f;
                monogram.alignment = TextAlignmentOptions.Center;
                monogram.fontStyle = FontStyles.Bold;
                monogram.raycastTarget = false;
                monogram.overflowMode = TextOverflowModes.Overflow;
                monogram.textWrappingMode = TextWrappingModes.NoWrap;
                // 這一顆是自己 new 出來的，沒走 MakeLabel 的 Style()，字型要自己指定
                // —— 名字的第一個字很可能是中文，用預設字型會變成豆腐。
                ClassicalBookUITheme.ApplyLocalizedFont(monogram);
            }
        }
        if (monogram != null)
        {
            bool hasPortrait = portraitImage != null && portraitImage.enabled
                && portraitImage.texture != null;
            monogram.enabled = !hasPortrait;
            SetText(monogram, PlayerProfile.Monogram);
            monogram.color = new Color(PlayerProfile.Seal.r * 0.45f + 0.55f,
                PlayerProfile.Seal.g * 0.45f + 0.50f, PlayerProfile.Seal.b * 0.45f + 0.40f, 0.95f);
        }

        float rating = 0f;
        float ceiling = 1f;
        try
        {
            rating = PlayerRating.Current(Band);
            ceiling = Mathf.Max(1f, PlayerRating.Ceiling(Band));
        }
        catch { }

        // 0 分印「—」不印「0」：第一次進來的人沒有 0 分，他是**還沒有**分數。
        SetText(ratingValue, rating > 0.5f ? $"{rating:N0}" : "—");
        ratingValue.color = rating > 0.5f ? Cream : CreamDim;

        string title = Localize.S("尚無紀錄");
        try { title = PlayerRating.Title(rating, Band); } catch { }
        SetText(ratingTitle, title);
        // 精熟以上才上金。金色在這套介面裡只有兩個意思：結構的邊，和值得注意的那一
        // 個 —— 每一階都給金的話，金就不是重點了。
        ratingTitle.color = rating >= PlayerRating.Proficient + Shift ? Gilt : Cream;

        SetText(ratingNext, NextStep(rating, ceiling));
        RefreshPlayedCount(true);
    }

    /// <summary>離下一個評語還差多少。</summary>
    /// <remarks>
    /// 名家的門檻就是這一段的滿分（Lv15 × 400 ＋ 演奏會的一千），所以直接用
    /// `Ceiling` —— 這兩個值只要 `PerLevel` 不是 400 就會分開，而那個時候先壞的是
    /// `PlayerRating.Title` 裡的 6000，不是這裡。
    /// </remarks>
    private static string NextStep(float rating, float ceiling)
    {
        if (rating <= 0.5f) return Localize.S("尚未演出");
        float ordinary = PlayerRating.Ordinary + Shift;
        float proficient = PlayerRating.Proficient + Shift;
        // 用字要和 PlayerRating.Title 對齊（精熟→熟練），不然同一塊牌上兩種說法。
        if (rating < ordinary) return Localize.SF("距普通 {0:N0}", ordinary - rating);
        if (rating < proficient) return Localize.SF("距精熟 {0:N0}", proficient - rating);
        if (rating < ceiling) return Localize.SF("距名家 {0:N0}", ceiling - rating);
        return Localize.SF("滿分 {0:N0}", ceiling);
    }

    /// <summary>
    /// 已演出幾首。**只在曲庫的長度變過之後才重算。**
    /// </summary>
    /// <remarks>
    /// `Attach` 跑在 `LoadSongOptions` **之前**（見 `SongSelectionManager.Start`），
    /// 所以蓋牌的那一刻曲庫是空的。上一版因此在開機後整行都是空白，要等打一局回到選
    /// 曲才會長出來 —— 而那正好是最不需要它的時候。改成順便盯著曲庫的長度，0 → 344
    /// 的那一幀補算一次。
    ///
    /// 不能每幀算：`LocalScoreRecords.GetBestScore` 內部是**線性**找曲子，一次全庫掃
    /// 是幾十萬次字串比對。
    /// </remarks>
    private void RefreshPlayedCount(bool force)
    {
        if (playedCount == null) return;
        int library = 0;
        try
        {
            IReadOnlyList<SongSelectionManager.SongOption> all = manager != null ? manager.AllSongs : null;
            library = all != null ? all.Count : 0;
        }
        catch { }
        if (!force && library == countedLibrary) return;
        countedLibrary = library;
        SetText(playedCount, PlayedLine());
    }

    /// <summary>
    /// 演出過幾首 / 全庫幾首。
    /// </summary>
    /// <remarks>
    /// 一首只要有任何一個難度留下過分數就算演出過，所以找到一個就跳出 —— 上一版還
    /// 在這裡逐難度撈 `GetTopScores` 找最新的一筆，而那份資料現在由
    /// `PlayerRating.LastAttempt` 直接給（而且它連練習模式那幾局都記得）。
    /// </remarks>
    private string PlayedLine()
    {
        int played = 0;
        int total = 0;

        IReadOnlyList<SongSelectionManager.SongOption> all = null;
        try { all = manager != null ? manager.AllSongs : null; } catch { }
        if (all == null) return string.Empty;

        for (int i = 0; i < all.Count; i++)
        {
            SongSelectionManager.SongOption song = all[i];
            if (song == null) continue;
            total++;

            List<SongSelectionManager.SongOption> variants = song.difficultyVariants;
            int count = variants != null && variants.Count > 0 ? variants.Count : 1;
            for (int v = 0; v < count; v++)
            {
                SongSelectionManager.SongOption chart =
                    variants != null && variants.Count > 0 ? variants[v] : song;
                if (chart == null) continue;
                int best = 0;
                try { best = LocalScoreRecords.GetBestScore(chart); } catch { }
                if (best <= 0) continue;
                played++;
                break;
            }
        }

        return total > 0 ? Localize.SF("已演出 {0} / {1} 首", played, total) : string.Empty;
    }

    private void RefreshDetail()
    {
        if (criterionBars[0] == null) return;

        float ceiling = 1f;
        float[] marks = null;
        try
        {
            ceiling = Mathf.Max(1f, PlayerRating.Ceiling(Band));
            marks = PlayerRating.Criteria(Band);
        }
        catch { }

        SetText(detailCeiling, Localize.SF("滿分 {0:N0}", ceiling));

        // 達標和沒達標自己說出來，和結算畫面同一條規則：金色是過了普通那條線，暖紅
        // 是還沒。四項裡只有一項落後的時候，這是唯一看得出來的地方。
        float pass = PlayerRating.Ordinary + Shift;
        for (int i = 0; i < 4; i++)
        {
            float earned = marks != null && i < marks.Length ? Mathf.Max(0f, marks[i]) : 0f;
            criterionBars[i].Set(earned / ceiling, earned >= pass ? Gilt : Shortfall);
            SetText(criterionValues[i], earned > 0.5f ? $"{earned:N0}" : "—");
            criterionValues[i].color = earned > 0.5f ? Cream : CreamDim;
        }

        PlayerRating.Attempt attempt = null;
        try { attempt = PlayerRating.LastAttempt; } catch { }

        if (attempt == null)
        {
            // 新玩家不該看到兩個空框。
            SetText(attemptGrade, string.Empty);
            SetText(attemptLine, Localize.S("尚無挑戰紀錄"));
            SetText(attemptResult, Localize.S("打一局就會記下來"));
            return;
        }

        SetContentText(attemptLine, AttemptTitle(attempt));

        string grade = string.Empty;
        try { grade = LocalScoreRecords.GetRank(attempt.score); } catch { }
        SetText(attemptGrade, grade);

        SetText(attemptResult, AttemptResult(attempt));
    }

    /// <summary>上次挑戰打的是什麼：曲子、階層、等級。</summary>
    /// <remarks>
    /// 階層用難度色印，和選曲畫面的寶石、卡片同一組顏色 —— 「EXTREME」四個字母在
    /// 這裡是紫的，在那裡也是紫的，不用讀字就知道那一局有多難。
    /// </remarks>
    private static string AttemptTitle(PlayerRating.Attempt attempt)
    {
        string song = OneLine(attempt.song);
        if (string.IsNullOrWhiteSpace(song)) song = Localize.S("（不知名的曲子）");

        string tier = string.IsNullOrWhiteSpace(attempt.tier) ? string.Empty : attempt.tier.ToUpperInvariant();
        if (attempt.level > 0) tier = tier.Length > 0 ? $"{tier} Lv.{attempt.level}" : $"Lv.{attempt.level}";
        if (tier.Length > 0)
        {
            Color hue = DifficultyVisualPalette.For(attempt.tier, attempt.level);
            song = $"{song}　<color=#{ColorUtility.ToHtmlStringRGB(hue)}>{tier}</color>";
        }

        // 練習模式那一局要標出來：它的分數和差距是**練習那一段刻度**的，不標的話會
        // 被讀成演奏會的數字，而兩段刻度差一千。
        if (!attempt.recital) song = Localize.SF("{0}　· 練習", song);
        return song;
    }

    /// <summary>那一局的結果，以及它有沒有算進評分。</summary>
    /// <remarks>
    /// **差多少要照實寫。** 評分只加不減 —— 打得比目前實力差不會把它拉下來，否則沒
    /// 有人敢挑戰難的曲子。但「沒有變化」和「沒有發生」是兩件事：不寫差距的話，畫面
    /// 看起來就像剛剛那一局沒有被記錄。所以照實寫差多少，寫成一個**距離**（差 240
    /// 分，未計入），不是一句評語 —— 是資訊，不是責備。
    /// </remarks>
    private static string AttemptResult(PlayerRating.Attempt attempt)
    {
        string head = Localize.SF("{0:N0} 分 · {1}", attempt.score, Ago(attempt.atUtcTicks));
        if (attempt.gap > 0.5f)
        {
            // 差距用暖紅，和四條尺上「還沒達標」同一個顏色；縮到 84% 是為了在最窄
            // 的牌面（520）上這一整行還能留在 22 級，不用縮到 16。
            string hex = ColorUtility.ToHtmlStringRGB(Shortfall);
            return head + Localize.SF(" · <size=84%><color=#{0}>差 {1:N0} 分，未計入</color></size>",
                hex, attempt.gap);
        }
        // gap = 0 有兩種：真的推高了，或者剛好打平（同一首同樣的四項分數，四個最佳值
        // 一個都沒被超過）。`Attempt` 沒有記是哪一種，而對玩家來說兩種的結論一樣：這
        // 一局沒有任何東西被丟掉。所以兩種都說「已計入」。
        return head + Localize.S(" · <size=84%>已計入評分</size>");
    }

    private static string OneLine(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        int cut = value.IndexOfAny(new[] { '\r', '\n' });
        return cut >= 0 ? value.Substring(0, cut) : value;
    }

    private static string Ago(long utcTicks)
    {
        if (utcTicks <= 0) return Localize.S("剛剛");
        try
        {
            TimeSpan gap = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);
            if (gap.TotalMinutes < 90) return Localize.S("剛剛");
            if (gap.TotalHours < 24) return Localize.SF("{0} 小時前", (int)gap.TotalHours);
            if (gap.TotalDays < 30) return Localize.SF("{0} 天前", (int)gap.TotalDays);
            if (gap.TotalDays < 365)
                return Localize.SF("{0} 個月前", (int)(gap.TotalDays / 30));
            return Localize.SF("{0} 年前", (int)(gap.TotalDays / 365));
        }
        catch { return string.Empty; }
    }

    /// <summary>窄螢幕上讓路。</summary>
    /// <remarks>
    /// 4:3 的半寬只有 1108，兩塊 780 寬的牌會直接壓到側卡的難度牌上；21:9 的下層
    /// 架又太矮。與其硬擠，不如退場 —— 缺一塊牌只是少了資訊，壓在寶石上是壞掉。
    ///
    /// 只改牌面的 sizeDelta：裡面跟著寬度變的東西（四條尺、兩行字、分隔線）都是用
    /// anchor 撐開的，會自己跟上。淡入淡出用 unscaledDeltaTime，而且是 MoveTowards
    /// 而不是 Lerp —— 它自己會結束在 0 或 1，不會永遠逼近。
    /// </remarks>
    private void LateUpdate()
    {
        // 曲庫是在這一塊蓋好之後才載入的，補一次。長度沒變就只是一次 Count 比較。
        RefreshPlayedCount(false);

        if (canvasRoot == null) return;
        Rect area = canvasRoot.rect;
        if (area.width < 1f || area.height < 1f) return;

        float room = area.width * 0.5f - SideStripEdge - Margin;
        float shelf = area.height * 0.5f - SideStripBottom - Margin;

        // **高度不夠就縮小，不要整塊消失。**
        //
        // 上一版是 `shelf >= PlaqueHeight` 一翻臉就 alpha 0，而 shelf 的算式解出來是
        // 「畫面寬高比一超過 1.874:1 就消失」—— 16:9 是 1.778，只剩 18.8 的餘裕，任何
        // 比 16:9 再寬一點的視窗（Editor 的 Game view、21:9、把視窗拉扁）就什麼都沒
        // 了。而這兩塊牌是畫面上**唯一**寫著「你是誰、你幾分」的地方，消失比變小糟得
        // 多。現在整塊等比縮到放得下為止，縮到 0.62 才放棄。
        float scale = Mathf.Clamp(shelf / PlaqueHeight, MinScale, 1f);
        float span = room / scale;                      // 縮小之後左右能用的邏輯寬度
        float alpha = span >= MinWidth && shelf >= PlaqueHeight * MinScale ? 1f : 0f;
        var size = new Vector2(Mathf.Clamp(span, MinWidth, PlaqueWidth), PlaqueHeight);
        if (performerRect != null)
        {
            performerRect.sizeDelta = size;
            performerRect.localScale = new Vector3(scale, scale, 1f);
        }
        if (detailRect != null)
        {
            detailRect.sizeDelta = size;
            detailRect.localScale = new Vector3(scale, scale, 1f);
        }
        if (performerGroup != null)
            performerGroup.alpha = Mathf.MoveTowards(performerGroup.alpha, alpha, Time.unscaledDeltaTime * 4f);
        if (detailGroup != null)
            detailGroup.alpha = Mathf.MoveTowards(detailGroup.alpha, alpha, Time.unscaledDeltaTime * 4f);
    }
}

/// <summary>
/// 一枚空的刻紋徽章：斜面橢圓、一圈金框，中間是**凹**下去的空場。
/// </summary>
/// <remarks>
/// **為什麼是空的。** 專案裡沒有演奏者頭像這種資源。一個空的方框會被讀成「圖載不
/// 出來」，一枚刻好的空徽章會被讀成「這裡是留給頭像的位置」—— 同樣沒有圖，一個是
/// 壞掉，一個是還沒掛上。掛法見 `RecitalSidePlaques.SetPortrait`。
///
/// **為什麼內壁的明暗和金框相反。** 這是一個槽，不是一顆鈕。光從左上來的時候，浮
/// 起來的東西是左上緣最亮；凹下去的東西是**右下的內壁**最亮（那面壁正對著光），左
/// 上的內壁反而在自己的陰影裡。兩圈同向的話，整枚看起來就是一顆浮在牌上的圓鈕 ——
/// 這是第一版看起來像貼紙的原因。
///
/// **為什麼底場有上下漸層。** 槽底的下半被右下那面受光的壁反射照到，上半在上壁的
/// 陰影裡。一塊均勻的暗色沒有深度，而深度是「這裡是一個凹槽」唯一的證據。
///
/// **為什麼外緣要羽化。** 專案沒開 MSAA（PC_RPAsset 的 m_MSAA: 1），而橢圓在斜的
/// 那幾段是最容易爬階梯的形狀。實心收進半格、外面補一圈 alpha 收到 0 的裙擺，大小
/// 不變，邊卻是連續的 —— 和牌子、寶石用的是同一招。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class PortraitMedallionGraphic : MaskableGraphic
{
    /// <summary>光從左上來。整個專案共用這一個方向。</summary>
    private static readonly Vector2 Light = new Vector2(-0.5f, 0.87f);

    /// <summary>橢圓切幾段。44 段在 110 像素的徽章上每段不到 4 像素。</summary>
    private const int Segments = 44;

    [SerializeField] private Color gilt = new Color(0.92f, 0.78f, 0.44f, 1f);
    [SerializeField] private Color field = new Color(0.07f, 0.038f, 0.026f, 0.97f);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 12f || r.height <= 12f) return;

        Vector2 centre = r.center;
        // 直立的橢圓，不是圓：這是一張人像的位置，而人像的框從來不是正方的。
        float rx = r.width * 0.44f;
        float ry = r.height * 0.50f;

        Vector2[] rim = Ellipse(centre, rx, ry);
        Vector2[] band = Ellipse(centre, rx * 0.88f, ry * 0.88f);
        Vector2[] wall = Ellipse(centre, rx * 0.78f, ry * 0.78f);
        Vector2[] outer = Silhouette.Offset(rim, centre, -0.5f);
        Vector2[] skirt = Silhouette.Offset(rim, centre, 0.5f);

        // 底場先鋪滿，金框和內壁疊上去：中間才不會在接縫處露出牌面。
        Field(vh, centre, wall, ry);

        Color darkGilt = new Color(gilt.r * 0.34f, gilt.g * 0.34f, gilt.b * 0.34f, 1f);
        Color litGilt = Color.Lerp(gilt, Color.white, 0.55f);
        Color wallDark = new Color(gilt.r * 0.18f, gilt.g * 0.18f, gilt.b * 0.18f, 1f);
        Color wallLit = new Color(gilt.r * 0.70f, gilt.g * 0.70f, gilt.b * 0.70f, 1f);

        for (int i = 0; i < Segments; i++)
        {
            int next = (i + 1) % Segments;
            Vector2 outward = ((outer[i] + outer[next]) * 0.5f - centre).normalized;
            float facing = Vector2.Dot(outward, Light);

            Color face = Color.Lerp(darkGilt, litGilt, Mathf.Clamp01(facing * 0.5f + 0.5f));
            Quad(vh, outer[i], outer[next], band[next], band[i], face, face);
            // 裙擺接的是這一段自己的顏色，一圈的明暗才不會在輪廓上錯開。
            Silhouette.Ramp(vh, outer[i], outer[next], skirt[i], skirt[next], face);

            Color inner = Color.Lerp(wallDark, wallLit, Mathf.Clamp01(-facing * 0.5f + 0.5f));
            Quad(vh, band[i], band[next], wall[next], wall[i], inner, inner);
        }

        // 場裡再一條刻線。沒有它，底場只是一塊暗色；有了它，這裡是一個**刻好**的
        // 空位 —— 和牌子內側那條細金線同一個道理。
        Vector2[] ruleOuter = Ellipse(centre, rx * 0.66f, ry * 0.66f);
        Vector2[] ruleInner = Ellipse(centre, rx * 0.645f, ry * 0.645f);
        Color hairline = new Color(gilt.r, gilt.g, gilt.b, 0.30f);
        for (int i = 0; i < Segments; i++)
        {
            int next = (i + 1) % Segments;
            Quad(vh, ruleOuter[i], ruleOuter[next], ruleInner[next], ruleInner[i], hairline, hairline);
        }
    }

    /// <summary>槽底：下緣被受光的壁面反照，上緣在上壁的陰影裡。</summary>
    private void Field(VertexHelper vh, Vector2 centre, Vector2[] ring, float ry)
    {
        Color low = Color.Lerp(field, Color.white, 0.10f);
        Color high = new Color(field.r * 0.70f, field.g * 0.70f, field.b * 0.70f, field.a);
        low.a = field.a;

        int first = vh.currentVertCount;
        AddVertex(vh, centre, Color.Lerp(high, low, 0.55f));
        for (int i = 0; i < ring.Length; i++)
        {
            float t = ry > 0.01f ? Mathf.Clamp01((ring[i].y - centre.y) / ry * 0.5f + 0.5f) : 0.5f;
            AddVertex(vh, ring[i], Color.Lerp(low, high, t));
        }
        for (int i = 0; i < ring.Length; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % ring.Length);
    }

    private static Vector2[] Ellipse(Vector2 centre, float rx, float ry)
    {
        var points = new Vector2[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float angle = Mathf.PI * 2f * i / Segments;
            points[i] = new Vector2(centre.x + Mathf.Sin(angle) * rx, centre.y + Mathf.Cos(angle) * ry);
        }
        return points;
    }

    private static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Color outerColour, Color innerColour)
    {
        int index = vh.currentVertCount;
        AddVertex(vh, p0, outerColour);
        AddVertex(vh, p1, outerColour);
        AddVertex(vh, p2, innerColour);
        AddVertex(vh, p3, innerColour);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index + 2, index + 3, index);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
