using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class SongImageAutoRepair
{
    private const string RepairMenu = "Tools/Songs/Repair Image Formats";

    private static bool repairScheduled;
    private static bool repairRunning;

    static SongImageAutoRepair()
    {
        ScheduleRepair();
    }

    internal static bool IsRepairing => repairRunning;

    internal static void ScheduleRepair()
    {
        if (repairScheduled || repairRunning)
        {
            return;
        }

        repairScheduled = true;
        EditorApplication.delayCall += RunScheduledRepair;
    }

    [MenuItem(RepairMenu)]
    public static void RepairFromMenu()
    {
        RepairAll(true, out _);
    }

    internal static bool RepairAll(bool logWhenClean, out string errorSummary)
    {
        errorSummary = string.Empty;
        if (repairRunning)
        {
            return true;
        }

        string songsFullPath = ResolvePortableSongFolder();
        if (!Directory.Exists(songsFullPath))
        {
            errorSummary =
                "Portable song backup does not exist. Expected one of:\n" +
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Songs_backup")) + "\n" +
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                    "Songs_backup", "songs"));
            Debug.LogError($"[SongImageAutoRepair] {errorSummary}");
            return false;
        }

        repairRunning = true;
        var repaired = new List<string>();
        var errors = new List<string>();

        try
        {
            string[] paths = Directory.GetFiles(songsFullPath, "*", SearchOption.AllDirectories)
                .Where(IsCandidateImage)
                .ToArray();

            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                ImageFormat actualFormat = DetectFormat(path);

                if (extension == ".png")
                {
                    if (actualFormat == ImageFormat.Png)
                    {
                        continue;
                    }

                    bool success;
                    string detail;
                    if (actualFormat == ImageFormat.Jpeg)
                    {
                        success = TryRepairJpegAsPng(path, out detail);
                    }
                    else if (actualFormat == ImageFormat.WebP)
                    {
                        success = TryRepairWebPAsPng(path, path, out detail);
                    }
                    else
                    {
                        success = false;
                        detail = "unknown or corrupt image header";
                    }

                    string assetPath = ToAssetPath(path);
                    if (success)
                    {
                        repaired.Add($"{assetPath} ({actualFormat} -> PNG)");
                    }
                    else
                    {
                        errors.Add($"{assetPath}: {detail}");
                    }
                }
                else if (extension == ".webp")
                {
                    string pngPath = ChooseWebPOutputPath(path);
                    if (TryRepairWebPAsPng(path, pngPath, out string detail))
                    {
                        string oldAssetPath = ToAssetPath(path);
                        string newAssetPath = ToAssetPath(pngPath);
                        DeleteAssetAndMeta(path);
                        repaired.Add($"{oldAssetPath} -> {newAssetPath}");
                    }
                    else
                    {
                        errors.Add($"{ToAssetPath(path)}: {detail}");
                    }
                }
            }

            if (repaired.Count > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log(
                    $"[SongImageAutoRepair] Repaired {repaired.Count} image(s):\n" +
                    string.Join("\n", repaired));
            }
            else if (logWhenClean && errors.Count == 0)
            {
                Debug.Log("[SongImageAutoRepair] All song images already use valid PNG/JPEG encoding.");
            }

            if (errors.Count > 0)
            {
                errorSummary = string.Join("\n", errors);
                Debug.LogError(
                    $"[SongImageAutoRepair] Could not repair {errors.Count} image(s). " +
                    "WebP repair requires Python with Pillow installed.\n" + errorSummary);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorSummary = ex.ToString();
            Debug.LogError($"[SongImageAutoRepair] Repair scan failed:\n{errorSummary}");
            return false;
        }
        finally
        {
            repairRunning = false;
        }
    }

    private static void RunScheduledRepair()
    {
        repairScheduled = false;
        RepairAll(false, out _);
    }

    private static string ResolvePortableSongFolder()
    {
        string projectBackup = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Songs_backup"));
        if (Directory.Exists(projectBackup)) return projectBackup;

        string workspaceBackup = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", "Songs_backup", "songs"));
        return workspaceBackup;
    }

    private static bool IsCandidateImage(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".png" || extension == ".webp";
    }

    private static bool TryRepairJpegAsPng(string path, out string detail)
    {
        Texture2D texture = null;
        try
        {
            byte[] source = File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, source, false))
            {
                detail = "Unity could not decode the JPEG data";
                return false;
            }

            byte[] png = ImageConversion.EncodeToPNG(texture);
            if (png == null || png.Length < 8)
            {
                detail = "Unity could not encode the decoded image as PNG";
                return false;
            }

            ReplaceFile(path, png);
            detail = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
        finally
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }

    private static bool TryRepairWebPAsPng(string sourcePath, string destinationPath, out string detail)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        string helperPath = projectRoot == null
            ? string.Empty
            : Path.Combine(projectRoot, "Tools", "repair_song_image.py");

        if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath))
        {
            detail = $"repair helper was not found: {helperPath}";
            return false;
        }

        string temporaryPath = destinationPath + ".song-image-repair.tmp";
        DeleteIfExists(temporaryPath);

        var candidates = new[]
        {
            new PythonCommand("py", "-3 "),
            new PythonCommand("python3", string.Empty),
            new PythonCommand("python", string.Empty)
        };

        var failures = new List<string>();
        foreach (PythonCommand candidate in candidates)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = candidate.Executable,
                    Arguments = candidate.ArgumentPrefix + Quote(helperPath) + " " +
                                Quote(sourcePath) + " " + Quote(temporaryPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        failures.Add($"{candidate.Executable}: failed to start");
                        continue;
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(60000))
                    {
                        process.Kill();
                        failures.Add($"{candidate.Executable}: timed out");
                        continue;
                    }

                    if (process.ExitCode != 0 || !File.Exists(temporaryPath))
                    {
                        string output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                        failures.Add($"{candidate.Executable}: {output.Trim()}");
                        continue;
                    }
                }

                if (DetectFormat(temporaryPath) != ImageFormat.Png)
                {
                    failures.Add($"{candidate.Executable}: helper output was not a valid PNG");
                    DeleteIfExists(temporaryPath);
                    continue;
                }

                byte[] png = File.ReadAllBytes(temporaryPath);
                ReplaceFile(destinationPath, png);
                DeleteIfExists(temporaryPath);
                detail = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failures.Add($"{candidate.Executable}: {ex.Message}");
            }
            finally
            {
                DeleteIfExists(temporaryPath);
            }
        }

        detail = "no usable Python/Pillow converter was found. " + string.Join(" | ", failures);
        return false;
    }

    private static string ChooseWebPOutputPath(string webPPath)
    {
        string normalPng = Path.ChangeExtension(webPPath, ".png");
        if (!File.Exists(normalPng))
        {
            return normalPng;
        }

        string directory = Path.GetDirectoryName(webPPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(webPPath) + "_webp";
        string candidate = Path.Combine(directory, stem + ".png");
        int suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, stem + "_" + suffix + ".png");
            suffix++;
        }

        return candidate;
    }

    private static ImageFormat DetectFormat(string path)
    {
        try
        {
            using (var stream = File.OpenRead(path))
            {
                var header = new byte[12];
                int bytesRead = stream.Read(header, 0, header.Length);
                if (bytesRead < 8)
                {
                    return ImageFormat.Unknown;
                }

                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E &&
                    header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A &&
                    header[6] == 0x1A && header[7] == 0x0A)
                {
                    return ImageFormat.Png;
                }

                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                {
                    return ImageFormat.Jpeg;
                }

                if (bytesRead >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' &&
                    header[2] == (byte)'F' && header[3] == (byte)'F' &&
                    header[8] == (byte)'W' && header[9] == (byte)'E' &&
                    header[10] == (byte)'B' && header[11] == (byte)'P')
                {
                    return ImageFormat.WebP;
                }
            }
        }
        catch
        {
            // The caller will report a useful path-specific error.
        }

        return ImageFormat.Unknown;
    }

    private static void ReplaceFile(string destinationPath, byte[] bytes)
    {
        string temporaryPath = destinationPath + ".song-image-write.tmp";
        DeleteIfExists(temporaryPath);
        File.WriteAllBytes(temporaryPath, bytes);

        try
        {
            if (File.Exists(destinationPath))
            {
                FileUtil.ReplaceFile(temporaryPath, destinationPath);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static void DeleteAssetAndMeta(string fullPath)
    {
        DeleteIfExists(fullPath);
        DeleteIfExists(fullPath + ".meta");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ToAssetPath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/');
        return normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
            ? "Assets" + normalized.Substring(dataPath.Length)
            : normalized;
    }

    private static string Quote(string argument)
    {
        return "\"" + argument.Replace("\"", "\\\"") + "\"";
    }

    private enum ImageFormat
    {
        Unknown,
        Png,
        Jpeg,
        WebP
    }

    private readonly struct PythonCommand
    {
        public PythonCommand(string executable, string argumentPrefix)
        {
            Executable = executable;
            ArgumentPrefix = argumentPrefix;
        }

        public string Executable { get; }
        public string ArgumentPrefix { get; }
    }
}

public sealed class SongImageImportWatcher : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (SongImageAutoRepair.IsRepairing)
        {
            return;
        }

        bool songsChanged = importedAssets.Concat(movedAssets).Any(IsSongImage);
        if (songsChanged)
        {
            SongImageAutoRepair.ScheduleRepair();
        }
    }

    private static bool IsSongImage(string assetPath)
    {
        if (!assetPath.StartsWith("Assets/Resources/songs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(assetPath).ToLowerInvariant();
        return extension == ".png" || extension == ".webp";
    }
}

public sealed class SongImageBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => -11000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!SongImageAutoRepair.RepairAll(false, out string errors))
        {
            throw new BuildFailedException(
                "Song image repair failed. Fix these files before building:\n" + errors);
        }
    }
}
