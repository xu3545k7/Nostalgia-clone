using System.Collections.Generic;
using TMPro;
using UIHelpers;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 設定畫面的兩種版面：攤開的設定頁，和左邊留一條側欄的譜面預覽。
/// </summary>
/// <remarks>
/// **兩層：先六個大項，按進去才有細部。** 這一頁一次該攤開的是六個名詞，不是六十
/// 個參數 —— 六十個全擺在眼前的時候，眼睛讀到的不是「有什麼」而是「好多東西」，
/// 要找的那一行就埋在裡面。收起來的代價是多按一下，換到的是**玩家知道自己在找哪
/// 一類**；而那一下不是盲按：卡片第二行就寫著這一類裡面有什麼。
///
/// 旋轉卡片（最早那一版）是另一個極端 —— 一次只給看一項，玩家得先知道自己要的
/// 東西叫什麼、在哪一類才找得到它，那等於要求他先讀過這份設定。兩層是中間值：
/// 第一層一眼掃完，進去之後那一類**整份攤開**，同一類的東西框在一起，框就是目錄。
///
/// **為什麼預覽另外一個畫面。** 預覽要開一整場遊戲在背後跑：放音樂、佔效能、而且
/// 對「語言」「曲目管理」這種設定什麼都說明不了。所以它不再是設定頁的背景，而是
/// 玩家主動按下去才進入的另一個畫面，而那個畫面左邊只留看得到效果的兩類：譜面顯
/// 示和判定。
/// </remarks>
public partial class SimpleCarouselSettings
{
    private enum ScreenMode { Page, Preview }

    private enum RowKind
    {
        /// <summary>數值：− 值 +，按住會連續調。</summary>
        Number,
        /// <summary>幾個選項之一：‹ 名稱 ›，循環切換。</summary>
        Choice,
        /// <summary>開／關：一個開關。</summary>
        Switch,
        /// <summary>按下去做一件事：一顆按鈕。</summary>
        Action,
    }

    private sealed class RowView
    {
        public int Index;
        public RowKind Kind;
        public GameObject Root;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Value;
        public Button Minus;
        public Button Plus;
        public SettingsSwitch Switch;
        public Button Action;
        public TextMeshProUGUI ActionLabel;
    }

    private sealed class GroupView
    {
        public SettingsGroup Group;
        public GameObject Header;
        public TextMeshProUGUI Label;
        public readonly List<RowView> Rows = new List<RowView>();
        public int VisibleRows;
    }

    private sealed class CategoryView
    {
        public SettingsCategory Category;
        public GameObject Root;
        public GameObject Header;
        public TextMeshProUGUI Title;
        public readonly List<GroupView> Groups = new List<GroupView>();
    }

    /// <summary>大項清單上的一張卡片。</summary>
    private sealed class MenuCardView
    {
        public SettingsCategory Category;
        public GameObject Root;
        public Button Button;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Detail;
    }

    private sealed class ListView
    {
        public ScrollRect Scroll;
        public RectTransform Viewport;
        public RectTransform Content;
        public float WheelPixels;
        public readonly List<CategoryView> Categories = new List<CategoryView>();
        public bool WheelActive;
        public float WheelTarget;
        public float WheelVelocity;

        /// <summary>這一份是抽屜式的（只露出正在看的那一類）還是整份攤開。</summary>
        /// <remarks>
        /// 設定頁是抽屜，預覽的側欄不是：側欄只有兩類、而且它存在的理由就是
        /// 「一邊看著譜面一邊改」—— 在那裡多一層要按進去，等於把它拆掉。
        /// </remarks>
        public bool DrillDown;
    }

    /// <summary>一份清單的尺寸。設定頁和側欄是同一套結構，只有大小不同。</summary>
    private readonly struct ListMetrics
    {
        public readonly float RowHeight, TitleSize, ValueSize, ButtonWidth, ButtonHeight, ValueWidth,
            SwitchWidth, SwitchHeight, ActionWidth, BoxPadX, BoxPadTop, BoxPadBottom,
            HeaderHeight, HeaderSize, GroupHeight, GroupSize, Spacing, WheelPixels;

        public ListMetrics(float rowHeight, float titleSize, float valueSize, float buttonWidth,
            float buttonHeight, float valueWidth, float switchWidth, float switchHeight, float actionWidth,
            float boxPadX, float boxPadTop, float boxPadBottom, float headerHeight, float headerSize,
            float groupHeight, float groupSize, float spacing, float wheelPixels)
        {
            RowHeight = rowHeight; TitleSize = titleSize; ValueSize = valueSize;
            ButtonWidth = buttonWidth; ButtonHeight = buttonHeight; ValueWidth = valueWidth;
            SwitchWidth = switchWidth; SwitchHeight = switchHeight; ActionWidth = actionWidth;
            BoxPadX = boxPadX; BoxPadTop = boxPadTop; BoxPadBottom = boxPadBottom;
            HeaderHeight = headerHeight; HeaderSize = headerSize;
            GroupHeight = groupHeight; GroupSize = groupSize; Spacing = spacing; WheelPixels = wheelPixels;
        }
    }

    // 畫布的參考解析度是 2560 x 1440。
    private static readonly ListMetrics PageMetrics = new ListMetrics(
        rowHeight: 58f, titleSize: 24f, valueSize: 23f, buttonWidth: 42f, buttonHeight: 38f,
        valueWidth: 220f, switchWidth: 74f, switchHeight: 34f, actionWidth: 150f,
        boxPadX: 28f, boxPadTop: 10f, boxPadBottom: 20f, headerHeight: 68f, headerSize: 30f,
        groupHeight: 50f, groupSize: 19f, spacing: 28f, wheelPixels: 130f);

    private static readonly ListMetrics RailMetrics = new ListMetrics(
        rowHeight: 50f, titleSize: 20f, valueSize: 19f, buttonWidth: 34f, buttonHeight: 32f,
        valueWidth: 150f, switchWidth: 62f, switchHeight: 30f, actionWidth: 124f,
        boxPadX: 18f, boxPadTop: 6f, boxPadBottom: 14f, headerHeight: 56f, headerSize: 24f,
        groupHeight: 42f, groupSize: 16f, spacing: 16f, wheelPixels: 96f);

    /// <summary>大項卡片的尺寸。兩欄三列，1684 x 720，置中。</summary>
    private const float CardWidth = 820f;
    private const float CardHeight = 216f;
    private const float CardGapX = 44f;
    private const float CardGapY = 36f;

    /// <summary>進到一類之後，那一欄有多寬。</summary>
    /// <remarks>
    /// 一次只有一類，所以不再分兩欄 —— 一欄的話讀的順序就是子分類寫下來的順序。
    /// 寬度寫死而不是用比例：2560 的參考寬度下一欄攤到滿會讓標題和數值隔著一整個
    /// 螢幕，眼睛得在兩端之間來回。
    /// </remarks>
    private const float DetailWidth = 1240f;

    /// <summary>預覽畫面的側欄只留這兩類 —— 看著譜面判斷得了的東西。</summary>
    private static readonly SettingsCategory[] PreviewCategories =
    {
        SettingsCategory.Visual,
        SettingsCategory.Judgment,
    };

    private ScreenMode screenMode = ScreenMode.Page;

    /// <summary>現在進到哪一個大項裡面。沒有值＝停在大項清單上。</summary>
    private SettingsCategory? openCategory;

    private GameObject gameplayPreviewFrame;
    private GameObject settingsPageRoot;
    private GameObject settingsMenuRoot;
    private GameObject settingsListHost;
    private readonly List<MenuCardView> menuCards = new List<MenuCardView>(6);

    /// <summary>大項清單上的鍵盤游標。-1 ＝ 還沒選。</summary>
    private int menuFocus = -1;
    private GameObject previewRailRoot;
    private GameObject chromeRoot;
    private ListView pageList;
    private ListView railList;
    private TextMeshProUGUI pageTitle;
    private TextMeshProUGUI chromeBackLabel;
    private TextMeshProUGUI chromeModeLabel;
    private SettingsGlyph chromeModeGlyph;

    /// <summary>
    /// 每個子分類裡有哪些項目，**照 SetGroup 寫的順序**。
    /// </summary>
    /// <remarks>
    /// 不能照 settings[] 的索引排：索引是依加入的先後編的（新的一律接在最後，免得
    /// 既有的全部錯位），和「這一組裡什麼該排第一」毫無關係。音量那一組照索引排會
    /// 變成打擊聲在音樂前面。
    /// </remarks>
    private readonly Dictionary<SettingsGroup, List<int>> groupMembers =
        new Dictionary<SettingsGroup, List<int>>();

    private readonly List<RaycastResult> wheelHits = new List<RaycastResult>();
    private PointerEventData wheelPointer;
    private EventSystem wheelPointerOwner;

    // ------------------------------------------------------------------
    // 建構
    // ------------------------------------------------------------------

    private void BuildSettingsScreens(Transform overlay)
    {
        if (overlay == null || settings == null || settingsPageRoot != null) return;
        BuildSettingsPage(overlay);
        BuildPreviewRail(overlay);
        BuildChrome(overlay);
        SetScreenMode(ScreenMode.Page);
        SetOpenCategory(null);
    }

    private void BuildSettingsPage(Transform overlay)
    {
        RectTransform root = SettingsUiKit.CreateRect("SettingsPage", overlay);
        SettingsUiKit.Stretch(root);
        SettingsUiKit.AddImage(root.gameObject, null, SettingsUiKit.PageColour, true);

        RectTransform vignette = SettingsUiKit.CreateRect("Vignette", root);
        SettingsUiKit.Stretch(vignette);
        SettingsUiKit.AddImage(vignette.gameObject, SettingsUiKit.VignetteSprite(), new Color(1f, 1f, 1f, 0.8f), false);

        pageTitle = SettingsUiKit.CreateLabel(root, "PageTitle", 40f, SettingsUiKit.GoldBright,
            TextAlignmentOptions.Center, FontStyles.Bold);
        pageTitle.characterSpacing = 12f;
        SettingsUiKit.Place(pageTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(900f, 62f));

        RectTransform rule = SettingsUiKit.CreateRect("HeaderRule", root);
        rule.anchorMin = new Vector2(0.03f, 1f);
        rule.anchorMax = new Vector2(0.97f, 1f);
        rule.pivot = new Vector2(0.5f, 1f);
        rule.anchoredPosition = new Vector2(0f, -110f);
        rule.sizeDelta = new Vector2(0f, 2f);
        SettingsUiKit.AddImage(rule.gameObject, SettingsUiKit.RuleSprite(true),
            WithAlpha(SettingsUiKit.Gold, 0.55f), false);

        BuildCategoryMenu(root);

        // 細部那一層：置中的一欄，上面讓出頁首、下面留一點。
        // anchorMin/Max 的 x 都是 0.5 ＝ 寬度寫死，只有高度跟著畫面。
        RectTransform host = SettingsUiKit.CreateRect("ListHost", root);
        host.anchorMin = new Vector2(0.5f, 0f);
        host.anchorMax = new Vector2(0.5f, 1f);
        host.pivot = new Vector2(0.5f, 0.5f);
        host.sizeDelta = new Vector2(DetailWidth, -148f);
        host.anchoredPosition = new Vector2(0f, -46f);
        pageList = BuildList(host, CategoryOrder, 1, PageMetrics, 0f);
        pageList.DrillDown = true;
        settingsListHost = host.gameObject;
        settingsPageRoot = root.gameObject;
    }

    /// <summary>
    /// 第一層：六張大項卡片。
    /// </summary>
    /// <remarks>
    /// 卡片而不是一排文字按鈕：第二行要寫得下「裡面有什麼」，而那一行正是這一層
    /// 能不能用的關鍵 —— 少了它，六張卡片就是六個都叫「○○設定」的盒子。
    /// </remarks>
    private void BuildCategoryMenu(Transform parent)
    {
        RectTransform root = SettingsUiKit.CreateRect("CategoryMenu", parent);
        SettingsUiKit.Stretch(root, 0f, 96f, 0f, 150f);

        var grid = root.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CardWidth, CardHeight);
        grid.spacing = new Vector2(CardGapX, CardGapY);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        // 整塊置中。靠上的話六張卡片會黏在頁首底下，下面空一大片。
        grid.childAlignment = TextAnchor.MiddleCenter;

        menuCards.Clear();
        for (int i = 0; i < CategoryOrder.Length; i++)
        {
            MenuCardView card = BuildCategoryCard(root, CategoryOrder[i]);
            if (card != null) menuCards.Add(card);
        }
        settingsMenuRoot = root.gameObject;
    }

    private MenuCardView BuildCategoryCard(Transform parent, SettingsCategory category)
    {
        if (!GroupOrder.TryGetValue(category, out SettingsGroup[] groups) || groups.Length == 0)
            return null;

        var view = new MenuCardView { Category = category };
        Button button = SettingsUiKit.CreateButton(parent, "Card_" + category,
            SettingsUiKit.Tone.Lacquer, CategoryGlyph(category), GetCategoryName(category), 34f,
            new Vector2(CardWidth, CardHeight), out view.Title, out SettingsGlyph glyph);
        view.Root = button.gameObject;
        view.Button = button;

        // CreateButton 排的是「圖示靠左、字填滿剩下的」。卡片要的是名字在上、
        // 內容在下，所以圖示和名字都重擺一次。
        const float textLeft = 164f;
        float textWidth = CardWidth - textLeft - 82f;
        if (glyph != null)
            SettingsUiKit.Place(glyph.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(60f, 0f), new Vector2(72f, 72f));

        SettingsUiKit.Place(view.Title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(textLeft, 34f), new Vector2(textWidth, 50f));
        view.Title.alignment = TextAlignmentOptions.MidlineLeft;
        view.Title.characterSpacing = 2f;

        view.Detail = SettingsUiKit.CreateLabel(button.transform, "Detail", 21f,
            SettingsUiKit.MutedText, TextAlignmentOptions.MidlineLeft);
        // 這一行的長度是語言決定的（英文的子分類名比中文長一倍以上），寬度是版面
        // 決定的 —— 讓字縮，不要讓它被截成「Track · Notes · Judgm…」。
        view.Detail.enableAutoSizing = true;
        view.Detail.fontSizeMin = 15f;
        view.Detail.fontSizeMax = 21f;
        SettingsUiKit.Place(view.Detail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(textLeft, -32f), new Vector2(textWidth, 44f));

        RectTransform enter = SettingsUiKit.CreateRect("Enter", button.transform);
        var arrow = enter.gameObject.AddComponent<SettingsGlyph>();
        arrow.Kind = SettingsGlyph.Shape.ChevronRight;
        arrow.color = WithAlpha(SettingsUiKit.Gold, 0.72f);
        arrow.raycastTarget = false;
        SettingsUiKit.Place(enter, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-36f, 0f), new Vector2(28f, 28f));

        SettingsCategory captured = category;
        button.onClick.AddListener(() => OpenCategory(captured));
        return view;
    }

    /// <summary>每一類自己的圖示。</summary>
    /// <remarks>
    /// 六個名字長得太像（音效設定／判定設定／顯示設定／系統設定），差別只在第一
    /// 個字。圖形是先被看到的那一層，所以每一類都要有一個分得開的形狀，不是六個
    /// 一樣的菱形。
    /// </remarks>
    private static SettingsGlyph.Shape CategoryGlyph(SettingsCategory category)
    {
        switch (category)
        {
            case SettingsCategory.Audio: return SettingsGlyph.Shape.Speaker;
            case SettingsCategory.Judgment: return SettingsGlyph.Shape.Judge;
            case SettingsCategory.Visual: return SettingsGlyph.Shape.Track;
            case SettingsCategory.Gameplay: return SettingsGlyph.Shape.Hourglass;
            case SettingsCategory.SongManagement: return SettingsGlyph.Shape.Quaver;
            default: return SettingsGlyph.Shape.Gear;
        }
    }

    /// <summary>卡片第二行：這一類**裡面有什麼**。</summary>
    /// <remarks>
    /// 收起來的代價是「要按下去才知道裡面有沒有我要找的東西」，這一行就是那個代價
    /// 的補償。子分類多的列子分類名；只有一組的直接列項目名 —— 那種情況下再寫一次
    /// 「曲目管理」只是把卡片的標題抄一遍。
    ///
    /// 藏起來的項目（換判定預設、換落下模式會藏掉好幾個）不算在裡面：這一行要說的
    /// 是**按進去會看到什麼**，不是這一類本來有幾項。
    /// </remarks>
    private string CategorySummary(SettingsCategory category)
    {
        if (settings == null) return string.Empty;
        if (!GroupOrder.TryGetValue(category, out SettingsGroup[] groups) || groups.Length == 0)
            return string.Empty;

        var parts = new List<string>(6);
        if (groups.Length > 1)
        {
            for (int g = 0; g < groups.Length; g++)
                if (VisibleRowCount(groups[g]) > 0) parts.Add(GetGroupName(groups[g]));
        }
        else if (groupMembers.TryGetValue(groups[0], out List<int> members))
        {
            int shown = 0;
            int total = 0;
            for (int i = 0; i < members.Count; i++)
            {
                SettingData data = members[i] >= 0 && members[i] < settings.Length
                    ? settings[members[i]] : null;
                if (data == null || data.isHidden) continue;
                total++;
                if (shown >= 4) continue;
                parts.Add(data.title);
                shown++;
            }
            if (total > shown) parts.Add("…");
        }
        return string.Join("  ·  ", parts.ToArray());
    }

    private int VisibleRowCount(SettingsGroup group)
    {
        if (settings == null || !groupMembers.TryGetValue(group, out List<int> members)) return 0;
        int count = 0;
        for (int i = 0; i < members.Count; i++)
        {
            SettingData data = members[i] >= 0 && members[i] < settings.Length
                ? settings[members[i]] : null;
            if (data != null && !data.isHidden) count++;
        }
        return count;
    }

    private void BuildPreviewRail(Transform overlay)
    {
        RectTransform root = SettingsUiKit.CreateRect("SettingsPreviewRail", overlay);
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(28f, -112f);
        root.sizeDelta = new Vector2(680f, -140f);
        SettingsUiKit.AddImage(root.gameObject, SettingsUiKit.SoftPanelSprite(), Color.white, true);

        RectTransform host = SettingsUiKit.CreateRect("ListHost", root);
        SettingsUiKit.Stretch(host, 4f, 10f, 4f, 10f);
        railList = BuildList(host, PreviewCategories, 1, RailMetrics, 14f);
        previewRailRoot = root.gameObject;
    }

    /// <summary>
    /// 左上返回、右上切換畫面。兩個畫面共用同一組，位置永遠不變。
    /// </summary>
    /// <remarks>
    /// 位置不變是重點：在設定頁和預覽之間來回切的時候，滑鼠停在右上角就能一直按，
    /// 不必每切一次就重新找按鈕在哪裡。
    /// </remarks>
    private void BuildChrome(Transform overlay)
    {
        RectTransform root = SettingsUiKit.CreateRect("SettingsChrome", overlay);
        SettingsUiKit.Stretch(root);
        chromeRoot = root.gameObject;

        Button back = SettingsUiKit.CreateButton(root, "BackButton", SettingsUiKit.Tone.Lacquer,
            SettingsGlyph.Shape.Back, Localize.T("返回", "返回", "Back"), 21f, new Vector2(158f, 50f),
            out chromeBackLabel, out _);
        RectTransform backRect = (RectTransform)back.transform;
        backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
        backRect.pivot = new Vector2(0f, 1f);
        backRect.anchoredPosition = new Vector2(36f, -32f);
        back.onClick.AddListener(BackOrClose);

        Button mode = SettingsUiKit.CreateButton(root, "ModeButton", SettingsUiKit.Tone.Accent,
            SettingsGlyph.Shape.Play, Localize.T("譜面預覽", "谱面预览", "Chart Preview"), 21f,
            new Vector2(218f, 50f), out chromeModeLabel, out chromeModeGlyph);
        RectTransform modeRect = (RectTransform)mode.transform;
        modeRect.anchorMin = modeRect.anchorMax = new Vector2(1f, 1f);
        modeRect.pivot = new Vector2(1f, 1f);
        modeRect.anchoredPosition = new Vector2(-36f, -32f);
        mode.onClick.AddListener(ToggleScreenMode);
    }

    private ListView BuildList(RectTransform host, SettingsCategory[] categories, int columns,
        ListMetrics m, float sidePadding)
    {
        var view = new ListView { WheelPixels = m.WheelPixels };

        RectTransform viewport = SettingsUiKit.CreateRect("Viewport", host);
        SettingsUiKit.Stretch(viewport);
        // 透明但接得到射線：分類框之間的空隙也要能拖、能捲。
        SettingsUiKit.AddImage(viewport.gameObject, null, new Color(0f, 0f, 0f, 0f), true);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = SettingsUiKit.CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        int side = Mathf.RoundToInt(sidePadding);
        var padding = new RectOffset(side, side, columns > 1 ? 18 : 10, columns > 1 ? 90 : 24);
        var parents = new List<Transform>();
        if (columns <= 1)
        {
            ConfigureStack(content.gameObject.AddComponent<VerticalLayoutGroup>(), padding, m.Spacing);
            parents.Add(content);
        }
        else
        {
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = padding;
            row.spacing = m.Spacing * 1.3f;
            row.childAlignment = TextAnchor.UpperCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            for (int c = 0; c < columns; c++)
            {
                RectTransform column = SettingsUiKit.CreateRect("Column" + c, content);
                ConfigureStack(column.gameObject.AddComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), m.Spacing);
                var element = column.gameObject.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;
                element.minWidth = 0f;
                parents.Add(column);
            }
        }

        // 兩欄的時候，每一類放進目前比較矮的那一欄。照順序一欄一類的話，一邊是
        // 二十列的「顯示」、一邊是兩列的「系統」，右半頁會空掉一大塊。
        var load = new float[parents.Count];
        for (int i = 0; i < categories.Length; i++)
        {
            int target = 0;
            for (int c = 1; c < load.Length; c++)
                if (load[c] < load[target] - 0.5f) target = c;
            CategoryView category = BuildCategory(parents[target], categories[i], m);
            if (category == null) continue;
            view.Categories.Add(category);
            load[target] += EstimateHeight(category, m);
        }

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.10f;
        // 滾輪自己處理（見 ScrollListBy）：內建的滾輪一格只動幾個像素，而且沒有緩動。
        scroll.scrollSensitivity = 0f;

        view.Scroll = scroll;
        view.Viewport = viewport;
        view.Content = content;
        return view;
    }

    private float EstimateHeight(CategoryView category, ListMetrics m)
    {
        float height = m.HeaderHeight + m.BoxPadTop + m.BoxPadBottom + m.Spacing;
        int visibleGroups = 0;
        for (int g = 0; g < category.Groups.Count; g++)
        {
            int rows = 0;
            foreach (RowView row in category.Groups[g].Rows)
                if (!settings[row.Index].isHidden) rows++;
            if (rows > 0) visibleGroups++;
            height += rows * m.RowHeight;
        }
        if (visibleGroups > 1) height += visibleGroups * m.GroupHeight;
        return height;
    }

    private static void ConfigureStack(VerticalLayoutGroup stack, RectOffset padding, float spacing)
    {
        stack.padding = padding;
        stack.spacing = spacing;
        stack.childAlignment = TextAnchor.UpperLeft;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
    }

    private static void FixHeight(RectTransform rect, float height)
    {
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    /// <summary>
    /// 一個分類框：標題在最上面，底下依子分類分段，每段列出它的項目。
    /// </summary>
    private CategoryView BuildCategory(Transform parent, SettingsCategory category, ListMetrics m)
    {
        if (!GroupOrder.TryGetValue(category, out SettingsGroup[] groups) || groups.Length == 0) return null;

        var view = new CategoryView { Category = category };
        RectTransform box = SettingsUiKit.CreateRect("Category_" + category, parent);
        SettingsUiKit.AddImage(box.gameObject, SettingsUiKit.FrameSprite(), Color.white, true);
        ConfigureStack(box.gameObject.AddComponent<VerticalLayoutGroup>(),
            new RectOffset(Mathf.RoundToInt(m.BoxPadX), Mathf.RoundToInt(m.BoxPadX),
                Mathf.RoundToInt(m.BoxPadTop), Mathf.RoundToInt(m.BoxPadBottom)), 0f);
        view.Root = box.gameObject;

        RectTransform header = SettingsUiKit.CreateRect("Header", box);
        FixHeight(header, m.HeaderHeight);
        view.Header = header.gameObject;

        float ornament = Mathf.Round(m.HeaderSize * 0.40f);
        RectTransform ornamentRect = SettingsUiKit.CreateRect("Ornament", header);
        var diamond = ornamentRect.gameObject.AddComponent<SettingsGlyph>();
        diamond.Kind = SettingsGlyph.Shape.Diamond;
        diamond.color = SettingsUiKit.Gold;
        diamond.raycastTarget = false;
        SettingsUiKit.Place(ornamentRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(2f, 3f), new Vector2(ornament, ornament));

        view.Title = SettingsUiKit.CreateLabel(header, "Title", m.HeaderSize, SettingsUiKit.GoldBright,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        view.Title.characterSpacing = 3f;
        SettingsUiKit.Stretch(view.Title.rectTransform, ornament + 16f, 6f, 0f, 0f);

        RectTransform rule = SettingsUiKit.CreateRect("Rule", header);
        rule.anchorMin = new Vector2(0f, 0f);
        rule.anchorMax = new Vector2(1f, 0f);
        rule.pivot = new Vector2(0.5f, 0f);
        rule.anchoredPosition = new Vector2(0f, 6f);
        rule.sizeDelta = new Vector2(0f, 2f);
        SettingsUiKit.AddImage(rule.gameObject, SettingsUiKit.RuleSprite(false),
            WithAlpha(SettingsUiKit.Gold, 0.6f), false);

        for (int g = 0; g < groups.Length; g++)
        {
            var group = new GroupView { Group = groups[g] };
            if (groups.Length > 1)
            {
                RectTransform groupHeader = SettingsUiKit.CreateRect("Group_" + groups[g], box);
                FixHeight(groupHeader, m.GroupHeight);
                group.Label = SettingsUiKit.CreateLabel(groupHeader, "Label", m.GroupSize,
                    WithAlpha(SettingsUiKit.Gold, 0.92f), TextAlignmentOptions.BottomLeft, FontStyles.SmallCaps);
                group.Label.characterSpacing = 4f;
                SettingsUiKit.Stretch(group.Label.rectTransform, 4f, 8f, 0f, 0f);
                group.Header = groupHeader.gameObject;
            }

            if (groupMembers.TryGetValue(groups[g], out List<int> members))
            {
                for (int i = 0; i < members.Count; i++)
                {
                    RowView row = BuildRow(box, members[i], m);
                    if (row != null) group.Rows.Add(row);
                }
            }
            view.Groups.Add(group);
        }
        return view;
    }

    private RowView BuildRow(Transform parent, int index, ListMetrics m)
    {
        if (settings == null || index < 0 || index >= settings.Length || settings[index] == null) return null;
        SettingData setting = settings[index];
        var view = new RowView { Index = index, Kind = ResolveRowKind(setting) };

        RectTransform row = SettingsUiKit.CreateRect("Row_" + index, parent);
        FixHeight(row, m.RowHeight);
        Image hover = SettingsUiKit.AddImage(row.gameObject, null, new Color(1f, 0.82f, 0.5f, 0f), true);
        row.gameObject.AddComponent<SettingsRowHover>().Configure(hover);
        view.Root = row.gameObject;

        RectTransform rule = SettingsUiKit.CreateRect("Rule", row);
        rule.anchorMin = new Vector2(0f, 0f);
        rule.anchorMax = new Vector2(1f, 0f);
        rule.pivot = new Vector2(0.5f, 0f);
        rule.anchoredPosition = Vector2.zero;
        rule.sizeDelta = new Vector2(0f, 1f);
        SettingsUiKit.AddImage(rule.gameObject, SettingsUiKit.RuleSprite(true),
            WithAlpha(SettingsUiKit.Gold, 0.18f), false);

        const float right = 8f;
        float controlWidth;
        int captured = index;

        switch (view.Kind)
        {
            case RowKind.Switch:
            {
                RectTransform track = SettingsUiKit.CreateRect("Switch", row);
                SettingsUiKit.Place(track, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-right, 0f), new Vector2(m.SwitchWidth, m.SwitchHeight));
                Image trackImage = SettingsUiKit.AddImage(track.gameObject, SettingsUiKit.PillSprite(false), Color.white, true);

                RectTransform fillRect = SettingsUiKit.CreateRect("On", track);
                SettingsUiKit.Stretch(fillRect);
                Image fill = SettingsUiKit.AddImage(fillRect.gameObject, SettingsUiKit.PillSprite(true),
                    new Color(1f, 1f, 1f, 0f), false);

                RectTransform glowRect = SettingsUiKit.CreateRect("Glow", track);
                SettingsUiKit.Stretch(glowRect, -14f, -14f, -14f, -14f);
                Image glow = SettingsUiKit.AddImage(glowRect.gameObject, SettingsUiKit.GlowSprite(),
                    WithAlpha(SettingsUiKit.Gold, 0f), false);

                float knobSize = m.SwitchHeight - 8f;
                RectTransform knob = SettingsUiKit.CreateRect("Knob", track);
                SettingsUiKit.Place(knob, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(knobSize, knobSize));
                SettingsUiKit.AddImage(knob.gameObject, SettingsUiKit.KnobSprite(), Color.white, false);
                Shadow knobShadow = knob.gameObject.AddComponent<Shadow>();
                knobShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
                knobShadow.effectDistance = new Vector2(0f, -2f);

                view.Switch = track.gameObject.AddComponent<SettingsSwitch>();
                view.Switch.Configure(fill, knob, (m.SwitchWidth - m.SwitchHeight) * 0.5f);

                var toggle = track.gameObject.AddComponent<Button>();
                toggle.targetGraphic = trackImage;
                toggle.transition = Selectable.Transition.None;
                track.gameObject.AddComponent<SettingsButtonFeel>().Configure(toggle, null, glow, null, null);
                toggle.onClick.AddListener(() => StepSetting(captured, 1));

                const float stateWidth = 100f;
                view.Value = SettingsUiKit.CreateLabel(row, "State", m.ValueSize * 0.9f, SettingsUiKit.MutedText,
                    TextAlignmentOptions.MidlineRight);
                SettingsUiKit.Place(view.Value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-(right + m.SwitchWidth + 12f), 0f), new Vector2(stateWidth, m.RowHeight));
                controlWidth = right + m.SwitchWidth + 12f + stateWidth;
                break;
            }

            case RowKind.Action:
            {
                view.Action = SettingsUiKit.CreateButton(row, "Action", SettingsUiKit.Tone.Lacquer, null,
                    ActionCaption(index), m.ValueSize * 0.88f, new Vector2(m.ActionWidth, m.ButtonHeight + 2f),
                    out view.ActionLabel, out _);
                RectTransform actionRect = (RectTransform)view.Action.transform;
                actionRect.anchorMin = actionRect.anchorMax = new Vector2(1f, 0.5f);
                actionRect.pivot = new Vector2(1f, 0.5f);
                actionRect.anchoredPosition = new Vector2(-right, 0f);
                view.Action.onClick.AddListener(() => RunSettingAction(captured));
                controlWidth = right + m.ActionWidth;
                break;
            }

            default:
            {
                bool number = view.Kind == RowKind.Number;
                var size = new Vector2(m.ButtonWidth, m.ButtonHeight);

                view.Plus = SettingsUiKit.CreateButton(row, "Next", SettingsUiKit.Tone.Quiet,
                    number ? SettingsGlyph.Shape.Plus : SettingsGlyph.Shape.ChevronRight, null, 0f, size, out _, out _);
                PlaceFromRight(view.Plus.transform, right);

                view.Value = SettingsUiKit.CreateLabel(row, "Value", m.ValueSize, SettingsUiKit.TextColour,
                    TextAlignmentOptions.Center);
                view.Value.enableAutoSizing = true;
                view.Value.fontSizeMin = m.ValueSize * 0.7f;
                view.Value.fontSizeMax = m.ValueSize;
                SettingsUiKit.Place(view.Value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-(right + m.ButtonWidth + 4f), 0f), new Vector2(m.ValueWidth, m.RowHeight));

                view.Minus = SettingsUiKit.CreateButton(row, "Previous", SettingsUiKit.Tone.Quiet,
                    number ? SettingsGlyph.Shape.Minus : SettingsGlyph.Shape.ChevronLeft, null, 0f, size, out _, out _);
                PlaceFromRight(view.Minus.transform, right + m.ButtonWidth + 4f + m.ValueWidth + 4f);

                if (number)
                {
                    // 數值按住會連續調。選項不會：循環切換按住的話一下就轉過頭。
                    AddRepeat(view.Minus, () => StepSetting(captured, -1));
                    AddRepeat(view.Plus, () => StepSetting(captured, 1));
                }
                else
                {
                    view.Minus.onClick.AddListener(() => StepSetting(captured, -1));
                    view.Plus.onClick.AddListener(() => StepSetting(captured, 1));
                }
                controlWidth = right + 2f * m.ButtonWidth + m.ValueWidth + 8f;
                break;
            }
        }

        view.Title = SettingsUiKit.CreateLabel(row, "Title", m.TitleSize, SettingsUiKit.TextColour,
            TextAlignmentOptions.MidlineLeft);
        view.Title.enableAutoSizing = true;
        view.Title.fontSizeMin = m.TitleSize * 0.72f;
        view.Title.fontSizeMax = m.TitleSize;
        SettingsUiKit.Stretch(view.Title.rectTransform, 12f, 0f, controlWidth + 14f, 0f);
        return view;
    }

    private static void PlaceFromRight(Transform target, float offset)
    {
        var rect = (RectTransform)target;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-offset, 0f);
    }

    private static void AddRepeat(Button button, UnityAction action)
    {
        var repeat = button.gameObject.AddComponent<RepeatPress>();
        repeat.onRepeat ??= new UnityEvent();
        repeat.onRepeat.AddListener(action);
    }

    /// <summary>
    /// 這一列該用哪一種控制項。
    /// </summary>
    /// <remarks>
    /// 兩個選項的設定不一定是「開／關」。「黑色大理石／白色大理石」做成開關的話，
    /// 玩家得猜「開」是哪一邊 —— 那種就當成選項，用名字切換。只有兩端真的叫開和關
    /// 的才做成開關。
    /// </remarks>
    private static RowKind ResolveRowKind(SettingData setting)
    {
        if (setting.isAction) return RowKind.Action;
        if (setting.isToggle && IsOffWord(setting.ResolveMinLabel()) && IsOnWord(setting.ResolveMaxLabel()))
            return RowKind.Switch;
        if (setting.isToggle || (setting.discreteLabels != null && setting.discreteLabels.Length > 0))
            return RowKind.Choice;
        return RowKind.Number;
    }

    /// <summary>這個字是不是「關」。</summary>
    /// <remarks>
    /// 日／韓的開關字是查表翻出來的（オフ／オン、끔／켬），所以不能只比寫死的那三個。
    /// 比不中的話每一個開關都會從 <see cref="RowKind.Switch"/> 掉到
    /// <see cref="RowKind.Choice"/> —— 整頁的開關變成「用名字切換的選項」，
    /// 而且只有日韓會這樣，很難從中文介面發現。
    ///
    /// 拿和設定畫面同一個查表函式去要那兩個字，加語言時這裡自動跟上。
    /// </remarks>
    private static bool IsOffWord(string label) =>
        label == "Off" || label == "關" || label == "关" || label == LocalizedSetting("關");

    /// <summary>這個字是不是「開」。見 <see cref="IsOffWord"/>。</summary>
    private static bool IsOnWord(string label) =>
        label == "On" || label == "開" || label == "开" || label == LocalizedSetting("開");

    private string ActionCaption(int index)
    {
        if (index == ApplyArcadeIndex)
            return arcadeAppliedShown
                ? Localize.T("已套用", "已套用", "Applied")
                : Localize.T("套用", "套用", "Apply");
        return Localize.T("開啟", "打开", "Open");
    }

    private static Color WithAlpha(Color colour, float alpha)
    {
        colour.a = alpha;
        return colour;
    }

    // ------------------------------------------------------------------
    // 操作
    // ------------------------------------------------------------------

    /// <summary>
    /// 把一項設定往前或往後調一格，並立刻套用。
    /// </summary>
    /// <remarks>
    /// 數值會吸附到步距的格線上。一路加 0.05 下去，浮點誤差會讓 30% 變成
    /// 30.000004%，下一次比較「是不是到頂了」就會差那一點點而多按一次沒反應。
    /// </remarks>
    // ──────────────────────────────────────────────────────────────
    //  鍵盤游標
    // ──────────────────────────────────────────────────────────────

    /// <summary>現在選到設定陣列的哪一格。-1 ＝ 還沒選。</summary>
    private int focusedSetting = -1;

    /// <summary>列上的游標：一層很淡的金。</summary>
    private static readonly Color RowFocusTint = new Color(0.98f, 0.80f, 0.36f, 0.22f);

    /// <summary>卡片上的游標：同一個顏色，但淡得多。</summary>
    /// <remarks>
    /// 一張卡片的面積是一列的六倍以上。同樣的透明度鋪滿整張卡片，讀起來不是
    /// 「游標停在這裡」而是「這張卡片自己亮著」—— 那是另一個意思。
    /// </remarks>
    private static readonly Color CardFocusTint = new Color(0.98f, 0.80f, 0.36f, 0.085f);

    /// <summary>高光。整個介面只有一條，被搬到目前那一列底下。</summary>
    /// <remarks>
    /// 每一列各掛一條、再逐條開關的話，列是動態重建的（語言、可見性、預設值都會
    /// 重建），高光就得跟著重建與同步。一條搬家的只有一份狀態。
    /// </remarks>
    private Image focusHighlight;

    private readonly List<RowView> focusable = new List<RowView>(64);

    /// <summary>設定畫面現在開著嗎。</summary>
    public bool SettingsOpen => settingsPanel != null && settingsPanel.activeInHierarchy;

    /// <summary>
    /// 把游標往下（+1）或往上（-1）移一列，並捲動到看得見的地方。
    /// </summary>
    /// <remarks>
    /// 走訪順序是「分類 → 群組 → 列」，也就是這些設定被分組的順序。進到一類之後
    /// 那一類是單欄的，所以它同時也是螢幕上的順序 —— 但照結構走這件事本身要留著：
    /// 版面哪天又變成多欄，照幾何走就會在欄與欄之間跳來跳去。
    /// </remarks>
    public void MoveSettingFocus(int delta)
    {
        if (CategoryMenuShowing) { MoveMenuFocus(delta); return; }
        CollectFocusable();
        if (focusable.Count == 0) return;

        int at = -1;
        for (int i = 0; i < focusable.Count; i++)
        {
            if (focusable[i].Index == focusedSetting) { at = i; break; }
        }
        // 還沒選過就從第一列開始，而不是從「第 0 格的下一格」—— 第一次按下應該
        // 停在開頭，不是跳過開頭。
        at = at < 0 ? (delta > 0 ? 0 : focusable.Count - 1)
                    : ((at + delta) % focusable.Count + focusable.Count) % focusable.Count;

        focusedSetting = focusable[at].Index;
        ShowFocus(focusable[at]);
    }

    /// <summary>調整目前選到的那一列的數值。</summary>
    public void StepFocusedSetting(int direction)
    {
        if (CategoryMenuShowing) { ConfirmSettingFocus(); return; }
        if (focusedSetting < 0) { MoveSettingFocus(1); return; }
        StepSetting(focusedSetting, direction);
    }

    /// <summary>現在停在大項清單上嗎。</summary>
    private bool CategoryMenuShowing =>
        screenMode == ScreenMode.Page && !openCategory.HasValue && menuCards.Count > 0;

    /// <summary>設定開著、而且停在大項清單上。區域條靠它決定那幾塊鍵要印什麼。</summary>
    public bool SettingsMenuShowing => SettingsOpen && CategoryMenuShowing;

    /// <summary>在大項清單上按「選定」：進到游標那一類。</summary>
    /// <remarks>
    /// 還沒選過就先讓游標停在第一張卡片上，而不是直接進去 —— 看不見游標的時候
    /// 按下去會進到一個自己沒挑的地方。
    /// </remarks>
    public void ConfirmSettingFocus()
    {
        if (!CategoryMenuShowing) return;
        if (menuFocus < 0 || menuFocus >= menuCards.Count) { MoveMenuFocus(1); return; }
        MenuCardView card = menuCards[menuFocus];
        if (card != null) OpenCategory(card.Category);
    }

    /// <summary>
    /// 大項清單上的游標。
    /// </summary>
    /// <remarks>
    /// 走的是陣列順序，而格線正是照這個順序填的（左到右、再往下）—— 也就是讀的
    /// 順序。這裡和列的游標不同：那邊的結構順序不見得等於螢幕順序，這邊兩者一樣。
    /// </remarks>
    private void MoveMenuFocus(int delta)
    {
        int count = menuCards.Count;
        if (count == 0) return;
        int at = menuFocus < 0
            ? (delta > 0 ? 0 : count - 1)
            : ((menuFocus + delta) % count + count) % count;
        menuFocus = at;

        MenuCardView card = menuCards[at];
        if (card == null || card.Root == null) return;
        EnsureFocusHighlight();
        if (focusHighlight == null) return;

        focusHighlight.color = CardFocusTint;
        RectTransform hl = focusHighlight.rectTransform;
        hl.SetParent(card.Root.transform, false);
        hl.anchorMin = Vector2.zero;
        hl.anchorMax = Vector2.one;
        hl.offsetMin = new Vector2(3f, 3f);
        hl.offsetMax = new Vector2(-3f, -3f);
        hl.SetAsFirstSibling();
        focusHighlight.gameObject.SetActive(true);
    }

    private void CollectFocusable()
    {
        focusable.Clear();
        if (pageList == null || settings == null) return;
        for (int c = 0; c < pageList.Categories.Count; c++)
        {
            CategoryView cat = pageList.Categories[c];
            if (cat == null || cat.Root == null || !cat.Root.activeInHierarchy) continue;
            for (int g = 0; g < cat.Groups.Count; g++)
            {
                GroupView grp = cat.Groups[g];
                if (grp == null) continue;
                for (int r = 0; r < grp.Rows.Count; r++)
                {
                    RowView row = grp.Rows[r];
                    if (row == null || row.Root == null || !row.Root.activeInHierarchy) continue;
                    if (row.Index < 0 || row.Index >= settings.Length) continue;
                    SettingData data = settings[row.Index];
                    if (data == null || data.isHidden) continue;
                    focusable.Add(row);
                }
            }
        }
    }

    private void ShowFocus(RowView row)
    {
        if (row == null || row.Root == null) return;
        EnsureFocusHighlight();
        if (focusHighlight == null) return;

        focusHighlight.color = RowFocusTint;
        RectTransform hl = focusHighlight.rectTransform;
        hl.SetParent(row.Root.transform, false);
        hl.anchorMin = Vector2.zero;
        hl.anchorMax = Vector2.one;
        hl.offsetMin = new Vector2(-6f, -2f);
        hl.offsetMax = new Vector2(6f, 2f);
        // 排在最底下：它是墊在這一列背後的光，不是蓋在字上面的一層膜。
        hl.SetAsFirstSibling();
        focusHighlight.gameObject.SetActive(true);

        ScrollRowIntoView(row);
    }

    private void EnsureFocusHighlight()
    {
        if (focusHighlight != null) return;
        if (settingsPanel == null) return;
        var go = new GameObject("~FocusHighlight", typeof(RectTransform));
        go.layer = settingsPanel.layer;
        focusHighlight = go.AddComponent<Image>();
        focusHighlight.raycastTarget = false;
        focusHighlight.color = RowFocusTint;
        focusHighlight.sprite = SettingsUiKit.PlainRoundedSprite(8f);
        focusHighlight.type = Image.Type.Sliced;
        focusHighlight.pixelsPerUnitMultiplier = 1f;

        // 左緣一條實心的細線。只有一塊淡色底的話，在做舊的紙上讀起來像污漬；
        // 一條線才讀得出是「指著這一列」。
        var edge = new GameObject("Edge", typeof(RectTransform));
        edge.layer = go.layer;
        edge.transform.SetParent(go.transform, false);
        RectTransform er = (RectTransform)edge.transform;
        er.anchorMin = new Vector2(0f, 0f);
        er.anchorMax = new Vector2(0f, 1f);
        er.pivot = new Vector2(0f, 0.5f);
        er.offsetMin = new Vector2(0f, 3f);
        er.offsetMax = new Vector2(4f, -3f);
        var edgeImage = edge.AddComponent<Image>();
        edgeImage.raycastTarget = false;
        edgeImage.color = new Color(1f, 0.86f, 0.44f, 0.95f);
    }

    /// <summary>把那一列捲進可視範圍。</summary>
    /// <remarks>
    /// 只在它真的跑出去的時候才捲，而且只捲到剛好進來 —— 每次都置中的話，
    /// 一列一列往下移會變成整頁跟著跳，讀不出自己移動了多少。
    /// </remarks>
    private void ScrollRowIntoView(RowView row)
    {
        if (pageList == null || pageList.Scroll == null) return;
        RectTransform viewport = pageList.Viewport;
        RectTransform content = pageList.Content;
        if (viewport == null || content == null) return;
        var target = row.Root.transform as RectTransform;
        if (target == null) return;

        Canvas.ForceUpdateCanvases();
        float viewHeight = viewport.rect.height;
        float contentHeight = content.rect.height;
        if (contentHeight <= viewHeight + 1f) return;

        // 這一列在 content 座標裡的上下緣（往下為負）。
        Vector3 localTop = content.InverseTransformPoint(
            target.TransformPoint(new Vector3(0f, target.rect.yMax, 0f)));
        Vector3 localBottom = content.InverseTransformPoint(
            target.TransformPoint(new Vector3(0f, target.rect.yMin, 0f)));
        float top = -localTop.y;              // 距 content 上緣多遠
        float bottom = -localBottom.y;

        float scrollable = contentHeight - viewHeight;
        float offset = (1f - pageList.Scroll.verticalNormalizedPosition) * scrollable;

        const float margin = 18f;
        if (top - margin < offset) offset = top - margin;
        else if (bottom + margin > offset + viewHeight) offset = bottom + margin - viewHeight;
        else return;

        offset = Mathf.Clamp(offset, 0f, scrollable);
        pageList.Scroll.verticalNormalizedPosition = 1f - offset / scrollable;
    }

    private void StepSetting(int index, int direction)
    {
        if (settings == null || index < 0 || index >= settings.Length) return;
        SettingData setting = settings[index];
        if (setting == null || setting.isHidden || setting.isAction) return;

        float before = setting.value;
        if (setting.isToggle)
        {
            setting.value = setting.value >= (setting.min + setting.max) * 0.5f ? setting.min : setting.max;
        }
        else if (setting.discreteLabels != null && setting.discreteLabels.Length > 0)
        {
            int count = setting.discreteLabels.Length;
            int at = Mathf.Clamp(Mathf.RoundToInt(setting.value), 0, count - 1);
            at = ((at + direction) % count + count) % count;
            setting.value = at;
        }
        else
        {
            float step = Mathf.Max(0.0001f, setting.step);
            float next = setting.value + direction * step;
            next = setting.min + Mathf.Round((next - setting.min) / step) * step;
            setting.value = Mathf.Clamp(next, setting.min, setting.max);
        }
        if (Mathf.Approximately(before, setting.value)) return;

        arcadeAppliedShown = false;
        currentIndex = index;
        ApplyToSettingsManager();
        RefreshLists();
    }

    private void RunSettingAction(int index)
    {
        currentIndex = index;
        if (index == ApplyArcadeIndex)
        {
            SettingsManager manager = SettingsManager.Instance;
            if (manager == null) return;
            manager.ApplyArcadeSettings();
            // 預設、細部參數的值都變了：重新讀一次，藏起來的列也跟著判定模式重算。
            LoadFromSettingsManager();
            RefreshJudgmentDetailVisibility();
            arcadeAppliedShown = true;
            RefreshLists();
            gameplayPreview?.RefreshNow();
            return;
        }

        if (index == AddSongsIndex || index == EditSongsIndex ||
            index == DeleteSongsIndex || index == CreateSongCategoryIndex)
        {
            RuntimeSongLibraryPanel.ManagementMode mode =
                index == CreateSongCategoryIndex
                    ? RuntimeSongLibraryPanel.ManagementMode.CreateCategory
                    : index == EditSongsIndex
                        ? RuntimeSongLibraryPanel.ManagementMode.Edit
                        : index == DeleteSongsIndex
                            ? RuntimeSongLibraryPanel.ManagementMode.Delete
                            : RuntimeSongLibraryPanel.ManagementMode.Add;
            RuntimeSongLibraryPanel.OpenFromSettings(
                settingsPanel != null ? settingsPanel.transform : transform, mode);
        }
    }

    /// <summary>進到一個大項裡面。</summary>
    private void OpenCategory(SettingsCategory category)
    {
        UiSfx.Select();
        SetOpenCategory(category);
    }

    /// <summary>
    /// 退一層：在某一類裡面就回到大項清單，已經在大項清單就離開設定。
    /// </summary>
    /// <remarks>
    /// 左上那顆鈕和 MIDI 的「返回（長按）」走同一個出口。兩個退法不一樣的話，用
    /// 鍵盤的人會被關在某一類裡面出不去 —— 他看得到的那顆鈕按不到。
    ///
    /// 譜面預覽裡的「返回」直接離開設定，不先回大項清單：預覽的側欄不分層，那裡
    /// 沒有「上一層」這個位置。
    /// </remarks>
    public void BackOrClose()
    {
        if (screenMode == ScreenMode.Page && openCategory.HasValue)
        {
            UiSfx.Back();
            SetOpenCategory(null);
            return;
        }
        HideSettings();
    }

    /// <summary>切換「大項清單」和「某一類的細部」。</summary>
    private void SetOpenCategory(SettingsCategory? category)
    {
        openCategory = category;
        bool detail = category.HasValue;
        if (settingsMenuRoot != null) settingsMenuRoot.SetActive(!detail);
        if (settingsListHost != null) settingsListHost.SetActive(detail);

        // 換層就把游標收掉：它原本指著的那一列（或那張卡片）已經不在畫面上了。
        focusedSetting = -1;
        menuFocus = -1;
        if (focusHighlight != null) focusHighlight.gameObject.SetActive(false);

        // 每一類都從最上面看起。沿用上一類捲到哪裡的話，項目少的那幾類會一打開
        // 就是空白 —— 捲動位置是屬於那一份清單的，不是屬於這一頁的。
        ResetListScroll(pageList);
        RefreshChrome();
        RefreshLists();
    }

    private void ToggleScreenMode()
    {
        SetScreenMode(screenMode == ScreenMode.Page ? ScreenMode.Preview : ScreenMode.Page);
    }

    private void SetScreenMode(ScreenMode mode)
    {
        screenMode = mode;
        bool preview = mode == ScreenMode.Preview;
        if (settingsPageRoot != null) settingsPageRoot.SetActive(!preview);
        if (previewRailRoot != null) previewRailRoot.SetActive(preview);
        // 預覽的外框整個關掉，不只是藏起來：GameplaySettingsPreview 在停用時會自己
        // 收掉那一場遊戲，設定頁上就不會有一首歌在背後偷偷播。
        if (gameplayPreviewFrame != null) gameplayPreviewFrame.SetActive(preview);
        if (chromeRoot != null) chromeRoot.transform.SetAsLastSibling();
        // 進預覽就回到**現在選的那一首與那個難度**（GetSongForSettingsPreview 會
        // 取 selectedVariant）。沿用上次瀏覽過的那一首會讓人在設定裡看著 A 調參數、
        // 回遊戲打的卻是 B。要看別首還是可以用左右鍵或滾輪翻。
        if (preview) ConfigurePreviewSong(true);
        RefreshChrome();
        RefreshLists();
        RefreshGameplayPreview();
    }

    private void RefreshChrome()
    {
        bool preview = screenMode == ScreenMode.Preview;
        // 進到某一類之後，頁首就是那一類的名字 —— 整頁只有它在說「你現在在哪」。
        SettingsUiKit.SetText(pageTitle,
            screenMode == ScreenMode.Page && openCategory.HasValue
                ? GetCategoryName(openCategory.Value)
                : Localize.T("設定", "设置", "SETTINGS"));
        SettingsUiKit.SetText(chromeBackLabel, Localize.T("返回", "返回", "Back"));
        SettingsUiKit.SetText(chromeModeLabel, preview
            ? Localize.T("設定", "设置", "Settings")
            : Localize.T("譜面預覽", "谱面预览", "Chart Preview"));
        if (chromeModeGlyph != null)
            chromeModeGlyph.Kind = preview ? SettingsGlyph.Shape.Sliders : SettingsGlyph.Shape.Play;
    }

    // ------------------------------------------------------------------
    // 更新
    // ------------------------------------------------------------------

    private void RefreshLists()
    {
        RefreshMenu();
        RefreshList(pageList);
        RefreshList(railList);
    }

    /// <summary>大項卡片上的字。語言換了、或哪一項被藏起來了，這一行都要跟著變。</summary>
    private void RefreshMenu()
    {
        for (int i = 0; i < menuCards.Count; i++)
        {
            MenuCardView card = menuCards[i];
            if (card == null || card.Root == null) continue;
            SettingsUiKit.SetText(card.Title, GetCategoryName(card.Category));
            SettingsUiKit.SetText(card.Detail, CategorySummary(card.Category));
        }
    }

    /// <summary>
    /// 值、標題、顯示與否全部重對一次。
    /// </summary>
    /// <remarks>
    /// 每次都整份重對，不追蹤「哪一列變了」：換判定模式會一次改掉十一個值、藏起
    /// 或露出一整段；換語言會改掉每一個字。只更新被按的那一列的話，這兩種情況都
    /// 會漏。五十幾列的比對在按一下的時候做一次，成本看不見。
    /// </remarks>
    private void RefreshList(ListView view)
    {
        if (view == null || settings == null) return;
        for (int c = 0; c < view.Categories.Count; c++)
        {
            CategoryView category = view.Categories[c];
            int visibleGroups = 0;
            for (int g = 0; g < category.Groups.Count; g++)
            {
                GroupView group = category.Groups[g];
                group.VisibleRows = 0;
                for (int r = 0; r < group.Rows.Count; r++)
                {
                    RowView row = group.Rows[r];
                    bool show = settings[row.Index] != null && !settings[row.Index].isHidden;
                    if (row.Root.activeSelf != show) row.Root.SetActive(show);
                    if (!show) continue;
                    group.VisibleRows++;
                    RefreshRow(row);
                }
                if (group.VisibleRows > 0) visibleGroups++;
            }

            // 只剩一段看得到的時候，段落標題也收起來：一個框裡只有一段，那個段名
            // 就只是把框的標題換句話再講一次。
            for (int g = 0; g < category.Groups.Count; g++)
            {
                GroupView group = category.Groups[g];
                if (group.Header == null) continue;
                bool show = group.VisibleRows > 0 && visibleGroups > 1;
                if (group.Header.activeSelf != show) group.Header.SetActive(show);
                SettingsUiKit.SetText(group.Label, GetGroupName(group.Group));
            }

            // 抽屜式的那一份只露出正在看的那一類；側欄不抽屜，有東西就攤開。
            bool anything = visibleGroups > 0
                && (!view.DrillDown || openCategory == category.Category);
            if (category.Root.activeSelf != anything) category.Root.SetActive(anything);

            // 抽屜式的那一份不要框內標題：進去之後頁首就是那一類的名字，框裡再寫
            // 一次只是同一句話用兩種大小各說一遍。側欄一次攤開兩類，那裡的標題是
            // 唯一分得出哪一段是哪一類的東西，不能拿掉。
            if (category.Header != null && category.Header.activeSelf == view.DrillDown)
                category.Header.SetActive(!view.DrillDown);
            SettingsUiKit.SetText(category.Title, GetCategoryName(category.Category));
        }
        if (view.Content != null && view.Content.gameObject.activeInHierarchy)
            LayoutRebuilder.MarkLayoutForRebuild(view.Content);
    }

    private void RefreshRow(RowView row)
    {
        SettingData setting = settings[row.Index];
        SettingsUiKit.SetText(row.Title, setting.title);
        switch (row.Kind)
        {
            case RowKind.Switch:
            {
                bool on = setting.value >= (setting.min + setting.max) * 0.5f;
                row.Switch.Set(on, !row.Root.activeInHierarchy);
                SettingsUiKit.SetText(row.Value, on ? setting.ResolveMaxLabel() : setting.ResolveMinLabel());
                break;
            }
            case RowKind.Action:
                SettingsUiKit.SetText(row.ActionLabel, ActionCaption(row.Index));
                break;
            default:
                SettingsUiKit.SetText(row.Value, FormatSettingValue(setting));
                if (row.Kind == RowKind.Number)
                {
                    // 到頂的那一側按鈕變暗。還能按，只是按了不會動 —— 暗掉是在
                    // 告訴玩家「不是沒反應，是到底了」。
                    float epsilon = Mathf.Max(0.0001f, setting.step * 0.25f);
                    if (row.Minus != null) row.Minus.interactable = setting.value > setting.min + epsilon;
                    if (row.Plus != null) row.Plus.interactable = setting.value < setting.max - epsilon;
                }
                break;
        }
    }

    private static void ResetListScroll(ListView view)
    {
        if (view == null || view.Content == null) return;
        view.WheelActive = false;
        view.WheelVelocity = 0f;
        view.Scroll?.StopMovement();
        view.Content.anchoredPosition = Vector2.zero;
    }

    // ------------------------------------------------------------------
    // 滾輪
    // ------------------------------------------------------------------

    private void Update()
    {
        if (settingsPanel == null || !settingsPanel.activeInHierarchy || settings == null) return;

        TickListScroll(pageList);
        TickListScroll(railList);

        float wheel = ReadWheel(out Vector2 pointer);
        if (Mathf.Abs(wheel) < 0.01f) return;

        if (screenMode == ScreenMode.Preview && IsPointerOverPreviewSongCard(pointer))
        {
            if (Time.unscaledTime < nextWheelNavigationTime) return;
            ChangePreviewSong(wheel > 0f ? -1 : 1);
            nextWheelNavigationTime = Time.unscaledTime + 0.16f;
            return;
        }

        // 只捲滑鼠底下那一份。判斷用射線打到的最上層物件，所以曲目管理的視窗蓋
        // 在設定頁上面的時候，捲的是那個視窗而不是被蓋住的設定。
        GameObject hit = TopHitAt(pointer);
        if (hit == null) return;
        if (screenMode == ScreenMode.Page)
        {
            bool onPage = settingsPageRoot != null && hit.transform.IsChildOf(settingsPageRoot.transform);
            bool onChrome = chromeRoot != null && hit.transform.IsChildOf(chromeRoot.transform);
            if (onPage || onChrome) ScrollListBy(pageList, -wheel * PageMetrics.WheelPixels);
        }
        else if (railList != null && hit.transform.IsChildOf(railList.Viewport))
        {
            ScrollListBy(railList, -wheel * RailMetrics.WheelPixels);
        }
    }

    /// <summary>
    /// 滾輪一格的量。Input System 預設把各平台的滾輪正規化成一格 1，舊版輸入也是一格 1。
    /// </summary>
    private static float ReadWheel(out Vector2 pointer)
    {
        float wheel = 0f;
        pointer = Vector2.zero;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (Mouse.current != null)
        {
            wheel = Mouse.current.scroll.ReadValue().y;
            pointer = Mouse.current.position.ReadValue();
        }
#elif ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        if (Mouse.current != null)
        {
            wheel = Mouse.current.scroll.ReadValue().y;
            pointer = Mouse.current.position.ReadValue();
        }
        else
        {
            wheel = UnityEngine.Input.mouseScrollDelta.y;
            pointer = UnityEngine.Input.mousePosition;
        }
#else
        wheel = UnityEngine.Input.mouseScrollDelta.y;
        pointer = UnityEngine.Input.mousePosition;
#endif
        return wheel;
    }

    private GameObject TopHitAt(Vector2 screenPoint)
    {
        EventSystem system = EventSystem.current;
        if (system == null) return null;
        if (wheelPointer == null || wheelPointerOwner != system)
        {
            wheelPointer = new PointerEventData(system);
            wheelPointerOwner = system;
        }
        wheelPointer.position = screenPoint;
        wheelHits.Clear();
        system.RaycastAll(wheelPointer, wheelHits);
        return wheelHits.Count > 0 ? wheelHits[0].gameObject : null;
    }

    /// <summary>
    /// 滾一格就把目標往下推一段，畫面用緩動追上去。
    /// </summary>
    /// <remarks>
    /// 連續滾的時候從**目標**往下推，不是從目前的位置：緩動還沒追上就再滾一格，
    /// 從目前位置算的話每一格都會少走一點，快速滾動時整份清單像被拖住一樣。
    /// </remarks>
    private static void ScrollListBy(ListView view, float pixels)
    {
        if (view == null || view.Content == null || view.Viewport == null) return;
        float max = Mathf.Max(0f, view.Content.rect.height - view.Viewport.rect.height);
        float from = view.WheelActive ? view.WheelTarget : view.Content.anchoredPosition.y;
        view.WheelTarget = Mathf.Clamp(from + pixels, 0f, max);
        view.WheelActive = true;
        view.Scroll?.StopMovement();
    }

    private static void TickListScroll(ListView view)
    {
        if (view == null || !view.WheelActive || view.Content == null || view.Viewport == null) return;

        // 玩家開始用拖的，滾輪的緩動就讓位。拖曳中 ScrollRect 才會有速度。
        if (!view.Content.gameObject.activeInHierarchy
            || (view.Scroll != null && view.Scroll.velocity.sqrMagnitude > 25f))
        {
            view.WheelActive = false;
            view.WheelVelocity = 0f;
            return;
        }

        float max = Mathf.Max(0f, view.Content.rect.height - view.Viewport.rect.height);
        view.WheelTarget = Mathf.Clamp(view.WheelTarget, 0f, max);
        Vector2 position = view.Content.anchoredPosition;
        position.y = Mathf.SmoothDamp(position.y, view.WheelTarget, ref view.WheelVelocity, 0.085f,
            Mathf.Infinity, Time.unscaledDeltaTime);
        if (Mathf.Abs(position.y - view.WheelTarget) < 0.5f)
        {
            position.y = view.WheelTarget;
            view.WheelActive = false;
            view.WheelVelocity = 0f;
        }
        view.Content.anchoredPosition = position;
    }
}
