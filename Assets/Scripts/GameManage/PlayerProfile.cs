using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 演奏者是誰。名字、印記顏色、從什麼時候開始彈。
/// </summary>
/// <remarks>
/// **只存在這台機器上。** 和 `LocalScoreRecords`、`PlayerRating` 走同一條路：
/// `Application.persistentDataPath`（Windows 上是 `%USERPROFILE%\AppData\LocalLow\
/// <公司>\<產品>\`）。那個位置**不在 build 裡面**，也不會被打包腳本帶走，而且每個
/// Windows 使用者各自一份。專案裡沒有任何上傳路徑，所以這些東西不會離開這台電腦。
///
/// **為什麼印記是顏色不是圖。** 這個遊戲沒有頭像素材，而臨時生一組人像只會得到
/// 六張很像佔位圖的東西。改成「名字的第一個字 + 一個封蠟印記的顏色」：它是書本語
/// 言裡本來就有的東西（信封上的火漆印），一個字母就足以認人，而且任何語言的名字
/// 都適用。未來真的要放圖，`PortraitId` 這個欄位留著就好。
/// </remarks>
public static class PlayerProfile
{
    private const string FileName = "player_profile.json";
    private const string PortraitFileName = "player_portrait.png";
    public const int MaxNameLength = 12;
    /// <summary>頭像存下來的邊長。圓形直徑就是它。</summary>
    public const int PortraitSize = 256;

    /// <summary>印記的六個顏色。火漆、銅綠、深藍、舊金、墨、酒紅。</summary>
    public static readonly Color[] SealColours =
    {
        new Color(0.63f, 0.18f, 0.16f, 1f),
        new Color(0.26f, 0.44f, 0.38f, 1f),
        new Color(0.20f, 0.30f, 0.48f, 1f),
        new Color(0.66f, 0.50f, 0.20f, 1f),
        new Color(0.24f, 0.20f, 0.18f, 1f),
        new Color(0.45f, 0.18f, 0.30f, 1f),
    };

    [Serializable]
    private sealed class ProfileFile
    {
        public int version = 1;
        public string name;
        public int seal;
        public long createdAtUtcTicks;
    }

    private static ProfileFile data;
    private static Texture2D portrait;
    private static bool portraitChecked;

    /// <summary>有沒有建立過。沒有的話選曲畫面會請他先報上名來。</summary>
    public static bool Exists
    {
        get
        {
            EnsureLoaded();
            return !string.IsNullOrWhiteSpace(data.name);
        }
    }

    public static string Name
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(data.name) ? Localize.S("演奏者") : data.name;
        }
    }

    public static int SealIndex
    {
        get
        {
            EnsureLoaded();
            return Mathf.Clamp(data.seal, 0, SealColours.Length - 1);
        }
    }

    public static Color Seal => SealColours[SealIndex];

    /// <summary>頭像。沒有就是 null，畫面退回火漆印那一套。</summary>
    public static Texture2D Portrait
    {
        get
        {
            if (portraitChecked) return portrait;
            portraitChecked = true;
            string path = PortraitPath;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (loaded.LoadImage(File.ReadAllBytes(path)))
                    {
                        loaded.filterMode = FilterMode.Bilinear;
                        loaded.wrapMode = TextureWrapMode.Clamp;
                        loaded.name = "PlayerPortrait";
                        loaded.hideFlags = HideFlags.DontSave;
                        portrait = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerProfile] Could not load portrait: {ex.Message}");
            }
            return portrait;
        }
    }

    public static bool HasPortrait => Portrait != null;

    private static string PortraitPath => PlayerSlots.PathFor(PortraitFileName);

    /// <summary>
    /// 換一張頭像：任何圖片檔都收，置中裁成正方形、縮到 256、切成圓形。
    /// </summary>
    /// <remarks>
    /// **為什麼要自己存一份。** 玩家選的可能是桌面上的一張圖，而那張圖隨時會被刪
    /// 掉或搬走。存進 `persistentDataPath`（和名字、成績、評分同一個位置，不在
    /// build 裡面）之後，頭像就不再依賴原檔還在不在。
    ///
    /// **為什麼在這裡就切成圓的。** uGUI 沒有圓形遮罩 —— `Mask` 是矩形的，要圓形
    /// 得用 stencil 或另外一張遮罩貼圖，每一個顯示的地方都得記得做一次。直接把
    /// alpha 寫進檔案裡，畫面上就只是一張普通的圖，怎麼放都不會變回方的。
    ///
    /// 邊緣留一點半透明的過渡：硬切的圓在關掉 MSAA 的畫面上是鋸齒。
    /// </remarks>
    public static bool SetPortrait(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        try
        {
            if (!File.Exists(sourcePath)) return false;
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(File.ReadAllBytes(sourcePath)))
            {
                UnityEngine.Object.Destroy(source);
                return false;
            }
            source.wrapMode = TextureWrapMode.Clamp;

            Texture2D circle = MakeCircular(source);
            UnityEngine.Object.Destroy(source);
            if (circle == null) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(PortraitPath));
            File.WriteAllBytes(PortraitPath, circle.EncodeToPNG());

            if (portrait != null) UnityEngine.Object.Destroy(portrait);
            portrait = circle;
            portrait.name = "PlayerPortrait";
            portrait.hideFlags = HideFlags.DontSave;
            portraitChecked = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerProfile] Could not read portrait: {ex.Message}");
            return false;
        }
    }

    public static void ClearPortrait()
    {
        try { if (File.Exists(PortraitPath)) File.Delete(PortraitPath); }
        catch (Exception ex) { Debug.LogWarning($"[PlayerProfile] Could not clear portrait: {ex.Message}"); }
        if (portrait != null) UnityEngine.Object.Destroy(portrait);
        portrait = null;
        portraitChecked = true;
    }

    /// <summary>置中裁成正方形、縮到 PortraitSize、切圓。</summary>
    private static Texture2D MakeCircular(Texture2D source)
    {
        if (source == null || source.width < 2 || source.height < 2) return null;

        // 置中裁切：長的那一邊各切掉一半，不要把人臉拉扁。
        int side = Mathf.Min(source.width, source.height);
        float left = (source.width - side) * 0.5f;
        float bottom = (source.height - side) * 0.5f;

        var result = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
        var pixels = new Color32[PortraitSize * PortraitSize];
        float radius = PortraitSize * 0.5f;
        float centre = radius - 0.5f;

        for (int y = 0; y < PortraitSize; y++)
        {
            for (int x = 0; x < PortraitSize; x++)
            {
                float u = (left + (x + 0.5f) / PortraitSize * side) / source.width;
                float v = (bottom + (y + 0.5f) / PortraitSize * side) / source.height;
                Color c = source.GetPixelBilinear(u, v);

                float dx = x - centre;
                float dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                // 1.5px 的過渡帶：硬切的圓在關掉 MSAA 的畫面上是一圈鋸齒。
                float alpha = Mathf.Clamp01((radius - 1f - distance) / 1.5f);
                pixels[y * PortraitSize + x] = new Color(c.r, c.g, c.b, c.a * alpha);
            }
        }
        result.SetPixels32(pixels);
        result.Apply(false, false);
        result.filterMode = FilterMode.Bilinear;
        result.wrapMode = TextureWrapMode.Clamp;
        return result;
    }

    /// <summary>印記上刻的那一個字：名字的第一個字。</summary>
    public static string Monogram
    {
        get
        {
            string name = Name.Trim();
            return name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "？";
        }
    }

    public static DateTime CreatedAtUtc
    {
        get
        {
            EnsureLoaded();
            try { return new DateTime(data.createdAtUtcTicks, DateTimeKind.Utc); }
            catch { return DateTime.UtcNow; }
        }
    }

    /// <summary>從建立到現在幾天。一個很小的「我彈多久了」。</summary>
    public static int DaysSinceCreated
    {
        get
        {
            EnsureLoaded();
            if (data.createdAtUtcTicks <= 0) return 0;
            try { return Mathf.Max(0, (int)(DateTime.UtcNow - CreatedAtUtc).TotalDays); }
            catch { return 0; }
        }
    }

    /// <summary>建立或改名。建立的時間只寫一次 —— 改名不該把資歷歸零。</summary>
    public static void Save(string name, int sealIndex)
    {
        EnsureLoaded();
        data.name = Sanitise(name);
        data.seal = Mathf.Clamp(sealIndex, 0, SealColours.Length - 1);
        if (data.createdAtUtcTicks <= 0) data.createdAtUtcTicks = DateTime.UtcNow.Ticks;
        Write();
    }

    /// <summary>
    /// 名字的清理。
    /// </summary>
    /// <remarks>
    /// 換行和控制字元要拿掉：這個名字會被印在銘牌上、印在一行字的中間，一個藏在
    /// 裡面的換行會把整塊版面撐開。長度上限是為了同一個理由。
    /// </remarks>
    private static string Sanitise(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new System.Text.StringBuilder(MaxNameLength);
        foreach (char c in value.Trim())
        {
            if (char.IsControl(c)) continue;
            builder.Append(c);
            if (builder.Length >= MaxNameLength) break;
        }
        return builder.ToString().Trim();
    }

    /// <summary>換人了：丟掉快取，下一次讀就會讀到新那位的檔案。</summary>
    /// <remarks>
    /// 靜態快取跨換人是這一整套最容易出的錯 —— 沒有丟的話，換過去的人會看到前一位的
    /// 成績，而且**下一次存檔會把它寫進新的人的檔案裡**，兩邊都毀掉。所以是 slot 一
    /// 變就丟，不是等誰記得呼叫。
    /// </remarks>
    private static void Forget() => data = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSlotChanges() => PlayerSlots.Changed += Forget;

    private static void EnsureLoaded()
    {
        if (data != null) return;
        string path = PlayerSlots.PathFor(FileName);
        try
        {
            data = File.Exists(path)
                ? JsonUtility.FromJson<ProfileFile>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerProfile] Could not load profile: {ex.Message}");
            data = null;
        }
        if (data == null) data = new ProfileFile();
        data.name = Sanitise(data.name);
        data.seal = Mathf.Clamp(data.seal, 0, SealColours.Length - 1);
    }

    private static void Write()
    {
        string path = PlayerSlots.PathFor(FileName);
        string temporary = path + ".tmp";
        try
        {
            // 檔案現在在 players/<id>/ 底下，建根目錄已經不夠。
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerProfile] Could not save profile: {ex.Message}");
        }
    }
}
