using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選完歌之後蓋住畫面的載入頁，等該備妥的東西都備妥了才讓遊戲開始。
/// </summary>
/// <remarks>
/// 開頭那幾格卡頓的根源不是任何單一的 bug，是**遊戲在還沒載完的時候就開始跑**：
/// 兩秒的前奏裡塞了譜面解析、兩個上百 MB 的音檔解碼、183 個鋼琴取樣的 Vorbis
/// 解壓、音符池和節拍線池、還有每個材質的第一次繪製。每一項都可以個別優化，
/// 但只要它們仍然發生在音符已經在跑的時候，就一定看得出來。
///
/// 所以這裡改成「先擋住、備妥了再開始」。同樣的工作、同樣的總時間，差別是它
/// 發生在一張靜止的載入頁後面，而不是玩家正在看的跑道上。
///
/// **這一頁該長什麼樣子。** 它是唯一一個玩家一定會看、而且什麼都不能做的畫面，
/// 所以它的工作是把「等待」換成「期待」：放這首歌的曲繪、曲名、難度，讓等的那
/// 幾秒是在認識接下來要彈的東西，而不是在盯著一條進度條。這也是為什麼曲繪同時
/// 出現兩次——鋪滿整個背景的那張負責氣氛，中間裱起來的那張負責辨識。
///
/// 整個 UI 都是執行期建出來的，不需要動場景——這個專案已經有好幾個地方這樣做
/// （見 ClassicalBookUITheme）。
/// </remarks>
public class GameplayLoadingScreen : MonoBehaviour
{
    /// <summary>蓋在所有東西上面。遊戲內的 HUD 也用不到這麼高的值。</summary>
    private const int OverlaySortingOrder = 32000;

    /// <summary>備妥之後的淡出時間。夠短不拖延，夠長不像閃一下。</summary>
    private const float FadeOutSeconds = 0.25f;

    /// <summary>中間那張裱起來的曲繪多大（參考解析度下的像素）。</summary>
    private const float PlateSize = 380f;

    private static GameplayLoadingScreen instance;

    private CanvasGroup group;
    private TextMeshProUGUI statusLabel;
    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI authorLabel;
    private TextMeshProUGUI difficultyLabel;
    private Image backdropImage;
    private RectTransform backdropRect;
    private Image coverImage;
    private Image plateFrame;
    private Image barFill;
    private RectTransform plateRect;
    private Coroutine fade;
    private float clock;
    private bool hasCover;

    public static GameplayLoadingScreen EnsureCreated()
    {
        if (instance != null) return instance;
        var go = new GameObject("GameplayLoadingScreen");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameplayLoadingScreen>();
        return instance;
    }

    public static GameplayLoadingScreen Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        Build();
        SetVisible(false);
    }

    private void Build()
    {
        var canvasGo = new GameObject("LoadingCanvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasGo.AddComponent<CanvasGroup>();
        // 載入頁不接受點擊：底下的選歌畫面在這段期間不該被誤觸。
        group.blocksRaycasts = true;
        group.interactable = false;

        // 最底：純色。曲繪載不出來、或這首歌根本沒有曲繪的時候，這一層就是背景。
        var floor = CreateStretched(canvasGo.transform, "Floor");
        floor.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.028f, 0.024f, 1f);

        // 鋪滿整個畫面的曲繪。它會被壓得很暗而且慢慢移動——這一層不是要讓人看
        // 清楚，是要讓這幾秒有這首歌的顏色。
        backdropRect = CreateStretched(canvasGo.transform, "Backdrop");
        backdropImage = backdropRect.gameObject.AddComponent<Image>();
        backdropImage.preserveAspect = false;
        backdropImage.color = new Color(1f, 1f, 1f, 0f);
        backdropImage.raycastTarget = false;

        // 暗幕。壓在曲繪上，讓中間的字永遠讀得到，不管曲繪本身是亮是暗。
        var shade = CreateStretched(canvasGo.transform, "Shade");
        var shadeImage = shade.gameObject.AddComponent<Image>();
        shadeImage.color = new Color(0.02f, 0.015f, 0.012f, 0.72f);
        shadeImage.raycastTarget = false;

        // 暈影。四邊壓暗一圈，畫面的重量就集中到中間；沒有它的話整頁是均勻的，
        // 而均勻的畫面沒有中心。
        var vignette = CreateStretched(canvasGo.transform, "Vignette");
        var vignetteImage = vignette.gameObject.AddComponent<Image>();
        vignetteImage.sprite = VignetteSprite();
        vignetteImage.type = Image.Type.Simple;
        vignetteImage.color = new Color(1f, 1f, 1f, 0.92f);
        vignetteImage.raycastTarget = false;

        var motes = CreateStretched(canvasGo.transform, "Motes");
        var motesGraphic = motes.gameObject.AddComponent<LoadingMotesGraphic>();
        motesGraphic.color = new Color(0.95f, 0.84f, 0.58f, 1f);

        BuildPlate(canvasGo.transform);
        BuildCaption(canvasGo.transform);
        BuildProgress(canvasGo.transform);
    }

    /// <summary>中間那張裱起來的曲繪：金色細框 + 一點內縮的襯底。</summary>
    private void BuildPlate(Transform parent)
    {
        var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image));
        plate.transform.SetParent(parent, false);
        plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(0.5f, 0.5f);
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.sizeDelta = new Vector2(PlateSize, PlateSize);
        plateRect.anchoredPosition = new Vector2(0f, 118f);
        plateFrame = plate.GetComponent<Image>();
        plateFrame.color = ClassicalBookUITheme.Gold;
        plateFrame.raycastTarget = false;

        // 框和畫之間留一條深色的縫。金框直接貼著曲繪的話，淺色的曲繪會和金色糊
        // 在一起，那條框就消失了。
        var mat = new GameObject("Mat", typeof(RectTransform), typeof(Image));
        mat.transform.SetParent(plate.transform, false);
        var matRect = (RectTransform)mat.transform;
        matRect.anchorMin = Vector2.zero;
        matRect.anchorMax = Vector2.one;
        matRect.offsetMin = new Vector2(3f, 3f);
        matRect.offsetMax = new Vector2(-3f, -3f);
        var matImage = mat.GetComponent<Image>();
        matImage.color = new Color(0.06f, 0.045f, 0.035f, 1f);
        matImage.raycastTarget = false;

        var cover = new GameObject("Cover", typeof(RectTransform), typeof(Image));
        cover.transform.SetParent(mat.transform, false);
        var coverRect = (RectTransform)cover.transform;
        coverRect.anchorMin = Vector2.zero;
        coverRect.anchorMax = Vector2.one;
        coverRect.offsetMin = new Vector2(5f, 5f);
        coverRect.offsetMax = new Vector2(-5f, -5f);
        coverImage = cover.GetComponent<Image>();
        coverImage.preserveAspect = true;
        coverImage.color = new Color(1f, 1f, 1f, 0f);
        coverImage.raycastTarget = false;
    }

    private void BuildCaption(Transform parent)
    {
        titleLabel = CreateLabel(parent, "Title", 44f, new Vector2(0f, -132f),
            new Vector2(1180f, 68f));
        ClassicalBookUITheme.StyleText(titleLabel,
            new Color(0.97f, 0.94f, 0.88f, 1f), 44f, FontStyles.Bold);

        authorLabel = CreateLabel(parent, "Author", 24f, new Vector2(0f, -180f),
            new Vector2(1180f, 40f));
        ClassicalBookUITheme.StyleText(authorLabel,
            new Color(0.84f, 0.79f, 0.70f, 0.80f), 24f, FontStyles.Italic);

        difficultyLabel = CreateLabel(parent, "Difficulty", 26f, new Vector2(0f, -226f),
            new Vector2(1180f, 42f));
        ClassicalBookUITheme.StyleText(difficultyLabel,
            ClassicalBookUITheme.Gold, 26f, FontStyles.Bold);
    }

    private void BuildProgress(Transform parent)
    {
        statusLabel = CreateLabel(parent, "Status", 22f, new Vector2(0f, -318f),
            new Vector2(900f, 40f));
        ClassicalBookUITheme.StyleText(statusLabel,
            new Color(0.88f, 0.85f, 0.79f, 0.70f), 22f, FontStyles.Normal);
        statusLabel.text = Localize.T("載入中…", "载入中…", "Loading…");

        var barBack = new GameObject("BarBackground", typeof(RectTransform), typeof(Image));
        barBack.transform.SetParent(parent, false);
        var barBackRect = (RectTransform)barBack.transform;
        barBackRect.anchorMin = barBackRect.anchorMax = new Vector2(0.5f, 0.5f);
        barBackRect.pivot = new Vector2(0.5f, 0.5f);
        barBackRect.sizeDelta = new Vector2(680f, 3f);
        barBackRect.anchoredPosition = new Vector2(0f, -352f);
        var barBackImage = barBack.GetComponent<Image>();
        barBackImage.color = new Color(1f, 1f, 1f, 0.12f);
        barBackImage.raycastTarget = false;

        var barFillGo = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
        barFillGo.transform.SetParent(barBack.transform, false);
        var fillRect = (RectTransform)barFillGo.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.sizeDelta = Vector2.zero;
        barFill = barFillGo.GetComponent<Image>();
        barFill.color = new Color(0.90f, 0.78f, 0.50f, 0.95f);
        barFill.raycastTarget = false;
    }

    private static RectTransform CreateStretched(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    /// <summary>
    /// 文字一律走 TextMeshPro。
    /// </summary>
    /// <remarks>
    /// Unity 內建字型（LegacyRuntime / Arial）**沒有中日韓字符**，用它的話中文
    /// 會整排變成空白或方框。專案的 CJK 字型是掛在 TMP 的 fallback 上的，
    /// 由 <see cref="ClassicalBookUITheme.ApplyLocalizedFont"/> 依語言和內容挑，
    /// 所以這裡走同一條路。
    ///
    /// overflowMode 明確設成 Overflow：Truncate 在框高不到字級 1.5 倍的時候會
    /// **整段不畫**，而那個症狀看起來和字型壞掉一模一樣。
    /// </remarks>
    private static TextMeshProUGUI CreateLabel(Transform parent, string name, float size,
        Vector2 position, Vector2 area)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = area;
        rect.anchoredPosition = position;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        try
        {
            ClassicalBookUITheme.StyleText(text, new Color(0.92f, 0.90f, 0.86f, 0.92f),
                size, FontStyles.Normal);
        }
        catch
        {
            text.color = new Color(0.92f, 0.90f, 0.86f, 0.92f);
            text.fontSize = size;
        }
        return text;
    }

    /// <summary>
    /// 四邊壓暗的暈影。一張 64x64 的徑向漸層就夠——它會被拉滿全螢幕，而漸層本來
    /// 就沒有需要保留的細節。
    /// </summary>
    private static Sprite vignetteSprite;

    private static Sprite VignetteSprite()
    {
        if (vignetteSprite != null) return vignetteSprite;
        const int Size = 64;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size * 2f - 1f;
                float dy = (y + 0.5f) / Size * 2f - 1f;
                // 用長方形的距離（取兩軸較大者和圓形距離的折衷），純圓形在寬螢幕
                // 上會在左右兩側壓出兩塊很明顯的黑弧。
                float radial = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f;
                float boxed = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                float d = Mathf.Lerp(boxed, radial, 0.55f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.42f) / 0.58f));
                pixels[y * Size + x] = new Color(0f, 0f, 0f, a * 0.85f);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        vignetteSprite.hideFlags = HideFlags.DontSave;
        return vignetteSprite;
    }

    /// <summary>
    /// 這一頁要介紹的是哪一首。曲繪沒有就只留文字，不會留下一個空的金框。
    /// </summary>
    public void SetSong(Sprite cover, string title, string author,
        string difficultyName, int difficultyLevel)
    {
        hasCover = cover != null;

        if (backdropImage != null)
        {
            backdropImage.sprite = cover;
            // 背景那張只要顏色，所以壓得很暗；它的工作是氣氛不是辨識。
            backdropImage.color = hasCover
                ? new Color(0.55f, 0.52f, 0.50f, 1f)
                : new Color(1f, 1f, 1f, 0f);
        }
        if (coverImage != null)
        {
            coverImage.sprite = cover;
            coverImage.color = hasCover ? Color.white : new Color(1f, 1f, 1f, 0f);
        }
        if (plateFrame != null)
        {
            plateFrame.color = hasCover
                ? ClassicalBookUITheme.Gold
                : new Color(0f, 0f, 0f, 0f);
            plateFrame.transform.GetChild(0).gameObject.SetActive(hasCover);
        }

        if (titleLabel != null)
        {
            titleLabel.text = title ?? string.Empty;
            try { ClassicalBookUITheme.ApplyLocalizedFont(titleLabel); } catch { }
        }
        if (authorLabel != null)
        {
            authorLabel.text = string.IsNullOrWhiteSpace(author) ? string.Empty : author;
            try { ClassicalBookUITheme.ApplyLocalizedFont(authorLabel); } catch { }
        }
        if (difficultyLabel != null)
        {
            bool named = !string.IsNullOrWhiteSpace(difficultyName);
            difficultyLabel.text = named
                ? (difficultyLevel > 0 ? $"{difficultyName}  {difficultyLevel}" : difficultyName)
                : string.Empty;
            // 難度用它自己的顏色，和選歌、結算是同一組——同一件事在三個畫面用三
            // 種顏色的話，那個顏色就不代表任何東西了。
            difficultyLabel.color =
                ClassicalBookUITheme.GetDifficultyColour(difficultyName, difficultyLevel);
            try { ClassicalBookUITheme.ApplyLocalizedFont(difficultyLabel); } catch { }
        }
    }

    /// <summary>Shows the screen immediately, cancelling any fade in progress.</summary>
    public void Show(string status)
    {
        if (fade != null) { StopCoroutine(fade); fade = null; }
        clock = 0f;
        SetVisible(true);
        SetProgress(status, 0f);
    }

    public void SetProgress(string status, float progress01)
    {
        if (statusLabel != null && status != null)
        {
            statusLabel.text = status;
            // 字型是按**內容**挑的（有沒有中日韓字符），所以每次換字都要重挑一次，
            // 不能只在建立時做。
            try { ClassicalBookUITheme.ApplyLocalizedFont(statusLabel); } catch { }
        }
        if (barFill == null) return;
        var parent = barFill.rectTransform.parent as RectTransform;
        float width = parent != null ? parent.rect.width : 0f;
        barFill.rectTransform.sizeDelta =
            new Vector2(width * Mathf.Clamp01(progress01), 0f);
    }

    /// <summary>
    /// 背景的緩慢推進和裱框的呼吸。
    /// </summary>
    /// <remarks>
    /// 兩個都刻意做得比「看得出來在動」再慢一點。載入頁上的動態只要證明畫面沒有
    /// 凍住就夠了；快到會被注意到的動態會一直把視線拉回自己身上，而這幾秒本來是
    /// 要給曲繪和曲名的。
    ///
    /// 背景是**推進**不是平移：一張慢慢逼近的圖有「正在往裡面走」的方向感，橫向
    /// 平移則會一直提醒你畫面的邊在哪裡。
    /// </remarks>
    private void Update()
    {
        if (group == null || group.alpha <= 0.001f) return;
        clock += Time.unscaledDeltaTime;

        if (backdropRect != null && hasCover)
        {
            float zoom = 1.06f + 0.055f * Mathf.Min(1f, clock / 14f);
            backdropRect.localScale = new Vector3(zoom, zoom, 1f);
            backdropRect.anchoredPosition = new Vector2(
                Mathf.Sin(clock * 0.11f) * 14f, Mathf.Cos(clock * 0.083f) * 9f);
        }

        if (plateRect != null && hasCover)
        {
            float breathe = 1f + 0.012f * Mathf.Sin(clock * 0.9f);
            plateRect.localScale = new Vector3(breathe, breathe, 1f);
        }
    }

    /// <summary>Fades the screen out. The caller starts the song right after.</summary>
    public void Hide()
    {
        if (group == null) return;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        // blocksRaycasts 立刻放掉：淡出期間遊戲已經在跑了，輸入不該被吃掉。
        if (group != null) group.blocksRaycasts = false;
        while (elapsed < FadeOutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            if (group != null) group.alpha = 1f - Mathf.Clamp01(elapsed / FadeOutSeconds);
            yield return null;
        }
        SetVisible(false);
        fade = null;
    }

    private void SetVisible(bool visible)
    {
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        group.blocksRaycasts = visible;
        group.gameObject.SetActive(true);
    }
}
