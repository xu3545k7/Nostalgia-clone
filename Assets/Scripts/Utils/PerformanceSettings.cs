using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 玩家可調的效能選項：幀率上限、渲染解析度、光暈品質、鋼琴同時發聲數。
/// </summary>
/// <remarks>
/// 值存在 <see cref="SettingsManager"/>，這裡只負責「把值套到引擎上」。
/// 未設定（-1）時用平台預設：桌機照原本的行為（跟螢幕、全解析度、高品質光暈、
/// 全部聲部），手機／平板保守一點（60 fps、一般光暈、128 聲部）——
/// 玩家覺得機器跑得動再往上調。
/// </remarks>
public static class PerformanceSettings
{
    // ── 幀率上限 ───────────────────────────────────────────────────────
    public const int FrameCap30 = 0;
    public const int FrameCap60 = 1;
    public const int FrameCapDisplay = 2;
    public static int DefaultFrameCap => Application.isMobilePlatform ? FrameCap60 : FrameCapDisplay;

    // ── 光暈 ───────────────────────────────────────────────────────────
    public const int BloomOff = 0;
    public const int BloomNormal = 1;
    public const int BloomHigh = 2;
    public static int DefaultBloom => Application.isMobilePlatform ? BloomNormal : BloomHigh;

    // ── 渲染解析度 ─────────────────────────────────────────────────────
    public const float MinRenderScale = 0.5f;
    public const float MaxRenderScale = 1f;
    public static float DefaultRenderScale => 1f;

    // ── 鋼琴發聲數 ─────────────────────────────────────────────────────
    /// <summary>選項對應的聲部數；最後一格 0 ＝ 不設上限（用整個聲部池）。</summary>
    public static readonly int[] VoiceOptions = { 32, 64, 128, 0 };
    public static int DefaultVoiceOption => Application.isMobilePlatform ? 2 : 3;

    public static void Apply(int frameCap, float renderScale, int bloom, int voiceOption)
    {
        ApplyFrameCap(frameCap);
        ApplyRenderScale(renderScale);
        ApplyBloom(bloom);
        ApplyVoiceLimit(voiceOption);
    }

    /// <remarks>
    /// 桌機開著垂直同步時 Unity 不理 targetFrameRate，所以上限要用「每幾次刷新
    /// 畫一幀」來做（144 Hz 選 60 會是 72，這是故意的：不整除的上限會讓長拍子線
    /// 一頓一頓，見 DisplaySync）。手機上 vSyncCount 被忽略，只看 targetFrameRate。
    /// </remarks>
    public static void ApplyFrameCap(int option)
    {
        double refreshValue = Screen.currentResolution.refreshRateRatio.value;
        int refresh = refreshValue > 1.0 ? Mathf.RoundToInt((float)refreshValue) : 60;
        int wanted = option == FrameCap30 ? 30 : option == FrameCap60 ? 60 : refresh;
        wanted = Mathf.Min(wanted, Mathf.Max(30, refresh));

        if (Application.isMobilePlatform)
        {
            Application.targetFrameRate = wanted;
            return;
        }

        int interval = Mathf.Max(1, Mathf.RoundToInt(refresh / (float)wanted));
        QualitySettings.vSyncCount = interval;
        Application.targetFrameRate = Mathf.Max(30, refresh / interval);
    }

#if UNITY_EDITOR
    // 在 Editor 裡改 renderScale 會直接寫進 .asset 檔：記住原值，離開 Play 時還回去。
    private static readonly Dictionary<UniversalRenderPipelineAsset, float> editorOriginalScale =
        new Dictionary<UniversalRenderPipelineAsset, float>();
    private static bool editorRestoreHooked;
#endif

    public static void ApplyRenderScale(float scale)
    {
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;
#if UNITY_EDITOR
        if (!editorOriginalScale.ContainsKey(asset)) editorOriginalScale[asset] = asset.renderScale;
        if (!editorRestoreHooked)
        {
            editorRestoreHooked = true;
            Application.quitting += () =>
            {
                foreach (var pair in editorOriginalScale)
                    if (pair.Key != null) pair.Key.renderScale = pair.Value;
                editorOriginalScale.Clear();
            };
        }
#endif
        asset.renderScale = Mathf.Clamp(scale, MinRenderScale, MaxRenderScale);
    }

    /// <remarks>
    /// 用 <c>volume.profile</c>（會複製一份）而不是 <c>sharedProfile</c>：
    /// 改共用的那份在 Editor 裡會寫回 SampleSceneProfile.asset。
    /// </remarks>
    public static void ApplyBloom(int option)
    {
        var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
        foreach (var volume in volumes)
        {
            if (volume == null || volume.sharedProfile == null) continue;
            if (!volume.sharedProfile.Has<Bloom>()) continue;
            if (!volume.profile.TryGet(out Bloom bloom)) continue;
            bloom.active = option != BloomOff;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = option == BloomHigh;
        }
    }

    public static void ApplyVoiceLimit(int option)
    {
        int index = Mathf.Clamp(option, 0, VoiceOptions.Length - 1);
        PianoVoiceManager.VoiceLimit = VoiceOptions[index];
    }
}
