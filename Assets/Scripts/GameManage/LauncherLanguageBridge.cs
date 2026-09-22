using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// NosMania 啟動器選的語言帶進遊戲。
///
/// 啟動器在遊戲資料夾（exe 旁邊）寫 <c>nosmania_launcher.json</c>：
/// <c>{"language": "English", "code": "en", "stamp": 1789491470223}</c>。
/// 換語言和每次從啟動器按「進入遊戲」都會換一個新的 stamp。
///
/// 這裡啟動時讀一次、之後每秒看一次修改時間；stamp 比上次套用過的新才換語言並存檔，
/// 所以直接開遊戲（沒經過啟動器）時，玩家在遊戲設定裡改的語言會保留。
/// 換語言走 <see cref="SettingsManager.SetLanguage"/>，開著的畫面會跟著換。
/// </summary>
public sealed class LauncherLanguageBridge : MonoBehaviour
{
    public const string FileName = "nosmania_launcher.json";
    private const string AppliedStampKey = "NosMania.LauncherLanguageStamp";
    private const float PollSeconds = 1f;

    private float nextPoll;
    private DateTime lastWriteUtc;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<LauncherLanguageBridge>() != null) return;
        var go = new GameObject("LauncherLanguageBridge");
        DontDestroyOnLoad(go);
        go.AddComponent<LauncherLanguageBridge>();
    }

    private void Start()
    {
        Poll();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + PollSeconds;
        Poll();
    }

    private void Poll()
    {
        string path = FilePath();
        if (path == null || !File.Exists(path)) return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch (Exception) { return; }
        if (stamp == lastWriteUtc) return;

        if (!TryRead(path, out AppLanguage language, out long fileStamp)) return;
        lastWriteUtc = stamp;
        if (fileStamp <= AppliedStamp()) return;

        SettingsManager settings = SettingsManager.Instance;
        if (settings == null) return;
        if (settings.CurrentLanguage != language)
        {
            settings.SetLanguage(language);
            settings.SaveSettings();
            Debug.Log("[LauncherLanguageBridge] 啟動器選的語言：" + language);
        }
        PlayerPrefs.SetString(AppliedStampKey, fileStamp.ToString());
        PlayerPrefs.Save();
    }

    private static long AppliedStamp()
    {
        return long.TryParse(PlayerPrefs.GetString(AppliedStampKey, "0"), out long value) ? value : 0;
    }

    private static string FilePath()
    {
        try
        {
            // Build：<遊戲資料夾>/xxx_Data 的上一層；Editor：專案根目錄
            string gameDir = Path.GetDirectoryName(Application.dataPath);
            return string.IsNullOrEmpty(gameDir) ? null : Path.Combine(gameDir, FileName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryRead(string path, out AppLanguage language, out long stamp)
    {
        language = AppLanguage.TraditionalChinese;
        stamp = 0;
        try
        {
            JObject data = JObject.Parse(File.ReadAllText(path));
            stamp = data.Value<long?>("stamp") ?? 0;
            string name = data.Value<string>("language");
            return stamp > 0 && !string.IsNullOrEmpty(name) &&
                   Enum.TryParse(name, false, out language) &&
                   Enum.IsDefined(typeof(AppLanguage), language);
        }
        catch (Exception)
        {
            // 啟動器正在寫（先寫暫存檔再換掉），下一輪再讀
            return false;
        }
    }
}
