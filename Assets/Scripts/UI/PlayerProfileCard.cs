using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 報上名來：第一次開遊戲時請演奏者留下名字和一個印記。
/// </summary>
/// <remarks>
/// **為什麼要有這張卡。** 畫面上開始有「演奏者資料」「你的評分」「上次挑戰」這些
/// 東西之後，那個「你」必須有名字 —— 一塊寫著「演奏者」的銘牌和一塊空白的銘牌一
/// 樣沒有份量。
///
/// **為什麼不是設定頁裡的一列。** 這是開場第一件事，而設定頁是「之後要調的東西」。
/// 它只在沒有名字的時候自己出現一次；要改名時從設定頁再叫同一張卡。
///
/// **資料只留在這台機器上**（見 <see cref="PlayerProfile"/>）—— 這張卡不會把任何
/// 東西送到任何地方。
/// </remarks>
internal sealed class PlayerProfileCard : MonoBehaviour
{
    private const int SortingOrder = 27000;      // 比譜面檢視器（26500）還高

    private static PlayerProfileCard instance;

    private CanvasGroup group;
    private TMP_InputField field;
    private TextMeshProUGUI monogram;
    private Image sealFace;
    private RawImage portraitFace;
    private TextMeshProUGUI portraitLabel;
    private Button confirm;
    private TextMeshProUGUI confirmLabel;
    private readonly Image[] chips = new Image[6];
    private int seal;
    private Action closed;

    /// <summary>叫出這張卡。已經在畫面上就不重複開。</summary>
    public static void Show(Action onClosed = null)
    {
        if (instance != null)
        {
            instance.closed = onClosed;
            return;
        }
        var host = new GameObject("PlayerProfileCard");
        instance = host.AddComponent<PlayerProfileCard>();
        instance.closed = onClosed;
    }

    /// <summary>還沒建立過就請他建立。已經有名字就什麼都不做。</summary>
    public static void ShowIfNew(Action onClosed = null)
    {
        if (PlayerProfile.Exists)
        {
            onClosed?.Invoke();
            return;
        }
        Show(onClosed);
    }

    private void Awake()
    {
        instance = this;
        seal = PlayerProfile.SealIndex;
        Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        RectTransform root = Rect("Root", transform);
        Stretch(root);

        // 底下整片壓暗：這張卡是一個問題，在回答之前不該能去點別的東西。
        var shade = root.gameObject.AddComponent<Image>();
        shade.color = new Color(0.05f, 0.028f, 0.014f, 0.72f);
        shade.raycastTarget = true;

        RectTransform card = Rect("Card", root);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        // 620 不是 520：最底下多了一條「換人」。那一條和上面是兩件不同的事
        // （改這個人 vs 換一個人），所以它有自己的規線和自己的一列，不是擠在按鈕旁。
        card.sizeDelta = new Vector2(760f, 620f);
        var face = card.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.StylePlate(face, false);
        ClassicalBookUITheme.AddShadow(card.gameObject, new Color(0f, 0f, 0f, 0.55f), new Vector2(12f, -12f));

        TextMeshProUGUI title = Label(card, "Title",
            PlayerProfile.Exists ? Localize.S("改個名字") : Localize.S("報上名來"), 34f,
            new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(640f, 48f),
            TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(0.96f, 0.90f, 0.76f, 1f);

        Label(card, "Hint",
            Localize.S("這個名字只留在這台電腦上，不會傳到任何地方。"), 20f,
            new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(660f, 30f),
            TextAlignmentOptions.Center).color = new Color(0.82f, 0.76f, 0.64f, 0.85f);

        // 印記：名字的第一個字刻在火漆上。
        RectTransform sealRect = Rect("Seal", card);
        sealRect.anchorMin = sealRect.anchorMax = new Vector2(0f, 1f);
        sealRect.pivot = new Vector2(0f, 1f);
        sealRect.anchoredPosition = new Vector2(58f, -128f);
        sealRect.sizeDelta = new Vector2(132f, 132f);
        sealFace = sealRect.gameObject.AddComponent<Image>();
        sealFace.sprite = SettingsUiKit.KnobSprite();
        sealFace.type = Image.Type.Simple;
        sealFace.color = PlayerProfile.SealColours[seal];
        monogram = Label(sealRect, "Monogram", "？", 56f,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 92f),
            TextAlignmentOptions.Center);
        monogram.fontStyle = FontStyles.Bold;
        monogram.color = new Color(0.98f, 0.94f, 0.82f, 0.94f);

        // 頭像疊在印記上：有圖就蓋掉印記，沒有就露出底下的字。圖在存檔的時候就
        // 已經切成圓的了（見 PlayerProfile.SetPortrait），這裡不必再做遮罩。
        RectTransform portraitRect = Rect("Portrait", sealRect);
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.one;
        portraitRect.offsetMin = new Vector2(3f, 3f);
        portraitRect.offsetMax = new Vector2(-3f, -3f);
        portraitFace = portraitRect.gameObject.AddComponent<RawImage>();
        portraitFace.raycastTarget = false;
        portraitFace.enabled = false;

        // 名字
        RectTransform box = Rect("NameBox", card);
        box.anchorMin = box.anchorMax = new Vector2(0f, 1f);
        box.pivot = new Vector2(0f, 1f);
        box.anchoredPosition = new Vector2(216f, -146f);
        box.sizeDelta = new Vector2(486f, 84f);
        var boxFace = box.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.StylePlate(boxFace, false);

        TextMeshProUGUI text = Label(box, "Text", string.Empty, 32f,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(446f, 56f),
            TextAlignmentOptions.MidlineLeft);
        TextMeshProUGUI placeholder = Label(box, "Placeholder", Localize.S("你的名字"), 32f,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(446f, 56f),
            TextAlignmentOptions.MidlineLeft);
        placeholder.color = new Color(0.70f, 0.63f, 0.52f, 0.75f);

        field = box.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = box;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = boxFace;
        field.characterLimit = PlayerProfile.MaxNameLength;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.text = PlayerProfile.Exists ? PlayerProfile.Name : string.Empty;
        field.onValueChanged.AddListener(_ => Refresh());

        // 印記顏色：六個色票。
        Label(card, "SealCaption", Localize.S("印記"), 20f,
            new Vector2(0f, 1f), new Vector2(58f, -284f), new Vector2(200f, 28f),
            TextAlignmentOptions.MidlineLeft).color = new Color(0.82f, 0.76f, 0.64f, 0.85f);
        for (int i = 0; i < chips.Length; i++)
        {
            int index = i;
            RectTransform chip = Rect("Seal" + i, card);
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
            chip.pivot = new Vector2(0f, 1f);
            // 色票 76×76：手指按得到（換算實體像素遠在 44 之上）。
            chip.anchoredPosition = new Vector2(58f + i * 92f, -318f);
            chip.sizeDelta = new Vector2(76f, 76f);
            var chipFace = chip.gameObject.AddComponent<Image>();
            chipFace.sprite = SettingsUiKit.KnobSprite();
            chipFace.color = PlayerProfile.SealColours[i];
            chips[i] = chipFace;
            var chipButton = chip.gameObject.AddComponent<Button>();
            chipButton.targetGraphic = chipFace;
            chipButton.onClick.AddListener(() =>
            {
                seal = index;
                UiSfx.Select();
                Refresh();
            });
        }

        Button pick = MakeButton(card, "Portrait", Localize.S("選一張頭像"), new Vector2(0f, 0f),
            new Vector2(58f, 146f), new Vector2(260f, 82f), out portraitLabel);
        pick.onClick.AddListener(PickPortrait);

        confirm = MakeButton(card, "Confirm", Localize.S("開始"), new Vector2(1f, 0f),
            new Vector2(-58f, 146f), new Vector2(220f, 82f), out confirmLabel);
        confirm.onClick.AddListener(Commit);

        BuildSwitcher(card);

        Refresh();
        StartCoroutine(FadeIn());
        // 直接給焦點：這張卡只有一件事要做。
        field.Select();
        field.ActivateInputField();
    }

    /// <summary>
    /// 最底下那一條：這台電腦上有誰，以及怎麼換人、怎麼多開一個。
    /// </summary>
    /// <remarks>
    /// **為什麼和上面用規線分開。** 上面在做的是「改這個人」，這一條是「換一個人」——
    /// 兩件事的後果完全不同（一個改名字，一個換掉整份成績），擠在同一排按鈕裡遲早會
    /// 有人按錯。
    ///
    /// **換人是立刻生效並關掉卡片，不等「開始」。** 「開始」是在確認你剛打的名字，而
    /// 名字是屬於**原本那個人**的；換完人再按「開始」會把新名字寫到新的人身上，那不
    /// 是任何人想要的。所以換人先把手上的編輯丟掉。
    ///
    /// 每個人自己的名字是從他自己的 `player_profile.json` 讀的，不是另外存一份
    /// （見 <see cref="PlayerSlots"/>）—— 存兩份遲早會不一致。
    /// </remarks>
    private void BuildSwitcher(RectTransform card)
    {
        RectTransform rule = Rect("SwitchRule", card);
        rule.anchorMin = new Vector2(0f, 0f);
        rule.anchorMax = new Vector2(1f, 0f);
        rule.pivot = new Vector2(0.5f, 0f);
        rule.offsetMin = new Vector2(58f, 118f);
        rule.offsetMax = new Vector2(-58f, 119.5f);
        var ruleImage = rule.gameObject.AddComponent<Image>();
        ruleImage.color = new Color(0.92f, 0.78f, 0.44f, 0.30f);
        ruleImage.raycastTarget = false;

        Label(card, "SwitchCaption", Localize.S("這台電腦上的演奏者"), 20f,
            new Vector2(0f, 0f), new Vector2(58f, 92f), new Vector2(300f, 26f),
            TextAlignmentOptions.MidlineLeft).color = new Color(0.82f, 0.76f, 0.64f, 0.85f);

        float x = 58f;
        var ids = PlayerSlots.Ids;
        for (int i = 0; i < ids.Count && x < 620f; i++)
        {
            string id = ids[i];
            bool active = string.Equals(id, PlayerSlots.ActiveId, System.StringComparison.Ordinal);
            string who = PlayerSlots.NameOf(id);
            if (string.IsNullOrEmpty(who)) who = Localize.S("未命名");

            Button slot = MakeButton(card, "Slot" + i, who, new Vector2(0f, 0f),
                new Vector2(x, 22f), new Vector2(150f, 58f), out TextMeshProUGUI slotLabel);
            slotLabel.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            if (active)
            {
                // 目前這位不做成可以按的：按了也沒事發生，而一個按了沒反應的按鈕比
                // 一個明顯停用的更讓人困惑。
                slot.interactable = false;
                slotLabel.color = new Color(0.96f, 0.90f, 0.76f, 1f);
            }
            else
            {
                slot.onClick.AddListener(() =>
                {
                    UiSfx.Select();
                    if (PlayerSlots.Switch(id)) StartCoroutine(FadeOutAndClose());
                });
            }
            x += 162f;
        }

        Button add = MakeButton(card, "AddPlayer", Localize.S("＋ 新增"), new Vector2(1f, 0f),
            new Vector2(-58f, 22f), new Vector2(150f, 58f), out TextMeshProUGUI _);
        add.onClick.AddListener(() =>
        {
            UiSfx.Open();
            PlayerSlots.Create();
            // 新的人還沒有名字，所以把這張卡收掉再開一張 —— 開出來會是「報上名來」，
            // 因為 PlayerProfile 已經指到新的（空的）那個資料夾了。
            Action after = closed;
            closed = () => Show(after);
            StartCoroutine(FadeOutAndClose());
        });
    }

    /// <summary>選一張圖。任何格式 Unity 讀得進來的都行，會被裁成圓的。</summary>
    private void PickPortrait()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // 用畫面上既有的那個原生對話框（RuntimeSongLibraryPanel 匯入曲目用的同
        // 一個），不要再寫一份 P/Invoke。
        string path = RuntimeSongLibraryPanel.WindowsFilePicker.TryPick(
            Localize.S("圖片\0*.png;*.jpg;*.jpeg;*.bmp;*.tga\0所有檔案\0*.*\0\0"), Localize.S("選一張頭像"));
        if (string.IsNullOrEmpty(path))
        {
            UiSfx.Back();
            return;
        }
        if (PlayerProfile.SetPortrait(path))
        {
            UiSfx.Select();
        }
        else
        {
            // 讀不進來（格式不支援、檔案壞了）要說出來，不要靜靜地什麼都沒發生。
            UiSfx.Denied();
            if (portraitLabel != null) portraitLabel.text = Localize.S("這張讀不進來");
        }
        Refresh();
#else
        UiSfx.Denied();
        if (portraitLabel != null) portraitLabel.text = Localize.S("這個平台不支援");
#endif
    }

    private void Refresh()
    {
        string name = field != null ? field.text : string.Empty;
        bool ready = !string.IsNullOrWhiteSpace(name);
        if (monogram != null)
        {
            monogram.text = ready ? name.Trim().Substring(0, 1).ToUpperInvariant() : "？";
        }
        Texture2D art = PlayerProfile.Portrait;
        if (portraitFace != null)
        {
            portraitFace.texture = art;
            portraitFace.enabled = art != null;
        }
        if (monogram != null) monogram.enabled = art == null;
        if (portraitLabel != null && !portraitLabel.text.StartsWith("這"))
        {
            portraitLabel.text = art != null ? Localize.S("換一張頭像") : Localize.S("選一張頭像");
        }
        if (sealFace != null) sealFace.color = PlayerProfile.SealColours[seal];
        for (int i = 0; i < chips.Length; i++)
        {
            if (chips[i] == null) continue;
            // 選中的那一個亮一階，其餘退到八成 —— 六個同樣飽和的圓點看不出誰被選中。
            Color c = PlayerProfile.SealColours[i];
            float k = i == seal ? 1.18f : 0.80f;
            chips[i].color = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k),
                Mathf.Clamp01(c.b * k), 1f);
        }
        if (confirm != null) confirm.interactable = ready;
        if (confirmLabel != null)
        {
            confirmLabel.color = ready
                ? new Color(0.22f, 0.13f, 0.06f, 1f)
                : new Color(0.38f, 0.30f, 0.22f, 0.7f);
        }
    }

    private void Commit()
    {
        string name = field != null ? field.text : string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            UiSfx.Denied();
            return;
        }
        UiSfx.Select();
        PlayerProfile.Save(name, seal);
        StartCoroutine(FadeOutAndClose());
    }

    private System.Collections.IEnumerator FadeIn()
    {
        float elapsed = 0f;
        const float span = 0.24f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / span), 3f);
            yield return null;
        }
        group.alpha = 1f;
    }

    private System.Collections.IEnumerator FadeOutAndClose()
    {
        // 還看得見就要繼續吃掉點擊，只關掉自己的按鈕。
        group.interactable = false;
        float elapsed = 0f;
        const float span = 0.20f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = 1f - (1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / span), 3f));
            yield return null;
        }
        Action done = closed;
        closed = null;
        Destroy(gameObject);
        done?.Invoke();
    }

    // ------------------------------------------------------------------
    // 小工具
    // ------------------------------------------------------------------

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, string value, float size,
        Vector2 anchor, Vector2 position, Vector2 box, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = box;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = value;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = new Color(0.94f, 0.90f, 0.80f, 1f);
        label.raycastTarget = false;
        // TMP 在「框高 < 字級 × 1.5」時整段不畫，所以一律用 Overflow。
        label.overflowMode = TextOverflowModes.Overflow;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }

    private static Button MakeButton(RectTransform parent, string name, string caption,
        Vector2 anchor, Vector2 position, Vector2 size, out TextMeshProUGUI label)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var face = rect.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.StylePlate(face, true);
        label = Label(rect, "Label", caption, 28f, new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(size.x - 24f, size.y - 16f), TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        // 放不下就縮，不要畫到鈕外面去（Label 開的是 Overflow）。
        // 下限比專案其他按鈕的 0.72 低：這四顆鈕裡有一顆印的是**玩家自己打的
        // 名字**，長度沒有上限。126 寬的格子在 28 級只放得下四個全角字 ——
        // 「未命名」的英文 Unnamed 就已經要 ×1.11，五個字的中文名字要 ×1.19，
        // Christopher 要 ×1.49。0.66 讓這些都進得來。
        label.enableAutoSizing = true;
        label.fontSizeMin = 28f * 0.66f;
        label.fontSizeMax = 28f;
        label.color = new Color(0.22f, 0.13f, 0.06f, 1f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        return button;
    }
}
