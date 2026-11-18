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
    private static TMP_FontAsset koreanFont;
    private static Sprite paperTextureSprite;
    private static Sprite woodTextureSprite;
    // 深色表面另外一組：同一個場、放大過對比、均值搬到 Pivot。key 是
    // 材質旗標加上 0.5 為一階的 gain，所以打錯一個數字不會炸出一堆貼圖。
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> darkSurfaceSprites
        = new System.Collections.Generic.Dictionary<int, Sprite>();
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
        ApplyDarkSurface(backdrop.GetComponent<Image>(), DarkWood, Surface.Wood, 2.5f);

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
            MutedInk, FontStyles.Normal);
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
            // **上下的空隙對稱：上面留多少，到曲名就留多少。**
            //
            // 縱向改用絕對位移而不是比例錨點。曲名和作者是從卡片底部用絕對像素排
            // 的，所以曲繪的下緣也必須用同一把尺量 —— 用比例的話，卡片一換尺寸，
            // 上下兩個空隙就會一個變大一個變小。
            //
            // 走過的兩個彎路：原本 0.32–0.88（中心 0.60，整張畫往上頂、底下空一片）；
            // 改成 0.22–0.78 想置中，但那是對**整張卡**置中，而卡的下半部是字 ——
            // 畫就壓到曲名上了。要置中的是「字上面那塊空間」，不是整張卡。
            //
            // TextTop 是量出來的：SetRect 的 pivot 和 anchor 同為 (0.5, 0)，所以曲名
            // 的 rect 是往上長的 —— 主卡 135+58=193、側卡 74+58=132。把 pivot 當成
            // 中心去算，會以為還有餘裕，實際上已經疊上去了。
            // 曲繪的框。**這一組比例是已知可用的，改之前先量卡片的實際尺寸。**
            //
            // 失敗紀錄（2026-11-08，兩次都讓圖衝出卡片）：
            //  1. 改成 0.22–0.78 想「上下置中」—— 那是對整張卡置中，而卡的下半部是
            //     曲名和作者，圖就壓到字上面。
            //  2. 改成絕對位移（offsetMin.y = 曲名頂端 + 30）—— 卡片的尺寸是從場景
            //     來的（SongSelectionManager 讀 centerSlot.root.sizeDelta），程式裡
            //     沒有常數可對；卡片不夠高就算出負高度，preserveAspect 拿它去除，
            //     圖整個炸開。
            //
            // 要做「上下空隙對稱」的話，先把卡片的實際高度量出來再說 —— 曲名的頂端
            // 是 193（主卡）／132（側卡），那是從 SetRect 的 pivot=(0.5,0) 推出來的。
            coverRect.anchorMin = new Vector2(0.11f, 0.32f);
            coverRect.anchorMax = new Vector2(0.89f, 0.88f);
            coverRect.offsetMin = Vector2.zero;
            coverRect.offsetMax = Vector2.zero;
            coverRect.SetSiblingIndex(Mathf.Min(2, root.childCount - 1));
            EnsureCoverEdge(cover);
        }
        StyleText(title, ParchmentLight, primary ? 31f : 25f, FontStyles.Bold);
        StyleText(difficulty, primary ? Burgundy : MutedInk, primary ? 24f : 19f, FontStyles.Bold);
        // 作者名不用斜體：中日文的字型沒有真正的斜體，TMP 只是把字幾何式地剪切
        // 過去，筆畫的粗細和轉角全歪掉。層級由字級和顏色分（20/16、金／紙色）。
        StyleText(author, primary ? new Color(0.91f, 0.77f, 0.46f, 1f) : AgedPaper,
            primary ? 20f : 16f, FontStyles.Normal);

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
            // 尺寸取自簾桿，和桿左邊那顆「前往練習模式」同寬同高。
            //
            // 這裡本來寫死 188x56，而
            // <see cref="ClassicalSettingsBookmark"/> 每幀又把它改成簾桿的尺寸 ——
            // 實際生效的是後者，所以 188 只是一個讀起來像真的騙人數字，而底下
            // 那一排圖示和標籤的偏移量都是照它算的 —— 換到 290 寬的牌子上，
            // 圖示走到最左、字漂在中間，中間讓出一大塊空白。
            rect.sizeDelta = new Vector2(SelectionRail.ControlWidth, SelectionRail.ControlHeight);
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
        iconRect.anchoredPosition = new Vector2(26f, 0f);
        iconRect.sizeDelta = new Vector2(46f, 38f);
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
        // 字級和簾桿上其他東西同一張表。
        //
        // 本來寫死 21，而桿上的標籤用的是
        // <c>ClassicalType.Label</c>（這張畫布的參考高 1440，換算出來是 26.7）。
        // 實測截圖：「前往練習模式」的字高 13 px，「設定」只有 10 px ——
        // 同一排控制項裡只有它矮一截。
        label.fontSize = ClassicalType.On(button, ClassicalType.Label);
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(84f, 6f);
        labelRect.offsetMax = new Vector2(-22f, -6f);
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
        // 日文和韓文的介面要用自己的字面：漢字的區域字形不同，拿繁體的字型去排日文
        // 會像是用別人的字寫自己的話；諺文在其他幾份字型檔裡根本沒有字。
        else if (containsCjk && language == AppLanguage.Japanese)
            resolvedFont = japaneseFont != null ? japaneseFont : traditionalCjkFont;
        else if (containsCjk && language == AppLanguage.Korean)
            resolvedFont = koreanFont != null ? koreanFont : traditionalCjkFont;
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
        if (ContainsKorean(text.text))
            resolvedFont = koreanFont != null ? koreanFont : resolvedFont;
        else if (ContainsJapanese(text.text))
            resolvedFont = japaneseFont != null ? japaneseFont : resolvedFont;
        else if (ContainsCjk(text.text))
            resolvedFont = traditionalCjkFont != null ? traditionalCjkFont
                : simplifiedCjkFont != null ? simplifiedCjkFont : resolvedFont;
        if (resolvedFont != null) text.font = resolvedFont;
    }

    /// <summary>諺文。音節區塊之外還有字母與相容字母，三段都要認。</summary>
    private static bool ContainsKorean(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < value.Length; i++)
        {
            int code = value[i];
            if ((code >= 0xAC00 && code <= 0xD7AF) ||
                (code >= 0x1100 && code <= 0x11FF) ||
                (code >= 0x3130 && code <= 0x318F))
                return true;
        }
        return false;
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
                (code >= 0x31F0 && code <= 0x31FF) ||
                (code >= 0xAC00 && code <= 0xD7AF))
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
                // **簡體要和繁體同一個字重。** 舊的寫死成 ExtraLight，而繁體是 Bold ——
                // 同一頁上切語言就像換了一套 UI，粗體標題在簡體下根本看不出是標題。
                //
                // 專用的簡體粗體檔不在專案裡，所以這裡排了一條退路：有 SC Bold 就用它；
                // 沒有就用 TC Bold —— Source Han Sans 各區域版本覆蓋的碼位是同一組，簡體字
                // 照樣畫得出來，只是少數區域分歧的字（戶／戸、骨、直）會是台灣字形。
                // 那比「整頁細得看不清」好。把 SC Bold 丟進 Resources 就會自動接手。
                Font source = Resources.Load<Font>("Fonts & Materials/NotoSansCJKsc-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/SourceHanSansSC-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/SourceHanSans-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/SourceHanSansTC-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/SourceHanSans-ExtraLight");
                simplifiedCjkFont = CreateDynamicFont(source, "Source Han Sans SC (Runtime)");
            }
            if (japaneseFont == null)
            {
                // **日文也要和繁體同一個字重。** 繁體是 Bold、簡體 2026-10-03 已經從
                // ExtraLight 改成 Bold，只有日文一直留在 Medium —— 同一頁切成日文就像
                // 整套 UI 換細了一號，粗體標題看不出是標題。和簡體當時完全同一個毛病。
                //
                // 源真ゴシック（GenShinGothic）是 Source Han Sans 加 M+ 的衍生字型，
                // 和繁體的 SourceHanSansTC-Bold 同一套字面，所以兩種語言並排時字形一致；
                // 漢字用的是日本字形（直／骨／海），那正是日文介面該有的樣子。授權是
                // SIL OFL，可以內建，授權條文在同一個資料夾的 GenShinGothic-LICENSE.txt。
                //
                // 退路留著 NotoSansJP：這條鏈任何一環載不到都還有字可印。
                Font source = Resources.Load<Font>("Fonts & Materials/GenShinGothic-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/NotoSansJP-Medium")
                    ?? Resources.Load<Font>("Fonts & Materials/NotoSansJP-SemiBold");
                japaneseFont = CreateDynamicFont(source, "GenShin Gothic (Runtime)");
            }
            if (koreanFont == null)
            {
                // 諺文在其他幾個字型檔裡都沒有字 —— 少了這一份，韓文曲名會印成一排
                // 豆腐。Noto Sans CJK 和 Source Han Sans 是同一套設計，字面對得上。
                Font source = Resources.Load<Font>("Fonts & Materials/NotoSansCJKkr-Bold")
                    ?? Resources.Load<Font>("Fonts & Materials/NotoSansKR-Bold");
                koreanFont = CreateDynamicFont(source, "Noto Sans KR (Runtime)");
            }

            bool simplified = SettingsManager.Instance != null &&
                SettingsManager.Instance.CurrentLanguage == AppLanguage.SimplifiedChinese;
            SetFallbackOrder(elegantFont, simplified, fallback);
            SetCjkFallbackOrder(traditionalCjkFont, simplifiedCjkFont, japaneseFont, koreanFont, elegantFont);
            SetCjkFallbackOrder(simplifiedCjkFont, traditionalCjkFont, japaneseFont, koreanFont, elegantFont);
            SetCjkFallbackOrder(japaneseFont, traditionalCjkFont, simplifiedCjkFont, koreanFont, elegantFont);
            SetCjkFallbackOrder(koreanFont, traditionalCjkFont, simplifiedCjkFont, japaneseFont, elegantFont);
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

        Color scoreTint = primary
            ? new Color(0.22f, 0.095f, 0.052f, 1f)
            : new Color(0.14f, 0.07f, 0.042f, 1f);
        RectTransform score = AddRect(root, "CardScoreBackground", scoreTint,
            Vector2.zero, Vector2.one, new Vector2(7f, 7f), new Vector2(-7f, -7f));
        score.SetAsFirstSibling();
        ApplyDarkSurface(score.GetComponent<Image>(), scoreTint, Surface.Wood, 1f);
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
    /// 每一塊面板底下那張會平鋪的表面：書板是暗色木紋，書頁是有簾紋和鏽點的棉紙。
    /// </summary>
    /// <remarks>
    /// **為什麼接得起來。** 它是鋪出來的，而 `Mathf.PerlinNoise` 在 u=0 和 u=1 取
    /// 到的值不一樣。舊解法是把四份環繞副本交叉淡接，縫沒了但**對比隨位置塌陷**
    /// （中央四份平均，變異數只剩 1/4）—— 所以鏽點全擠在晶格角落。現在底層換成
    /// <see cref="SurfaceNoise"/> 的環繞晶格噪聲：精確週期、各處對比一致。
    ///
    /// **為什麼紙的振幅只有原來的六成。** 換了底層之後同一組權重的對比變成 2.2
    /// 倍（實測標準差 0.0164 → 0.0357）。書頁和遊玩紙卡是目前唯一讀得出來是紙的
    /// 表面，不該被翻倍 —— 收到 0.60 之後亮色 tint 上是 5.2 級（今天 4.0），是看得
    /// 出差別但不會變成花崗岩的幅度。
    ///
    /// 比鏽點更大的東西屬於 <see cref="AgedPaperGraphic"/>：在這個尺寸下它一頁會
    /// 重複三四次，而重複的污痕讀起來是壁紙。
    /// </remarks>
    private static Sprite ResolveTextureSprite(bool wood)
    {
        if (wood && woodTextureSprite != null) return woodTextureSprite;
        if (!wood && paperTextureSprite != null) return paperTextureSprite;

        Surface kind = wood ? Surface.Wood : Surface.Paper;
        Sprite sprite = BuildSurfaceSprite(kind, 1f, MeanOf(kind));
        if (wood) woodTextureSprite = sprite;
        else paperTextureSprite = sprite;
        return sprite;
    }

    /// <summary>一張深色面底下鋪的是什麼東西。</summary>
    /// <remarks>
    /// 以前只有兩種，而「皮」是拿木紋去充數的 —— 木紋的對比一放大，結算頁那本書
    /// 就明白寫著它是一塊木板。皮和木在物理上是相反的兩種表面：木是**有方向**的
    /// 年輪，皮是**沒有方向**的細胞脊。同一張貼圖裝不了兩個。
    /// </remarks>
    public enum Surface
    {
        Paper,
        Wood,
        Leather
    }

    private static float MeanOf(Surface kind)
    {
        switch (kind)
        {
            case Surface.Wood: return WoodMean;
            case Surface.Leather: return LeatherMean;
            default: return PaperMean;
        }
    }

    // 實測的場均值（`SurfaceNoise` 的晶格版、256 的 tile、下面這組權重）。放大對比
    // 的時候要繞著它轉，不是繞著 0.5 轉 —— 繞錯了整個面的亮度就跟著跑掉。
    private const float PaperMean = 0.8568f;
    private const float WoodMean = 0.762993f;
    private const float LeatherMean = LeatherSurface.Mean;
    // 放大之後把均值搬到這裡。為什麼不留在原處：紙的均值是 0.914，往上只有 0.086
    // 的餘裕，gain 一過 1.7 高光就被 clamp 切掉。搬到 0.55 之後兩邊都有空間，代價是
    // 呼叫端的 tint 要乘回 Mean/Pivot 才能維持原來的亮度 —— 那一步在
    // <see cref="ApplyDarkSurface"/> 裡。
    private const float DarkPivot = 0.55f;

    /// <summary>
    /// 深色表面專用的同一張場，對比放大過、均值搬到 <see cref="DarkPivot"/>。
    /// </summary>
    /// <remarks>
    /// **為什麼深色面需要另一張貼圖。** 貼圖乘上 tint 之後才是螢幕上的值，而深色的
    /// tint 會把調變一起壓掉：0.12 的 tint 讓紙的 0.0214 標準差變成 8-bit 上的
    /// **0.74 級** —— 整塊面板從最亮到最暗跨不到一個色階，那不是紋理，那是 dither。
    ///
    /// **為什麼不能直接乘。** `value * gain` 會把高光切掉（見 <see cref="DarkPivot"/>
    /// 的註解）。這裡是繞著均值放大：`Pivot + (value - mean) * gain`。
    ///
    /// **為什麼不給 <see cref="GetPaperTextureSprite"/> 用。** <see cref="BookBoardGraphic"/>
    /// 把同一張貼圖當 `mainTexture` 乘到皮、凹面、紙block 和鍍金四種頂點色上，而鍍金
    /// 是 0.84 —— 均值搬到 0.55 之後那些頂點色要乘 1.66 才補得回來，鍍金就被夾掉了。
    /// 放大過的貼圖只從 <see cref="ApplyDarkSurface"/> 走得到。
    /// </remarks>
    private static Sprite ResolveDarkSurfaceSprite(Surface kind, float gain)
    {
        gain = Mathf.Clamp(gain, 1f, 6f);
        int step = Mathf.RoundToInt(gain * 2f);          // 0.5 一階
        int key = (step << 2) | (int)kind;
        if (darkSurfaceSprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        Sprite sprite = BuildSurfaceSprite(kind, step * 0.5f, DarkPivot);
        darkSurfaceSprites[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// 把一張深色面接上放大過的貼圖，並且把 tint 補回來。
    /// </summary>
    /// <remarks>
    /// `mean_out = centre * tintNew` 必須等於今天的 `mean * tint`，所以 tint 要乘
    /// `mean / Pivot`（紙 1.661、木 1.387）。少了這一步整個面會亮 39-66%。
    ///
    /// gain 怎麼挑：目標是最終 8-bit 標準差 4 級以上、零 clip。實測
    /// `sd = fieldSd * gain * tint * (mean / Pivot) * 255`，所以 tint 越暗 gain 越大。
    /// </remarks>
    /// <summary>
    /// 同一張放大過的貼圖，但給自繪網格用（`Image` 以外的 `Graphic`）。
    /// </summary>
    /// <remarks>
    /// 貼圖會乘到**整個 mesh** 上，所以一個同時畫暗場和金邊的 graphic 不能有
    /// `mainTexture`：金色 0.92 乘上均值 0.55 的貼圖是 0.51，金子會發黑。要用它的元件
    /// 必須把金工拆成另一個沒有貼圖的 graphic —— 見 `PlaqueGraphic`。
    ///
    /// 頂點色要自己乘上 <see cref="DarkSurfaceLift"/>，和 <see cref="ApplyDarkSurface"/>
    /// 對 `image.color` 做的是同一件事。
    /// </remarks>
    public static Texture DarkSurfaceTexture(Surface kind, float gain)
    {
        Sprite sprite = ResolveDarkSurfaceSprite(kind, gain);
        return sprite != null ? sprite.texture : null;
    }

    /// <summary>
    /// tint 要乘的倍率，才能讓平均亮度和放大之前一樣。給**本來就鋪著這張貼圖**的面。
    /// </summary>
    public static float DarkSurfaceLift(Surface kind)
    {
        return MeanOf(kind) / DarkPivot;
    }

    /// <summary>
    /// 同上，但給**本來是純色、現在才補上貼圖**的面。
    /// </summary>
    /// <remarks>
    /// 兩者差一個材質均值，而且差很多（皮是 0.81，所以用錯會暗 19%）。
    ///
    /// 鋪著貼圖的面，它的「改之前」是 `tint × 材質均值`；純色的面，它的「改之前」就
    /// 是 `tint`。放大過的貼圖均值是 <see cref="DarkPivot"/>，所以要回到 `tint` 得乘
    /// `1 / DarkPivot`，回到 `tint × 均值` 才是乘 `均值 / DarkPivot`。
    /// </remarks>
    public static float DarkSurfaceFlatLift => 1f / DarkPivot;

    public static void ApplyDarkSurface(Image image, Color tint, Surface kind, float gain)
    {
        if (image == null) return;
        image.sprite = ResolveDarkSurfaceSprite(kind, gain);
        image.type = Image.Type.Tiled;
        float lift = MeanOf(kind) / DarkPivot;
        image.color = new Color(tint.r * lift, tint.g * lift, tint.b * lift, tint.a);
    }

    private static Sprite BuildSurfaceSprite(Surface kind, float gain, float centre)
    {
        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = kind.ToString() + " Surface x" + gain.ToString("0.0"),
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            hideFlags = HideFlags.DontSave
        };
        // 皮不是逐像素的閉式函式 —— 它的摺痕是一整張場的分水線，要先烤。快取的是
        // **場**不是 Sprite：gain 只是事後的線性變換，錯層的話第二個 gain 會把整個分
        // 水嶺重解一次。
        float[] leather = kind == Surface.Leather ? LeatherSurface.Field() : null;
        if (leather != null && leather.Length != size * size) kind = Surface.Wood;
        float mean = MeanOf(kind);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                if (kind == Surface.Wood)
                {
                    float value = Mathf.Clamp01(centre + (WoodValue(u, v) - mean) * gain);
                    pixels[y * size + x] = new Color(value, value * 0.86f, value * 0.72f, 1f);
                }
                else if (kind == Surface.Leather)
                {
                    float value = Mathf.Clamp01(centre + (leather[y * size + x] - mean) * gain);
                    // 色相比照它取代掉的木紋，所以換的是花樣不是顏色。
                    pixels[y * size + x] = new Color(value, value * 0.86f, value * 0.72f, 1f);
                }
                else
                {
                    float broad = SurfaceNoise.Lattice(x, y, 0.022f, 41, size);
                    float fine = SurfaceNoise.Lattice(x, y, 0.115f, 83, size);
                    // 雲：紙漿聚散不勻，這是最大片的那一層。
                    float cloud = SurfaceNoise.Lattice(x, y, 0.0085f, 217, size);
                    // 簾紋：抄紙的簾子壓出來的細橫線。0.613592 = 25 個整數週期 / 256
                    // （原本 0.62 是 25.26 個，橫向接縫差 94°）。
                    float laid = Mathf.Sin(y * 0.613592f + broad * 2.2f) * 0.5f + 0.5f;
                    // 纖維：斜著走的短纖，靠兩個不同頻率的雜訊相乘挑出來。
                    float fibre = Mathf.Clamp01(fine * SurfaceNoise.Lattice(x + y, y - x, 0.075f, 334, size) * 2.1f);

                    // 0.908343 不是 0.965：權重收到六成之後場的均值會從 0.857 漂到
                    // 0.914，而書頁的 tint 是對著舊均值挑的，不壓回去整本書會亮 6.6%。
                    float value = 0.908343f - cloud * 0.096f - laid * 0.0168f - fibre * 0.045f
                                  + broad * 0.027f;
                    value = Mathf.Clamp01(centre + (Mathf.Clamp01(value) - mean) * gain);

                    // 鏽點：稀疏、獨立、偏紅。它們是紙上唯一有顏色的東西。
                    //
                    // 三個數字都是量出來的：頻率 0.094（週期 24，每格 10.7px，所以一
                    // 顆斑是 3-4px 而不是 mip 鏈會吃掉的 1px 點）、門檻 0.97（覆蓋
                    // 0.25%；晶格場恢復原始分布之後舊的 0.80 會打中 9.9%）。
                    //
                    // 而且它要畫在放大**之後**：鏽點在場裡是 15% 的壓暗，gain 4 會把
                    // 它變成 60% 的破洞。改成乘法加色相位移，所以每一個 gain 下的斑
                    // 深淺一致。
                    float g = 0.982f;
                    float b = 0.928f;
                    float spot = SurfaceNoise.Lattice(x, y, 0.094f, 572, size);
                    if (spot > 0.97f)
                    {
                        float bite = (spot - 0.97f) / 0.03f;
                        value *= 1f - bite * 0.149f;
                        g = Mathf.Lerp(0.982f, 0.854f, bite);
                        b = Mathf.Lerp(0.928f, 0.689f, bite);
                    }
                    pixels[y * size + x] = new Color(value, value * g, value * b, 1f);
                }
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name + " Sprite";
        return sprite;
    }

    /// <summary>龜裂的雜訊場。要取三次（本點和東、北各一 texel）算梯度。</summary>
    private static float CrazeField(int x, int y, int size)
    {
        return SurfaceNoise.Lattice(x, y, 0.12f, 719, size) * 3.4f
               + SurfaceNoise.Lattice(x, y, 0.44f, 55, size) * 0.7f;
    }

    /// <summary>木頭的剖面：窄的晚材線、寬的早材面、順木理的導管。</summary>
    /// <remarks>
    /// **為什麼不是正弦。** 原本是 `sin(x * 0.13)`，那是**對稱**的 —— 深色線和淺色
    /// 面一樣寬。真的木頭剖面裡晚材是一條窄而硬的暗線，早材是一片寬而軟的亮面，
    /// 而且年輪是彎的。對稱的正弦加上單八度的值噪聲（256 的 tile 上一顆 42.7px 的
    /// 團塊）交叉出來是格子紋 —— 今天看不見是因為對比只有 8-bit 上的一級，一放大
    /// 立刻變成瓦楞紙。
    ///
    /// 這個模型是練習室木牆（`PracticeRoomScreen.BuildWoodPixels`，全專案最好的程序
    /// 材質）的環模型，去掉板縫 —— 這張貼圖會被鋪在各種尺寸上，板縫的間距會亂掉。
    /// </remarks>
    private static float WoodValue(float u, float v)
    {
        float warp = (SurfaceNoise.Fbm(u, v, 3, 2, 3) - 0.5f) * 0.40f
                     + (SurfaceNoise.Fbm(u, v, 9, 5, 2) - 0.5f) * 0.09f;
        float r = (u + warp) * 9f;                    // 9 個整數週期，所以接得回去
        float t = r - Mathf.Floor(r);
        float band = Mathf.Min(t, 1f - t);
        float late = Mathf.Exp(-band * band * 180f);  // 晚材：窄、硬、暗
        float growth = Mathf.Clamp01((t - 0.10f) / 0.62f);
        growth = growth * growth * (3f - 2f * growth);
        float grain = Mathf.Clamp01((1f - late) * (0.78f + growth * 0.22f));
        // 導管：順著木理跑的細長孔，所以格子是 72x14 不是正方的。
        //
        // 72 不是 180。180 格鋪在 256 的 tile 上是一格 1.42 像素 —— 在 Nyquist 之下，
        // 畫出來不是導管而是一片寬窄亂跳的硬邊直條（取樣不足的鋸齒，FFT 上別名到
        // −76 剛好對得上）。72 是一格 3.56 像素，形狀才長得出來。
        float pore = SurfaceNoise.Noise(u * 72f, v * 14f, 72, 14);
        grain = Mathf.Clamp01(grain - Mathf.Max(0f, pore - 0.60f) * 1.10f);
        return 0.60f + grain * 0.22f;
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
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = "Classical Ribbon Weave",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
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
                value -= SurfaceNoise.Lattice(x, y, 0.030f, 173, size) * 0.09f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value * 0.995f, value * 0.985f, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
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
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = "Classical Cover Card",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
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
                float tooth = SurfaceNoise.Lattice(x, y, 0.44f, 127, size);
                float grain = SurfaceNoise.Lattice(x, y, 0.155f, 33, size);
                float mottle = SurfaceNoise.Lattice(x, y, 0.046f, 611, size);
                float cloud = SurfaceNoise.Lattice(x, y, 0.011f, 473, size);

                // 龜裂：把雜訊場的等高線挑出來。三個高度，線就會分岔、交會。
                // 頻率要高：龜裂是幾個像素一格的細網，不是幾十個像素一格的
                // 大理石紋。低頻的等高線讀起來是地圖，不是老掉的漆面。
                // 等高線的寬度要**以像素計**。原本是「場的值離 level 不到 0.022
                // 就上色」—— 場的梯度是 0.236/px，所以那道帶的全寬是 0.19 像素，四個
                // level 裡 0.55 更只打中 0.005% 的面積。整張龜裂網是量得出來的，看
                // 不出來的。現在拿 ±1 texel 算梯度，把值域的距離換成像素距離，得到固
                // 定 1.6px 的線。
                float field = CrazeField(x, y, size);
                float slope = Mathf.Max(1e-4f, Mathf.Sqrt(
                    (CrazeField(x + 1, y, size) - field) * (CrazeField(x + 1, y, size) - field)
                    + (CrazeField(x, y + 1, size) - field) * (CrazeField(x, y + 1, size) - field)));
                float crack = 0f;
                for (int level = 0; level < 3; level++)
                {
                    // level 從實測的場範圍導出（0.159-4.033），不是寫死的 0.55 起跳 ——
                    // 舊的四個 level 裡 0.55 只打中 0.005% 的面積，那一條等高線是死的。
                    float target = 0.159f + (4.033f - 0.159f) * (level + 0.5f) / 3f;
                    crack = Mathf.Max(crack, Mathf.Clamp01(1f - Mathf.Abs(field - target) / slope / 0.8f));
                }

                // 底紋的權重收到 0.65：晶格場的對比是舊 Perlin 的 2.2 倍，原權重會把
                // 封面變成花崗岩。龜裂現在是看得見的，底不需要再搶戲。
                float value = 0.995f - (cloud * 0.085f + mottle * 0.105f + grain * 0.065f
                                        + tooth * 0.075f) * 0.65f - crack * 0.13f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value * 0.992f, value * 0.975f, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        coverTextureSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        coverTextureSprite.name = texture.name + " Sprite";
        return coverTextureSprite;
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
    ///
    /// **任何一邊都不能小於 40 單位。** 牌面是 64 格、邊框 20 的九宮切片，所以左右
    /// 的鎏金線腳各吃掉 20。rect 比 40 窄的時候 uGUI 不會報錯，它會把兩條邊框**等比
    /// 縮小**（<c>Image.GetAdjustedBorders</c>）然後把中間的凹面壓成 0 ——
    /// 切角和鉚釘互相疊在一起、凹面整個消失，剩下一塊糊掉的銅片。分類列的兩顆箭頭
    /// （31.7 寬）和分組選單的每一列（38 高）都踩過這一條。
    ///
    /// **也不要拿暗色來調它。** 這是乘在貼圖上的，給平塗用的那種 0.1～0.3 會把整張
    /// 牌乘成近黑。要分出「選中／沒選中」就在白色附近動（例如 1.0 對 0.72）。
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
            // 九宮格**不要** mipmap：mip 層會跨過切片邊界混色，邊角會糊掉。沒有
            // mip 的話 trilinear 和 aniso 都是空設定，所以維持 Bilinear。
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

    /// <summary>曲繪的金邊。見 CoverEdgeFit：uGUI 的 Outline 畫出來是封面自己。</summary>
    /// <remarks>複製出來的卡片要自己再叫一次 —— 執行期的綁定不會跟著 Instantiate。</remarks>
    internal static void EnsureCoverEdge(Image cover)
    {
        if (cover == null) return;
        Transform parent = cover.rectTransform.parent;
        if (parent == null) return;

        Transform existing = parent.Find("~CoverEdge");
        RectTransform rect;
        if (existing != null) rect = existing as RectTransform;
        else
        {
            var edgeObject = new GameObject("~CoverEdge", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(EdgeLightGraphic), typeof(CoverEdgeFit));
            edgeObject.layer = cover.gameObject.layer;
            rect = edgeObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
        }
        if (rect == null) return;

        // 緊接在曲繪後面畫。**位置由 CoverEdgeFit 每幀對齊世界中心**，這裡只把
        // 錨點收成一個點 —— CoverEdgeFit 是用 sizeDelta 當實際大小的，拉伸的錨點
        // 會讓 sizeDelta 變成「額外大小」，算出來的框就不對。
        RectTransform coverRect = cover.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.SetSiblingIndex(Mathf.Min(coverRect.GetSiblingIndex() + 1, parent.childCount - 1));

        var graphic = rect.GetComponent<EdgeLightGraphic>();
        if (graphic != null) graphic.raycastTarget = false;
        var fit = rect.GetComponent<CoverEdgeFit>();
        if (fit != null) fit.Bind(cover, graphic);
    }

    private static void AddOutline(GameObject target, Color color, Vector2 distance)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    internal static void AddShadow(GameObject target, Color color, Vector2 distance)
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
    /// <summary>箭頭指哪一邊。</summary>
    public enum Facing { Up, Down, Left, Right }

    [SerializeField] private Facing direction = Facing.Up;

    public Facing Direction
    {
        get => direction;
        set
        {
            if (direction == value) return;
            direction = value;
            SetVerticesDirty();
        }
    }

    /// <summary>只有上下的舊介面。</summary>
    public bool PointsUp
    {
        get => direction != Facing.Down;
        set => Direction = value ? Facing.Up : Facing.Down;
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
            Vector2 p = Orient(shape[i]);
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector2(
                Mathf.Lerp(r.xMin, r.xMax, p.x),
                Mathf.Lerp(r.yMin, r.yMax, p.y));
            vh.AddVert(vertex);
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 5);
        vh.AddTriangle(0, 5, 6);
        vh.AddTriangle(2, 3, 4);
        vh.AddTriangle(2, 4, 5);
    }

    /// <summary>把朝上的那份形狀轉到 <see cref="direction"/>。</summary>
    /// <remarks>
    /// **轉座標，不轉 RectTransform。** 轉 transform 的話寬高會跟著轉：一顆 56×68
    /// 的鈕裡會出現一支 68 寬的箭頭，兩頭戳到鈕外面去。這裡是在自己那塊區域裡重新
    /// 排點，所以指哪一邊都正好填滿同一塊地方。
    /// </remarks>
    private Vector2 Orient(Vector2 p)
    {
        switch (direction)
        {
            case Facing.Down: return new Vector2(p.x, 1f - p.y);
            case Facing.Left: return new Vector2(1f - p.y, p.x);
            case Facing.Right: return new Vector2(p.y, 1f - p.x);
            default: return p;
        }
    }
}
