using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared runtime styling for the song library and settings pages. Everything
/// is made from regular UI primitives so the theme remains build-safe and does
/// not depend on editor-only generated assets.
/// </summary>
public static class ClassicalBookUITheme
{
    private static TMP_FontAsset elegantFont;
    private static TMP_FontAsset traditionalCjkFont;
    private static TMP_FontAsset simplifiedCjkFont;
    private static TMP_FontAsset japaneseFont;
    private static Sprite paperTextureSprite;
    private static Sprite woodTextureSprite;
    private static Sprite coverTextureSprite;
    private static Sprite clothTextureSprite;
    public static readonly Color Ink = new Color(0.16f, 0.105f, 0.065f, 1f);
    public static readonly Color MutedInk = new Color(0.34f, 0.245f, 0.15f, 1f);
    public static readonly Color Parchment = new Color(0.91f, 0.84f, 0.67f, 0.985f);
    public static readonly Color ParchmentLight = new Color(0.97f, 0.925f, 0.79f, 0.985f);
    public static readonly Color AgedPaper = new Color(0.72f, 0.62f, 0.43f, 0.95f);
    public static readonly Color DarkWood = new Color(0.075f, 0.040f, 0.025f, 0.98f);
    public static readonly Color Wood = new Color(0.20f, 0.105f, 0.050f, 1f);
    public static readonly Color Gold = new Color(0.78f, 0.58f, 0.22f, 1f);
    public static readonly Color Burgundy = new Color(0.40f, 0.075f, 0.065f, 1f);

    public static void EnsureBookBackdrop(GameObject panel, string heading, bool cleanOuterTop = false)
    {
        if (panel == null || panel.transform.Find("ClassicalBookBackdrop") != null) return;

        RectTransform backdrop = AddRect(panel.transform, "ClassicalBookBackdrop", DarkWood,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        backdrop.SetAsFirstSibling();
        ApplyTexture(backdrop, ResolveTextureSprite(true));

        AddCentered(backdrop, "ConcertVelvet", new Vector2(2320f, 1280f), Vector2.zero,
            new Color(0.115f, 0.025f, 0.028f, 0.96f));
        AddCentered(backdrop, "WarmStageHalo", new Vector2(1960f, 1090f), new Vector2(0f, 16f),
            new Color(0.46f, 0.31f, 0.12f, 0.22f));

        AddCentered(backdrop, "BookShadow", new Vector2(1940f, 1135f), new Vector2(16f, -15f),
            new Color(0f, 0f, 0f, 0.72f));
        RectTransform leftPage = AddCentered(backdrop, "LeftScorePage", new Vector2(940f, 1080f),
            new Vector2(-466f, 0f), Parchment);
        RectTransform rightPage = AddCentered(backdrop, "RightScorePage", new Vector2(940f, 1080f),
            new Vector2(466f, 0f), ParchmentLight);
        ApplyTexture(leftPage, ResolveTextureSprite(false));
        ApplyTexture(rightPage, ResolveTextureSprite(false));
        leftPage.localRotation = Quaternion.identity;
        rightPage.localRotation = Quaternion.identity;
        AddOutline(leftPage.gameObject, Gold, new Vector2(3f, -3f));
        AddOutline(rightPage.gameObject, Gold, new Vector2(3f, -3f));

        AddCentered(backdrop, "BookSpineShadow", new Vector2(38f, 1080f), Vector2.zero,
            new Color(0.11f, 0.055f, 0.025f, 0.9f));
        AddCentered(backdrop, "BookSpineGold", new Vector2(5f, 1080f), Vector2.zero, Gold);

        AddStaffLines(leftPage);
        AddStaffLines(rightPage);
        AddPageCorners(leftPage);
        AddPageCorners(rightPage);
        AddArcadeScoreRails(backdrop, !cleanOuterTop);

        TextMeshProUGUI header = AddText(backdrop, "BookHeading", heading, 34f, Ink, FontStyles.Bold);
        SetRect(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(920f, 62f), new Vector2(0f, -82f));
        header.characterSpacing = 5f;
        header.alignment = TextAlignmentOptions.Center;

        TextMeshProUGUI subtitle = AddText(backdrop, "BookSubtitle", "—  SELECT MUSIC / MUSICAL SCORE ARCHIVE  —", 16f,
            MutedInk, FontStyles.Italic);
        SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(700f, 36f), new Vector2(0f, -127f));
        subtitle.characterSpacing = 3f;
        subtitle.alignment = TextAlignmentOptions.Center;
        if (cleanOuterTop)
        {
            header.gameObject.SetActive(false);
            subtitle.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 難度**名稱**的顏色：Normal 綠、Hard 黃、Expert 紅，其餘（Real、Master…）
    /// 紫。和等級無關。
    /// </summary>
    /// <remarks>
    /// 顏色照名字而不是照等級，是因為同一個名字在不同曲子上的等級差很多——
    /// 一首簡單曲子的 Expert 可能只有 Lv.9，難曲的 Hard 就有 Lv.12，照等級
    /// 上色的話同一個難度會在不同曲子間變色，看不出「這是第幾階」。
    ///
    /// 認不出名字時（自訂難度名）退回等級判斷，才不會整片變成同一個顏色。
    /// </remarks>
    public static Color GetDifficultyColour(string difficultyName, int level)
    {
        // 唯一一份配色在 DifficultyVisualPalette，這裡只是轉呼叫。
        return DifficultyVisualPalette.For(difficultyName, level);
    }

    /// <summary>
    /// 沒有難度名可用時的退路：green below 7, yellow to 10, red to 13,
    /// violet above that.
    /// </summary>
    public static Color GetDifficultyColour(int level)
    {
        if (level <= 0) return AgedPaper;
        return DifficultyVisualPalette.ForLevel(level);
    }

    /// <summary>
    /// The rim of light around a difficulty label.  Same hue, carrying less of
    /// the light, so the text stays the brightest thing on the bookmark.
    /// </summary>
    public static Color GetDifficultyGlow(int level)
    {
        return Dim(GetDifficultyColour(level));
    }

    public static Color GetDifficultyGlow(string difficultyName, int level)
    {
        return Dim(GetDifficultyColour(difficultyName, level));
    }

    private static Color Dim(Color tint)
    {
        return new Color(tint.r * 0.70f, tint.g * 0.70f, tint.b * 0.70f, 1f);
    }

    /// <summary>The bookmark body: dark enough to read on, tinted by its difficulty.</summary>
    public static Color GetDifficultyFace(int level)
    {
        return Face(GetDifficultyColour(level));
    }

    public static Color GetDifficultyFace(string difficultyName, int level)
    {
        return Face(GetDifficultyColour(difficultyName, level));
    }

    private static Color Face(Color tint)
    {
        return Color.Lerp(new Color(0.15f, 0.075f, 0.055f, 1f), tint, 0.22f);
    }

    public static void ConfigureFocusCarousel(RectTransform center, RectTransform left, RectTransform right,
        bool songSelection)
    {
        float sideX = songSelection ? 800f : 610f;
        float centerY = songSelection ? 28f : 20f;
        float sideY = songSelection ? 42f : 0f;
        if (center != null)
        {
            center.anchoredPosition = new Vector2(0f, centerY);
            // A score volume is taller than it is wide; the first draft was
            // landscape, which read as a photo album rather than sheet music.
            if (songSelection) center.sizeDelta = new Vector2(620f, 700f);
            center.localRotation = Quaternion.identity;
            center.SetAsLastSibling();
        }
        if (left != null)
        {
            left.anchoredPosition = new Vector2(-sideX, sideY);
            left.localRotation = Quaternion.identity;
        }
        if (right != null)
        {
            right.anchoredPosition = new Vector2(sideX, sideY);
            right.localRotation = Quaternion.identity;
        }
    }

    public static void StyleCard(RectTransform root, Image cover, TextMeshProUGUI title,
        TextMeshProUGUI difficulty, TextMeshProUGUI author, bool primary)
    {
        if (root == null) return;
        Image paper = root.GetComponent<Image>();
        if (paper == null) paper = root.gameObject.AddComponent<Image>();
        paper.color = primary
            ? new Color(0.17f, 0.075f, 0.045f, 1f)
            : new Color(0.12f, 0.065f, 0.045f, 0.96f);

        AddOutline(root.gameObject, primary ? Gold : new Color(0.42f, 0.29f, 0.14f, 1f),
            primary ? new Vector2(4f, -4f) : new Vector2(2f, -2f));
        AddShadow(root.gameObject, new Color(0f, 0f, 0f, primary ? 0.58f : 0.38f),
            primary ? new Vector2(13f, -13f) : new Vector2(8f, -8f));
        EnsureCardScoreBackground(root, primary);
        EnsureScoreBookStructure(root, primary);
        EnsureInnerBorder(root);
        EnsureCardRails(root, primary);

        if (cover != null)
        {
            cover.preserveAspect = true;
            RectTransform coverRect = cover.rectTransform;
            // Centred on the page. The bookmarks used to sit over the right of
            // the cover, so it was pushed left; they now hang off the edge of the
            // book the way a real bookmark does.
            coverRect.anchorMin = new Vector2(0.11f, 0.32f);
            coverRect.anchorMax = new Vector2(0.89f, 0.88f);
            coverRect.offsetMin = Vector2.zero;
            coverRect.offsetMax = Vector2.zero;
            coverRect.SetSiblingIndex(Mathf.Min(2, root.childCount - 1));
            AddOutline(cover.gameObject, primary ? Gold : MutedInk, new Vector2(3f, -3f));
        }
        StyleText(title, ParchmentLight, primary ? 31f : 25f, FontStyles.Bold);
        StyleText(difficulty, primary ? Burgundy : MutedInk, primary ? 24f : 19f, FontStyles.Bold);
        StyleText(author, primary ? new Color(0.91f, 0.77f, 0.46f, 1f) : AgedPaper,
            primary ? 20f : 16f, FontStyles.Italic);

        if (title != null)
        {
            SetRect(title.rectTransform, new Vector2(0.5f, 0f),
                primary ? new Vector2(520f, 58f) : new Vector2(520f, 58f),
                primary ? new Vector2(0f, 135f) : new Vector2(0f, 74f));
            title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.SetAsLastSibling();
        }
        if (author != null)
        {
            SetRect(author.rectTransform, new Vector2(0.5f, 0f),
                primary ? new Vector2(500f, 38f) : new Vector2(500f, 36f),
                primary ? new Vector2(0f, 88f) : new Vector2(0f, 30f));
            author.alignment = TextAlignmentOptions.Center;
            author.rectTransform.SetAsLastSibling();
        }
        if (difficulty != null)
        {
            if (primary)
            {
                difficulty.gameObject.SetActive(false);
            }
            else
            {
                SetRect(difficulty.rectTransform, new Vector2(1f, 1f), new Vector2(205f, 38f), new Vector2(-18f, -16f));
                difficulty.alignment = TextAlignmentOptions.Right;
                difficulty.rectTransform.SetAsLastSibling();
            }
        }
    }

    public static void StyleSettingsBox(GameObject box, bool primary)
    {
        if (box == null) return;
        Image image = box.GetComponent<Image>();
        if (image == null) image = box.AddComponent<Image>();
        image.color = primary ? ParchmentLight : AgedPaper;
        AddOutline(box, primary ? Gold : MutedInk, primary ? new Vector2(4f, -4f) : new Vector2(2f, -2f));
        AddShadow(box, new Color(0f, 0f, 0f, primary ? 0.55f : 0.34f), new Vector2(10f, -10f));
        if (box.transform is RectTransform rt) EnsureInnerBorder(rt);
    }

    public static void StyleButton(Button button, bool accent = false)
    {
        if (button == null) return;
        ApplyPlate(button, accent);

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = accent ? new Color(1.15f, 0.88f, 0.70f, 1f) : new Color(1.22f, 1.08f, 0.82f, 1f);
        colors.pressedColor = new Color(0.68f, 0.58f, 0.43f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
            StyleText(labels[i], ParchmentLight, labels[i].fontSize > 0f ? labels[i].fontSize : 20f, FontStyles.Bold);
    }

    public static void StyleSettingsIconButton(Button button)
    {
        if (button == null) return;
        RectTransform rect = button.transform as RectTransform;
        if (rect != null)
        {
            Transform panel = rect.parent;
            // Older styling placed the gear inside ClassicalBookBackdrop. Move
            // it back to SelectionPanel so it stays above the new room image.
            if (panel != null && panel.name == "ClassicalBookBackdrop" && panel.parent != null)
            {
                panel = panel.parent;
                rect.SetParent(panel, false);
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(188f, 56f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }
        ApplyPlate(button, false);
        Image image = button.GetComponent<Image>();
        image.type = Image.Type.Tiled;
        image.raycastTarget = true;
        button.targetGraphic = image;
        button.interactable = true;
        button.transition = Selectable.Transition.None;
        AddOutline(button.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.82f), new Vector2(2f, -2f));
        AddShadow(button.gameObject, new Color(0f, 0f, 0f, 0.62f), new Vector2(7f, -7f));

        TextMeshProUGUI[] oldLabels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < oldLabels.Length; i++) oldLabels[i].gameObject.SetActive(false);

        Transform iconTransform = button.transform.Find("TuningSliders");
        TuningSlidersGraphic sliders;
        if (iconTransform == null)
        {
            GameObject iconObject = new GameObject("TuningSliders", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TuningSlidersGraphic));
            iconObject.layer = button.gameObject.layer;
            iconObject.transform.SetParent(button.transform, false);
            iconTransform = iconObject.transform;
        }
        sliders = iconTransform.GetComponent<TuningSlidersGraphic>();
        RectTransform iconRect = iconTransform as RectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(18f, 0f);
        iconRect.sizeDelta = new Vector2(38f, 30f);
        sliders.raycastTarget = false;

        Transform labelTransform = button.transform.Find("SettingsLabel");
        TextMeshProUGUI label;
        if (labelTransform == null)
        {
            GameObject labelObject = new GameObject("SettingsLabel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = button.gameObject.layer;
            labelObject.transform.SetParent(button.transform, false);
            labelTransform = labelObject.transform;
        }
        label = labelTransform.GetComponent<TextMeshProUGUI>();
        label.gameObject.SetActive(true);
        label.text = Localize.T("設定", "设置", "Settings");
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 21f;
        label.fontStyle = FontStyles.Bold;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(59f, 3f);
        labelRect.offsetMax = new Vector2(-12f, -3f);
        ApplyLocalizedFont(label);

        ClassicalSettingsBookmark hover = button.GetComponent<ClassicalSettingsBookmark>();
        if (hover == null) hover = button.gameObject.AddComponent<ClassicalSettingsBookmark>();
        hover.Configure(image, button.GetComponent<Outline>(), label, sliders);
    }

    public static void StyleVerticalNavigationButton(Button button, bool pointsUp, Transform card)
    {
        if (button == null) return;
        RectTransform rect = button.transform as RectTransform;
        if (rect == null) return;
        if (card != null) rect.SetParent(card, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(246f, pointsUp ? 78f : -78f);
        rect.sizeDelta = new Vector2(62f, 62f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.SetAsLastSibling();

        Image plate = button.GetComponent<Image>();
        if (plate == null) plate = button.gameObject.AddComponent<Image>();
        plate.sprite = GetPaperTextureSprite();
        plate.type = Image.Type.Tiled;
        plate.color = new Color(0.20f, 0.095f, 0.045f, 0.98f);
        plate.raycastTarget = true;
        button.targetGraphic = plate;
        AddOutline(button.gameObject, Gold, new Vector2(2f, -2f));
        AddShadow(button.gameObject, new Color(0f, 0f, 0f, 0.55f), new Vector2(6f, -6f));

        Transform oldFace = rect.Find("ClassicalArrowFace");
        if (oldFace != null) Object.Destroy(oldFace.gameObject);
        GameObject faceObject = new GameObject("ClassicalArrowFace", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(ClassicalArrowGraphic));
        faceObject.layer = button.gameObject.layer;
        RectTransform faceRect = faceObject.GetComponent<RectTransform>();
        faceRect.SetParent(rect, false);
        faceRect.anchorMin = new Vector2(0.18f, 0.18f);
        faceRect.anchorMax = new Vector2(0.82f, 0.82f);
        faceRect.offsetMin = Vector2.zero;
        faceRect.offsetMax = Vector2.zero;
        ClassicalArrowGraphic arrow = faceObject.GetComponent<ClassicalArrowGraphic>();
        arrow.color = new Color(0.94f, 0.76f, 0.34f, 1f);
        arrow.PointsUp = pointsUp;
        arrow.raycastTarget = false;

        TextMeshProUGUI[] labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++) labels[i].gameObject.SetActive(false);
    }

    public static void StyleText(TextMeshProUGUI text, Color color, float size, FontStyles style)
    {
        if (text == null) return;
        ApplyLocalizedFont(text);
        text.color = color;
        text.fontSize = size;
        text.fontStyle = style;
        text.raycastTarget = false;
    }

    public static void ApplyLocalizedFont(TextMeshProUGUI text)
    {
        if (text == null) return;
        TMP_FontAsset resolvedFont = ResolveElegantFont(text.font);
        bool containsCjk = ContainsCjk(text.text);
        AppLanguage language = SettingsManager.Instance != null
            ? SettingsManager.Instance.CurrentLanguage
            : AppLanguage.TraditionalChinese;
        if (containsCjk && language == AppLanguage.SimplifiedChinese)
            resolvedFont = simplifiedCjkFont != null ? simplifiedCjkFont : traditionalCjkFont;
        else if (containsCjk && language == AppLanguage.TraditionalChinese)
            resolvedFont = traditionalCjkFont != null ? traditionalCjkFont : simplifiedCjkFont;
        if (resolvedFont != null) text.font = resolvedFont;
    }

    /// <summary>
    /// Applies a font to user/library-authored content without converting its
    /// appearance to the selected UI language. Song titles, artists and category
    /// names can legitimately remain Japanese or Traditional Chinese in every UI.
    /// </summary>
    public static void ApplyContentFont(TextMeshProUGUI text)
    {
        if (text == null) return;
        TMP_FontAsset resolvedFont = ResolveElegantFont(text.font);
        if (ContainsJapanese(text.text))
            resolvedFont = japaneseFont != null ? japaneseFont : resolvedFont;
        else if (ContainsCjk(text.text))
            resolvedFont = traditionalCjkFont != null ? traditionalCjkFont
                : simplifiedCjkFont != null ? simplifiedCjkFont : resolvedFont;
        if (resolvedFont != null) text.font = resolvedFont;
    }

    private static bool ContainsJapanese(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < value.Length; i++)
        {
            int code = value[i];
            if ((code >= 0x3040 && code <= 0x30FF) ||
                (code >= 0x31F0 && code <= 0x31FF))
                return true;
        }
        return false;
    }

    private static bool ContainsCjk(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < value.Length; i++)
        {
            int code = value[i];
            if ((code >= 0x3400 && code <= 0x9FFF) ||
                (code >= 0xF900 && code <= 0xFAFF) ||
                (code >= 0x3040 && code <= 0x30FF) ||
                (code >= 0x31F0 && code <= 0x31FF))
                return true;
        }
        return false;
    }

    private static TMP_FontAsset ResolveElegantFont(TMP_FontAsset fallback)
    {
        try
        {
            if (elegantFont == null)
            {
                Font source = Resources.Load<Font>("Fonts & Materials/ZenAntique-Regular");
                if (source == null) return fallback;
                elegantFont = CreateDynamicFont(source, "Zen Antique UI (Runtime)");
                if (elegantFont == null) return fallback;
            }

            if (traditionalCjkFont == null)
            {
                Font source = Resources.Load<Font>("Fonts & Materials/SourceHanSansTC-Bold");
                traditionalCjkFont = CreateDynamicFont(source, "Source Han Sans TC (Runtime)");
            }
            if (simplifiedCjkFont == null)
            {
                Font source = Resources.Load<Font>("Fonts & Materials/SourceHanSans-ExtraLight");
                simplifiedCjkFont = CreateDynamicFont(source, "Source Han Sans SC (Runtime)");
            }
            if (japaneseFont == null)
            {
                Font source = Resources.Load<Font>("Fonts & Materials/NotoSansJP-Medium");
                japaneseFont = CreateDynamicFont(source, "Noto Sans JP (Runtime)");
            }

            bool simplified = SettingsManager.Instance != null &&
                SettingsManager.Instance.CurrentLanguage == AppLanguage.SimplifiedChinese;
            SetFallbackOrder(elegantFont, simplified, fallback);
            SetCjkFallbackOrder(traditionalCjkFont, simplifiedCjkFont, japaneseFont, elegantFont);
            SetCjkFallbackOrder(simplifiedCjkFont, traditionalCjkFont, japaneseFont, elegantFont);
            SetCjkFallbackOrder(japaneseFont, traditionalCjkFont, simplifiedCjkFont, elegantFont);
            return elegantFont;
        }
        catch
        {
            elegantFont = null;
            return fallback;
        }
    }

    /// <summary>
    /// The score's two colours, wherever the score appears: a soft green face
    /// inside a gold rim.
    /// </summary>
    /// <remarks>
    /// Kept here, not in each screen, because the results page and the gameplay
    /// HUD have to agree and have drifted apart once already. Anything that reads
    /// as the score takes both from here.
    ///
    /// White letters over a black drop shadow. The shadow, not a coloured rim,
    /// is what separates them from the ground: a rim has to differ from both the
    /// letter and the background, which is two constraints on one colour, and it
    /// is why the green and the gold each failed on one of the two screens. A
    /// shadow only has to be darker than the background, and both grounds here
    /// are lighter than black.
    /// </remarks>
    public static readonly Color ScoreFace = new Color(1f, 1f, 1f, 1f);
    public static readonly Color ScoreShadow = new Color(0f, 0f, 0f, 0.85f);

    private static Font scoreSourceFont;

    /// <summary>
    /// The one typeface the score prints in, wherever the score appears.
    /// </summary>
    /// <remarks>
    /// **Why this exists rather than each screen loading its own.** The results
    /// page and the gameplay HUD used to resolve the score's face independently:
    /// the page through <c>Resources.Load&lt;Font&gt;</c> with a built-in fallback,
    /// the HUD through <see cref="ApplyLocalizedFont"/>, which picks by what the
    /// string contains and can hand back a CJK face or whatever the scene had.
    /// Two paths that agree only when both succeed do not agree -- and when one
    /// silently takes its fallback, the same number is set in two different
    /// typefaces on two screens, with nothing in the code saying so.
    ///
    /// Both callers now take the face from here, **including the fallback**, so
    /// they match whether or not the asset loads.
    /// </remarks>
    public static Font GetScoreSourceFont()
    {
        if (scoreSourceFont != null) return scoreSourceFont;
        scoreSourceFont = Resources.Load<Font>("Fonts & Materials/ZenAntique-Regular");
        if (scoreSourceFont == null)
        {
            Debug.LogWarning("[ClassicalBookUITheme] ZenAntique-Regular did not load; " +
                "the score falls back to the built-in face on every screen.");
            scoreSourceFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return scoreSourceFont;
    }

    /// <summary>The score's face for TMP: the book UI's own, never a new one.</summary>
    /// <remarks>
    /// **Why it does not build its own asset from <see cref="GetScoreSourceFont"/>.**
    /// That method has a built-in font as its fallback, and a runtime TMP asset
    /// built from a built-in font has no glyph data to populate its atlas from --
    /// every character comes out as a missing-glyph box, and the rich-text tags
    /// in the HUD string get drawn as literal text because nothing can be laid
    /// out. Returning the asset the rest of the book UI already renders with is
    /// both the same face and one that is known to build.
    ///
    /// Null means the typeface could not be resolved at all; callers must leave
    /// the text's existing font alone rather than assign null.
    /// </remarks>
    public static TMP_FontAsset GetScoreFontAsset()
    {
        return ResolveElegantFont(null);
    }

    private static TMP_FontAsset CreateDynamicFont(Font source, string assetName)
    {
        if (source == null) return null;
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source);
        if (asset == null) return null;
        asset.name = assetName;
        asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        asset.isMultiAtlasTexturesEnabled = true;
        return asset;
    }

    private static void SetFallbackOrder(TMP_FontAsset primary, bool simplified,
        TMP_FontAsset originalFallback)
    {
        if (primary == null || primary.fallbackFontAssetTable == null) return;
        var table = primary.fallbackFontAssetTable;
        table.Remove(traditionalCjkFont);
        table.Remove(simplifiedCjkFont);
        table.Remove(japaneseFont);
        if (simplified)
        {
            if (simplifiedCjkFont != null) table.Add(simplifiedCjkFont);
            if (traditionalCjkFont != null) table.Add(traditionalCjkFont);
            if (japaneseFont != null) table.Add(japaneseFont);
        }
        else
        {
            if (traditionalCjkFont != null) table.Add(traditionalCjkFont);
            if (simplifiedCjkFont != null) table.Add(simplifiedCjkFont);
            if (japaneseFont != null) table.Add(japaneseFont);
        }
        if (originalFallback != null && originalFallback != primary &&
            !table.Contains(originalFallback))
            table.Add(originalFallback);
    }

    private static void SetCjkFallbackOrder(TMP_FontAsset primary, params TMP_FontAsset[] fallbacks)
    {
        if (primary == null || primary.fallbackFontAssetTable == null) return;
        var table = primary.fallbackFontAssetTable;
        for (int i = 0; i < fallbacks.Length; i++)
        {
            TMP_FontAsset fallback = fallbacks[i];
            if (fallback != null && fallback != primary && !table.Contains(fallback))
                table.Add(fallback);
        }
    }

    public static void StyleDifficultyModal(GameObject overlay, GameObject container, TextMeshProUGUI title)
    {
        if (overlay != null && overlay.TryGetComponent(out Image veil))
            veil.color = new Color(0.025f, 0.012f, 0.008f, 0.78f);
        if (container != null)
        {
            Image page = container.GetComponent<Image>();
            if (page != null) page.color = ParchmentLight;
            AddOutline(container, Gold, new Vector2(4f, -4f));
            AddShadow(container, new Color(0f, 0f, 0f, 0.7f), new Vector2(14f, -14f));
        }
        StyleText(title, Ink, 27f, FontStyles.Bold);
    }

    private static void AddStaffLines(RectTransform page)
    {
        for (int group = 0; group < 2; group++)
        {
            float groupY = group == 0 ? 220f : -260f;
            for (int line = 0; line < 5; line++)
            {
                RectTransform staff = AddCentered(page, $"Staff_{group}_{line}", new Vector2(810f, 2f),
                    new Vector2(0f, groupY - line * 17f), new Color(Ink.r, Ink.g, Ink.b, 0.085f));
                staff.SetAsFirstSibling();
            }
        }
    }

    private static void AddArcadeScoreRails(RectTransform backdrop, bool includeTop)
    {
        if (includeTop)
        {
            AddCentered(backdrop, "TopGoldRail", new Vector2(2080f, 9f), new Vector2(0f, 548f), Gold);
            AddCentered(backdrop, "TopWoodRail", new Vector2(1940f, 28f), new Vector2(0f, 526f), Wood);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    RectTransform slash = AddCentered(backdrop, $"RailSlash_{side}_{i}",
                        new Vector2(78f, 8f), new Vector2(side * (850f + i * 72f), 548f), Gold);
                    slash.localRotation = Quaternion.Euler(0f, 0f, side * 48f);
                }
            }
        }
        AddCentered(backdrop, "BottomWoodRail", new Vector2(1940f, 34f), new Vector2(0f, -524f), Wood);
        AddCentered(backdrop, "BottomGoldRail", new Vector2(2080f, 9f), new Vector2(0f, -548f), Gold);
    }

    private static void EnsureCardRails(RectTransform root, bool primary)
    {
        if (root.Find("ArcadeCardTopRail") != null) return;
        RectTransform top = AddRect(root, "ArcadeCardTopRail", primary ? Gold : MutedInk,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -9f), Vector2.zero);
        top.sizeDelta = new Vector2(0f, 9f);
        top.anchoredPosition = new Vector2(0f, -4.5f);
        RectTransform bottom = AddRect(root, "ArcadeCardBottomRail", primary ? Burgundy : Wood,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), Vector2.zero);
        bottom.sizeDelta = new Vector2(0f, primary ? 18f : 12f);
        bottom.anchoredPosition = new Vector2(0f, primary ? 9f : 6f);
        top.SetAsLastSibling();
        bottom.SetAsLastSibling();
    }

    private static void EnsureScoreBookStructure(RectTransform root, bool primary)
    {
        if (root.Find("ClosedBookSpine") != null) return;

        // A single spine on the left and page block on the other edges make this
        // read as a closed score volume instead of two open pages.
        RectTransform spine = AddRect(root, "ClosedBookSpine",
            new Color(0.07f, 0.025f, 0.018f, 0.96f),
            new Vector2(0f, 0f), new Vector2(0.035f, 1f), Vector2.zero, Vector2.zero);
        RectTransform spineGold = AddRect(root, "ClosedBookSpineGold",
            new Color(Gold.r, Gold.g, Gold.b, primary ? 0.72f : 0.45f),
            new Vector2(0.04f, 0.035f), new Vector2(0.04f, 0.965f), Vector2.zero, Vector2.zero);
        spineGold.sizeDelta = new Vector2(primary ? 3f : 2f, 0f);

        RectTransform rightPages = AddRect(root, "ClosedBookRightPages",
            new Color(ParchmentLight.r, ParchmentLight.g, ParchmentLight.b, primary ? 0.82f : 0.55f),
            new Vector2(1f, 0.045f), new Vector2(1f, 0.955f), new Vector2(-7f, 0f), Vector2.zero);
        rightPages.sizeDelta = new Vector2(primary ? 10f : 7f, 0f);

        for (int i = 0; i < 3; i++)
        {
            float inset = 7f + i * 3f;
            RectTransform pageEdge = AddRect(root, $"ClosedBookPageEdge_{i}",
                new Color(Parchment.r, Parchment.g, Parchment.b,
                    primary ? 0.58f - i * 0.10f : 0.36f - i * 0.06f),
                new Vector2(0.03f, 0f), new Vector2(0.97f, 0f),
                new Vector2(0f, inset), Vector2.zero);
            pageEdge.sizeDelta = new Vector2(0f, 2f);
        }

        spine.SetAsLastSibling();
        spineGold.SetAsLastSibling();
        rightPages.SetAsLastSibling();
    }

    private static void EnsureCardScoreBackground(RectTransform root, bool primary)
    {
        if (root.Find("CardScoreBackground") != null) return;

        RectTransform score = AddRect(root, "CardScoreBackground",
            primary ? new Color(0.22f, 0.095f, 0.052f, 1f) : new Color(0.14f, 0.07f, 0.042f, 1f),
            Vector2.zero, Vector2.one, new Vector2(7f, 7f), new Vector2(-7f, -7f));
        score.SetAsFirstSibling();
        ApplyTexture(score, ResolveTextureSprite(true));
    }

    private static void ApplyTexture(RectTransform target, Sprite sprite)
    {
        if (target == null || sprite == null) return;
        Image image = target.GetComponent<Image>();
        if (image == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Tiled;
    }

    /// <summary>
    /// The tiling surface under every panel: dark grain for the boards, laid and
    /// foxed rag paper for the pages.
    /// </summary>
    /// <remarks>
    /// **Why it is seamless now.** It is drawn tiled, and plain Perlin noise does
    /// not meet itself at the tile edge -- so every 192 pixels there was a hard
    /// seam running the height of the page, which is the one artefact that says
    /// "texture" out loud. <see cref="Tileable"/> blends the four wrapped copies
    /// of the same field, which costs four samples and removes the grid.
    ///
    /// **Why the paper is no longer flat.** The old fibre held to a range of
    /// about 0.88-1.00 -- under a page-sized tint that is a plain cream
    /// rectangle. Real rag paper is unevenly beaten: clouds where the pulp
    /// gathered, laid lines from the mould, and rust-coloured specks. The range
    /// runs to 0.72 now, and the specks carry their own hue, so the paper has
    /// something to look at up close as well as at arm's length.
    ///
    /// Anything larger than a speck belongs in <see cref="AgedPaperGraphic"/>
    /// instead: at this size it would repeat three or four times across one
    /// page, and a repeated stain reads as wallpaper.
    /// </remarks>
    private static Sprite ResolveTextureSprite(bool wood)
    {
        if (wood && woodTextureSprite != null) return woodTextureSprite;
        if (!wood && paperTextureSprite != null) return paperTextureSprite;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = wood ? "Classical Dark Wood Grain" : "Classical Score Paper Fiber",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float broad = Tileable(x, y, 0.022f, 4.1f, size);
                float fine = Tileable(x, y, 0.115f, 8.3f, size);
                if (wood)
                {
                    float grain = Mathf.Sin((x + broad * 24f) * 0.13f) * 0.5f + 0.5f;
                    float value = Mathf.Clamp01(0.56f + broad * 0.23f + fine * 0.07f + grain * 0.09f);
                    pixels[y * size + x] = new Color(value, value * 0.86f, value * 0.72f, 1f);
                }
                else
                {
                    // 雲：紙漿聚散不勻，這是最大片的那一層。
                    float cloud = Tileable(x, y, 0.0085f, 21.7f, size);
                    // 簾紋：抄紙的簾子壓出來的細橫線，很淡，但少了它紙沒有方向。
                    float laid = Mathf.Sin(y * 0.62f + broad * 2.2f) * 0.5f + 0.5f;
                    // 纖維：斜著走的短纖，靠兩個不同頻率的雜訊相乘挑出來。
                    float fibre = Mathf.Clamp01(fine * Tileable(x + y, y - x, 0.075f, 33.4f, size) * 2.1f);

                    float value = 0.965f - cloud * 0.16f - laid * 0.028f - fibre * 0.075f
                                  + broad * 0.045f;
                    value = Mathf.Clamp01(value);
                    Color tone = new Color(value, value * 0.982f, value * 0.928f, 1f);

                    // 鏽點：稀疏、獨立、偏紅。它們是紙上唯一有顏色的東西。
                    float spot = Tileable(x, y, 0.19f, 57.2f, size);
                    if (spot > 0.80f)
                    {
                        float bite = (spot - 0.80f) / 0.20f;
                        tone = Color.Lerp(tone, new Color(0.62f, 0.44f, 0.26f, 1f), bite * 0.55f);
                    }
                    pixels[y * size + x] = tone;
                }
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name + " Sprite";
        if (wood) woodTextureSprite = sprite;
        else paperTextureSprite = sprite;
        return sprite;
    }

    /// <summary>
    /// Shared procedural paper fibre used by runtime-built classical UI panels.
    /// </summary>
    public static Sprite GetPaperTextureSprite()
    {
        return ResolveTextureSprite(false);
    }

    /// <summary>
    /// Woven silk: a warp and a weft crossing, with the sheen that only comes
    /// from thread lying in two directions.
    /// </summary>
    /// <remarks>
    /// **Why a weave and not noise.** Cloth is the one material whose texture is
    /// *regular*. Fibre noise says paper, grain says wood, and a crossing of two
    /// thread directions -- one catching light, one in shadow -- is the only
    /// thing that says fabric. At the size a ribbon is drawn you never resolve a
    /// single thread, but you do see the two directions beating against each
    /// other, and that is what the eye is actually reading.
    ///
    /// **Why the threads are not the same brightness.** A thread is a cylinder:
    /// bright along its crown, dark in the ditch beside it. The pair of sine
    /// waves gives each direction that rounded profile, and where a warp thread
    /// passes over a weft one it takes the light -- that alternation is the
    /// weave pattern proper, and it is what stops it reading as a grid.
    /// </remarks>
    public static Sprite GetClothTextureSprite()
    {
        if (clothTextureSprite != null) return clothTextureSprite;

        const int size = 128;
        const float threads = 26f;      // 一格裡幾根線
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "Classical Ribbon Weave",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * threads * Mathf.PI * 2f;
                float v = y / (float)size * threads * Mathf.PI * 2f;
                // 每一根線都是圓的：頂上亮、旁邊的溝暗。
                float warp = Mathf.Cos(u) * 0.5f + 0.5f;
                float weft = Mathf.Cos(v) * 0.5f + 0.5f;
                // 平紋：一上一下交替，壓在上面的那一根拿到光。
                int cell = (Mathf.FloorToInt(x / (float)size * threads)
                            + Mathf.FloorToInt(y / (float)size * threads)) & 1;
                float over = cell == 0 ? warp : weft;
                float under = cell == 0 ? weft : warp;

                float value = 0.80f + over * 0.30f - (1f - under) * 0.10f;
                // 織不勻：真的布每隔幾根就有一根粗一點。
                value -= Tileable(x, y, 0.030f, 17.3f, size) * 0.09f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value * 0.995f, value * 0.985f, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        clothTextureSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        clothTextureSprite.name = texture.name + " Sprite";
        return clothTextureSprite;
    }

    /// <summary>The dark grain used for boards and shelves.</summary>
    public static Sprite GetBoardTextureSprite()
    {
        return ResolveTextureSprite(true);
    }

    /// <summary>
    /// The card a study edition is wrapped in: coarse, matte, and crazed all
    /// over with the fine cracks that coated paper gets with age.
    /// </summary>
    /// <remarks>
    /// **Why crackle and not more fibre.** Fibre is what you see in a sheet held
    /// to the light; what you see on a cover that has been opened a thousand
    /// times is a net of hairline cracks in the coating. It is the single most
    /// recognisable thing about an old score wrapper, and it is what stops a
    /// flat coloured rectangle reading as a flat coloured rectangle.
    ///
    /// **How the net is drawn.** The cracks are the contour lines of a noise
    /// field: wherever the field passes one of a few chosen levels, darken. The
    /// contours of smooth noise close on themselves and meet at odd angles,
    /// which is exactly the topology of crazing -- and it costs one noise sample
    /// rather than a Voronoi diagram.
    /// </remarks>
    public static Sprite GetCoverTextureSprite()
    {
        if (coverTextureSprite != null) return coverTextureSprite;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "Classical Cover Card",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 粗紋要細到一兩個像素才看得出是紙的顆粒；再加一層中頻的
                // 斑，那是染料吃進紙裡不勻的地方。原本只有一層很淡的雲，遠看
                // 就是一塊平的色塊。
                float tooth = Tileable(x, y, 0.44f, 12.7f, size);
                float grain = Tileable(x, y, 0.155f, 3.3f, size);
                float mottle = Tileable(x, y, 0.046f, 61.1f, size);
                float cloud = Tileable(x, y, 0.011f, 47.3f, size);

                // 龜裂：把雜訊場的等高線挑出來。三個高度，線就會分岔、交會。
                // 頻率要高：龜裂是幾個像素一格的細網，不是幾十個像素一格的
                // 大理石紋。低頻的等高線讀起來是地圖，不是老掉的漆面。
                float field = Tileable(x, y, 0.155f, 71.9f, size) * 3.4f
                              + Tileable(x, y, 0.36f, 5.5f, size) * 0.7f;
                float crack = 0f;
                for (int level = 0; level < 4; level++)
                {
                    float d = Mathf.Abs(field - (0.55f + level * 0.62f));
                    if (d < 0.022f) crack = Mathf.Max(crack, 1f - d / 0.022f);
                }

                float value = 0.995f - cloud * 0.085f - mottle * 0.105f - grain * 0.065f
                              - tooth * 0.075f - crack * 0.17f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value * 0.992f, value * 0.975f, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        coverTextureSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        coverTextureSprite.name = texture.name + " Sprite";
        return coverTextureSprite;
    }

    /// <summary>
    /// Perlin noise that meets itself across the tile, by cross-fading the four
    /// wrapped copies of the same field.
    /// </summary>
    private static float Tileable(float x, float y, float frequency, float offset, int size)
    {
        float u = Mathf.Repeat(x, size) / size;
        float v = Mathf.Repeat(y, size) / size;
        float a = Mathf.PerlinNoise(x * frequency + offset, y * frequency + offset);
        float b = Mathf.PerlinNoise((x - size) * frequency + offset, y * frequency + offset);
        float c = Mathf.PerlinNoise(x * frequency + offset, (y - size) * frequency + offset);
        float d = Mathf.PerlinNoise((x - size) * frequency + offset, (y - size) * frequency + offset);
        return a * (1f - u) * (1f - v) + b * u * (1f - v) + c * (1f - u) * v + d * u * v;
    }

    private static void AddPageCorners(RectTransform page)
    {
        Vector2[] anchors = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
        for (int i = 0; i < anchors.Length; i++)
        {
            RectTransform h = AddRect(page, $"CornerH_{i}", Gold, anchors[i], anchors[i],
                new Vector2(anchors[i].x < 0.5f ? 24f : -24f, anchors[i].y < 0.5f ? 16f : -16f), new Vector2(82f, 4f));
            RectTransform v = AddRect(page, $"CornerV_{i}", Gold, anchors[i], anchors[i],
                new Vector2(anchors[i].x < 0.5f ? 16f : -16f, anchors[i].y < 0.5f ? 24f : -24f), new Vector2(4f, 82f));
            h.pivot = anchors[i];
            v.pivot = anchors[i];
        }
    }

    private static void EnsureInnerBorder(RectTransform root)
    {
        if (root.Find("BookInnerBorder") != null) return;
        RectTransform border = AddRect(root, "BookInnerBorder", new Color(1f, 1f, 1f, 0.001f),
            Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
        border.SetAsLastSibling();
        AddOutline(border.gameObject, new Color(Gold.r, Gold.g, Gold.b, 0.72f), new Vector2(2f, -2f));
    }

    private static RectTransform AddCentered(Transform parent, string name, Vector2 size, Vector2 position, Color color)
    {
        return AddRect(parent, name, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
    }

    private static RectTransform AddRect(Transform parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 positionOrMin, Vector2 sizeOrMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        if (anchorMin == anchorMax)
        {
            rt.sizeDelta = sizeOrMax;
            rt.anchoredPosition = positionOrMin;
        }
        else
        {
            rt.offsetMin = positionOrMin;
            rt.offsetMax = sizeOrMax;
        }
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    private static TextMeshProUGUI AddText(Transform parent, string name, string value, float size, Color color, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        StyleText(text, color, size, style);
        return text;
    }

    private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
    }

    /// <summary>
    /// Dresses a button as a cast plate: a graded field inside a gilt border.
    /// </summary>
    /// <remarks>
    /// The buttons used to be a flat fill with an <see cref="Outline"/> and a
    /// <see cref="Shadow"/> on top. Neither of those is a border -- both are the
    /// whole mesh drawn again at an offset -- so the edge came out as a doubled
    /// smear rather than a rule, and the face had no light on it at all.
    ///
    /// The plate is one nine-sliced sprite instead: the border lives in the
    /// corners and edges, which nine-slicing never stretches, so a wide category
    /// button and a small arrow get the same crisp rule. Both effect components
    /// are turned off rather than removed, so anything that still looks them up
    /// finds them.
    /// </remarks>
    private static void ApplyPlate(Button button, bool accent)
    {
        Image image = button.GetComponent<Image>();
        if (image == null) image = button.gameObject.AddComponent<Image>();
        StylePlate(image, accent);

        Outline outline = button.GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
        foreach (Shadow shadow in button.GetComponents<Shadow>())
            if (!(shadow is Outline)) shadow.enabled = false;
    }

    /// <summary>
    /// Puts the plate face on any image, for the panels that build their own
    /// buttons instead of going through <see cref="StyleButton"/>.
    /// </summary>
    /// <remarks>
    /// Left white so the caller can tint it: the category strip wants the same
    /// plate a shade quieter than the mode button beside it, and one sprite
    /// tinted twice keeps them obviously the same object.
    /// </remarks>
    public static void StylePlate(Image image, bool accent)
    {
        if (image == null) return;
        image.sprite = GetPlateSprite(accent);
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = Color.white;
        image.preserveAspect = false;
    }

    private static Sprite plateSprite;
    private static Sprite accentPlateSprite;

    /// <summary>
    /// The button face, generated once: a mitred brass plaque with a directional
    /// gilt moulding, a recessed field and a stud at each cut corner.
    /// </summary>
    /// <remarks>
    /// **Why the gilding is not one colour.** The first version drew the same
    /// gold all the way round, and a rule of even brightness reads as a line
    /// somebody drew rather than as metal catching light. Here each pixel of the
    /// border knows which way it faces -- the outward normal of the plate's
    /// silhouette -- and is lit against a fixed source at the upper left. The top
    /// edge comes out bright, the bottom dark, and the corners grade between
    /// them. That one change does more than any amount of extra ornament.
    ///
    /// **Why the field is recessed.** A moulding needs something to be a moulding
    /// around. Between the gilt and the face there is a dark groove, and the face
    /// starts a shade darker than the rule -- so the label sits down inside the
    /// plate instead of floating on a coloured rectangle.
    ///
    /// **Why the corners are cut.** A right angle is the one shape nothing else
    /// on this screen has. The cut is drawn into the corner cells of the nine
    /// slice, which are never stretched, so it stays the same size on a wide
    /// category button and on a small arrow.
    /// </remarks>
    private static Sprite GetPlateSprite(bool accent)
    {
        if (accent && accentPlateSprite != null) return accentPlateSprite;
        if (!accent && plateSprite != null) return plateSprite;

        const int size = 64;
        const int border = 20;
        const float chamfer = 10f;
        Vector2 light = new Vector2(-0.45f, 0.89f);
        Color face = accent ? Burgundy : Wood;

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = accent ? "ClassicalPlateAccent" : "ClassicalPlate",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ax = Mathf.Min(x, size - 1 - x);
                float ay = Mathf.Min(y, size - 1 - y);
                float diagonal = ax + ay - chamfer;

                // 切角外面直接是空的。九宮格不會拉伸角落，所以這個斜切在任何
                // 尺寸下都是同樣的長度。
                if (diagonal < 0f)
                {
                    pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                bool inCorner = ax < chamfer && ay < chamfer;
                float edge = inCorner ? diagonal * 0.7071f : Mathf.Min(ax, ay);

                // 這一點朝外的方向。角落走 45 度，其餘走最近的那一邊。
                Vector2 outward = inCorner
                    ? new Vector2(x < size * 0.5f ? -0.7071f : 0.7071f,
                                  y < size * 0.5f ? -0.7071f : 0.7071f)
                    : (ax < ay
                        ? new Vector2(x < size * 0.5f ? -1f : 1f, 0f)
                        : new Vector2(0f, y < size * 0.5f ? -1f : 1f));

                float facing = Vector2.Dot(outward, light);       // -1 背光, +1 受光
                float lit = Mathf.Lerp(0.42f, 1.45f, (facing + 1f) * 0.5f);

                Color pixel;
                if (edge < 1f)
                {
                    pixel = new Color(0.05f, 0.028f, 0.018f, 1f);
                }
                else if (edge < 4f)
                {
                    pixel = Gold * lit;
                    pixel.a = 1f;
                }
                else if (edge < 6f)
                {
                    // 溝槽。金線和字盤之間要有一段暗的，線腳才有東西可以「圍」。
                    pixel = new Color(0.045f, 0.025f, 0.016f, 1f);
                }
                else
                {
                    // 下沉的字盤：整體比金線暗，上緣留一點反光。
                    float t = y / (float)(size - 1);
                    float grade = Mathf.Lerp(-0.10f, 0.22f, Mathf.Pow(t, 0.8f));
                    if (edge < 8f) grade += facing * 0.10f;
                    pixel = new Color(
                        Mathf.Clamp01(face.r + grade * 0.55f),
                        Mathf.Clamp01(face.g + grade * 0.42f),
                        Mathf.Clamp01(face.b + grade * 0.32f), 0.99f);
                }

                // 四個切角上的鉚釘，鑄件的收頭。
                if (inCorner && Mathf.Abs(diagonal - 7f) < 1.6f && Mathf.Abs(ax - ay) < 2.4f)
                    pixel = Gold * 1.5f;

                pixels[y * size + x] = pixel;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        sprite.name = texture.name;
        if (accent) accentPlateSprite = sprite; else plateSprite = sprite;
        return sprite;
    }

    private static void AddOutline(GameObject target, Color color, Vector2 distance)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    private static void AddShadow(GameObject target, Color color, Vector2 distance)
    {
        Shadow[] shadows = target.GetComponents<Shadow>();
        Shadow shadow = null;
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] is not Outline) { shadow = shadows[i]; break; }
        }
        if (shadow == null) shadow = target.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }
}

[AddComponentMenu("UI/Classical Arrow Graphic")]
public sealed class ClassicalArrowGraphic : MaskableGraphic
{
    [SerializeField] private bool pointsUp = true;

    public bool PointsUp
    {
        get => pointsUp;
        set { pointsUp = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2[] shape =
        {
            new Vector2(0.50f, 0.96f), new Vector2(0.04f, 0.47f),
            new Vector2(0.29f, 0.47f), new Vector2(0.29f, 0.08f),
            new Vector2(0.71f, 0.08f), new Vector2(0.71f, 0.47f),
            new Vector2(0.96f, 0.47f)
        };
        for (int i = 0; i < shape.Length; i++)
        {
            float normalizedY = pointsUp ? shape[i].y : 1f - shape[i].y;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector2(
                Mathf.Lerp(r.xMin, r.xMax, shape[i].x),
                Mathf.Lerp(r.yMin, r.yMax, normalizedY));
            vh.AddVert(vertex);
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 5);
        vh.AddTriangle(0, 5, 6);
        vh.AddTriangle(2, 3, 4);
        vh.AddTriangle(2, 4, 5);
    }
}
