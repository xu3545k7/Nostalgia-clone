using UnityEngine;

/// <summary>
/// 秒倒數之後那三拍的鼓棒聲。
/// </summary>
/// <remarks>
/// **為什麼要出聲。** 那三拍是這首歌自己報的速度，而速度是聽的東西不是看的東西。
/// 譜面滾進來只告訴玩家「快開始了」；敲三下才告訴他們「這麼快」。指揮起拍、鼓手
/// 數棒，用的都是這一招，而且都是用聲音。
///
/// **為什麼用 DSP 排程而不是協程。** 這三下必須和音樂落在同一條時間軸上 —— 差幾
/// 毫秒還能忍，差一格畫面（16ms）就已經是聽得出來的「沒對上」。<c>PlayScheduled</c>
/// 和歌曲的排程用的是同一個 <see cref="AudioSettings.dspTime"/>，所以它們的關係
/// 是算出來的，不是每幀去追出來的。
///
/// 三個 AudioSource 而不是一個叫三次：排程是「這個 source 在那個時刻開始播」，
/// 同一個 source 排第二次會蓋掉第一次。
/// </remarks>
public sealed class CountInSticks : MonoBehaviour
{
    /// <summary>Resources 底下的路徑，不含副檔名。</summary>
    private const string ClipPath = "Sound/CountInStick";

    /// <summary>敲幾下。和 GameManager 的預備拍數是同一件事。</summary>
    private const int Sticks = 3;

    private static CountInSticks instance;
    private static AudioClip clip;
    private static bool clipMissingReported;

    private AudioSource[] sources;

    private static CountInSticks EnsureCreated()
    {
        if (instance != null) return instance;
        var go = new GameObject("CountInSticks");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CountInSticks>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        sources = new AudioSource[Sticks];
        for (int i = 0; i < Sticks; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            // 2D。這三下不屬於場景裡的任何位置，它們是講給玩家聽的。
            source.spatialBlend = 0f;
            source.priority = 32;
            sources[i] = source;
        }
    }

    /// <summary>
    /// 排定三下鼓棒，最後一下的**下一拍**就是第一小節。
    /// </summary>
    /// <param name="downbeatDsp">第一小節落在判定線上的 DSP 時刻。</param>
    /// <param name="beatSeconds">一拍多長。</param>
    /// <remarks>
    /// 往回數而不是往前數：要對齊的是**小節**，不是倒數的起點。從起點往前加的話，
    /// 任何一個夾住、保險、或是浮點的零頭都會累積到最後一下，而最後一下正是唯一
    /// 不能歪的那一下。
    /// </remarks>
    public static void Schedule(double downbeatDsp, double beatSeconds)
    {
        if (beatSeconds <= 0.0) return;
        AudioClip loaded = Load();
        if (loaded == null)
        {
            Debug.LogWarning("[CountInSticks] Schedule: clip is null.");
            return;
        }

        CountInSticks view = EnsureCreated();
        if (view.sources == null)
        {
            Debug.LogWarning("[CountInSticks] Schedule: no sources.");
            return;
        }

        float volume = 1f;
        try
        {
            var settings = SettingsManager.Instance;
            // 跟著**音樂**的音量走，不是打擊聲。
            //
            // 這一開始綁在打擊聲上，理由是「兩者都是遊戲講給玩家聽的提示」——
            // 錯了。用真鋼琴彈的人會把打擊聲關掉（每顆音再疊一個電子聲是干擾），
            // 但他們要的正是這三下：那是這首曲子的起拍，不是判定的回饋。
            //
            // 而且它從來不是在評論玩家的表現，它是在報速度。報速度屬於音樂。
            //
            // 用 MusicVolume 而不是 GameplayMusicVolume：後者在練習模式是 0（練習
            // 模式不放伴奏），而練習模式正是最需要有人幫你數拍子的時候。
            if (settings != null) volume = Mathf.Clamp01(settings.MusicVolume);
        }
        catch { }
        // 音樂整個靜音的時候才不響。這時候什麼都沒有，三下敲在全靜的畫面上
        // 只會像是壞掉了。
        if (volume <= 0.001f)
        {
            Debug.Log("[CountInSticks] Schedule: music volume is zero, staying silent.");
            return;
        }

        double now = AudioSettings.dspTime;
        Debug.Log($"[CountInSticks] clip={loaded.name} len={loaded.length:F3}s "
            + $"state={loaded.loadState} volume={volume:F2} beat={beatSeconds:F3}s "
            + $"downbeat={downbeatDsp:F3} now={now:F3} first={downbeatDsp - Sticks * beatSeconds:F3}");
        for (int i = 0; i < Sticks; i++)
        {
            AudioSource source = view.sources[i];
            if (source == null) continue;
            // 第三下在小節的前一拍，第一下在前三拍。
            double at = downbeatDsp - (Sticks - i) * beatSeconds;
            source.Stop();
            source.clip = loaded;
            source.volume = volume;
            // 最後一下重一點。人數拍子的時候最後一下本來就會加重，那一下是
            // 「下一個就是了」的意思。
            if (i == Sticks - 1) source.volume = Mathf.Clamp01(volume * 1.25f);
            if (at <= now + 0.005)
            {
                // 已經來不及排了（載入拖太久，或這首歌的拍子極快）。寧可晚一點
                // 響也不要不響——三下裡少一下比三下都沒有更難察覺。
                source.PlayScheduled(now + 0.005);
            }
            else
            {
                source.PlayScheduled(at);
            }
        }
    }

    /// <summary>換歌、退出、暫停時把還沒響的那幾下收掉。</summary>
    public static void Cancel()
    {
        if (instance == null || instance.sources == null) return;
        for (int i = 0; i < instance.sources.Length; i++)
        {
            if (instance.sources[i] != null) instance.sources[i].Stop();
        }
    }

    private static AudioClip Load()
    {
        if (clip != null) return clip;
        clip = Resources.Load<AudioClip>(ClipPath);
        if (clip == null && !clipMissingReported)
        {
            clipMissingReported = true;
            // Debug 而不是 BuildLogger.Log：後者在正式版會被編掉，而「預備拍沒
            // 有聲音」正是只有在正式版才會被發現的那種問題。
            Debug.LogWarning($"[CountInSticks] 找不到 Resources/{ClipPath}，預備拍不會出聲。");
        }
        return clip;
    }
}
