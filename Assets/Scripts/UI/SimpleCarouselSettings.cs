
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UIHelpers;

public class SimpleCarouselSettings : MonoBehaviour
{
    [System.Serializable]
    public class SettingData
    {
        public string title;
        public float value;
        public float min;
        public float max;
        public float step;
        public string unit;
        public bool isPercentage;

            // If true, the +/- buttons support fast repeat (hold to continuously change)
            public bool fastSelect = false;

        // If true, +/- will toggle between min/max (used for mode toggles)
        public bool isToggle;

        public string GetDisplayText()
        {
            if (isToggle)
            {
                return $"{title}: {(value >= (min + max) * 0.5f ? "開" : "關")}";
            }
            else if (isPercentage)
            {
                return $"{title}: {(value * 100):F0}{unit}";
            }
            else
            {
                if (step >= 1f) return $"{title}: {value:F0}{unit}";
                return $"{title}: {value:F1}{unit}";
            }
        }
    }

    [Header("UI References")]
    public GameObject settingsPanel;
    public Button settingsButton;
    public Button closeButton;
    
    [Header("Selection Panel Management")]
    [Tooltip("如果設定，會在開啟設定時自動隱藏選擇面板")]
    public bool hideSelectionPanelWhenOpen = true;

    [Header("Carousel Style")]
    public GameObject centerBox;  // 中央顯示框
    public GameObject leftBox;    // 左側顯示框  
    public GameObject rightBox;   // 右側顯示框
    public Button leftArrow;     // 左箭頭按鈕
    public Button rightArrow;    // 右箭頭按鈕

    [Header("Center Box Content")]
    public TextMeshProUGUI centerTitle;  // 中央標題
    public TextMeshProUGUI centerValue;  // 中央數值
    public Button minusButton;           // 減少按鈕
    public Button plusButton;            // 增加按鈕

    [Header("Side Boxes Content")]
    public TextMeshProUGUI leftTitle;    // 左側標題
    public TextMeshProUGUI leftValue;    // 左側數值
    public TextMeshProUGUI rightTitle;   // 右側標題
    public TextMeshProUGUI rightValue;   // 右側數值

    private SettingData[] settings;
    private int currentIndex = 0;
    // RepeatPress components for +/- buttons (cached so we can enable/disable per-setting)
    private RepeatPress minusRepeat = null;
    private RepeatPress plusRepeat = null;

    void Start()
    {
        InitializeSettings();
        SetupButtons();
        UpdateDisplay();
        
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    void InitializeSettings()
    {
        // Add judge popup Z and debug mode toggle
        settings = new SettingData[]
        {
            new SettingData { title = "輸入模式", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true }, // 0:鍵盤 1:MIDI
            new SettingData { title = "打擊聲音量", value = 0.3f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "音樂音量", value = 0.8f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "鋼琴音量", value = 0.8f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "預設速度", value = 30f, min = 10f, max = 2000f, step = 5f, unit = "" },
            new SettingData { title = "開始延遲", value = 3f, min = 2f, max = 5f, step = 0.5f, unit = "秒" },
            new SettingData { title = "判定顯示高度", value = 0f, min = -5f, max = 50f, step = 0.5f, unit = "" },
            new SettingData { title = "鏡頭相對判定線位置", value = 100.0f, min = -100f, max = 100.0f, step = 2.0f, unit = "" },
            new SettingData { title = "譜面斜率", value = 70f, min = 40f, max = 90f, step = 1f, unit = "°" },
            new SettingData { title = "除錯模式", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true },
            new SettingData { title = "判定補償", value = 0f, min = -500f, max = 500f, step = 1f, unit = "ms", fastSelect = true },
            new SettingData { title = "樂曲播放延遲", value = 0f, min = -3000f, max = 3000f, step = 1f, unit = "ms", fastSelect = true },
            new SettingData { title = "鋼琴音源", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true },
            new SettingData { title = "影片遮罩強度", value = 0.4f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true }
        };

        LoadFromSettingsManager();
    }

    void SetupButtons()
    {
        if (settingsButton != null)
            settingsButton.onClick.AddListener(ShowSettings);

        if (closeButton != null)
            closeButton.onClick.AddListener(HideSettings);

        if (leftArrow != null)
            leftArrow.onClick.AddListener(PreviousSetting);

        if (rightArrow != null)
            rightArrow.onClick.AddListener(NextSetting);

        if (minusButton != null)
        {
            minusButton.onClick.AddListener(DecreaseValue);
            // Attach RepeatPress component to enable hold-to-repeat behavior (we will enable per-setting)
            minusRepeat = minusButton.gameObject.GetComponent<RepeatPress>();
            if (minusRepeat == null) minusRepeat = minusButton.gameObject.AddComponent<RepeatPress>();
            minusRepeat.initialDelay = 0.45f;
            minusRepeat.repeatRate = 0.08f;
            if (minusRepeat.onRepeat == null) minusRepeat.onRepeat = new UnityEngine.Events.UnityEvent();
            // Avoid duplicate listeners in case SetupButtons called again
            minusRepeat.onRepeat.RemoveListener(DecreaseValue);
            minusRepeat.onRepeat.AddListener(DecreaseValue);
            minusRepeat.enabled = false; // default disabled; enabled in UpdateDisplay for fastSelect settings
        }

        if (plusButton != null)
        {
            plusButton.onClick.AddListener(IncreaseValue);
            plusRepeat = plusButton.gameObject.GetComponent<RepeatPress>();
            if (plusRepeat == null) plusRepeat = plusButton.gameObject.AddComponent<RepeatPress>();
            plusRepeat.initialDelay = 0.45f;
            plusRepeat.repeatRate = 0.08f;
            if (plusRepeat.onRepeat == null) plusRepeat.onRepeat = new UnityEngine.Events.UnityEvent();
            plusRepeat.onRepeat.RemoveListener(IncreaseValue);
            plusRepeat.onRepeat.AddListener(IncreaseValue);
            plusRepeat.enabled = false;
        }
    }

    public void ShowSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadFromSettingsManager();
            UpdateDisplay();
            
            // 只隱藏選擇面板，不啟動遊戲面板
            if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
            {
                SongSelectionManager.Instance.HideSelectionPanelOnly();
                //Debug.Log("SimpleCarouselSettings: Hidden selection panel for settings");
            }
        }
    }

    public void HideSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            
            // 只恢復選擇面板顯示，不影響遊戲面板
            if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
            {
                SongSelectionManager.Instance.ShowSelectionPanelOnly();
                //Debug.Log("SimpleCarouselSettings: Restored selection panel after settings");
            }
        }
    }

    public void PreviousSetting()
    {
        currentIndex = (currentIndex - 1 + settings.Length) % settings.Length;
        UpdateDisplay();
    }

    public void NextSetting()
    {
        currentIndex = (currentIndex + 1) % settings.Length;
        UpdateDisplay();
    }

    public void DecreaseValue()
    {
        var setting = settings[currentIndex];
        if (setting.isToggle)
        {
            setting.value = (Mathf.Approximately(setting.value, setting.min) ? setting.max : setting.min);
        }
        else
        {
            setting.value = Mathf.Max(setting.min, setting.value - setting.step);
        }
        UpdateDisplay();
        ApplyToSettingsManager();
    }

    public void IncreaseValue()
    {
        var setting = settings[currentIndex];
        if (setting.isToggle)
        {
            setting.value = (Mathf.Approximately(setting.value, setting.max) ? setting.min : setting.max);
        }
        else
        {
            setting.value = Mathf.Min(setting.max, setting.value + setting.step);
        }
        UpdateDisplay();
        ApplyToSettingsManager();
    }

    void UpdateDisplay()
    {
        // 更新中央框 (當前設定)
        var center = settings[currentIndex];
        if (centerTitle != null) centerTitle.text = center.title;
        if (centerValue != null) 
        {
            centerValue.text = FormatSettingValue(center);
        }

        // 更新左側框 (上一個設定)
        var leftIndex = (currentIndex - 1 + settings.Length) % settings.Length;
        var left = settings[leftIndex];
        if (leftTitle != null) leftTitle.text = left.title;
        if (leftValue != null)
        {
            leftValue.text = FormatSettingValue(left);
        }

        // 更新右側框 (下一個設定)
        var rightIndex = (currentIndex + 1) % settings.Length;
        var right = settings[rightIndex];
        if (rightTitle != null) rightTitle.text = right.title;
        if (rightValue != null)
        {
            rightValue.text = FormatSettingValue(right);
        }

        // 調整框的透明度和縮放 (模仿歌曲選擇的效果)
        if (centerBox != null) 
        {
            centerBox.transform.localScale = Vector3.one;
            SetBoxAlpha(centerBox, 1f);
        }
        if (leftBox != null)
        {
            leftBox.transform.localScale = Vector3.one * 0.8f;
            SetBoxAlpha(leftBox, 0.7f);
        }
        if (rightBox != null)
        {
            rightBox.transform.localScale = Vector3.one * 0.8f;
            SetBoxAlpha(rightBox, 0.7f);
        }

        // Enable or disable hold-to-repeat behavior based on current setting's fastSelect flag
        if (minusRepeat != null) minusRepeat.enabled = center.fastSelect;
        if (plusRepeat != null) plusRepeat.enabled = center.fastSelect;
    }

    void SetBoxAlpha(GameObject box, float alpha)
    {
        // 設定整個框的透明度
        var images = box.GetComponentsInChildren<Image>();
        foreach (var img in images)
        {
            var color = img.color;
            color.a = alpha;
            img.color = color;
        }

        var texts = box.GetComponentsInChildren<TextMeshProUGUI>();
        foreach (var text in texts)
        {
            var color = text.color;
            color.a = alpha;
            text.color = color;
        }
    }

    string FormatSettingValue(SettingData s)
    {
        if (s == null) return "";
        // 輸入模式自訂顯示
        if (currentIndex == 0)
        {
            return s.value >= 0.5f ? "MIDI" : "鍵盤";
        }
        if (s.isToggle) return (s.value >= (s.min + s.max) * 0.5f) ? "開" : "關";
        if (s.isPercentage) return $"{(s.value * 100):F0}{s.unit}";
        if (s.step >= 1f) return $"{s.value:F0}{s.unit}";
        return $"{s.value:F1}{s.unit}";
    }

    void LoadFromSettingsManager()
    {
        var manager = SettingsManager.Instance;
        if (manager == null) return;

        settings[0].value = manager.InputMode == InputModeType.MIDI ? 1f : 0f; // 輸入模式
        settings[1].value = manager.HitSoundVolume;      // 打擊聲音量
        settings[2].value = manager.MusicVolume;         // 音樂音量
        if (settings.Length > 3) settings[3].value = manager.PianoVolume;         // 鋼琴音量
        if (settings.Length > 4) settings[4].value = manager.DefaultSpeed;        // 預設速度
        if (settings.Length > 5) settings[5].value = manager.GameStartDelay;      // 開始延遲
        if (settings.Length > 6) settings[6].value = manager.JudgePopupZOffset;   // 判定 popup Z
        if (settings.Length > 7) settings[7].value = manager.CameraZ;             // 鏡頭 Z
        if (settings.Length > 8) settings[8].value = manager.CameraRotX;          // 鏡頭 X
        if (settings.Length > 9) settings[9].value = manager.DebugMode ? 1f : 0f; // 除錯模式
        if (settings.Length > 10) settings[10].value = manager.JudgmentOffsetMs;    // 判定補償
        if (settings.Length > 11) settings[11].value = manager.MusicPlaybackOffsetMs; // 樂曲播放延遲
        if (settings.Length > 12) settings[12].value = manager.EnablePianoPreview ? 1f : 0f; // 鋼琴音源預覽
        if (settings.Length > 13) settings[13].value = manager.TrackVideoDimmer; // 影片遮罩強度
    }

    void ApplyToSettingsManager()
    {
        var manager = SettingsManager.Instance;
        if (manager == null) return;

        switch (currentIndex)
        {
            case 0:
                manager.SetInputMode(settings[0].value >= 0.5f ? InputModeType.MIDI : InputModeType.Keyboard);
                break;
            case 1: manager.SetHitSoundVolume(settings[1].value); break;
            case 2:
                manager.SetMusicVolume(settings[2].value);
                SongSelectionManager.Instance?.RefreshPreviewAudio(false);
                break;
            case 3:
                manager.SetPianoVolume(settings[3].value);
                break;
            case 4: manager.SetDefaultSpeed(settings[4].value); break;
            case 5: manager.SetGameStartDelay(settings[5].value); break;
            case 6: manager.SetJudgePopupZOffset(settings[6].value); break;
            case 7: manager.SetCameraZ(settings[7].value); break;
            case 8: manager.SetCameraRotX(settings[8].value); break;
            case 9: manager.SetDebugMode(settings[9].value >= 0.5f); break;
            case 10: manager.SetJudgmentOffsetMs(settings[10].value); break;
            case 11: manager.SetMusicPlaybackOffsetMs(settings[11].value); break;
            case 12:
                manager.SetEnablePianoPreview(settings[12].value >= 0.5f);
                break;
            case 13:
                manager.SetTrackVideoDimmer(settings[13].value);
                break;
        }

        manager.SaveSettings();
        manager.ApplySettings();
    }
}