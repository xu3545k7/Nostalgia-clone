using UnityEngine;

[System.Serializable]
public enum InputModeType { Keyboard, MIDI }

[System.Serializable]
public class GameSettings
{
    [Header("Input Mode")]
    public InputModeType inputMode = InputModeType.Keyboard;

    [Header("Audio Settings")]
    [Range(0f, 1f)]
    public float hitSoundVolume = 1.0f;

    [Range(0f, 1f)]
    public float musicVolume = 0.5f;

    [Range(0f, 1f)]
    public float pianoVolume = 0.5f;

    [Header("Gameplay Settings")]
    [Range(10f, 2000f)]
    public float defaultSpeed = 80f;

    [Range(2f, 5f)]
    public float gameStartDelay = 2f;

    [Header("Gameplay - Judgment")]
    public bool judgmentMode = true; // when true, use manual judgment mode driven by button mappings
    // Z offset for judge popups (world units). Default 0 keeps popup at anchor Z.
    public float judgePopupZOffset = 0f;

    [Tooltip("Judgment offset in milliseconds. Positive = treat hits as earlier, Negative = treat hits as later.")]
    public float judgmentOffsetMs = 0f;

    [Tooltip("Music playback delay in milliseconds. Positive values delay the music, negative values start it earlier.")]
    public float musicPlaybackOffsetMs = 0f;

    [Header("Camera")]
    [Tooltip("World-space Z position for the main camera. Changing this will move the main camera along its Z axis.")]
    public float cameraZ = -20f;

    [Tooltip("Rotation X (degrees) for the main camera. Adjusts the camera's X Euler angle.")]
    public float cameraRotX = 50f;
    // Debug mode toggle exposed to UI
    public bool debugMode = false;

    [Tooltip("Enable layered piano preview playback on the song selection screen when available.")]
    public bool enablePianoPreview = true;

    [Header("Video Background")]
    [Tooltip("Black overlay intensity applied to the gameplay track when a background video is active. 0 = no overlay, 1 = fully black.")]
    [Range(0f, 1f)]
    public float trackVideoDimmer = 0.4f;
}

public class SettingsManager : MonoBehaviour
{
    // 取得目前輸入模式
    public InputModeType InputMode {
        get => gameSettings != null ? gameSettings.inputMode : InputModeType.Keyboard;
    }

    // 設定輸入模式
    public void SetInputMode(InputModeType mode)
    {
        if (gameSettings != null)
        {
            gameSettings.inputMode = mode;
        }
    }
    private static SettingsManager _instance;
    public static SettingsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SettingsManager>();
                if (_instance == null)
                {
                    GameObject settingsObj = new GameObject("SettingsManager");
                    _instance = settingsObj.AddComponent<SettingsManager>();
                    DontDestroyOnLoad(settingsObj);
                }
            }
            return _instance;
        }
    }

    [Header("Game Settings")]
    public GameSettings gameSettings = new GameSettings();

    private const string SETTINGS_KEY = "GameSettings";

    void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        // Ensure this is a root GameObject for DontDestroyOnLoad
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);

        // Load settings
        LoadSettings();
    }

    void Start()
    {
        // Apply loaded settings
        ApplySettings();
    }

    public void SaveSettings()
    {
        EnsureJudgmentFlagsConsistency();
        string json = JsonUtility.ToJson(gameSettings);
        PlayerPrefs.SetString(SETTINGS_KEY, json);
        PlayerPrefs.Save();
        //Debug.Log("Settings saved");
    }

    public void LoadSettings()
    {
        if (PlayerPrefs.HasKey(SETTINGS_KEY))
        {
            string json = PlayerPrefs.GetString(SETTINGS_KEY);
            gameSettings = JsonUtility.FromJson<GameSettings>(json);
            if (gameSettings != null && !json.Contains("enablePianoPreview"))
            {
                gameSettings.enablePianoPreview = true;
            }
            if (gameSettings != null && !json.Contains("pianoVolume"))
            {
                gameSettings.pianoVolume = gameSettings.musicVolume;
            }
            if (gameSettings != null && !json.Contains("trackVideoDimmer"))
            {
                gameSettings.trackVideoDimmer = 0.4f;
            }
            //Debug.Log("Settings loaded");
        }
        else
        {
            //Debug.Log("No saved settings found, using defaults");
        }

        // Guarantee we never leave both DebugMode and JudgmentMode disabled simultaneously
        if (EnsureJudgmentFlagsConsistency())
        {
            SaveSettings();
        }
    }

    private bool EnsureJudgmentFlagsConsistency()
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
            return true;
        }

        bool changed = false;

        // If both flags accidentally end up false, prefer normal judgment mode.
        if (!gameSettings.debugMode && !gameSettings.judgmentMode)
        {
            gameSettings.judgmentMode = true;
            changed = true;
        }
        // If both flags are true (legacy data), disable judgment mode when debug is active.
        else if (gameSettings.debugMode && gameSettings.judgmentMode)
        {
            gameSettings.judgmentMode = false;
            changed = true;
        }

        return changed;
    }

    public void ApplySettings()
    {
        // Apply music volume to Conductor (so it affects only the music playback, not hit sounds)
        var conductor = FindFirstObjectByType<Conductor>();
        if (conductor != null)
        {
            conductor.SetMusicVolume(gameSettings.musicVolume);
        }

        // Apply game start delay to GameManager
        var gmInstance = GameManager.Instance;
        if (gmInstance != null)
        {
            gmInstance.SetStartDelay(gameSettings.gameStartDelay);
            gmInstance.SetPianoLayerVolume(gameSettings.pianoVolume);
        }
        
        // Apply default speed to UIManager
        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null)
        {
            uiManager.SetDefaultSpeed(gameSettings.defaultSpeed);
        }

        // Apply track dimmer to GameBackgroundManager so track visuals update immediately.
        var backgroundManager = FindFirstObjectByType<GameBackgroundManager>();
        if (backgroundManager != null)
        {
            backgroundManager.ApplyTrackDimmerSetting(gameSettings.trackVideoDimmer);
        }
        
        //Debug.Log($"Settings applied - Hit: {gameSettings.hitSoundVolume}, Music: {gameSettings.musicVolume}, Speed: {gameSettings.defaultSpeed}, Delay: {gameSettings.gameStartDelay}");

        // Apply judge popup Z offset to SimpleJudgePopupManager if present
        var popupMgr = FindFirstObjectByType<SimpleJudgePopupManager>();
        if (popupMgr != null)
        {
            // Assign judgePopupZOffset via reflection if the field/property exists to avoid compile-time dependency
            var pmType = popupMgr.GetType();
            var fz = pmType.GetField("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fz != null)
            {
                fz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
            }
            else
            {
                var pz = pmType.GetProperty("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (pz != null && pz.CanWrite) pz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
            }

            // If a NoteSpawner exists, point the popup manager at it to allow anchor interpolation
            var ns = FindFirstObjectByType<NoteSpawner>();
            if (ns != null)
            {
                // Use reflection to assign spawnAnchorTransform if the field/property exists
                var f = pmType.GetField("spawnAnchorTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(popupMgr, ns.transform);
                }
                else
                {
                    var p = pmType.GetProperty("spawnAnchorTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p != null && p.CanWrite) p.SetValue(popupMgr, ns.transform);
                }
            }
        }

        // Apply camera settings to the main camera if available
        var cam = Camera.main;
        if (cam != null)
        {
            // Set camera Z while preserving x/y
            var p = cam.transform.position;
            p.z = gameSettings.cameraZ;
            cam.transform.position = p;

            // Set camera rotation X while preserving y/z
            var e = cam.transform.eulerAngles;
            e.x = gameSettings.cameraRotX;
            cam.transform.eulerAngles = e;
        }
    }

    // Judgment mode accessor
    public bool JudgmentMode => gameSettings != null ? gameSettings.judgmentMode : false;

    // Public accessors for other scripts
    public float HitSoundVolume => gameSettings.hitSoundVolume;
    public float MusicVolume => gameSettings.musicVolume;
    public float PianoVolume => gameSettings.pianoVolume;
    public float DefaultSpeed => gameSettings.defaultSpeed;
    public float GameStartDelay => gameSettings.gameStartDelay;
    public float TrackVideoDimmer => gameSettings != null ? gameSettings.trackVideoDimmer : 0f;

    // Setters with immediate application
    public void SetHitSoundVolume(float volume)
    {
        gameSettings.hitSoundVolume = Mathf.Clamp01(volume);
    }

    public void SetMusicVolume(float volume)
    {
        gameSettings.musicVolume = Mathf.Clamp01(volume);
        // Apply immediately to Conductor if present (affect music only)
        var conductor = FindFirstObjectByType<Conductor>();
        if (conductor != null)
        {
            conductor.SetMusicVolume(gameSettings.musicVolume);
        }
    }
    
    public void SetPianoVolume(float volume)
    {
        gameSettings.pianoVolume = Mathf.Clamp01(volume);
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SetPianoLayerVolume(gameSettings.pianoVolume);
        }
        var songSelection = SongSelectionManager.Instance;
        if (songSelection != null)
        {
            songSelection.RefreshPreviewAudio(false);
        }
    }

    public void SetDefaultSpeed(float speed)
    {
        gameSettings.defaultSpeed = Mathf.Clamp(speed, 10f, 2000f);
    }

    public void SetGameStartDelay(float delay)
    {
        gameSettings.gameStartDelay = Mathf.Clamp(delay, 2f, 5f);
    }

    public void SetTrackVideoDimmer(float value)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }
        gameSettings.trackVideoDimmer = Mathf.Clamp01(value);
        var backgroundManager = FindFirstObjectByType<GameBackgroundManager>();
        if (backgroundManager != null)
        {
            backgroundManager.ApplyTrackDimmerSetting(gameSettings.trackVideoDimmer);
        }
    }

    public void SetJudgePopupZOffset(float z)
    {
        gameSettings.judgePopupZOffset = z;
        var popupMgr = FindFirstObjectByType<SimpleJudgePopupManager>();
        if (popupMgr != null)
        {
            var pmType = popupMgr.GetType();
            var fz = pmType.GetField("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fz != null)
            {
                fz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
                Debug.Log($"SettingsManager: SetJudgePopupZOffset -> set field on popupMgr to {gameSettings.judgePopupZOffset}");
            }
            else
            {
                var pz = pmType.GetProperty("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (pz != null && pz.CanWrite) pz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
                Debug.Log($"SettingsManager: SetJudgePopupZOffset -> set property on popupMgr to {gameSettings.judgePopupZOffset}");
            }
        }
    }

    public float CameraZ => gameSettings.cameraZ;
    public float CameraRotX => gameSettings.cameraRotX;

    public void SetCameraZ(float z)
    {
        gameSettings.cameraZ = z;
        var cam = Camera.main;
        if (cam != null)
        {
            var p = cam.transform.position;
            p.z = gameSettings.cameraZ;
            cam.transform.position = p;
        }
    }

    public void SetCameraRotX(float rotX)
    {
        gameSettings.cameraRotX = rotX;
        var cam = Camera.main;
        if (cam != null)
        {
            var e = cam.transform.eulerAngles;
            e.x = gameSettings.cameraRotX;
            cam.transform.eulerAngles = e;
        }
    }

    public float JudgePopupZOffset => gameSettings.judgePopupZOffset;

    public float JudgmentOffsetMs => gameSettings != null ? gameSettings.judgmentOffsetMs : 0f;

    public float MusicPlaybackOffsetMs => gameSettings != null ? gameSettings.musicPlaybackOffsetMs : 0f;

    public bool EnablePianoPreview => gameSettings != null && gameSettings.enablePianoPreview;

    public void SetJudgmentOffsetMs(float ms)
    {
        gameSettings.judgmentOffsetMs = ms;
    }

    public void SetMusicPlaybackOffsetMs(float ms)
    {
        // allow a broad range but clamp to reasonable values (-3000ms .. 3000ms)
        gameSettings.musicPlaybackOffsetMs = Mathf.Clamp(ms, -3000f, 3000f);
    }

    public void SetEnablePianoPreview(bool enabled)
    {
        if (gameSettings.enablePianoPreview == enabled) return;

        gameSettings.enablePianoPreview = enabled;
        Debug.Log($"SettingsManager: Piano preview toggled {(enabled ? "ON" : "OFF")}");

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.OnPianoPreviewToggleChanged(enabled);
        }

        // Refresh song selection preview audio to reflect the new layered state immediately.
        var songSelection = SongSelectionManager.Instance;
        if (songSelection != null)
        {
            Debug.Log("SettingsManager: Notifying SongSelectionManager to refresh preview audio.");
            songSelection.RefreshPreviewAudio(true);
        }
        else
        {
            Debug.LogWarning("SettingsManager: SongSelectionManager instance not available when toggling piano preview.");
        }
    }

    public void SetDebugMode(bool enabled)
    {
        // Debug mode and judgment mode are mutually exclusive: enable debug -> disable judgment, and vice versa
        gameSettings.debugMode = enabled;
        gameSettings.judgmentMode = !enabled;
        EnsureJudgmentFlagsConsistency();
    }

    public bool DebugMode => gameSettings != null ? gameSettings.debugMode : false;

    // Allow other systems to explicitly set judgment mode; keep debug mode as inverse
    public void SetJudgmentMode(bool enabled)
    {
        gameSettings.judgmentMode = enabled;
        gameSettings.debugMode = !enabled;
        EnsureJudgmentFlagsConsistency();
    }
}