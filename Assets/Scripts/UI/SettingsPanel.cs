using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsPanel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject settingsPanel;
    public Button settingsButton;
    public Button closeButton;
    public Button saveButton;
    public Button resetButton;

    [Header("Audio Settings")]
    public Slider hitSoundVolumeSlider;
    public TextMeshProUGUI hitSoundVolumeText;
    public Slider musicVolumeSlider;
    public TextMeshProUGUI musicVolumeText;

    [Header("Gameplay Settings")]
    public Slider speedSlider;
    public TextMeshProUGUI speedText;
    public Slider delaySlider;
    public TextMeshProUGUI delayText;
    
    [Header("Judge Popup Z")]
    public Slider judgePopupZSlider;
    public TextMeshProUGUI judgePopupZText;
    
    [Header("Judgment Offset (ms)")]
    public TMP_InputField judgmentOffsetInput;
    public TextMeshProUGUI judgmentOffsetText;

    [Header("Video Background")]
    public Slider trackVideoDimmerSlider;
    public TextMeshProUGUI trackVideoDimmerText;

    private SettingsManager settingsManager;

    void Awake()
    {
    //Debug.Log("SettingsPanel Awake() called");
    }

    void Start()
    {
        settingsManager = SettingsManager.Instance;
        
        // Initialize UI
        SetupUI();
        LoadSettingsToUI();
        
        // Hide panel initially
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
        
        // Debug log to verify setup
            //Debug.Log($"SettingsPanel initialized. Settings button: {(settingsButton != null ? "Connected" : "Missing")}");
    }

    void SetupUI()
    {
        // Setup button events with debug logging
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners(); // Clear existing listeners
            settingsButton.onClick.AddListener(ShowSettings);
            //Debug.Log("Settings button event configured");
        }
        else
        {
            //Debug.LogWarning("SettingsPanel: Settings button is not assigned!");
        }
        
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HideSettings);
            //Debug.Log("Close button event configured");
        }
        else
        {
            //Debug.LogWarning("SettingsPanel: Close button is not assigned!");
        }
        
        if (saveButton != null)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(SaveSettings);
        }
        
        if (resetButton != null)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(ResetSettings);
        }

        // Setup slider events
        if (hitSoundVolumeSlider != null)
        {
            hitSoundVolumeSlider.onValueChanged.AddListener(OnHitSoundVolumeChanged);
        }
        
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }
        
        if (speedSlider != null)
        {
            // Ensure runtime slider max allows speeds up to 2000 (in case Inspector still set lower)
            speedSlider.maxValue = 2000f;
            speedSlider.onValueChanged.AddListener(OnSpeedChanged);
        }
        
        if (delaySlider != null)
        {
            delaySlider.onValueChanged.AddListener(OnDelayChanged);
        }

        if (judgePopupZSlider != null)
        {
            // reasonable default range; user can change in Inspector
            judgePopupZSlider.minValue = -5f;
            judgePopupZSlider.maxValue = 5f;
            judgePopupZSlider.onValueChanged.AddListener(OnJudgePopupZChanged);
        }

        if (judgmentOffsetInput != null)
        {
            // Prefer decimal number input for judgement offset
            try { judgmentOffsetInput.contentType = TMPro.TMP_InputField.ContentType.DecimalNumber; } catch { }
            judgmentOffsetInput.onEndEdit.AddListener(OnJudgmentOffsetInputChanged);
        }

        if (trackVideoDimmerSlider != null)
        {
            trackVideoDimmerSlider.minValue = 0f;
            trackVideoDimmerSlider.maxValue = 1f;
            trackVideoDimmerSlider.onValueChanged.AddListener(OnTrackVideoDimmerChanged);
        }
    }

    void LoadSettingsToUI()
    {
        if (settingsManager == null) return;

        // Load audio settings
        if (hitSoundVolumeSlider != null)
        {
            hitSoundVolumeSlider.value = settingsManager.HitSoundVolume;
            UpdateHitSoundVolumeText(settingsManager.HitSoundVolume);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = settingsManager.MusicVolume;
            UpdateMusicVolumeText(settingsManager.MusicVolume);
        }

        // Load gameplay settings
        if (speedSlider != null)
        {
            // Guard: ensure slider range allows up to 2000
            speedSlider.maxValue = 2000f;
            speedSlider.value = settingsManager.DefaultSpeed;
            UpdateSpeedText(settingsManager.DefaultSpeed);
        }

        if (delaySlider != null)
        {
            delaySlider.value = settingsManager.GameStartDelay;
            UpdateDelayText(settingsManager.GameStartDelay);
        }

        if (judgePopupZSlider != null)
        {
            judgePopupZSlider.value = settingsManager != null ? settingsManager.JudgePopupZOffset : 0f;
            UpdateJudgePopupZText(judgePopupZSlider.value);
        }

        if (judgmentOffsetInput != null)
        {
            float v = settingsManager != null ? settingsManager.JudgmentOffsetMs : 0f;
            judgmentOffsetInput.text = v.ToString("F0");
            UpdateJudgmentOffsetText(v);
        }

        if (trackVideoDimmerSlider != null)
        {
            float dimmer = settingsManager != null ? settingsManager.TrackVideoDimmer : 0f;
            trackVideoDimmerSlider.value = dimmer;
            UpdateTrackVideoDimmerText(dimmer);
        }
    }

    public void ShowSettings()
    {
            //Debug.Log("ShowSettings called!");
        
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadSettingsToUI(); // Refresh UI with current settings
            //Debug.Log("Settings panel shown");
        }
        else
        {
            //Debug.LogError("Settings panel is null!");
        }
    }

    public void HideSettings()
    {
        // Apply settings when the panel is closed to ensure immediate propagation
        if (settingsManager != null)
        {
            settingsManager.ApplySettings();
            //Debug.Log("SettingsPanel: Applied settings on close.");
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    public void SaveSettings()
    {
        if (settingsManager != null)
        {
            settingsManager.SaveSettings();
            settingsManager.ApplySettings();
        }
    }

    public void ResetSettings()
    {
        if (settingsManager != null)
        {
            // Reset to default values
            settingsManager.gameSettings = new GameSettings();
            LoadSettingsToUI();
            settingsManager.ApplySettings();
        }
    }

    // Slider event handlers
    void OnHitSoundVolumeChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetHitSoundVolume(value);
            UpdateHitSoundVolumeText(value);
        }
    }

    void OnMusicVolumeChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetMusicVolume(value);
            UpdateMusicVolumeText(value);
        }
    }

    void OnSpeedChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetDefaultSpeed(value);
            UpdateSpeedText(value);
        }
    }

    void OnDelayChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetGameStartDelay(value);
            UpdateDelayText(value);
        }
    }

    void OnJudgePopupZChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetJudgePopupZOffset(value);
            UpdateJudgePopupZText(value);
            // Apply immediately so popup manager receives the update
            settingsManager.ApplySettings();
        }
    }

    void OnJudgmentOffsetInputChanged(string text)
    {
        if (settingsManager != null)
        {
            float v = settingsManager.JudgmentOffsetMs;
            if (!float.TryParse(text, out v))
            {
                // if parse fails, keep previous value
                v = settingsManager.JudgmentOffsetMs;
            }
            settingsManager.SetJudgmentOffsetMs(v);
            UpdateJudgmentOffsetText(v);
        }
    }

    void OnTrackVideoDimmerChanged(float value)
    {
        if (settingsManager != null)
        {
            settingsManager.SetTrackVideoDimmer(value);
            UpdateTrackVideoDimmerText(value);
        }
    }

    // Text update methods
    void UpdateHitSoundVolumeText(float value)
    {
        if (hitSoundVolumeText != null)
        {
            hitSoundVolumeText.text = $"打擊聲音量: {value:P0}";
        }
    }

    void UpdateMusicVolumeText(float value)
    {
        if (musicVolumeText != null)
        {
            musicVolumeText.text = $"音樂音量: {value:P0}";
        }
    }

    void UpdateSpeedText(float value)
    {
        if (speedText != null)
        {
            speedText.text = $"預設速度: {value:F0}";
        }
    }

    void UpdateDelayText(float value)
    {
        if (delayText != null)
        {
            delayText.text = $"遊戲延遲: {value:F1}秒";
        }
    }

    void UpdateJudgePopupZText(float value)
    {
        if (judgePopupZText != null)
        {
            judgePopupZText.text = $"判定 popup Z 偏移: {value:F2}";
        }
    }

    void UpdateJudgmentOffsetText(float value)
    {
        if (judgmentOffsetText != null)
        {
            judgmentOffsetText.text = $"判定補償: {value:F0} ms";
        }
    }

    void UpdateTrackVideoDimmerText(float value)
    {
        if (trackVideoDimmerText != null)
        {
            trackVideoDimmerText.text = $"影片時 Track 遮罩: {value:F2}";
        }
    }
}