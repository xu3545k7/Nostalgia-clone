using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Writable song library used by player builds. It supports both complete
/// register folders and the field-by-field importer used by Settings.
/// </summary>
public static class ExternalSongLibrary
{
    public const string ExternalPrefix = "external://";
    public const string AllCategory = "ALL";
    public const string DefaultCategory = "Other";
    private const string IndexFileName = "library.json";
    private const string SeedMarkerFileName = ".bundled-library-seeded";
    private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3", ".aiff", ".aif" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
    private static string resolvedRootPath;
    private static bool synchronizingPortableFolders;

    // ── 離線量測的響度 ───────────────────────────────────────────────────────
    //
    // 每首歌的 register.json 可以帶一個 audioLoudness 區塊，是打包工具
    // （qt_editor 的 loudness_library）照 BS.1770 量出來的。遊戲**不改音檔**，只在
    // 播放時把增益乘在音量上，所以使用者的原始檔案永遠是原樣，換目標重量一次就好。
    //
    // 沒有這個區塊的曲子走 LoudnessNormalizer 執行時的估算，兩邊可以並存。

    /// <summary>目前認得的格式。工具端改演算法就換版本，舊資料自動被忽略。</summary>
    public const string LoudnessSchema = "nos-clone-loudness-v1";

    public struct TrackLoudness
    {
        /// <summary>要乘上去的增益（dB）。</summary>
        public float GainDb;
        /// <summary>量到的整曲響度（LUFS-I）。</summary>
        public float Lufs;
        /// <summary>量到的真峰值（dBTP）。</summary>
        public float PeakDbtp;
        /// <summary>gain／limited／needs_review／ok。</summary>
        public string Status;
    }

    private static readonly Dictionary<string, TrackLoudness> loudnessByPath =
        new Dictionary<string, TrackLoudness>(StringComparer.OrdinalIgnoreCase);

    /// <summary>這條音軌有沒有離線量過。傳 external:// 或實際路徑都可以。</summary>
    public static bool TryGetTrackLoudness(string path, out TrackLoudness info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string key = NormaliseLoudnessKey(path);
        return key != null && loudnessByPath.TryGetValue(key, out info);
    }

    private static string NormaliseLoudnessKey(string path)
    {
        try
        {
            string local = ToLocalPath(path) ?? path;
            if (string.IsNullOrWhiteSpace(local)) return null;
            return Path.GetFullPath(local);
        }
        catch { return null; }
    }

    /// <summary>把一首歌的 audioLoudness 收進查詢表，鍵是音檔的實際路徑。</summary>
    private static void IndexLoudness(JObject metadata, string folder)
    {
        JObject block = metadata?["audioLoudness"] as JObject;
        if (block == null) return;
        if (!string.Equals((string)block["schema"], LoudnessSchema, StringComparison.Ordinal)) return;
        JObject tracks = block["tracks"] as JObject;
        if (tracks == null) return;

        foreach (JProperty track in tracks.Properties())
        {
            if (!(track.Value is JObject values)) continue;
            string resolved = ResolveAsset(folder, track.Name, AudioExtensions);
            string key = resolved != null ? NormaliseLoudnessKey(resolved) : null;
            if (key == null) continue;
            loudnessByPath[key] = new TrackLoudness
            {
                GainDb = (float?)values["gainDb"] ?? 0f,
                Lufs = (float?)values["lufs"] ?? 0f,
                PeakDbtp = (float?)values["peakDbtp"] ?? 0f,
                Status = (string)values["status"] ?? string.Empty,
            };
        }
    }

    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string folderName;
        public string category;
        public List<string> categories;
    }

    [Serializable]
    private sealed class IndexData
    {
        public List<Entry> songs = new List<Entry>();
        public List<string> categories = new List<string>();
    }

    public sealed class LoadedSong
    {
        public Entry entry;
        public string metadataJson;
        public string registerPath;
    }

    public sealed class StructuredImportRequest
    {
        public string chartPath;
        public string displayName;
        public string author;
        public string audioPath;
        public string pianoAudioPath;
        public string coverPath;
        public string difficultyName;
        public int difficultyLevel;
        public string category;
    }

    public sealed class DifficultyInfo
    {
        public int index;
        public string difficultyName;
        public int difficultyLevel;
        public string chartPath;
        public string audioPath;
        public string pianoAudioPath;
        public string coverPath;

        public string DisplayLabel => $"{difficultyName}  Lv. {difficultyLevel}";
    }

    public sealed class DifficultyEditRequest
    {
        public string chartPath;
        public string audioPath;
        public string pianoAudioPath;
        public string coverPath;
        public string difficultyName;
        public int difficultyLevel;
    }

    public sealed class ValidationResult
    {
        public bool IsValid => errors.Count == 0;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();

        public string ToDisplayText()
        {
            if (IsValid && warnings.Count == 0) return "驗證成功，檔案可匯入。";
            var lines = new List<string>();
            if (errors.Count > 0)
            {
                lines.Add("無法匯入：");
                lines.AddRange(errors.Select(value => "• " + value));
            }
            if (warnings.Count > 0)
            {
                lines.Add("提醒：");
                lines.AddRange(warnings.Select(value => "• " + value));
            }
            return string.Join("\n", lines);
        }
    }

    public static string RootPath
    {
        get
        {
            if (!string.IsNullOrEmpty(resolvedRootPath)) return resolvedRootPath;
            // 曲庫和遊戲分開放：NosMania 啟動器在 exe 旁邊的 nosmania_launcher.json 寫曲庫位置
            string launcherRoot = LauncherLibraryRoot();
            if (!string.IsNullOrEmpty(launcherRoot))
            {
                resolvedRootPath = launcherRoot;
                return resolvedRootPath;
            }
            string portable = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSongs"));
            string legacy = Path.Combine(Application.persistentDataPath, "UserSongs");
            try
            {
                bool portableWasMissing = !Directory.Exists(portable);
                Directory.CreateDirectory(portable);
                bool portableHasIndex = File.Exists(Path.Combine(portable, IndexFileName));
                // 舊的本機曲庫只在 Editor 裡搬：發行版自己複製一份，遊戲資料夾就多出曲子，
                // NosMania 也會把它當成「真的曲庫」而不敢換成連結。
                if (Application.isEditor && (portableWasMissing || !portableHasIndex) && Directory.Exists(legacy) &&
                    !string.Equals(Path.GetFullPath(legacy), portable, StringComparison.OrdinalIgnoreCase))
                    CopyDirectoryContents(legacy, portable);
                resolvedRootPath = portable;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ExternalSongLibrary] Portable library is not writable; " +
                                 "falling back to the local player library. " + ex.Message);
                Directory.CreateDirectory(legacy);
                resolvedRootPath = legacy;
            }
            return resolvedRootPath;
        }
    }

    /// <summary>
    /// NosMania 啟動器指定的曲庫（<c>nosmania_launcher.json</c> 的 <c>library_root</c>）。
    /// 沒有檔案、沒有這個鍵、或資料夾不存在都回傳 null，照舊用 exe 旁邊的 UserSongs。
    /// </summary>
    private static string LauncherLibraryRoot()
    {
        try
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "nosmania_launcher.json"));
            if (!File.Exists(file)) return null;
            JObject data = JObject.Parse(File.ReadAllText(file));
            string root = (string)data["library_root"];
            if (string.IsNullOrWhiteSpace(root)) return null;
            root = Path.GetFullPath(root);
            return Directory.Exists(root) ? root : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ExternalSongLibrary] Could not read launcher library_root: " + ex.Message);
            return null;
        }
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        SeedPortableLibraryFromBackup();
        string indexPath = Path.Combine(RootPath, IndexFileName);
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, JsonConvert.SerializeObject(new IndexData(), Formatting.Indented));
        SynchronizePortableFolders();
    }

    private static void SeedPortableLibraryFromBackup()
    {
        // 發行的遊戲本體不含任何曲子、也不自己補曲子：曲庫由 NosMania 連過來。
        // 以前遊戲放在 D:\Nostalgia\<資料夾> 時，上兩層剛好是工作區的 Songs_backup，
        // 就會自己長出 61 首。只有在 Editor 裡才從 Songs_backup 補。
        if (!Application.isEditor) return;
        string projectBackup = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Songs_backup"));
        string workspaceBackup = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", "Songs_backup", "songs"));
        string backup = Directory.Exists(projectBackup) ? projectBackup : workspaceBackup;
        string target = Path.GetFullPath(RootPath);
        string marker = Path.Combine(target, SeedMarkerFileName);
        if (!Directory.Exists(backup) ||
            string.Equals(backup, target, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(marker))
            return;

        try
        {
            CopyDirectoryContents(backup, target);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ExternalSongLibrary] Could not seed UserSongs from Songs_backup: " +
                             ex.Message);
        }
    }

    public static List<string> GetCategories()
    {
        IndexData index = ReadIndex(null);
        var result = new List<string> { DefaultCategory };
        AddDistinctCategories(result, index.categories);
        AddDistinctCategories(result, index.songs.Where(song => song != null).Select(song => song.category));
        AddDistinctCategories(result, index.songs.Where(song => song?.categories != null)
            .SelectMany(song => song.categories));
        return result;
    }

    public static bool CreateCategory(string value, out string message)
    {
        string category = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(category))
        {
            message = "分類名稱不能空白。";
            return false;
        }
        if (category.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || category.Length > 40)
        {
            message = "分類名稱含有不支援的字元，或長度超過 40 個字。";
            return false;
        }

        IndexData index = ReadIndex(null);
        if (string.Equals(category, AllCategory, StringComparison.OrdinalIgnoreCase))
        {
            message = "ALL 是自動彙整頁，不能建立或指派給單一曲目。";
            return false;
        }
        if (index.categories.Any(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(category, DefaultCategory, StringComparison.OrdinalIgnoreCase))
        {
            message = "這個分類已經存在。";
            return false;
        }

        index.categories.Add(category);
        WriteIndex(index);
        message = $"已建立分類「{category}」。";
        return true;
    }

    public static List<LoadedSong> LoadAll(out List<string> warnings)
    {
        EnsureCreated();
        warnings = new List<string>();
        IndexData index = ReadIndex(warnings);
        var result = new List<LoadedSong>();
        foreach (Entry entry in index.songs)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.folderName)) continue;
            string folder = GetOwnedSongFolder(entry.folderName);
            string register = Path.Combine(folder, "register.json");
            if (!Directory.Exists(folder) || !File.Exists(register))
            {
                warnings.Add($"{entry.folderName}：找不到 register.json。");
                continue;
            }

            try
            {
                JObject metadata = JObject.Parse(File.ReadAllText(register));
                // 量測表要在改寫路徑**之前**收：它的鍵是 register 裡原本的資源路徑。
                IndexLoudness(metadata, folder);
                RewriteAssetPaths(metadata, folder);
                List<string> entryCategories = GetEntryCategories(entry);
                for (int categoryIndex = 0; categoryIndex < entryCategories.Count; categoryIndex++)
                {
                    result.Add(new LoadedSong
                    {
                        entry = new Entry
                        {
                            id = entry.id,
                            folderName = entry.folderName,
                            category = entryCategories[categoryIndex],
                            categories = new List<string>(entryCategories)
                        },
                        registerPath = register,
                        metadataJson = metadata.ToString(Formatting.None)
                    });
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"{entry.folderName}：register.json 無法讀取（{ex.Message}）。");
            }
        }
        return result;
    }

    public static ValidationResult ValidateStructured(StructuredImportRequest request)
    {
        var result = new ValidationResult();
        if (request == null)
        {
            result.errors.Add("沒有收到曲目資料。");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.displayName)) result.errors.Add("請輸入曲子名稱。");
        if (string.IsNullOrWhiteSpace(request.author)) result.errors.Add("請輸入作者。");
        if (string.IsNullOrWhiteSpace(request.difficultyName)) result.errors.Add("請輸入難度名稱。");
        if (request.difficultyLevel < 1 || request.difficultyLevel > 99)
            result.errors.Add("難度等級必須介於 1 到 99。");

        ValidateFile(result, request.chartPath, "譜面", new[] { ".json", ".xml" }, true);
        ValidateFile(result, request.audioPath, "歌曲音訊", AudioExtensions, true);
        ValidateFile(result, request.pianoAudioPath, "鋼琴音訊", AudioExtensions, false);
        ValidateFile(result, request.coverPath, "曲繪", ImageExtensions, false);

        if (File.Exists(CleanPath(request.chartPath)))
        {
            try
            {
                JObject chart = LoadAndNormalizeChart(CleanPath(request.chartPath));
                ValidateChartObject(chart, result);
            }
            catch (Exception ex)
            {
                result.errors.Add("譜面內容無法解析：" + ex.Message);
            }
        }
        if (File.Exists(CleanPath(request.audioPath)))
            ValidateNonEmptyFile(result, CleanPath(request.audioPath), "歌曲音訊");
        if (File.Exists(CleanPath(request.pianoAudioPath)))
            ValidateNonEmptyFile(result, CleanPath(request.pianoAudioPath), "鋼琴音訊");
        if (File.Exists(CleanPath(request.coverPath)))
            ValidateImageHeader(result, CleanPath(request.coverPath));

        if (string.IsNullOrWhiteSpace(request.coverPath))
            result.warnings.Add("沒有選擇曲繪，選歌畫面會使用預設圖片。");
        return result;
    }

    public static bool ImportStructured(StructuredImportRequest request, out string id, out string message)
    {
        id = null;
        ValidationResult validation = ValidateStructured(request);
        if (!validation.IsValid)
        {
            message = validation.ToDisplayText();
            return false;
        }

        id = Guid.NewGuid().ToString("N");
        string songBase = SanitizeFolderName(request.displayName);
        string folderName = songBase + "_" + id.Substring(0, 8);
        string destination = GetOwnedSongFolder(folderName);
        string difficultyFolderName = SanitizeFolderName(request.difficultyName);
        string difficultyFolder = Path.Combine(destination, difficultyFolderName);

        try
        {
            Directory.CreateDirectory(difficultyFolder);
            string chartName = songBase + ".json";
            string chartDestination = Path.Combine(difficultyFolder, chartName);
            JObject normalizedChart = LoadAndNormalizeChart(CleanPath(request.chartPath));
            File.WriteAllText(chartDestination, normalizedChart.ToString(Formatting.Indented));

            string audioDestination = CopyNamedAsset(CleanPath(request.audioPath), destination, "audio");
            string pianoDestination = CopyNamedAsset(CleanPath(request.pianoAudioPath), destination, "piano");
            string coverDestination = CopyNamedAsset(CleanPath(request.coverPath), destination, "cover");

            var difficulty = new JObject
            {
                ["difficultyName"] = request.difficultyName.Trim(),
                ["difficultyLevel"] = request.difficultyLevel,
                ["chartFileName"] = RelativeWithoutExtension(destination, chartDestination),
                ["audioResourcePath"] = RelativeWithoutExtension(destination, audioDestination)
            };
            if (!string.IsNullOrEmpty(pianoDestination))
                difficulty["pianoAudioResourcePath"] = RelativeWithoutExtension(destination, pianoDestination);
            if (!string.IsNullOrEmpty(coverDestination))
                difficulty["coverResourcePath"] = RelativeWithoutExtension(destination, coverDestination);

            var register = new JObject
            {
                ["displayName"] = request.displayName.Trim(),
                ["author"] = request.author.Trim(),
                ["difficulties"] = new JArray(difficulty)
            };
            File.WriteAllText(Path.Combine(destination, "register.json"), register.ToString(Formatting.Indented));

            ValidationResult generatedValidation = ValidateSource(destination);
            if (!generatedValidation.IsValid)
                throw new InvalidDataException("生成後的曲目結構未通過驗證：\n" +
                                               generatedValidation.ToDisplayText());

            IndexData index = ReadIndex(null);
            string category = NormalizeCategory(request.category);
            index.songs.Add(new Entry
            {
                id = id,
                folderName = folderName,
                category = category,
                categories = new List<string> { category }
            });
            if (!string.Equals(category, AllCategory, StringComparison.OrdinalIgnoreCase) &&
                !index.categories.Any(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase)))
                index.categories.Add(category);
            WriteIndex(index);

            message = validation.warnings.Count == 0
                ? "匯入成功，已建立曲目資料夾並更新曲庫。\n儲存位置：" + destination
                : "匯入成功。\n" + string.Join("\n", validation.warnings) +
                  "\n儲存位置：" + destination;
            return true;
        }
        catch (Exception ex)
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            id = null;
            message = "匯入失敗：" + ex.Message;
            return false;
        }
    }

    public static ValidationResult ValidateSource(string sourcePath)
    {
        var result = new ValidationResult();
        string register = FindRegisterPath(sourcePath);
        if (string.IsNullOrEmpty(register))
        {
            result.errors.Add("找不到 register.json。");
            return result;
        }

        try
        {
            JObject root = JObject.Parse(File.ReadAllText(register));
            if (string.IsNullOrWhiteSpace((string)root["displayName"]))
                result.errors.Add("register.json 缺少 displayName。");
            if (string.IsNullOrWhiteSpace((string)root["author"]))
                result.errors.Add("register.json 缺少 author。");
            JArray difficulties = root["difficulties"] as JArray;
            if (difficulties == null || difficulties.Count == 0)
            {
                result.errors.Add("register.json 沒有 difficulties。");
                return result;
            }

            string folder = Path.GetDirectoryName(register);
            for (int i = 0; i < difficulties.Count; i++)
            {
                JObject diff = difficulties[i] as JObject;
                if (diff == null)
                {
                    result.errors.Add($"難度 {i + 1} 的格式不正確。");
                    continue;
                }
                string label = (string)diff["difficultyName"] ?? $"難度 {i + 1}";
                string chart = ResolveAsset(folder, (string)diff["chartFileName"], new[] { ".json" });
                RequireAsset(result, folder, (string)diff["chartFileName"], label, "譜面", new[] { ".json" });
                RequireAsset(result, folder, (string)diff["audioResourcePath"], label, "歌曲音訊", AudioExtensions);
                OptionalAsset(result, folder, (string)diff["pianoAudioResourcePath"], label, "鋼琴音訊", AudioExtensions);
                OptionalAsset(result, folder, (string)diff["coverResourcePath"], label, "曲繪", ImageExtensions);
                if (!string.IsNullOrEmpty(chart))
                {
                    try { ValidateChartObject(JObject.Parse(File.ReadAllText(chart)), result); }
                    catch (Exception ex) { result.errors.Add($"{label} 譜面 JSON 無法解析：{ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            result.errors.Add("register.json 無法解析：" + ex.Message);
        }
        return result;
    }

    // Kept for compatibility with older complete-folder imports.
    public static bool Import(string sourcePath, string category, out string id, out string message)
    {
        id = null;
        ValidationResult validation = ValidateSource(sourcePath);
        if (!validation.IsValid)
        {
            message = validation.ToDisplayText();
            return false;
        }

        string register = FindRegisterPath(sourcePath);
        string sourceFolder = Path.GetDirectoryName(register);
        string baseName = SanitizeFolderName(new DirectoryInfo(sourceFolder).Name);
        id = Guid.NewGuid().ToString("N");
        string folderName = baseName + "_" + id.Substring(0, 8);
        string destination = GetOwnedSongFolder(folderName);
        try
        {
            CopyDirectory(sourceFolder, destination);
            IndexData index = ReadIndex(null);
            string normalizedCategory = NormalizeCategory(category);
            index.songs.Add(new Entry
            {
                id = id,
                folderName = folderName,
                category = normalizedCategory,
                categories = new List<string> { normalizedCategory }
            });
            WriteIndex(index);
            message = "匯入成功。";
            return true;
        }
        catch (Exception ex)
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            id = null;
            message = "匯入失敗：" + ex.Message;
            return false;
        }
    }

    public static bool Update(string id, string displayName, string author, string category, out string message)
    {
        IndexData index = ReadIndex(null);
        Entry entry = index.songs.Find(item => item != null &&
            string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            message = "目前選擇的不是可修改的匯入曲目。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(author))
        {
            message = "曲子名稱與作者不能空白。";
            return false;
        }

        try
        {
            string registerPath = Path.Combine(GetOwnedSongFolder(entry.folderName), "register.json");
            JObject metadata = JObject.Parse(File.ReadAllText(registerPath));
            metadata["displayName"] = displayName.Trim();
            metadata["author"] = author.Trim();
            File.WriteAllText(registerPath, metadata.ToString(Formatting.Indented));
            entry.category = NormalizeCategory(category);
            entry.categories = new List<string> { entry.category };
            WriteIndex(index);
            message = "曲目資料已更新。";
            return true;
        }
        catch (Exception ex)
        {
            message = "修改失敗：" + ex.Message;
            return false;
        }
    }

    public static List<DifficultyInfo> GetDifficulties(string id, out string message)
    {
        var result = new List<DifficultyInfo>();
        if (!TryGetSongDocument(id, out _, out string folder, out _, out JObject metadata,
                out message))
            return result;

        JArray difficulties = metadata["difficulties"] as JArray;
        if (difficulties == null)
        {
            message = "曲目資料沒有 difficulties 陣列。";
            return result;
        }

        for (int i = 0; i < difficulties.Count; i++)
        {
            if (!(difficulties[i] is JObject difficulty)) continue;
            result.Add(new DifficultyInfo
            {
                index = i,
                difficultyName = ((string)difficulty["difficultyName"] ?? $"難度 {i + 1}").Trim(),
                difficultyLevel = (int?)difficulty["difficultyLevel"] ?? 1,
                chartPath = ResolveAsset(folder, (string)difficulty["chartFileName"], new[] { ".json" }),
                audioPath = ResolveAsset(folder, (string)difficulty["audioResourcePath"], AudioExtensions),
                pianoAudioPath = ResolveAsset(folder, (string)difficulty["pianoAudioResourcePath"], AudioExtensions),
                coverPath = ResolveAsset(folder, (string)difficulty["coverResourcePath"], ImageExtensions)
            });
        }

        message = result.Count > 0 ? "已載入難度資料。" : "此曲目沒有可用的難度。";
        return result;
    }

    public static bool AddDifficulty(string id, DifficultyEditRequest request, out string message)
    {
        if (!TryGetSongDocument(id, out _, out string folder, out string registerPath,
                out JObject metadata, out message))
            return false;
        if (!ValidateDifficultyEditRequest(request, true, out message))
            return false;

        JArray difficulties = metadata["difficulties"] as JArray;
        if (difficulties == null || difficulties.Count == 0)
        {
            message = "曲目至少要先有一個可供繼承音訊的難度。";
            return false;
        }
        if (HasDifficultyName(difficulties, request.difficultyName, -1))
        {
            message = "難度名稱已存在，請使用不同名稱。";
            return false;
        }

        string originalRegister = File.ReadAllText(registerPath);
        var createdFiles = new List<string>();
        try
        {
            JObject inherited = difficulties.Children<JObject>().FirstOrDefault();
            string chart = CreateManagedChart(folder, request.chartPath, request.difficultyName, createdFiles);
            string audio = CreateOrInheritAsset(folder, request.audioPath, inherited,
                "audioResourcePath", "audio", AudioExtensions, createdFiles);
            if (string.IsNullOrEmpty(audio))
                throw new InvalidDataException("請選擇歌曲音訊，或確認原有難度具備歌曲音訊。");
            string piano = CreateOrInheritAsset(folder, request.pianoAudioPath, inherited,
                "pianoAudioResourcePath", "piano", AudioExtensions, createdFiles);
            string cover = CreateOrInheritAsset(folder, request.coverPath, inherited,
                "coverResourcePath", "cover", ImageExtensions, createdFiles);

            var difficulty = new JObject
            {
                ["difficultyName"] = request.difficultyName.Trim(),
                ["difficultyLevel"] = request.difficultyLevel,
                ["chartFileName"] = RelativeWithoutExtension(folder, chart),
                ["audioResourcePath"] = audio
            };
            if (!string.IsNullOrEmpty(piano)) difficulty["pianoAudioResourcePath"] = piano;
            if (!string.IsNullOrEmpty(cover)) difficulty["coverResourcePath"] = cover;
            difficulties.Add(difficulty);

            WriteAndValidateRegister(registerPath, folder, metadata);
            message = $"已新增難度：{request.difficultyName.Trim()} Lv. {request.difficultyLevel}";
            return true;
        }
        catch (Exception ex)
        {
            File.WriteAllText(registerPath, originalRegister);
            DeleteCreatedFiles(createdFiles);
            message = "新增難度失敗：" + ex.Message;
            return false;
        }
    }

    public static bool UpdateDifficulty(string id, int difficultyIndex,
        DifficultyEditRequest request, out string message)
    {
        if (!TryGetSongDocument(id, out _, out string folder, out string registerPath,
                out JObject metadata, out message))
            return false;
        if (!ValidateDifficultyEditRequest(request, false, out message))
            return false;

        JArray difficulties = metadata["difficulties"] as JArray;
        if (difficulties == null || difficultyIndex < 0 || difficultyIndex >= difficulties.Count ||
            !(difficulties[difficultyIndex] is JObject difficulty))
        {
            message = "找不到要修改的難度。";
            return false;
        }
        if (HasDifficultyName(difficulties, request.difficultyName, difficultyIndex))
        {
            message = "難度名稱已存在，請使用不同名稱。";
            return false;
        }

        string originalRegister = File.ReadAllText(registerPath);
        var createdFiles = new List<string>();
        try
        {
            difficulty["difficultyName"] = request.difficultyName.Trim();
            difficulty["difficultyLevel"] = request.difficultyLevel;
            if (!string.IsNullOrWhiteSpace(request.chartPath))
                difficulty["chartFileName"] = RelativeWithoutExtension(folder,
                    CreateManagedChart(folder, request.chartPath, request.difficultyName, createdFiles));
            if (!string.IsNullOrWhiteSpace(request.audioPath))
                difficulty["audioResourcePath"] = CopyManagedAsset(folder, request.audioPath,
                    request.difficultyName, "audio", AudioExtensions, createdFiles);
            if (!string.IsNullOrWhiteSpace(request.pianoAudioPath))
                difficulty["pianoAudioResourcePath"] = CopyManagedAsset(folder, request.pianoAudioPath,
                    request.difficultyName, "piano", AudioExtensions, createdFiles);
            if (!string.IsNullOrWhiteSpace(request.coverPath))
                difficulty["coverResourcePath"] = CopyManagedAsset(folder, request.coverPath,
                    request.difficultyName, "cover", ImageExtensions, createdFiles);

            WriteAndValidateRegister(registerPath, folder, metadata);
            RemoveUnreferencedOwnedAssets(folder, JObject.Parse(originalRegister), metadata);
            message = $"已修改難度：{request.difficultyName.Trim()} Lv. {request.difficultyLevel}";
            return true;
        }
        catch (Exception ex)
        {
            File.WriteAllText(registerPath, originalRegister);
            DeleteCreatedFiles(createdFiles);
            message = "修改難度失敗：" + ex.Message;
            return false;
        }
    }

    public static bool DeleteDifficulty(string id, int difficultyIndex, out string message)
    {
        if (!TryGetSongDocument(id, out _, out string folder, out string registerPath,
                out JObject metadata, out message))
            return false;

        JArray difficulties = metadata["difficulties"] as JArray;
        if (difficulties == null || difficultyIndex < 0 || difficultyIndex >= difficulties.Count ||
            !(difficulties[difficultyIndex] is JObject difficulty))
        {
            message = "找不到要刪除的難度。";
            return false;
        }
        if (difficulties.Count <= 1)
        {
            message = "曲目至少必須保留一個難度；若不需要此曲目，請使用「刪除曲目」。";
            return false;
        }

        string label = (string)difficulty["difficultyName"] ?? $"難度 {difficultyIndex + 1}";
        string originalRegister = File.ReadAllText(registerPath);
        try
        {
            JObject before = JObject.Parse(originalRegister);
            difficulties.RemoveAt(difficultyIndex);
            WriteAndValidateRegister(registerPath, folder, metadata);
            RemoveUnreferencedOwnedAssets(folder, before, metadata);
            message = $"已刪除難度：{label}";
            return true;
        }
        catch (Exception ex)
        {
            File.WriteAllText(registerPath, originalRegister);
            message = "刪除難度失敗：" + ex.Message;
            return false;
        }
    }

    public static bool Delete(string id, out string message)
    {
        IndexData index = ReadIndex(null);
        int position = index.songs.FindIndex(item => item != null &&
            string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase));
        if (position < 0)
        {
            message = "目前選擇的不是可刪除的匯入曲目。";
            return false;
        }

        try
        {
            string folder = GetOwnedSongFolder(index.songs[position].folderName);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            index.songs.RemoveAt(position);
            WriteIndex(index);
            message = "曲目已刪除。";
            return true;
        }
        catch (Exception ex)
        {
            message = "刪除失敗：" + ex.Message;
            return false;
        }
    }

    public static string ToLocalPath(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(ExternalPrefix, StringComparison.OrdinalIgnoreCase)
            ? value.Substring(ExternalPrefix.Length)
            : null;
    }

    private static bool TryGetSongDocument(string id, out Entry entry, out string folder,
        out string registerPath, out JObject metadata, out string message)
    {
        entry = null;
        folder = registerPath = null;
        metadata = null;
        IndexData index = ReadIndex(null);
        entry = index.songs.Find(item => item != null &&
            string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            message = "找不到指定的匯入曲目。";
            return false;
        }

        try
        {
            folder = GetOwnedSongFolder(entry.folderName);
            registerPath = Path.Combine(folder, "register.json");
            metadata = JObject.Parse(File.ReadAllText(registerPath));
            message = null;
            return true;
        }
        catch (Exception ex)
        {
            message = "讀取曲目資料失敗：" + ex.Message;
            return false;
        }
    }

    private static bool ValidateDifficultyEditRequest(DifficultyEditRequest request,
        bool requireChart, out string message)
    {
        var result = new ValidationResult();
        if (request == null)
        {
            message = "沒有可用的難度資料。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.difficultyName))
            result.errors.Add("請輸入難度名稱。");
        if (request.difficultyLevel < 1 || request.difficultyLevel > 99)
            result.errors.Add("難度等級必須介於 1 到 99。");

        ValidateFile(result, request.chartPath, "譜面", new[] { ".json", ".xml" }, requireChart);
        ValidateFile(result, request.audioPath, "歌曲音訊", AudioExtensions, false);
        ValidateFile(result, request.pianoAudioPath, "鋼琴音訊", AudioExtensions, false);
        ValidateFile(result, request.coverPath, "曲繪", ImageExtensions, false);

        string chartPath = CleanPath(request.chartPath);
        if (!string.IsNullOrEmpty(chartPath) && File.Exists(chartPath))
        {
            try { ValidateChartObject(LoadAndNormalizeChart(chartPath), result); }
            catch (Exception ex) { result.errors.Add("譜面無法解析：" + ex.Message); }
        }
        message = result.ToDisplayText();
        return result.IsValid;
    }

    private static bool HasDifficultyName(JArray difficulties, string name, int exceptIndex)
    {
        string expected = (name ?? string.Empty).Trim();
        for (int i = 0; i < difficulties.Count; i++)
        {
            if (i == exceptIndex || !(difficulties[i] is JObject difficulty)) continue;
            if (string.Equals(((string)difficulty["difficultyName"] ?? string.Empty).Trim(),
                    expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string CreateManagedChart(string folder, string source, string difficultyName,
        List<string> createdFiles)
    {
        string cleanSource = CleanPath(source);
        JObject normalized = LoadAndNormalizeChart(cleanSource);
        string difficultyFolder = Path.Combine(folder, "Difficulties");
        Directory.CreateDirectory(difficultyFolder);
        string stem = SanitizeFolderName(difficultyName) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string target = Path.Combine(difficultyFolder, stem + ".json");
        File.WriteAllText(target, normalized.ToString(Formatting.Indented));
        createdFiles.Add(target);
        return target;
    }

    private static string CreateOrInheritAsset(string folder, string source, JObject inherited,
        string propertyName, string kind, string[] extensions, List<string> createdFiles)
    {
        if (!string.IsNullOrWhiteSpace(source))
            return CopyManagedAsset(folder, source, (string)inherited?["difficultyName"],
                kind, extensions, createdFiles);
        return (string)inherited?[propertyName];
    }

    private static string CopyManagedAsset(string folder, string source, string difficultyName,
        string kind, string[] extensions, List<string> createdFiles)
    {
        string cleanSource = CleanPath(source);
        if (!File.Exists(cleanSource) ||
            !extensions.Any(ext => string.Equals(Path.GetExtension(cleanSource), ext,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"{kind} 檔案不存在或格式不受支援。");

        string assetFolder = Path.Combine(folder, "Assets");
        Directory.CreateDirectory(assetFolder);
        string stem = SanitizeFolderName(difficultyName) + "_" + kind + "_" +
                      Guid.NewGuid().ToString("N").Substring(0, 8);
        string target = Path.Combine(assetFolder,
            stem + Path.GetExtension(cleanSource).ToLowerInvariant());
        File.Copy(cleanSource, target, false);
        createdFiles.Add(target);
        return RelativeWithoutExtension(folder, target);
    }

    private static void WriteAndValidateRegister(string registerPath, string folder, JObject metadata)
    {
        string temporary = registerPath + ".tmp";
        File.WriteAllText(temporary, metadata.ToString(Formatting.Indented));
        if (File.Exists(registerPath)) File.Replace(temporary, registerPath, null);
        else File.Move(temporary, registerPath);

        ValidationResult generated = ValidateSource(folder);
        if (!generated.IsValid)
            throw new InvalidDataException(generated.ToDisplayText());
    }

    private static void DeleteCreatedFiles(List<string> files)
    {
        if (files == null) return;
        for (int i = 0; i < files.Count; i++)
        {
            try { if (File.Exists(files[i])) File.Delete(files[i]); }
            catch { }
        }
    }

    private static void RemoveUnreferencedOwnedAssets(string folder, JObject before, JObject after)
    {
        var oldPaths = CollectResolvedAssetPaths(folder, before);
        var retainedPaths = CollectResolvedAssetPaths(folder, after);
        string ownedRoot = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (string path in oldPaths)
        {
            if (retainedPaths.Contains(path) ||
                !path.StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ExternalSongLibrary] Could not remove unused asset '{path}': {ex.Message}");
            }
        }
    }

    private static HashSet<string> CollectResolvedAssetPaths(string folder, JObject metadata)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JArray difficulties = metadata?["difficulties"] as JArray;
        if (difficulties == null) return result;
        foreach (JObject difficulty in difficulties.Children<JObject>())
        {
            AddResolvedPath(result, ResolveAsset(folder, (string)difficulty["chartFileName"],
                new[] { ".json" }));
            AddResolvedPath(result, ResolveAsset(folder, (string)difficulty["audioResourcePath"],
                AudioExtensions));
            AddResolvedPath(result, ResolveAsset(folder, (string)difficulty["pianoAudioResourcePath"],
                AudioExtensions));
            AddResolvedPath(result, ResolveAsset(folder, (string)difficulty["coverResourcePath"],
                ImageExtensions));
        }
        return result;
    }

    private static void AddResolvedPath(HashSet<string> paths, string path)
    {
        if (!string.IsNullOrEmpty(path)) paths.Add(Path.GetFullPath(path));
    }

    private static JObject LoadAndNormalizeChart(string chartPath)
    {
        string extension = Path.GetExtension(chartPath).ToLowerInvariant();
        JObject chart = extension == ".xml" ? ConvertXmlChart(chartPath) : JObject.Parse(File.ReadAllText(chartPath));
        if (chart["first_bpm"] == null && chart["bpm"] != null) chart["first_bpm"] = chart["bpm"];
        double bpm = (double?)chart["first_bpm"] ?? 0d;
        if (bpm > 10000d) bpm /= 100000d;
        chart["first_bpm"] = bpm;
        chart["bpm"] = bpm;
        return chart;
    }

    private static JObject ConvertXmlChart(string path)
    {
        XDocument document = XDocument.Load(path, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("XML 沒有根節點。");
        XElement noteData = root.Element("note_data") ?? throw new InvalidDataException("XML 找不到 note_data。");
        double bpm = ReadDouble(root, "header/first_bpm", "header/bpm", "first_bpm", "bpm");
        if (bpm > 10000d) bpm /= 100000d;
        int finish = (int)Math.Round(ReadDouble(root,
            "header/music_finish_time_msec", "music_finish_time_msec"));
        int numerator = (int)Math.Round(ReadDouble(root,
            "header/time_signature_numerator", "time_signature_numerator"));
        int denominator = (int)Math.Round(ReadDouble(root,
            "header/time_signature_denominator", "time_signature_denominator"));
        if (numerator <= 0) numerator = 4;
        if (denominator <= 0) denominator = 4;

        var rawNotes = noteData.Elements("note").ToList();
        int minObserved = rawNotes.Count == 0 ? 0 : rawNotes.Min(note => ReadInt(note, "min_key_index"));
        int maxObserved = rawNotes.Count == 0 ? 0 : rawNotes.Max(note => ReadInt(note, "max_key_index"));
        int laneBase = minObserved >= 1 && maxObserved <= 28 ? 1 : 0;
        var notes = new JArray();
        foreach (XElement source in rawNotes)
        {
            int start = ReadInt(source, "start_timing_msec");
            int end = ReadInt(source, "end_timing_msec", start);
            int noteType = ReadInt(source, "note_type");
            int minLane = Mathf.Clamp(ReadInt(source, "min_key_index") - laneBase, 0, 27);
            int maxLane = Mathf.Clamp(ReadInt(source, "max_key_index") - laneBase, minLane, 27);
            var note = new JObject
            {
                ["startTime"] = start,
                ["endTime"] = end,
                ["gateTime"] = ReadInt(source, "gate_time_msec", Math.Max(0, end - start)),
                ["startLane"] = minLane,
                ["endLane"] = maxLane,
                ["type"] = NoteTypeName(noteType),
                ["note_type"] = noteType,
                ["hand"] = ReadInt(source, "hand"),
                ["param1"] = ReadInt(source, "param1"),
                ["param2"] = ReadInt(source, "param2"),
                ["param3"] = ReadInt(source, "param3")
            };
            int? index = TryReadInt(source, "index");
            int? scalePiano = TryReadInt(source, "scale_piano");
            int? track = TryReadInt(source, "track");
            if (index.HasValue) note["index"] = index.Value;
            if (scalePiano.HasValue) note["pitch"] = Mathf.Clamp(scalePiano.Value + 20, 21, 108);
            if (track.HasValue) note["track"] = track.Value;

            XElement subRoot = source.Element("sub_note_data");
            if (subRoot != null)
            {
                var subs = new JArray();
                foreach (XElement sub in subRoot.Elements())
                {
                    var converted = new JObject();
                    foreach (string key in new[]
                    {
                        "start_timing_msec", "end_timing_msec", "scale_piano", "velocity",
                        "track_index", "src_min_key", "src_max_key", "src_note_type", "src_hand",
                        "src_pitch", "src_velocity", "src_track"
                    })
                    {
                        int? value = TryReadInt(sub, key);
                        if (value.HasValue) converted[key] = value.Value;
                    }
                    subs.Add(converted);
                }
                if (subs.Count > 0) note["subNotes"] = subs;
            }
            notes.Add(note);
        }

        var beats = new JArray();
        XElement beatData = root.Element("beat_data");
        if (beatData != null)
        {
            foreach (int value in beatData.Elements("beat")
                         .Select(beat => ReadInt(beat, "start_timing_msec"))
                         .Distinct().OrderBy(value => value))
                beats.Add(value);
        }

        var result = new JObject
        {
            ["bpm"] = bpm,
            ["first_bpm"] = bpm,
            ["music_finish_time_msec"] = finish,
            ["time_signature"] = numerator + "/" + denominator,
            ["time_signature_numerator"] = numerator,
            ["time_signature_denominator"] = denominator,
            ["notes"] = notes,
            ["beat_timings"] = beats
        };

        // Sustain pedal is an extra sibling section the editor writes next to
        // note_data. Official charts have none, so its absence is normal and
        // the key is simply left out rather than written as an empty array.
        JArray pedal = ReadPedalData(root);
        if (pedal.Count > 0) result["pedal_data"] = pedal;
        return result;
    }

    /// <summary>
    /// Reads &lt;pedal_data&gt;/&lt;pedal&gt; into the same shape the JSON charts use.
    /// Spans are dropped rather than repaired when reversed or empty.
    /// </summary>
    private static JArray ReadPedalData(XElement root)
    {
        var spans = new JArray();
        XElement pedalData = root.Element("pedal_data");
        if (pedalData == null) return spans;

        foreach (XElement pedal in pedalData.Elements("pedal"))
        {
            int start = ReadInt(pedal, "start_timing_msec");
            int end = ReadInt(pedal, "end_timing_msec", start);
            if (end <= start) continue;
            spans.Add(new JObject { ["start_ms"] = start, ["end_ms"] = end });
        }
        return spans;
    }

    private static void ValidateChartObject(JObject chart, ValidationResult result)
    {
        double bpm = (double?)chart["first_bpm"] ?? (double?)chart["bpm"] ?? 0d;
        if (bpm > 10000d) bpm /= 100000d;
        if (bpm <= 0d || bpm > 1000d) result.errors.Add("譜面 BPM 不在合理範圍內。");
        int finish = (int?)chart["music_finish_time_msec"] ?? 0;
        if (finish <= 0) result.errors.Add("譜面缺少有效的 music_finish_time_msec。");
        JArray notes = chart["notes"] as JArray;
        if (notes == null || notes.Count == 0)
        {
            result.errors.Add("譜面沒有可遊玩的 notes。");
            return;
        }

        int invalid = 0;
        foreach (JObject note in notes.Children<JObject>())
        {
            int start = (int?)note["startTime"] ?? (int?)note["start_timing_msec"] ?? -1;
            int end = (int?)note["endTime"] ?? (int?)note["end_timing_msec"] ?? start;
            int minLane = (int?)note["startLane"] ?? (int?)note["min_key_index"] ?? -1;
            int maxLane = (int?)note["endLane"] ?? (int?)note["max_key_index"] ?? minLane;
            if (start < 0 || end < start || minLane < 0 || maxLane < minLane || maxLane > 28) invalid++;
        }
        if (invalid > 0) result.errors.Add($"譜面有 {invalid} 個音符的時間或鍵位不合法。");

        JArray beats = chart["beat_timings"] as JArray;
        if (beats == null || beats.Count < 2)
            result.warnings.Add("譜面沒有完整 beat_timings，變速提示可能無法運作。");
    }

    private static void RewriteAssetPaths(JObject metadata, string songFolder)
    {
        JArray difficulties = metadata["difficulties"] as JArray;
        if (difficulties == null) return;
        foreach (JObject diff in difficulties.Children<JObject>())
        {
            RewritePath(diff, "chartFileName", songFolder, new[] { ".json" });
            RewritePath(diff, "audioResourcePath", songFolder, AudioExtensions);
            RewritePath(diff, "pianoAudioResourcePath", songFolder, AudioExtensions);
            RewritePath(diff, "coverResourcePath", songFolder, ImageExtensions);
            RewritePath(diff, "videoPath", songFolder, new[] { ".mp4", ".webm", ".mov" });
        }
    }

    private static void RewritePath(JObject owner, string key, string folder, string[] extensions)
    {
        string value = (string)owner[key];
        if (string.IsNullOrWhiteSpace(value)) return;
        string resolved = ResolveAsset(folder, value, extensions);
        if (!string.IsNullOrEmpty(resolved)) owner[key] = ExternalPrefix + resolved;
    }

    private static void RequireAsset(ValidationResult result, string folder, string value,
        string difficulty, string kind, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(value) || ResolveAsset(folder, value, extensions) == null)
            result.errors.Add($"{difficulty}：找不到{kind}（{value ?? "未設定"}）。");
    }

    private static void OptionalAsset(ValidationResult result, string folder, string value,
        string difficulty, string kind, string[] extensions)
    {
        if (!string.IsNullOrWhiteSpace(value) && ResolveAsset(folder, value, extensions) == null)
            result.warnings.Add($"{difficulty}：找不到選填的{kind}（{value}）。");
    }

    private static string ResolveAsset(string folder, string configuredPath, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        string path = configuredPath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar).Trim();
        if (Path.IsPathRooted(path) && File.Exists(path)) return Path.GetFullPath(path);

        string folderName = new DirectoryInfo(folder).Name;
        string songsPrefix = "songs" + Path.DirectorySeparatorChar + folderName + Path.DirectorySeparatorChar;
        if (path.StartsWith(songsPrefix, StringComparison.OrdinalIgnoreCase))
            path = path.Substring(songsPrefix.Length);
        else if (path.StartsWith("songs" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            int first = path.IndexOf(Path.DirectorySeparatorChar);
            int second = first >= 0 ? path.IndexOf(Path.DirectorySeparatorChar, first + 1) : -1;
            if (second >= 0 && second + 1 < path.Length) path = path.Substring(second + 1);
        }
        else if (path.StartsWith(folderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            path = path.Substring(folderName.Length + 1);

        string candidate = Path.GetFullPath(Path.Combine(folder, path));
        if (!candidate.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) return null;
        if (File.Exists(candidate)) return candidate;
        foreach (string extension in extensions)
            if (File.Exists(candidate + extension)) return candidate + extension;
        // Path.HasExtension is true for any dot in the file name, so titles such as
        // "Etude Op.25 No.11" or "...ice-vs.-morimori-atsushi" look extension-bearing.
        // Only give up early when the trailing segment really is one of the extensions
        // we handle; otherwise fall through to the basename scan below.
        if (extensions.Any(ext => string.Equals(Path.GetExtension(candidate), ext,
                StringComparison.OrdinalIgnoreCase))) return null;

        string basename = Path.GetFileName(path);
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(file), basename, StringComparison.OrdinalIgnoreCase) &&
                extensions.Any(ext => string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)))
                return file;
        }
        return null;
    }

    private static string FindRegisterPath(string sourcePath)
    {
        string expanded = CleanPath(sourcePath);
        if (string.IsNullOrEmpty(expanded)) return null;
        if (File.Exists(expanded) &&
            string.Equals(Path.GetFileName(expanded), "register.json", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(expanded);
        if (Directory.Exists(expanded))
        {
            string direct = Path.Combine(expanded, "register.json");
            if (File.Exists(direct)) return Path.GetFullPath(direct);
        }
        return null;
    }

    private static void SynchronizePortableFolders()
    {
        if (synchronizingPortableFolders) return;
        synchronizingPortableFolders = true;
        try
        {
            string indexPath = Path.Combine(RootPath, IndexFileName);
            IndexData index = JsonConvert.DeserializeObject<IndexData>(
                File.ReadAllText(indexPath)) ?? new IndexData();
            index.songs ??= new List<Entry>();
            index.categories ??= new List<string>();
            Dictionary<string, List<string>> categoryMap = ReadPortableCategoryMap();
            bool changed = false;

            foreach (string directory in Directory.GetDirectories(RootPath))
            {
                string register = Path.Combine(directory, "register.json");
                if (!File.Exists(register)) continue;
                string folderName = new DirectoryInfo(directory).Name;
                Entry entry = index.songs.Find(item => item != null &&
                    string.Equals(item.folderName, folderName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    List<string> categories = categoryMap.TryGetValue(folderName, out var mapped)
                        ? new List<string>(mapped)
                        : new List<string> { DefaultCategory };
                    entry = new Entry
                    {
                        id = "portable:" + folderName,
                        folderName = folderName,
                        category = categories[0],
                        categories = categories
                    };
                    index.songs.Add(entry);
                    changed = true;
                }
                else if (categoryMap.TryGetValue(folderName, out var mapped) &&
                         (entry.categories == null || entry.categories.Count == 0))
                {
                    entry.categories = new List<string>(mapped);
                    entry.category = entry.categories[0];
                    changed = true;
                }
            }

            foreach (Entry entry in index.songs)
            {
                if (entry == null) continue;
                List<string> normalized = GetEntryCategories(entry);
                if (entry.categories == null || !entry.categories.SequenceEqual(normalized,
                        StringComparer.OrdinalIgnoreCase))
                {
                    entry.categories = normalized;
                    changed = true;
                }
                if (!string.Equals(entry.category, normalized[0], StringComparison.Ordinal))
                {
                    entry.category = normalized[0];
                    changed = true;
                }
                for (int i = 0; i < normalized.Count; i++)
                {
                    if (!index.categories.Any(item => string.Equals(item, normalized[i],
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        index.categories.Add(normalized[i]);
                        changed = true;
                    }
                }
            }

            if (changed)
                File.WriteAllText(indexPath,
                    JsonConvert.SerializeObject(index, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ExternalSongLibrary] Could not synchronize portable song folders: " +
                             ex.Message);
        }
        finally
        {
            synchronizingPortableFolders = false;
        }
    }

    private static Dictionary<string, List<string>> ReadPortableCategoryMap()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(RootPath, "songlist.json");
        if (!File.Exists(path)) return result;
        try
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            JObject categories = root["categories"] as JObject;
            if (categories == null) return result;
            foreach (JProperty categoryProperty in categories.Properties())
            {
                string category = NormalizeCategory(categoryProperty.Name);
                if (!(categoryProperty.Value is JArray folders)) continue;
                foreach (JToken token in folders)
                {
                    string folder = token.Type == JTokenType.String ? token.Value<string>()?.Trim() : null;
                    if (string.IsNullOrEmpty(folder)) continue;
                    if (!result.TryGetValue(folder, out List<string> values))
                    {
                        values = new List<string>();
                        result[folder] = values;
                    }
                    if (!values.Any(item => string.Equals(item, category,
                            StringComparison.OrdinalIgnoreCase)))
                        values.Add(category);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ExternalSongLibrary] Could not read portable song categories: " +
                             ex.Message);
        }
        return result;
    }

    private static List<string> GetEntryCategories(Entry entry)
    {
        var result = new List<string>();
        if (entry?.categories != null) AddDistinctCategories(result, entry.categories);
        if (result.Count == 0 && entry != null)
            AddDistinctCategories(result, new[] { entry.category });
        if (result.Count == 0) result.Add(DefaultCategory);
        return result;
    }

    private static IndexData ReadIndex(List<string> warnings)
    {
        EnsureCreated();
        try
        {
            IndexData data = JsonConvert.DeserializeObject<IndexData>(
                File.ReadAllText(Path.Combine(RootPath, IndexFileName))) ?? new IndexData();
            data.songs ??= new List<Entry>();
            data.categories ??= new List<string>();
            bool migrated = false;
            foreach (Entry entry in data.songs)
            {
                if (entry != null && (string.IsNullOrWhiteSpace(entry.category) ||
                    string.Equals(entry.category, AllCategory, StringComparison.OrdinalIgnoreCase)))
                {
                    entry.category = DefaultCategory;
                    migrated = true;
                }
            }
            int removed = data.categories.RemoveAll(category =>
                string.IsNullOrWhiteSpace(category) ||
                string.Equals(category, AllCategory, StringComparison.OrdinalIgnoreCase));
            migrated |= removed > 0;
            if (migrated)
                File.WriteAllText(Path.Combine(RootPath, IndexFileName),
                    JsonConvert.SerializeObject(data, Formatting.Indented));
            return data;
        }
        catch (Exception ex)
        {
            warnings?.Add("曲庫索引無法讀取：" + ex.Message);
            return new IndexData();
        }
    }

    private static void WriteIndex(IndexData index)
    {
        EnsureCreated();
        string target = Path.Combine(RootPath, IndexFileName);
        string temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(index, Formatting.Indented));
        if (File.Exists(target)) File.Replace(temporary, target, null);
        else File.Move(temporary, target);
    }

    private static string GetOwnedSongFolder(string folderName)
    {
        string root = Path.GetFullPath(RootPath) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(RootPath, folderName ?? string.Empty));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("曲目資料夾超出玩家曲庫範圍。");
        return path;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectoryContents(directory, Path.Combine(destination, Path.GetFileName(directory)));
        foreach (string file in Directory.GetFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
    }

    private static string CopyNamedAsset(string source, string destination, string stem)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        string target = Path.Combine(destination, stem + Path.GetExtension(source).ToLowerInvariant());
        File.Copy(source, target, false);
        return target;
    }

    private static string RelativeWithoutExtension(string root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string relative = path.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.ChangeExtension(relative, null).Replace('\\', '/');
    }

    private static void ValidateFile(ValidationResult result, string value, string label,
        string[] extensions, bool required)
    {
        string path = CleanPath(value);
        if (string.IsNullOrEmpty(path))
        {
            if (required) result.errors.Add($"請選擇{label}檔案。");
            return;
        }
        if (!File.Exists(path))
        {
            result.errors.Add($"{label}檔案不存在。");
            return;
        }
        if (!extensions.Any(ext => string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase)))
            result.errors.Add($"{label}格式不支援（{Path.GetExtension(path)}）。");
    }

    private static void ValidateNonEmptyFile(ValidationResult result, string path, string label)
    {
        if (new FileInfo(path).Length <= 16) result.errors.Add($"{label}檔案是空的或已損壞。");
    }

    private static void ValidateImageHeader(ValidationResult result, string path)
    {
        ValidateNonEmptyFile(result, path, "曲繪");
        byte[] header = new byte[8];
        using (FileStream stream = File.OpenRead(path))
            stream.Read(header, 0, header.Length);
        bool png = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        bool jpeg = header[0] == 0xFF && header[1] == 0xD8;
        if (!png && !jpeg) result.errors.Add("曲繪內容不是有效的 PNG 或 JPEG。");
    }

    private static string CleanPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    }

    private static void AddDistinctCategories(List<string> target, IEnumerable<string> values)
    {
        if (values == null) return;
        foreach (string raw in values)
        {
            string value = NormalizeCategory(raw);
            if (!target.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                target.Add(value);
        }
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) ||
            string.Equals(category.Trim(), AllCategory, StringComparison.OrdinalIgnoreCase))
            return DefaultCategory;
        return category.Trim();
    }

    private static string SanitizeFolderName(string value)
    {
        value ??= string.Empty;
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(value) ? "ImportedSong" : value;
    }

    private static string NoteTypeName(int value)
    {
        if (value == 64 || (value & 64) != 0) return "trill";
        switch (value & ~128)
        {
            case 1: return "soft";
            case 2: return "hold";
            case 3: return "staccato";
            case 4: return "slide";
            default: return "tap";
        }
    }

    private static int ReadInt(XElement owner, string path, int fallback = 0)
    {
        int? value = TryReadInt(owner, path);
        return value ?? fallback;
    }

    private static int? TryReadInt(XElement owner, string path)
    {
        XElement element = FindElement(owner, path);
        string raw = element?.Value ?? owner.Attribute(path)?.Value;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? (int)Math.Round(value)
            : (int?)null;
    }

    private static double ReadDouble(XElement owner, params string[] paths)
    {
        foreach (string path in paths)
        {
            XElement element = FindElement(owner, path);
            string raw = element?.Value ?? owner.Attribute(path)?.Value;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
        }
        return 0d;
    }

    private static XElement FindElement(XElement owner, string path)
    {
        XElement current = owner;
        foreach (string part in path.Split('/'))
        {
            current = current?.Element(part);
            if (current == null) break;
        }
        return current;
    }
}
