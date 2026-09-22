using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Seconds count-in and movement-aware cursor visibility.</summary>
public sealed class GameplayEntryPresentation : MonoBehaviour
{
    private static GameplayEntryPresentation instance;
    private CanvasGroup countGroup;
    private TextMeshProUGUI countLabel;
    private Coroutine countRoutine;
    private bool gameplayActive;
    private Vector2 lastPointer;
    private float cursorVisibleUntil;

    private static GameplayEntryPresentation EnsureCreated()
    {
        if (instance != null) return instance;
        GameObject root = new GameObject("GameplayEntryPresentation");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<GameplayEntryPresentation>();
        instance.Build();
        return instance;
    }

    /// <summary>
    /// 秒倒數，數完打一下 START。
    /// </summary>
    /// <param name="seconds">倒數的長度。</param>
    /// <param name="rollSeconds">
    /// 倒數結束之後、第一小節落下之前譜面要滾多久（三拍）。START 的脹大淡出就
    /// 鋪在這一段上，所以它退場的時候剛好是小節進來的時候，中間沒有空窗。
    /// </param>
    public static void BeginCountdown(float seconds, float rollSeconds = 0f)
    {
        GameplayEntryPresentation view = EnsureCreated();
        view.gameplayActive = true;
        view.lastPointer = ReadPointer();
        view.cursorVisibleUntil = 0f;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;
        if (view.countRoutine != null) view.StopCoroutine(view.countRoutine);
        view.countRoutine = view.StartCoroutine(
            view.Countdown(Mathf.Max(0.1f, seconds), Mathf.Max(0f, rollSeconds)));
    }

    public static void EndGameplay()
    {
        if (instance != null)
        {
            instance.gameplayActive = false;
            if (instance.countRoutine != null) instance.StopCoroutine(instance.countRoutine);
            instance.countRoutine = null;
            if (instance.countGroup != null) instance.countGroup.alpha = 0f;
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public static void RevealCursor()
    {
        GameplayEntryPresentation view = EnsureCreated();
        Cursor.visible = true;
        view.cursorVisibleUntil = Time.unscaledTime + 1.6f;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject labelObject = new GameObject("CountIn", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(CanvasGroup));
        labelObject.transform.SetParent(transform, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        // 放得下「START」，不是只放得下一個數字。窄框加上自動換行會把它折成
        // 兩行，而那一下正好是最不該分心的一刻。
        rect.sizeDelta = new Vector2(1100f, 260f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        countLabel = labelObject.GetComponent<TextMeshProUGUI>();
        countLabel.alignment = TextAlignmentOptions.Center;
        countLabel.textWrappingMode = TextWrappingModes.NoWrap;
        countLabel.overflowMode = TextOverflowModes.Overflow;
        countLabel.fontSize = 128f;
        countLabel.fontStyle = FontStyles.Bold;
        countLabel.color = new Color(0.97f, 0.86f, 0.54f, 1f);
        countLabel.outlineColor = new Color(0.12f, 0.035f, 0.02f, 0.95f);
        countLabel.outlineWidth = 0.18f;
        countLabel.raycastTarget = false;
        ClassicalBookUITheme.ApplyLocalizedFont(countLabel);
        countGroup = labelObject.GetComponent<CanvasGroup>();
        countGroup.alpha = 0f;
        countGroup.blocksRaycasts = false;
    }

    private IEnumerator Countdown(float seconds, float rollSeconds)
    {
        float end = Time.unscaledTime + seconds;
        int previous = -1;
        countGroup.alpha = 1f;
        while (Time.unscaledTime < end)
        {
            float remaining = end - Time.unscaledTime;
            int value = Mathf.Max(1, Mathf.CeilToInt(remaining));
            if (value != previous)
            {
                previous = value;
                countLabel.text = value.ToString();
                countLabel.rectTransform.localScale = Vector3.one * 1.18f;
            }
            countLabel.rectTransform.localScale = Vector3.Lerp(
                countLabel.rectTransform.localScale, Vector3.one, 10f * Time.unscaledDeltaTime);
            // 數字之間不再淡出。原本每一秒都會淡到近乎透明再跳回來，「1」那一格
            // 尤其糟：最需要看清楚的一下反而最淡。
            countGroup.alpha = 1f;
            yield return null;
        }

        yield return Flash(rollSeconds);
        countRoutine = null;
    }

    /// <summary>數完之後的那一下：START 脹大、淡出。</summary>
    /// <remarks>
    /// 它不是第四個數字，是「數完了」這件事本身，所以它不能停在原地淡掉 ——
    /// 停著淡掉讀成被忽略，脹大才讀成放手。
    ///
    /// 長度鋪在那三拍上（夾在 0.35～0.9 秒之間）：慢歌的三拍有三秒多，一個字
    /// 撐那麼久會變成擋在譜面前面的東西；快歌的三拍不到一秒，太短又會看不見。
    /// </remarks>
    private IEnumerator Flash(float rollSeconds)
    {
        float span = rollSeconds > 0f ? Mathf.Clamp(rollSeconds, 0.35f, 0.9f) : 0.6f;
        countLabel.text = "START";
        countGroup.alpha = 1f;

        float begun = Time.unscaledTime;
        while (Time.unscaledTime - begun < span)
        {
            float k = (Time.unscaledTime - begun) / span;
            // 快起慢收。等速脹大讀起來是一個被拉大的圖形，先快後慢才像被推出去。
            float ease = 1f - (1f - k) * (1f - k);
            countLabel.rectTransform.localScale = Vector3.one * (1f + 0.9f * ease);
            countGroup.alpha = 1f - ease;
            yield return null;
        }

        countGroup.alpha = 0f;
        countLabel.rectTransform.localScale = Vector3.one;
    }

    private void Update()
    {
        if (!gameplayActive) return;
        Vector2 pointer = ReadPointer();
        if ((pointer - lastPointer).sqrMagnitude > 2.25f)
        {
            Cursor.visible = true;
            cursorVisibleUntil = Time.unscaledTime + 1.6f;
            lastPointer = pointer;
        }
        else if (Cursor.visible && Time.unscaledTime >= cursorVisibleUntil)
        {
            Cursor.visible = false;
        }
    }

    private static Vector2 ReadPointer()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
