using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 從曲庫挑一份譜，給考程的某一格用。
/// </summary>
/// <remarks>
/// **挑的是「一份譜」不是「一首歌」。** 同一首歌的普通和困難是兩份完全不同的考題，
/// 而考程記的是 <c>chartFileName</c>。所以清單每一列是一個難度變體，後面印等級。
///
/// **沒有分析檔的譜不列出來。** 考程的排序鍵、容錯點、顯著度全部讀
/// <see cref="ChartAnalysis"/>；挑一份算不出這些的譜進去，那一階的考卷上會有一格永遠
/// 是問號，而且容錯點會被少算。與其讓人挑了才發現，不如一開始就不給挑。
///
/// **教學課程也不列。** 它們是課不是曲子，長度和難度都不能和曲子一起比。
/// </remarks>
public sealed class SyllabusSongPicker : MonoBehaviour
{
    private static SyllabusSongPicker instance;

    private const float PanelWidth = 900f;
    private const float PanelHeight = 720f;
    private const float RowHeight = 46f;

    private static readonly Color Paper = new Color(0.93f, 0.89f, 0.74f, 1f);
    private static readonly Color Ink = new Color(0.22f, 0.115f, 0.055f, 1f);
    private static readonly Color InkSoft = new Color(0.36f, 0.24f, 0.13f, 1f);
    private static readonly Color InkFaint = new Color(0.50f, 0.38f, 0.25f, 1f);

    private struct Entry
    {
        public string Chart;
        public string Name;
        public string Difficulty;
        public int Level;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private Action<string> picked;
    private RectTransform content;
    private TMP_InputField search;
    private TextMeshProUGUI count;

    public static bool IsOpen => instance != null;

    public static void Open(Action<string> onPicked)
    {
        if (instance != null) Destroy(instance.gameObject);
        var root = new GameObject("SyllabusSongPicker");
        instance = root.AddComponent<SyllabusSongPicker>();
        instance.picked = onPicked;
        instance.Build();
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 比那張編輯卡再高一層：它是從卡上開出來的。
        canvas.sortingOrder = 26200;
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

        RectTransform panel = Rect(transform, "Panel", new Vector2(PanelWidth, PanelHeight));
        Image face = panel.gameObject.AddComponent<Image>();
        face.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        face.type = Image.Type.Tiled;
        face.color = Paper;
        face.raycastTarget = true;
        PageRuleGraphic rule = PageRuleGraphic.Attach(panel, 29,
            new Color(0.22f, 0.115f, 0.05f, 0.85f), 0.024f, 3.4f, true, 1);
        if (rule != null) rule.Wear = 0.22f;

        Collect();

        Label(panel, Localize.S("挑一份譜"), 40f, 30f, 420f, 28f, Ink, TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        count = Label(panel, string.Empty, -40f, 34f, 300f, 20f, InkFaint,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal, true);

        BuildSearch(panel);

        RectTransform viewport = Rect(panel, "Viewport", Vector2.zero);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.offsetMin = new Vector2(36f, 36f);
        viewport.offsetMax = new Vector2(-36f, -132f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = Rect(viewport, "Content", Vector2.zero);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.spacing = 2f;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        Fill(string.Empty);
    }

    private void BuildSearch(RectTransform panel)
    {
        RectTransform box = Rect(panel, "Search", Vector2.zero);
        box.anchorMin = box.anchorMax = new Vector2(0f, 1f);
        box.pivot = new Vector2(0f, 1f);
        box.anchoredPosition = new Vector2(36f, -76f);
        box.sizeDelta = new Vector2(PanelWidth - 72f, 44f);
        Image face = box.gameObject.AddComponent<Image>();
        face.color = new Color(0.42f, 0.27f, 0.11f, 0.10f);

        RectTransform area = Rect(box, "TextArea", Vector2.zero);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(12f, 4f);
        area.offsetMax = new Vector2(-12f, -4f);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = Label(area, Localize.S("輸入曲名篩選"), 0f, 0f, 0f, 20f, InkFaint,
            TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.rectTransform);
        TextMeshProUGUI text = Label(area, string.Empty, 0f, 0f, 0f, 20f, Ink,
            TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);

        search = box.gameObject.AddComponent<TMP_InputField>();
        search.textViewport = area;
        search.textComponent = text;
        search.placeholder = placeholder;
        search.onValueChanged.AddListener(Fill);
    }

    /// <summary>曲庫裡每一份「考得成」的譜。</summary>
    private void Collect()
    {
        entries.Clear();
        SongSelectionManager manager = SongSelectionManager.Instance;
        if (manager?.AllSongs == null) return;
        foreach (SongSelectionManager.SongOption group in manager.AllSongs)
        {
            if (group == null || SongSelectionManager.IsTutorialSong(group)) continue;
            List<SongSelectionManager.SongOption> variants =
                group.difficultyVariants != null && group.difficultyVariants.Count > 0
                    ? group.difficultyVariants
                    : new List<SongSelectionManager.SongOption> { group };
            for (int i = 0; i < variants.Count; i++)
            {
                SongSelectionManager.SongOption variant = variants[i];
                if (variant == null || string.IsNullOrWhiteSpace(variant.chartFileName)) continue;
                ChartAnalysis analysis = ChartAnalysisCache.GetImmediate(variant.chartFileName);
                if (analysis == null || analysis.noteCount <= 0) continue;
                entries.Add(new Entry
                {
                    Chart = variant.chartFileName,
                    Name = string.IsNullOrWhiteSpace(variant.displayName)
                        ? group.displayName ?? variant.chartFileName
                        : variant.displayName,
                    Difficulty = variant.difficultyName ?? string.Empty,
                    Level = Mathf.Max(1, variant.difficultyLevel),
                });
            }
        }
        // 由易到難：考程的每一格都有一個等級帶，照等級排才找得到想要的那一份。
        entries.Sort((a, b) => a.Level != b.Level
            ? a.Level.CompareTo(b.Level)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void Fill(string filter)
    {
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform child = content.GetChild(i);
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        int shown = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (!string.IsNullOrEmpty(filter) &&
                entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            AddRow(entry);
            shown++;
        }
        count.text = string.IsNullOrEmpty(filter)
            ? Localize.SF("{0} 份譜", entries.Count)
            : Localize.SF("{0} / {1} 份譜", shown, entries.Count);
    }

    private void AddRow(Entry entry)
    {
        var rowObject = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(LayoutElement));
        rowObject.layer = content.gameObject.layer;
        var rect = rowObject.GetComponent<RectTransform>();
        rect.SetParent(content, false);
        rowObject.GetComponent<LayoutElement>().preferredHeight = RowHeight;

        Image face = rowObject.GetComponent<Image>();
        face.color = new Color(0.42f, 0.27f, 0.11f, 0.06f);
        Button button = rowObject.GetComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(2.4f, 2.0f, 1.4f, 2.2f);
        colours.pressedColor = new Color(1.5f, 1.2f, 0.85f, 3.0f);
        button.colors = colours;
        string chart = entry.Chart;
        button.onClick.AddListener(() =>
        {
            UiSfx.Select();
            Action<string> callback = picked;
            Close();
            callback?.Invoke(chart);
        });

        // 寬度要留給右邊那欄：清單寬是 PanelWidth − 72，右欄 220 從右緣排回來，
        // 所以曲名最多到 PanelWidth − 72 − 220 − 14。
        TextMeshProUGUI name = Label(rect, entry.Name, 14f, 12f, PanelWidth - 330f, 20f, Ink,
            TextAlignmentOptions.MidlineLeft);
        // 曲名是使用者曲庫裡的字，可能是日文。
        ClassicalBookUITheme.ApplyContentFont(name);
        Label(rect, $"{entry.Difficulty}　Lv{entry.Level}", -14f, 12f, 220f, 18f, InkSoft,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal, true);
    }

    private void Update()
    {
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;
        // 搜尋框有焦點時 Esc 讓 TMP 自己處理（清掉編輯狀態），不然按一次會直接關窗。
        if (search != null && search.isFocused) return;
        if (keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    private void Close()
    {
        if (instance == this) instance = null;
        Destroy(gameObject);
    }

    // ── 版面小工具 ────────────────────────────────────────────────────

    private static Image Img(Transform parent, string name, Color colour)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.layer = parent.gameObject.layer;
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = colour;
        return image;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        var rectObject = new GameObject(name, typeof(RectTransform));
        rectObject.layer = parent.gameObject.layer;
        var rect = rectObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float x, float y,
        float width, float size, Color colour, TextAlignmentOptions alignment,
        FontStyles style = FontStyles.Normal, bool fromRight = false)
    {
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = parent.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.pivot = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, size + 10f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.fontStyle = style;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
