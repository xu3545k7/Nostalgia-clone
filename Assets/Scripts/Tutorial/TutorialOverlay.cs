using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 教學的對話框：右下角一張羊皮紙，講完就消失。
/// </summary>
/// <remarks>
/// * **放右下角**：上方是河道的遠端和連段數字，擋在那裡會蓋住正要落下來的音符。
/// * **說明 → 敲三下 → 開始**（使用者定的順序）：說明框在三拍之前讀完、收起，接著
///   用開場那組鼓棒聲敲三下，第四拍就是段落的第一個音。三拍讓玩家聽到速度，不用看。
/// * 閱讀時間照字數給，但塞不下時會壓縮；多出來的空檔留在說明**之前**（剛打完上一段
///   的自然停頓），說明結束到音符出現之間不再乾等。
/// * 一課的第一段前面先講這一課要學什麼，再講第一段。
///
/// 時間表在載入時由段落算好（<see cref="BuildCues"/>），每幀只是找出現在的那一句。
/// 排序在暫停選單（9999）底下，暫停時會被蓋住。
/// </remarks>
public class TutorialOverlay : MonoBehaviour
{
    private const int SortingOrder = 9000;
    private const float FadeMs = 250f;
    /// <summary>段落開始後對話框至少再留這麼久，玩家看得到「換你」再專心打。</summary>
    // 和 qt_editor/build_tutorial_k265.py 的 READ_* 一致：產生器照同一個公式留空白。
    private const float ReadBaseMs = 500f;
    private const float MinReadMs = 1200f;
    private const float MaxReadMs = 3000f;
    private const float ReadMsPerChar = 65f;
    private const int CountInBeats = 3;
    /// <summary>說明框在第一下鼓棒後再留一下才收，免得聲音和框同時消失顯得突兀。</summary>
    private const float LingerAfterCountMs = 200f;

    private struct Cue
    {
        public float start;
        public float end;
        public string badge;
        public string text;
        public bool yourTurn;
    }

    private static TutorialOverlay instance;

    private readonly List<Cue> cues = new List<Cue>();

    private struct CountIn
    {
        public float downbeat;
        public float beat;
    }

    private readonly List<CountIn> countIns = new List<CountIn>();
    private int nextCountIn;
    private const float PanelWidth = 640f;
    private const float CaptionTop = 62f;
    private const float PanelBottomPadding = 20f;
    private const float MinCaptionHeight = 40f;

    private CanvasGroup group;
    private RectTransform panel;
    private TextMeshProUGUI badge;
    private TextMeshProUGUI caption;
    private string lastBadge;
    private string lastCaption;

    public static void Show(TutorialSession.Lesson lesson)
    {
        if (instance == null)
        {
            var go = new GameObject("TutorialOverlay");
            instance = go.AddComponent<TutorialOverlay>();
            instance.Build();
        }
        instance.BuildCues(lesson);
        instance.lastBadge = instance.lastCaption = null;
        instance.group.alpha = 0f;
    }

    public static void Hide()
    {
        if (instance != null) Destroy(instance.gameObject);
        instance = null;
    }

    private static float ReadMs(string text)
    {
        int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
        return Mathf.Clamp(ReadBaseMs + length * ReadMsPerChar, MinReadMs, MaxReadMs);
    }

    private void BuildCues(TutorialSession.Lesson lesson)
    {
        cues.Clear();
        countIns.Clear();
        nextCountIn = 0;
        if (lesson == null) return;
        List<TutorialSession.Segment> segments = lesson.segments;

        // 每一課有幾段、這是第幾段（全課程裡每一課各自數）。
        var perLesson = new Dictionary<int, int>();
        foreach (TutorialSession.Segment s in segments)
            perLesson[s.lesson] = perLesson.TryGetValue(s.lesson, out int n) ? n + 1 : 1;
        var seen = new Dictionary<int, int>();

        for (int i = 0; i < segments.Count; i++)
        {
            TutorialSession.Segment segment = segments[i];
            float beat = segment.beatMs > 0 ? segment.beatMs : 0f;
            float countStart = segment.startMs - CountInBeats * beat;
            float windowStart = i == 0 ? 0f : segments[i - 1].endMs + 200f;
            bool intro = !string.IsNullOrEmpty(segment.lessonTitle);
            float introRead = intro ? ReadMs(segment.goal) : 0f;
            float captionRead = ReadMs(segment.caption);
            float available = countStart - windowStart;
            if (introRead + captionRead > available && available > 0f)
            {
                float scale = available / (introRead + captionRead);
                introRead *= scale;
                captionRead *= scale;
            }
            // 說明貼著三拍之前結束；多的空檔在說明前面。
            float t = Mathf.Max(windowStart, countStart - introRead - captionRead);
            if (beat > 0f) countIns.Add(new CountIn { downbeat = segment.startMs, beat = beat });

            if (intro)
            {
                string title = !string.IsNullOrEmpty(segment.lessonLabel)
                    ? segment.lessonLabel
                    : segment.lesson > 0
                        ? $"教學 {segment.lesson}：{segment.lessonTitle}"
                        : segment.lessonTitle;
                cues.Add(new Cue { start = t, end = t + introRead, badge = title, text = segment.goal });
                t += introRead;
            }

            int index = seen.TryGetValue(segment.lesson, out int k) ? k + 1 : 1;
            seen[segment.lesson] = index;
            int count = perLesson[segment.lesson];
            string what = segment.IsDemo
                ? "示範"
                : (segment.recital ? "換你　演奏會模式" : "換你");
            // 舊檔沒有 beatMs：沒有三拍，說明撐到段落開始後一下子。
            float end = beat > 0f ? countStart + LingerAfterCountMs : segment.startMs + 1200f;
            cues.Add(new Cue
            {
                start = t,
                end = Mathf.Max(end, t + 600f),
                badge = count > 1 ? $"{what}　（{index}/{count}）" : what,
                text = segment.caption,
                yourTurn = !segment.IsDemo,
            });
        }
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
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        panel = SettingsUiKit.CreateRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
        // 底邊固定、往上長：說明多一行，框就高一行，不會蓋到下面的東西。
        panel.pivot = new Vector2(1f, 0f);
        panel.sizeDelta = new Vector2(PanelWidth, 210f);
        panel.anchoredPosition = new Vector2(-36f, 150f);
        var background = panel.gameObject.AddComponent<Image>();
        background.color = ClassicalBookUITheme.Parchment;
        background.raycastTarget = false;

        badge = CreateText(panel, "Badge", 28f, FontStyles.Bold, new Vector2(0f, -14f), 46f);
        caption = CreateText(panel, "Caption", 27f, FontStyles.Normal, new Vector2(0f, -CaptionTop), 136f);
    }

    /// <summary>框的高度跟著說明的行數走。以前框高寫死 210，長一點的說明就溢出框外。</summary>
    private void FitPanelToCaption()
    {
        if (panel == null || caption == null) return;
        float width = PanelWidth - 40f;
        Vector2 preferred = caption.GetPreferredValues(caption.text, width, 0f);
        float height = Mathf.Max(MinCaptionHeight, preferred.y + 6f);
        caption.rectTransform.sizeDelta = new Vector2(caption.rectTransform.sizeDelta.x, height);
        panel.sizeDelta = new Vector2(PanelWidth, CaptionTop + height + PanelBottomPadding);
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, float size,
        FontStyles style, Vector2 position, float height)
    {
        RectTransform rect = SettingsUiKit.CreateRect(name, parent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-40f, height);
        rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.color = ClassicalBookUITheme.Ink;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        // Truncate 在框高不足時會整段不畫，寧可凸出去被看見。
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void Update()
    {
        Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
        if (TutorialSession.Current == null || conductor == null || !conductor.isPlaying)
        {
            group.alpha = 0f;
            return;
        }

        float songPos = conductor.effectiveSongPosition;
        ScheduleCountIns(songPos);
        int found = -1;
        for (int i = 0; i < cues.Count; i++)
        {
            if (songPos >= cues[i].start && songPos < cues[i].end) { found = i; break; }
            if (songPos < cues[i].start) break;
        }
        if (found < 0)
        {
            group.alpha = 0f;
            return;
        }

        Cue cue = cues[found];
        SetText(badge, ref lastBadge, cue.badge);
        if (SetText(caption, ref lastCaption, cue.text)) FitPanelToCaption();
        badge.color = cue.yourTurn ? ClassicalBookUITheme.Burgundy : ClassicalBookUITheme.MutedInk;
        // 相鄰兩句接得很緊時不要閃一下：只有前後真的沒有話才淡入淡出。
        bool joinedBefore = found > 0 && cues[found - 1].end >= cue.start - 1f;
        bool joinedAfter = found + 1 < cues.Count && cues[found + 1].start <= cue.end + 1f;
        float fadeIn = joinedBefore ? 1f : Mathf.Clamp01((songPos - cue.start) / FadeMs);
        float fadeOut = joinedAfter ? 1f : Mathf.Clamp01((cue.end - songPos) / FadeMs);
        group.alpha = Mathf.Min(fadeIn, fadeOut);
    }

    /// <summary>
    /// 快到的那一組三拍用 DSP 排程敲出來（和開場倒數同一組鼓棒）。
    /// </summary>
    /// <remarks>
    /// 提早一點排：DSP 排程要在發聲之前交出去才準。已經錯過的（例如跳著播）整組略過，
    /// 不要補敲一堆在錯的時間。
    /// </remarks>
    private void ScheduleCountIns(float songPos)
    {
        while (nextCountIn < countIns.Count)
        {
            CountIn next = countIns[nextCountIn];
            float first = next.downbeat - CountInBeats * next.beat;
            if (songPos < first - 300f) return;
            nextCountIn++;
            if (songPos > first + 50f) continue;
            double downbeatDsp = AudioSettings.dspTime + (next.downbeat - songPos) / 1000.0;
            try { CountInSticks.Schedule(downbeatDsp, next.beat / 1000.0); } catch { }
        }
    }

    private static bool SetText(TextMeshProUGUI text, ref string cache, string value)
    {
        value ??= string.Empty;
        if (cache == value) return false;
        cache = value;
        text.text = value;
        // 字型照文字內容決定，換字之後要重新套。課程文字是繁中寫死的，不跟介面語系走，
        // 所以用內容字型：英文介面下 ApplyLocalizedFont 不會換成中文字型。
        ClassicalBookUITheme.ApplyContentFont(text);
        return true;
    }
}
