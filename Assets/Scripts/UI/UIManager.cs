using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Speed Control")]
    public Button speedUpButton;
    public Button speedDownButton;
    public TextMeshProUGUI speedText;
    private float currentSpeed = 30f; // Default speed changed to match SettingsManager
    /// <summary>currentSpeed 已經是真正的設定值，不是欄位初始值 30f。</summary>
    private bool speedResolved;
    private string lastDisplayedSpeedText = "";

    [Header("End Buttons")]
    public Button restartButton;
    public Button reselectButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    void Start()
    {
        // 這四個是**選配**的：底下每一處使用都先做了 null 檢查，沒接上去就只是
        // 少一顆按鈕，不是故障。把選配講成警告，久了就沒有人看警告了。
        BuildLogger.Log("UIManager wiring: "
            + $"speedUp={(speedUpButton != null)} speedDown={(speedDownButton != null)} "
            + $"speedText={(speedText != null)} restart={(restartButton != null)}");

        // Add listeners for the buttons
        if (speedUpButton != null) speedUpButton.onClick.AddListener(IncreaseSpeed);
        if (speedDownButton != null) speedDownButton.onClick.AddListener(DecreaseSpeed);
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
        if (reselectButton != null) reselectButton.onClick.AddListener(ReselectSong);

        ResolvePlayerSpeed();

        UpdateSpeedDependencies();
        UpdateSpeedUI();
        // Keep restart/reselect always visible
        EnsureEndButtonsVisible();

        // Try to anchor buttons to the top-right by code as a safety net
        TryAnchorButtonTopRight(restartButton, new Vector2(40f, 40f));
        TryAnchorButtonTopRight(reselectButton, new Vector2(40f, 104f));
    }

    public float GetCurrentSpeed()
    {
        return currentSpeed;
    }

    private void IncreaseSpeed()
    {
        ResolvePlayerSpeed();
        speedResolved = true;
        currentSpeed += 10f;
        UpdateSpeedDependencies();
        UpdateSpeedUI();
    }

    private void DecreaseSpeed()
    {
        ResolvePlayerSpeed();
        speedResolved = true;
        currentSpeed = Mathf.Max(1f, currentSpeed - 10f);
        UpdateSpeedDependencies();
        UpdateSpeedUI();
    }

    /// <summary>
    /// 設定的速度套上這份譜的比例，得到真正送給 spawner 的值。
    /// </summary>
    /// <remarks>
    /// <c>currentSpeed</c> 永遠是玩家調的那個數（畫面上顯示的、±10 加減的也
    /// 是它），比例只在寫進 spawner 的那一刻乘上去。這樣換一首曲子時比例跟著
    /// 譜變，而玩家的設定不會被惄掉 —— 若反過來把比例寫回 <c>currentSpeed</c>，
    /// 每換一首就乘一次，幾首之後就飛了。
    /// </remarks>
    /// <summary>
    /// 把玩家的設定值拉進 <see cref="currentSpeed"/>。
    /// </summary>
    /// <remarks>
    /// **不要從 spawner 讀回來。** spawner 上的值已經乘過譜面比例，讀回來下一次
    /// 套比例就乘第二次。玩家的設定值只有 SettingsManager 一個來源。
    ///
    /// **但不能只在 Start() 拉一次。** 兩個都是 MonoBehaviour，執行順序沒有釘，
    /// SettingsManager 可能還沒 Awake。那時候拉不到就會把欄位初始值 30f 當成設定
    /// 值推給 spawner，等 ApplySettings 跑完再推一次 80 —— 畫面上就是流速突然跳一下。
    /// 所以拉不到就不標記完成，下一次再試。
    /// </remarks>
    private void ResolvePlayerSpeed()
    {
        if (speedResolved) return;
        var settings = SettingsManager.Instance;
        if (settings == null) return;
        currentSpeed = Mathf.Clamp(settings.DefaultSpeed, 10f, 2000f);
        speedResolved = true;
    }

    /// <summary>設定裡有沒有把「跟著密度走」打開。預設是關的。</summary>
    private static bool ScalingEnabled
    {
        get
        {
            var settings = SettingsManager.Instance;
            return settings != null && settings.DensityScaledScroll;
        }
    }

    private float ScrollSpeedForCurrentChart()
    {
        if (!ScalingEnabled) return currentSpeed;
        var game = GameManager.Instance;
        if (game == null) return currentSpeed;
        return ChartScrollSpeed.Resolve(game.CurrentChart, currentSpeed);
    }

    /// <summary>
    /// 把算好的流速推給兩個 spawner。
    /// </summary>
    /// <remarks>
    /// **要套比例而譜還沒載好就不推**（不套的話設定值本身就是最終值，早推晚推
    /// 都一樣）。比例是從 <c>CurrentChart</c> 算的，而這個函式有兩個
    /// 會在載譜之前跑的呼叫者：Start() 和 <c>SettingsManager.ApplySettings</c>。那時
    /// <c>CurrentChart</c> 不是 null（→ 比例 1，推原始設定值）就是**上一首的譜**
    /// （→ 推上一首的比例），等 GameManager 載完再推一次正確的。
    ///
    /// 而音符的 Z 是**每幀拿當下的 speed 重算**的
    /// （<c>targetZ = judgmentZ + timeToStart / 1000 * speed</c>），所以中途換值會讓整片
    /// 音符瞬間位移，再被平滑函式拉回來 —— 這就是「流速不穩定」的真正原因。
    /// 跳過這些早期的推送不會漏掉任何事：GameManager 在 <c>NoteSpawner.Initialize</c>
    /// 之後马上就會呼叫一次，而那之前根本沒有音符在落。
    /// </remarks>
    private void UpdateSpeedDependencies()
    {
        ResolvePlayerSpeed();
        if (GameManager.Instance != null
            && (!ScalingEnabled || GameManager.Instance.CurrentChart != null))
        {
            float applied = ScrollSpeedForCurrentChart();
            // Only write to spawners when the value actually changed to avoid triggering
            // potential expensive recomputation inside their setters or related listeners.
            const float eps = 0.0001f;
            if (GameManager.Instance.NoteSpawner != null)
            {
                if (Mathf.Abs(GameManager.Instance.NoteSpawner.speed - applied) > eps)
                {
                    GameManager.Instance.NoteSpawner.speed = applied;
                }
            }
            if (GameManager.Instance.BeatLineSpawner != null)
            {
                // Some spawners may expose a property; read current value then compare.
                try
                {
                    float currentBeatSpeed = GameManager.Instance.BeatLineSpawner.Speed;
                    if (Mathf.Abs(currentBeatSpeed - applied) > eps)
                    {
                        GameManager.Instance.BeatLineSpawner.Speed = applied;
                    }
                }
                catch
                {
                    // In case BeatLineSpawner.Speed getter/setter misbehaves, fallback to set directly
                    GameManager.Instance.BeatLineSpawner.Speed = applied;
                }
            }
            // 拋物線模式的生成距離是**絕對長度**，換算成秒數要用新的速度。
            try { SettingsManager.Instance?.ApplyArcSpawnDistance(); } catch { }
        }
    }

    public void ApplyCurrentSpeedToSpawners()
    {
        UpdateSpeedDependencies();
    }

    private void UpdateSpeedUI()
    {
        if (speedText != null)
        {
            // Avoid setting .text redundantly which can trigger TextMeshPro rebuilds
            string txt = currentSpeed.ToString("F1");
            if (!string.Equals(lastDisplayedSpeedText, txt))
            {
                speedText.text = txt;
                lastDisplayedSpeedText = txt;
            }
        }
    }

    public void ShowRestartButton()
    {
        EnsureEndButtonsVisible();
    }

    public void HideEndButtons()
    {
        // Requirement: Do NOT hide; keep them visible at all times
        EnsureEndButtonsVisible();
    }

    private void EnsureEndButtonsVisible()
    {
        if (restartButton != null) restartButton.gameObject.SetActive(true);
        if (reselectButton != null) reselectButton.gameObject.SetActive(true);
    }

    private void TryAnchorButtonTopRight(Button btn, Vector2 inset)
    {
        if (btn == null) return;

        var rt = btn.transform as RectTransform;
        if (rt == null) return;

        // Warn if any parent has a LayoutGroup that may override our positioning
        var layoutParents = rt.GetComponentsInParent<LayoutGroup>(true);
        if (layoutParents != null && layoutParents.Length > 0)
        {
            var parentName = layoutParents[0].gameObject.name;
            Debug.LogWarning($"UIManager: '{btn.name}' is inside a LayoutGroup ('{parentName}'). That group may override its anchored position. Consider moving it under the root Canvas or a dedicated TopRight container without layouts.");
        }

        // Ensure it is anchored to Top-Right
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        // Move inward by the inset
        rt.anchoredPosition = new Vector2(-inset.x, -inset.y);
    }

    private void RestartGame()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartSong();
        }
    }

    private void ReselectSong()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReselectSong();
        }
    }

    /// <summary>
    /// Sets the default speed from SettingsManager
    /// </summary>
    /// <param name="speed">Default speed value</param>
    public void SetDefaultSpeed(float speed)
    {
        currentSpeed = Mathf.Clamp(speed, 10f, 2000f);
        speedResolved = true;
        UpdateSpeedUI();
        ApplyCurrentSpeedToSpawners();
    }
}
