using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using TMPro;

/// <summary>
/// Backup utility to pause/resume the entire gameplay (freeze game time).
/// This will call Conductor.Pause()/Resume() and disable common spawners to halt updates.
/// Use only as a global pause helper (e.g., for editor/testing or as a fallback).
/// </summary>
public class GamePauseManager : MonoBehaviour
{
    public static GamePauseManager Instance { get; private set; }

    [Header("Pause UI")]
    [Tooltip("Optional Image to use as the pause trigger. If assigned, it will be made clickable.")]
    public Image triggerImage;
    [Tooltip("Optional Canvas to parent the overlay. If null, the script will find a Canvas in scene.")]
    public Canvas parentCanvas;
    [Tooltip("If false, GamePauseManager will not create the on-screen overlay UI and will only pause/resume via input triggers.")]
    public bool showOverlayUI = true;

    private GameObject _overlayRoot;
    private TextMeshProUGUI _countdownLabel;
    private Coroutine _countdownCoroutine;
    private CoroutineRunner _overlayRunner;
    private List<AudioSource> _pausedAudioSources = new List<AudioSource>();

    private bool _noteSpawnerEnabled;
    private bool _beatSpawnerEnabled;
    // keep references to the specific spawners we disabled so we can restore them
    private NoteSpawner _noteSpawnerRef;
    private BeatLineSpawner _beatSpawnerRef;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);

        if (triggerImage != null)
        {
            var go = triggerImage.gameObject;
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            triggerImage.raycastTarget = true;
            btn.interactable = true;
            btn.targetGraphic = triggerImage;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => ShowPauseOverlay());
        }
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        try { if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ShowPauseOverlay(); }
        catch { }
#else
        try { if (Input.GetMouseButtonDown(1)) ShowPauseOverlay(); }
        catch { }
#endif
    }

    public void PauseGame(Conductor conductor)
    {
        if (conductor == null) return;
        try
        {
            var ns = FindObjectOfType<NoteSpawner>();
            var bs = FindObjectOfType<BeatLineSpawner>();
            _noteSpawnerRef = ns;
            _beatSpawnerRef = bs;
            if (ns != null) { _noteSpawnerEnabled = ns.enabled; ns.enabled = false; Debug.Log("GamePauseManager: Disabled NoteSpawner"); }
            if (bs != null) { _beatSpawnerEnabled = bs.enabled; bs.enabled = false; Debug.Log("GamePauseManager: Disabled BeatLineSpawner"); }
        }
        catch { }

        try
        {
            _pausedAudioSources.Clear();
            var allAudio = FindObjectsOfType<AudioSource>();
            foreach (var a in allAudio)
            {
                try { if (a != null && a.isPlaying) { a.Pause(); _pausedAudioSources.Add(a); } }
                catch { }
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to pause audio sources: " + ex); }

        try { conductor.Pause(); } catch (Exception ex) { Debug.LogWarning("GamePauseManager: Conductor.Pause failed: " + ex); }
    }

    public void ResumeGame(Conductor conductor)
    {
        Debug.Log("GamePauseManager: ResumeGame called");
        if (conductor == null)
        {
            Debug.LogWarning("GamePauseManager: No Conductor found when resuming; still restoring audio and spawners.");
        }
        else
        {
            try { conductor.Resume(); } catch (Exception ex) { Debug.LogWarning("GamePauseManager: Conductor.Resume failed: " + ex); }
        }

        try
        {
            int resumed = 0;
            foreach (var a in _pausedAudioSources)
            {
                try { if (a != null) { a.UnPause(); resumed++; } }
                catch { }
            }
            _pausedAudioSources.Clear();
            Debug.Log($"GamePauseManager: Resumed {resumed} AudioSource(s)");
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to resume audio sources: " + ex); }

        try
        {
            // Prefer restoring the exact instances we disabled earlier
            if (_noteSpawnerRef != null)
            {
                try { _noteSpawnerRef.enabled = _noteSpawnerEnabled; Debug.Log("GamePauseManager: Restored NoteSpawner (ref)"); }
                catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore NoteSpawner ref: " + ex); }
            }
            else
            {
                var ns2 = FindObjectOfType<NoteSpawner>();
                if (ns2 != null) { ns2.enabled = true; Debug.Log("GamePauseManager: Restored NoteSpawner (found) to enabled"); }
                else Debug.LogWarning("GamePauseManager: No NoteSpawner found to restore");
            }

            if (_beatSpawnerRef != null)
            {
                try { _beatSpawnerRef.enabled = _beatSpawnerEnabled; Debug.Log("GamePauseManager: Restored BeatLineSpawner (ref)"); }
                catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore BeatLineSpawner ref: " + ex); }
            }
            else
            {
                var bs2 = FindObjectOfType<BeatLineSpawner>();
                if (bs2 != null) { bs2.enabled = true; Debug.Log("GamePauseManager: Restored BeatLineSpawner (found) to enabled"); }
                else Debug.LogWarning("GamePauseManager: No BeatLineSpawner found to restore");
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore spawners: " + ex); }
    }

    [ContextMenu("ShowPauseOverlay")]
    public void ShowPauseOverlay()
    {
        var conductor = FindObjectOfType<Conductor>();
        PauseGame(conductor);

        if (!showOverlayUI) return;
        if (_overlayRoot != null) return;

        Canvas canvas = parentCanvas ?? FindObjectOfType<Canvas>();
        if (canvas == null) { Debug.LogWarning("GamePauseManager: No Canvas found for overlay."); return; }

        _overlayRoot = new GameObject("PauseOverlay", typeof(RectTransform));
        _overlayRoot.transform.SetParent(canvas.transform, false);
        var rt = _overlayRoot.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var img = _overlayRoot.AddComponent<Image>(); img.color = new Color(0f, 0f, 0f, 0.6f); img.raycastTarget = true;
        var overlayCanvas = _overlayRoot.AddComponent<Canvas>(); overlayCanvas.overrideSorting = true; overlayCanvas.sortingOrder = 9999;
        _overlayRoot.AddComponent<CanvasScaler>(); _overlayRoot.AddComponent<GraphicRaycaster>();
        _overlayRoot.transform.SetAsLastSibling();

        var container = new GameObject("PauseButtons", typeof(RectTransform));
        container.transform.SetParent(_overlayRoot.transform, false);
        var crt = container.GetComponent<RectTransform>(); crt.sizeDelta = new Vector2(360, 140);
        crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f); crt.anchoredPosition = Vector2.zero;
        var containerImg = container.AddComponent<Image>(); containerImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f); containerImg.raycastTarget = true;
        var vLayout = container.AddComponent<VerticalLayoutGroup>(); vLayout.childControlHeight = true; vLayout.childControlWidth = true; vLayout.spacing = 12; vLayout.padding = new RectOffset(12,12,12,12);
        var csf = container.AddComponent<ContentSizeFitter>(); csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var left = CreateButtonStyled("Reselect", container.transform);
        left.onClick.AddListener(() => { try { GameManager.Instance?.ReselectSong(); } catch { } ; StartOverlayCoroutine(WaitThenHideOverlay(0.15f)); });

        var middle = CreateButtonStyled("Cancel", container.transform);
        middle.onClick.AddListener(() => { StartCountdown(3); });

        var right = CreateButtonStyled("Restart", container.transform);
        right.onClick.AddListener(() => { try { GameManager.Instance?.RestartSong(); } catch { } ; StartOverlayCoroutine(WaitThenHideOverlay(0.15f)); });

        var closeBtn = _overlayRoot.AddComponent<Button>(); closeBtn.onClick.RemoveAllListeners(); closeBtn.onClick.AddListener(() => HidePauseOverlay());
        var cg = _overlayRoot.AddComponent<CanvasGroup>(); cg.interactable = true; cg.blocksRaycasts = true; cg.ignoreParentGroups = true;

        _overlayRunner = _overlayRoot.AddComponent<CoroutineRunner>();

        var countdownGO = new GameObject("CountdownLabel", typeof(RectTransform)); countdownGO.transform.SetParent(container.transform, false);
        var clrt = countdownGO.GetComponent<RectTransform>(); clrt.sizeDelta = new Vector2(0, 28);
        _countdownLabel = countdownGO.AddComponent<TextMeshProUGUI>(); _countdownLabel.alignment = TextAlignmentOptions.Center; _countdownLabel.fontSize = 20; _countdownLabel.color = Color.white; _countdownLabel.text = string.Empty; _countdownLabel.gameObject.SetActive(false);
    }

    private Button CreateButtonStyled(string label, Transform parent)
    {
        var go = new GameObject("Btn_" + (label ?? "btn"), typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(0, 44);
        var btn = go.AddComponent<Button>(); var bimg = go.AddComponent<Image>(); bimg.color = new Color(0.2f, 0.2f, 0.2f, 1f); bimg.raycastTarget = true;
        var le = go.AddComponent<LayoutElement>(); le.preferredHeight = 44f; le.preferredWidth = 280f; le.minWidth = 160f; le.flexibleWidth = 1f;
        var lblGO = new GameObject("Label"); lblGO.transform.SetParent(go.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>(); lbl.text = label ?? string.Empty; lbl.alignment = TextAlignmentOptions.Center; lbl.fontSize = 22; lbl.color = Color.white; lbl.enableWordWrapping = false; lbl.richText = false;
        var lblRT = lblGO.GetComponent<RectTransform>(); lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
        return btn;
    }

    public void HidePauseOverlay()
    {
        Debug.Log("GamePauseManager: HidePauseOverlay called");
        if (_overlayRoot != null)
        {
            if (_countdownCoroutine != null)
            {
                try { StopOverlayCoroutine(_countdownCoroutine); } catch { }
                _countdownCoroutine = null;
            }
            Destroy(_overlayRoot);
            _overlayRoot = null;
            _overlayRunner = null;
        }
        var conductor = FindObjectOfType<Conductor>();
        ResumeGame(conductor);
    }

    private System.Collections.IEnumerator WaitThenHideOverlay(float seconds)
    {
        Debug.Log($"GamePauseManager: WaitThenHideOverlay starting wait {seconds}s");
        yield return new WaitForSecondsRealtime(seconds);
        HidePauseOverlay();
    }

    private System.Collections.IEnumerator StartResumeCountdown(int seconds)
    {
        if (_countdownLabel == null) yield break;
        _countdownLabel.gameObject.SetActive(true);
        for (int i = seconds; i > 0; i--)
        {
            _countdownLabel.text = $"Resuming in {i}...";
            yield return new WaitForSecondsRealtime(1f);
        }
        _countdownLabel.text = "";
        _countdownLabel.gameObject.SetActive(false);
        _countdownCoroutine = null;
        HidePauseOverlay();
    }

    private void StartCountdown(int seconds)
    {
        try
        {
            // If the overlay runner exists but its GameObject is inactive, starting coroutines on it will fail.
            // Prefer the runner only when it's active; otherwise fall back to this MonoBehaviour.
            var host = GetActiveCoroutineHost();
            if (_countdownCoroutine != null) host.StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = host.StartCoroutine(StartResumeCountdown(seconds));
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to start countdown: " + ex); }
    }

    private Coroutine StartOverlayCoroutine(System.Collections.IEnumerator routine)
    {
        var host = GetActiveCoroutineHost();
        return host.StartCoroutine(routine);
    }

    private void StopOverlayCoroutine(Coroutine c)
    {
        if (c == null) return;
        var host = GetActiveCoroutineHost();
        host.StopCoroutine(c);
    }

    // Returns an active MonoBehaviour to host coroutines. Preference order:
    // 1) overlay runner (if active), 2) persistent host kept alive via DontDestroyOnLoad.
    private MonoBehaviour GetActiveCoroutineHost()
    {
        if (_overlayRunner != null && _overlayRunner.gameObject.activeInHierarchy) return _overlayRunner;
        if (_persistentHost == null)
        {
            var existing = GameObject.Find("GamePauseManager_CoroutineHost");
            if (existing == null)
            {
                existing = new GameObject("GamePauseManager_CoroutineHost");
                UnityEngine.Object.DontDestroyOnLoad(existing);
            }
            _persistentHost = existing.GetComponent<PersistentCoroutineHost>() ?? existing.AddComponent<PersistentCoroutineHost>();
        }
        return _persistentHost;
    }

    // lightweight persistent host
    private static PersistentCoroutineHost _persistentHost;
    private class PersistentCoroutineHost : MonoBehaviour { }

    // Lightweight runner used to host coroutines on the overlay GameObject
    private class CoroutineRunner : MonoBehaviour { }
}
