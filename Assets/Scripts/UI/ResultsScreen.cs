using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Judgment; 
public class ResultsScreen : MonoBehaviour
{
    private static ResultsScreen _instance;
    public static ResultsScreen Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<ResultsScreen>();
            return _instance;
        }
    }

    [Header("UI References")]
    public GameObject panel; // root panel to show/hide
    public TextMeshProUGUI justText;
    public TextMeshProUGUI greatText;
    public TextMeshProUGUI goodText;
    public TextMeshProUGUI missText;
    public TextMeshProUGUI fastText;
    public TextMeshProUGUI lateText;
    public TextMeshProUGUI scoreText;
    public Button closeButton;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        if (panel != null) panel.SetActive(false);
        if (closeButton != null)
        {
            // ensure we don't double-register listeners
            try { closeButton.onClick.RemoveAllListeners(); } catch { }
            closeButton.onClick.AddListener(OnClosePressed);
        }
        // Ensure layout containers and alignment are set up so texts are vertically listed and horizontally centered
        EnsureLayout();

        // If the closeButton was not assigned in the Inspector, try to auto-find one
        TryAutoWireCloseButton();
    }

    // Called when the close button is pressed: hide results and return to song selection
    private void OnClosePressed()
    {
        // 只呼叫 GameManager 來處理回到選歌流程（舊名已移除）
        Hide();
        GameManager.Instance?.ReselectSong();
    }

    // If closeButton wasn't assigned in the inspector, attempt to find and wire a suitable Button
    private void TryAutoWireCloseButton()
    {
        if (panel == null) return;
        if (closeButton != null) return; // already assigned

        // Look for a child button named 'Close' (case-insensitive) first
        var buttons = panel.GetComponentsInChildren<Button>(true);
        Button found = null;
        foreach (var b in buttons)
        {
            if (b == null) continue;
            if (string.Equals(b.gameObject.name, "Close", System.StringComparison.InvariantCultureIgnoreCase)) { found = b; break; }
        }
        // If not found by name, pick the first button under the panel
        if (found == null && buttons.Length > 0) found = buttons[0];

        if (found != null)
        {
            closeButton = found;
            try { closeButton.onClick.RemoveAllListeners(); } catch { }
            closeButton.onClick.AddListener(OnClosePressed);

            // Ensure it's visible and has a reasonable size so layout doesn't crush it
            try
            {
                var rt = closeButton.GetComponent<RectTransform>();
                if (rt != null && rt.sizeDelta.sqrMagnitude < 1e-6f)
                {
                    rt.sizeDelta = new Vector2(160f, 40f);
                }
            }
            catch { }

            // Ensure its label (TextMeshProUGUI or legacy Text) is set to "Close" and won't wrap vertically
            try
            {
                var tmp = closeButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (tmp != null)
                {
                    tmp.text = "Close";
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                }
                else
                {
                    var legacy = closeButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (legacy != null) { legacy.text = "Close"; legacy.alignment = TextAnchor.MiddleCenter; }
                }
            }
            catch { }
        }
    }

    private void EnsureLayout()
    {
        if (panel == null) return;
        // Create or find a central Root container to vertically stack counts, score and button
        Transform rootT = panel.transform.Find("ResultsRoot");
        if (rootT == null)
        {
            var rootGo = new GameObject("ResultsRoot", typeof(RectTransform));
            rootGo.transform.SetParent(panel.transform, false);
            rootT = rootGo.transform;
            var rt = rootGo.GetComponent<RectTransform>();
            // center the root in the panel
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(600f, 400f);

            var vgRoot = rootGo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vgRoot.childAlignment = TextAnchor.MiddleCenter;
            vgRoot.childForceExpandHeight = false;
            vgRoot.childForceExpandWidth = false;
            vgRoot.spacing = 12f;
            var csRoot = rootGo.AddComponent<ContentSizeFitter>();
            csRoot.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csRoot.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Find or create CountsContainer (holds just/great/good/miss) as child of root
        Transform countsT = rootT.Find("CountsContainer");
        if (countsT == null)
        {
            var go = new GameObject("CountsContainer", typeof(RectTransform));
            go.transform.SetParent(rootT, false);
            countsT = go.transform;
            var vg = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vg.childAlignment = TextAnchor.MiddleCenter;
            vg.childForceExpandHeight = false;
            vg.childForceExpandWidth = false;
            vg.spacing = 6f;
            var cs = go.AddComponent<ContentSizeFitter>();
            cs.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cs.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Find or create ScoreContainer (holds score) as child of root
        Transform scoreT = rootT.Find("ScoreContainer");
        if (scoreT == null)
        {
            var go = new GameObject("ScoreContainer", typeof(RectTransform));
            go.transform.SetParent(rootT, false);
            scoreT = go.transform;
            var vg = go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vg.childAlignment = TextAnchor.MiddleCenter;
            vg.childForceExpandHeight = false;
            vg.childForceExpandWidth = false;
            vg.spacing = 4f;
            var cs = go.AddComponent<ContentSizeFitter>();
            cs.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cs.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Find or create CloseButtonContainer (holds the close button with a LayoutElement
        // so we can control the button size and prevent layout from collapsing it)
        Transform closeContainerT = rootT.Find("CloseButtonContainer");
        if (closeContainerT == null)
        {
            var go = new GameObject("CloseButtonContainer", typeof(RectTransform));
            go.transform.SetParent(rootT, false);
            closeContainerT = go.transform;
            // Give the container a LayoutElement with preferred size so the VerticalLayoutGroup
            // will allocate adequate space for the button.
            try
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = 160f;
                le.preferredHeight = 40f;
            }
            catch { }
        }

        // Reparent count texts into CountsContainer in desired order
        if (justText != null) ReparentToContainer(justText.transform, countsT);
        if (greatText != null) ReparentToContainer(greatText.transform, countsT);
        if (goodText != null) ReparentToContainer(goodText.transform, countsT);
        if (missText != null) ReparentToContainer(missText.transform, countsT);
        EnsureTimingText(ref fastText, countsT, "Fast: 0");
        EnsureTimingText(ref lateText, countsT, "Late: 0");


        // Reparent score text into ScoreContainer
        if (scoreText != null) ReparentToContainer(scoreText.transform, scoreT);

        // Optionally place closeButton inside the CloseButtonContainer so layout gives it space
        if (closeButton != null)
        {
            ReparentToContainer(closeButton.transform, closeContainerT ?? rootT);
            var rt = closeButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                // Make the button stretch to fill the container. The container's LayoutElement
                // will control the size, so set anchors to stretch and zero sizeDelta.
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }
            // Ensure the button label won't wrap vertically and is centered
            try
            {
                var tmp = closeButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (tmp != null)
                {
                    // use the newer API to disable wrapping
                    try { tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap; } catch { }
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.overflowMode = TMPro.TextOverflowModes.Truncate;
                }
                else
                {
                    var legacy = closeButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (legacy != null) { legacy.alignment = TextAnchor.MiddleCenter; }
                }
            }
            catch { }
        }

        // Ensure TMP alignment is centered for each text
        CenterTMP(justText);
        CenterTMP(greatText);
        CenterTMP(goodText);
        CenterTMP(missText);
        CenterTMP(fastText);
        CenterTMP(lateText);
        CenterTMP(scoreText);
    }

    private void ReparentToContainer(Transform child, Transform container)
    {
        if (child == null || container == null) return;
        child.SetParent(container, false);
        var rt = child as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
        // Add LayoutElement so VerticalLayoutGroup can size it based on preferred size.
        // IMPORTANT: do NOT add a LayoutElement blindly for TextMeshProUGUI or Button
        // because adding a LayoutElement with no preferred sizes can override the
        // child's intrinsic preferred size and collapse it to zero (which makes
        // buttons appear as tiny dots or texts invisible). Only add a LayoutElement
        // for other children that truly need it.
        var le = child.GetComponent<LayoutElement>();
        if (le == null && child.GetComponent<TextMeshProUGUI>() == null && child.GetComponent<Button>() == null)
            le = child.gameObject.AddComponent<LayoutElement>();
        // leave preferred sizes unset so layout uses the text's preferred sizes for TMP
    }

    private void CenterTMP(TextMeshProUGUI t)
    {
        if (t == null) return;
        t.alignment = TMPro.TextAlignmentOptions.Center;
    }

    private void EnsureTimingText(ref TextMeshProUGUI target, Transform container, string defaultLabel)
    {
        if (container == null)
        {
            return;
        }

        if (target == null)
        {
            var safeName = defaultLabel.Replace(":", string.Empty).Replace(" ", string.Empty);
            var go = new GameObject(string.IsNullOrEmpty(safeName) ? "TimingText" : safeName, typeof(RectTransform));
            go.transform.SetParent(container, false);
            target = go.AddComponent<TextMeshProUGUI>();
            target.text = defaultLabel;

            if (justText != null)
            {
                try
                {
                    target.font = justText.font;
                    target.fontSize = justText.fontSize;
                    target.enableAutoSizing = justText.enableAutoSizing;
                    target.color = justText.color;
                }
                catch { }
            }
            else
            {
                target.fontSize = 36f;
                target.color = Color.white;
            }
        }

        ReparentToContainer(target.transform, container);
    }

    public void ShowResults()
    {
        if (panel != null) panel.SetActive(true);

        // Get stats from JudgmentManager
        var jm = JudgmentManager.Instance;
        if (jm == null)
        {
            BuildLogger.LogWarning("ResultsScreen: JudgmentManager not found");
            SetTexts(0,0,0,0,0,0,0);
            return;
        }

        try { jm.FlushPendingJudgments(); } catch { }

        var stats = jm.GetStatsWithCombo();
        var timing = jm.GetTimingStats();
        int just = stats.perfect;
        int great = stats.great;
        int good = stats.good;
        int miss = stats.miss;
        int finalScore = stats.score;
        int fast = timing.fast;
        int late = timing.late;

        SetTexts(just, great, good, miss, finalScore, fast, late);
    }

    private void SetTexts(int just, int great, int good, int miss, int score, int fast, int late)
    {
        if (justText != null) justText.text = $"Just: {just}";
        if (greatText != null) greatText.text = $"Great: {great}";
        if (goodText != null) goodText.text = $"Good: {good}";
        if (missText != null) missText.text = $"Miss: {miss}";
        if (fastText != null) fastText.text = $"Fast: {fast}";
        if (lateText != null) lateText.text = $"Late: {late}";
        if (scoreText != null) scoreText.text = $"Score: {score:N0}";
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
