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
    // Track last applied/displayed values to avoid redundant updates that may trigger work
    private float lastAppliedSpeed = float.NaN;
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

        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.NoteSpawner != null)
            {
                currentSpeed = GameManager.Instance.NoteSpawner.speed;
            }
            else if (GameManager.Instance.BeatLineSpawner != null)
            {
                currentSpeed = GameManager.Instance.BeatLineSpawner.Speed;
            }
        }

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
        currentSpeed += 10f;
        UpdateSpeedDependencies();
        UpdateSpeedUI();
    }

    private void DecreaseSpeed()
    {
        currentSpeed = Mathf.Max(1f, currentSpeed - 10f);
        UpdateSpeedDependencies();
        UpdateSpeedUI();
    }

    private void UpdateSpeedDependencies()
    {
        if (GameManager.Instance != null)
        {
            // Only write to spawners when the value actually changed to avoid triggering
            // potential expensive recomputation inside their setters or related listeners.
            const float eps = 0.0001f;
            if (GameManager.Instance.NoteSpawner != null)
            {
                if (Mathf.Abs(GameManager.Instance.NoteSpawner.speed - currentSpeed) > eps)
                {
                    GameManager.Instance.NoteSpawner.speed = currentSpeed;
                }
            }
            if (GameManager.Instance.BeatLineSpawner != null)
            {
                // Some spawners may expose a property; read current value then compare.
                try
                {
                    float currentBeatSpeed = GameManager.Instance.BeatLineSpawner.Speed;
                    if (Mathf.Abs(currentBeatSpeed - currentSpeed) > eps)
                    {
                        GameManager.Instance.BeatLineSpawner.Speed = currentSpeed;
                    }
                }
                catch
                {
                    // In case BeatLineSpawner.Speed getter/setter misbehaves, fallback to set directly
                    GameManager.Instance.BeatLineSpawner.Speed = currentSpeed;
                }
            }
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
        UpdateSpeedUI();
        ApplyCurrentSpeedToSpawners();
    }
}
