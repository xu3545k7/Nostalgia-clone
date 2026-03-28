using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class SettingPreviewSlot
{
    [Header("UI References")]
    public RectTransform root;
    public Image backgroundImage;
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI valueLabel;
    public Button selectButton;
}

[System.Serializable]
public class SettingItem
{
    public string title;
    public string displayName;
    public float currentValue;
    public float minValue;
    public float maxValue;
    public float stepSize = 1.0f;
    public string unit = "";
    public bool isPercentage = false;

    public string GetDisplayValue()
    {
        if (isPercentage)
        {
            return $"{(currentValue * 100):F0}{unit}";
        }
        return $"{currentValue:F1}{unit}";
    }
}

public class SongStyleSettingsPanel : MonoBehaviour
{
    [Header("Carousel Layout")]
    [SerializeField] private SettingPreviewSlot centerSlot;
    [SerializeField] private SettingPreviewSlot leftSlot;
    [SerializeField] private SettingPreviewSlot rightSlot;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private float centerScale = 1.0f;
    [SerializeField] private float sideScale = 0.8f;

    [Header("Value Control")]
    [SerializeField] private Button decreaseButton;
    [SerializeField] private Button increaseButton;
    [SerializeField] private TextMeshProUGUI currentSettingLabel;

    [Header("Panel Control")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button closeButton;
    
    [Header("Selection Panel Management")]
    [Tooltip("如果設定，會在開啟設定時自動隱藏選擇面板")]
    [SerializeField] private bool hideSelectionPanelWhenOpen = true;

    [Header("Settings Data")]
    [SerializeField] private SettingItem[] settingsItems = new SettingItem[]
    {
        new SettingItem { 
            title = "HitSoundVolume", 
            displayName = "打擊聲音量",
            currentValue = 0.3f, 
            minValue = 0f, 
            maxValue = 1f, 
            stepSize = 0.05f, 
            unit = "%",
            isPercentage = true
        },
        new SettingItem { 
            title = "MusicVolume", 
            displayName = "音樂音量",
            currentValue = 0.8f, 
            minValue = 0f, 
            maxValue = 1f, 
            stepSize = 0.05f, 
            unit = "%",
            isPercentage = true
        },
        new SettingItem { 
            title = "DefaultSpeed", 
            displayName = "預設速度",
            currentValue = 30f, 
            minValue = 10f, 
            maxValue = 2000f, 
            stepSize = 5f, 
            unit = ""
        },
        new SettingItem { 
            title = "StartDelay", 
            displayName = "開始延遲",
            currentValue = 3.0f, 
            minValue = 2.0f, 
            maxValue = 5.0f, 
            stepSize = 0.5f, 
            unit = "秒"
        }
    };

    private int currentIndex = 0;
    private bool carouselInitialized = false;

    private void Start()
    {
        LoadSettings();
        ConfigureCarouselControls();
        UpdateCarouselVisuals();
        
        // Hide panel initially
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    private void ConfigureCarouselControls()
    {
        if (carouselInitialized)
            return;

        // Main panel controls
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(ShowSettings);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HideSettings);
        }

        // Carousel navigation
        if (previousButton != null)
        {
            previousButton.onClick.RemoveAllListeners();
            previousButton.onClick.AddListener(PreviousSetting);
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(NextSetting);
        }

        // Value adjustment
        if (decreaseButton != null)
        {
            decreaseButton.onClick.RemoveAllListeners();
            decreaseButton.onClick.AddListener(DecreaseValue);
        }

        if (increaseButton != null)
        {
            increaseButton.onClick.RemoveAllListeners();
            increaseButton.onClick.AddListener(IncreaseValue);
        }

        // Configure center slot button for selection
        if (centerSlot != null && centerSlot.selectButton != null)
        {
            centerSlot.selectButton.onClick.RemoveAllListeners();
            centerSlot.selectButton.onClick.AddListener(() => {
                //Debug.Log($"Selected setting: {settingsItems[currentIndex].displayName}");
            });
        }

        carouselInitialized = true;
    }

    public void ShowSettings()
    {
    //Debug.Log("ShowSettings called!");
        
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            LoadSettings(); // Refresh from SettingsManager
            UpdateCarouselVisuals();
            
            // 隱藏選擇面板讓使用者專心設定
            if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
            {
                SongSelectionManager.Instance.HideSelectionPanelOnly();
                //Debug.Log("SongStyleSettingsPanel: Hidden selection panel for settings");
            }
        }
    }

    public void HideSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            
            // 恢復選擇面板顯示
            if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
            {
                SongSelectionManager.Instance.ShowSelectionPanelOnly();
                //Debug.Log("SongStyleSettingsPanel: Restored selection panel after settings");
            }
        }
    }

    public void PreviousSetting()
    {
        if (settingsItems.Length == 0) return;
        
        currentIndex = (currentIndex - 1 + settingsItems.Length) % settingsItems.Length;
        UpdateCarouselVisuals();
    }

    public void NextSetting()
    {
        if (settingsItems.Length == 0) return;
        
        currentIndex = (currentIndex + 1) % settingsItems.Length;
        UpdateCarouselVisuals();
    }

    public void DecreaseValue()
    {
        if (settingsItems.Length == 0) return;
        
        SettingItem current = settingsItems[currentIndex];
        current.currentValue = Mathf.Max(current.minValue, current.currentValue - current.stepSize);
        UpdateCarouselVisuals();
        ApplySetting(current);
    }

    public void IncreaseValue()
    {
        if (settingsItems.Length == 0) return;
        
        SettingItem current = settingsItems[currentIndex];
        current.currentValue = Mathf.Min(current.maxValue, current.currentValue + current.stepSize);
        UpdateCarouselVisuals();
        ApplySetting(current);
    }

    private void UpdateCarouselVisuals()
    {
        if (!isActiveAndEnabled)
            return;

        if (settingsItems.Length == 0)
        {
            UpdatePreviewSlot(centerSlot, null, true);
            UpdatePreviewSlot(leftSlot, null, false);
            UpdatePreviewSlot(rightSlot, null, false);
            if (currentSettingLabel != null)
            {
                currentSettingLabel.text = "No Settings";
            }
            return;
        }

        SettingItem centerOption = settingsItems[currentIndex];
        SettingItem leftOption = settingsItems[GetWrappedIndex(currentIndex - 1)];
        SettingItem rightOption = settingsItems[GetWrappedIndex(currentIndex + 1)];

        UpdatePreviewSlot(centerSlot, centerOption, true);
        UpdatePreviewSlot(leftSlot, leftOption, false);
        UpdatePreviewSlot(rightSlot, rightOption, false);
        UpdateDetailPanel(centerOption);
    }

    private void UpdatePreviewSlot(SettingPreviewSlot slot, SettingItem setting, bool isCenter)
    {
        if (slot == null || slot.root == null)
            return;

        bool hasSetting = setting != null;
        if (slot.root.gameObject.activeSelf != hasSetting)
        {
            slot.root.gameObject.SetActive(hasSetting);
        }

        if (!hasSetting)
            return;

        if (slot.titleLabel != null)
        {
            slot.titleLabel.text = setting.displayName;
        }

        if (slot.valueLabel != null)
        {
            slot.valueLabel.text = setting.GetDisplayValue();
        }

        // Apply different visual style for center vs side slots
        ApplySlotScale(slot.root, isCenter);
        
        // Apply different colors or alpha for center vs side
        if (slot.backgroundImage != null)
        {
            Color bgColor = slot.backgroundImage.color;
            bgColor.a = isCenter ? 1.0f : 0.7f;
            slot.backgroundImage.color = bgColor;
        }

        // Update text colors for better visibility
        if (slot.titleLabel != null)
        {
            Color titleColor = slot.titleLabel.color;
            titleColor.a = isCenter ? 1.0f : 0.8f;
            slot.titleLabel.color = titleColor;
        }

        if (slot.valueLabel != null)
        {
            Color valueColor = slot.valueLabel.color;
            valueColor.a = isCenter ? 1.0f : 0.8f;
            slot.valueLabel.color = valueColor;
        }
    }

    private void ApplySlotScale(RectTransform slotTransform, bool isCenter)
    {
        if (slotTransform == null)
            return;

        float targetScale = isCenter ? centerScale : sideScale;
        slotTransform.localScale = Vector3.one * targetScale;
    }

    private void UpdateDetailPanel(SettingItem setting)
    {
        if (currentSettingLabel != null)
        {
            currentSettingLabel.text = $"{setting.displayName}: {setting.GetDisplayValue()}";
        }
    }

    private int GetWrappedIndex(int index)
    {
        if (settingsItems.Length == 0) return 0;
        
        while (index < 0)
            index += settingsItems.Length;
        return index % settingsItems.Length;
    }

    private void ApplySetting(SettingItem setting)
    {
        var settingsManager = SettingsManager.Instance;
        if (settingsManager == null) return;

        switch (setting.title)
        {
            case "HitSoundVolume":
                settingsManager.SetHitSoundVolume(setting.currentValue);
                break;
            case "MusicVolume":
                settingsManager.SetMusicVolume(setting.currentValue);
                break;
            case "DefaultSpeed":
                settingsManager.SetDefaultSpeed(setting.currentValue);
                break;
            case "StartDelay":
                settingsManager.SetGameStartDelay(setting.currentValue);
                break;
        }

        SaveSettings();
    }

    private void LoadSettings()
    {
        var settingsManager = SettingsManager.Instance;
        if (settingsManager == null) return;

        foreach (var setting in settingsItems)
        {
            switch (setting.title)
            {
                case "HitSoundVolume":
                    setting.currentValue = settingsManager.HitSoundVolume;
                    break;
                case "MusicVolume":
                    setting.currentValue = settingsManager.MusicVolume;
                    break;
                case "DefaultSpeed":
                    setting.currentValue = settingsManager.DefaultSpeed;
                    break;
                case "StartDelay":
                    setting.currentValue = settingsManager.GameStartDelay;
                    break;
            }
        }
    }

    private void SaveSettings()
    {
        var settingsManager = SettingsManager.Instance;
        if (settingsManager != null)
        {
            settingsManager.SaveSettings();
            settingsManager.ApplySettings();
        }
    }
}