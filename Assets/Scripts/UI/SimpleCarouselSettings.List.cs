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
/// **為什麼一打開就全部攤開。** 旋轉卡片一次只給看一項，玩家要先知道自己要的東西
/// 叫什麼、在哪一類，才找得到它 —— 那等於要求他先讀過這份設定。攤開之後眼睛可以
/// 直接掃，要改的東西會自己跳出來；同一類的東西框在一起，框本身就是目錄。
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
        public TextMeshProUGUI Title;
        public readonly List<GroupView> Groups = new List<GroupView>();
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

    /// <summary>預覽畫面的側欄只留這兩類 —— 看著譜面判斷得了的東西。</summary>
    private static readonly SettingsCategory[] PreviewCategories =
    {
        SettingsCategory.Visual,
        SettingsCategory.Judgment,
    };

    private ScreenMode screenMode = ScreenMode.Page;
    private GameObject gameplayPreviewFrame;
    private GameObject settingsPageRoot;
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

        RectTransform host = SettingsUiKit.CreateRect("ListHost", root);
        SettingsUiKit.Stretch(host, 0f, 0f, 0f, 120f);
        pageList = BuildList(host, CategoryOrder, 2, PageMetrics, 120f);
        settingsPageRoot = root.gameObject;
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
        back.onClick.AddListener(HideSettings);

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

    private static bool IsOffWord(string label) => label == "Off" || label == "關" || label == "关";
    private static bool IsOnWord(string label) => label == "On" || label == "開" || label == "开";

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
        SettingsUiKit.SetText(pageTitle, Localize.T("設定", "设置", "SETTINGS"));
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
        RefreshList(pageList);
        RefreshList(railList);
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

            bool anything = visibleGroups > 0;
            if (category.Root.activeSelf != anything) category.Root.SetActive(anything);
            SettingsUiKit.SetText(category.Title, GetCategoryName(category.Category));
        }
        if (view.Content != null) LayoutRebuilder.MarkLayoutForRebuild(view.Content);
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
