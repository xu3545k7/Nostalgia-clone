#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 預設**不**把曲子放進遊戲 build：遊戲本體、曲庫、製譜器、NosMania 分開發行，
/// build 資料夾裡不該有任何曲子（NosMania 會把遊戲連到另外放的曲庫）。
/// 要舊行為就勾選單 <c>Nostalgia/Build/Bundle Songs Into Build</c>。
///
/// When bundling is on: keeps the large editable song library outside Resources. A player build
/// receives the verified defaults directly as its writable UserSongs folder.
///
/// Building into a folder that already has a library (e.g. one edited with the
/// chart editor that lives next to the game exe) only fills in missing files:
/// existing files are never overwritten, and songs the editor moved to
/// <c>UserSongs_editor/trash</c> are not brought back.
/// </summary>
public sealed class PortableSongLibraryBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    private const string LibraryIndex = "library.json";
    private const string BundlePref = "Nostalgia.BundleSongsIntoBuild";
    private const string BundleMenu = "Nostalgia/Build/Bundle Songs Into Build";

    public static bool BundleSongs
    {
        get => EditorPrefs.GetBool(BundlePref, false);
        set => EditorPrefs.SetBool(BundlePref, value);
    }

    [MenuItem(BundleMenu)]
    private static void ToggleBundleSongs() => BundleSongs = !BundleSongs;

    [MenuItem(BundleMenu, true)]
    private static bool ToggleBundleSongsValidate()
    {
        Menu.SetChecked(BundleMenu, BundleSongs);
        return true;
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (!BundleSongs)
        {
            Debug.Log("[PortableSongLibrary] Songs are not bundled into the build " +
                      "(game and song library ship separately; toggle " + BundleMenu + ").");
            return;
        }
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string projectBackup = Path.Combine(projectRoot, "Songs_backup");
        string workspaceBackup = Path.GetFullPath(
            Path.Combine(projectRoot, "..", "Songs_backup", "songs"));
        string source = Directory.Exists(projectBackup) ? projectBackup : workspaceBackup;
        if (!Directory.Exists(source))
            throw new BuildFailedException("Portable song backup is missing: " + source);

        string output = report.summary.outputPath;
        string outputRoot = Directory.Exists(output)
            ? output
            : Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(outputRoot))
            throw new BuildFailedException("Could not resolve the build output folder.");

        string destination = Path.Combine(outputRoot, "UserSongs");
        bool keepExisting = File.Exists(Path.Combine(destination, LibraryIndex));
        var stats = new CopyStats();
        if (keepExisting)
        {
            HashSet<string> deleted = TrashedFolders(destination);
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                string target = Path.Combine(destination, name);
                if (!Directory.Exists(target) && deleted.Contains(name))
                {
                    stats.TrashedSongs++;
                    continue;
                }
                CopyPortableSongs(directory, target, false, stats);
            }
            foreach (string file in Directory.GetFiles(source))
                CopyFile(file, destination, false, stats);
        }
        else
        {
            CopyPortableSongs(source, destination, true, stats);
        }

        File.WriteAllText(Path.Combine(destination, ".bundled-library-seeded"),
            DateTime.UtcNow.ToString("O"));
        if (keepExisting)
            Debug.Log($"[PortableSongLibrary] Kept the existing library at {destination}: " +
                      $"added {stats.Copied} missing files, left {stats.Skipped} existing files " +
                      $"untouched, skipped {stats.TrashedSongs} songs deleted in the editor.");
        else
            Debug.Log($"[PortableSongLibrary] Copied editable songs to: {destination}");
    }

    private sealed class CopyStats
    {
        public int Copied;
        public int Skipped;
        public int TrashedSongs;
    }

    /// <summary>
    /// Song folder names the chart editor deleted. Its trash entries are named
    /// <c>yyyyMMdd-HHmmss_&lt;folder&gt;</c>.
    /// </summary>
    private static HashSet<string> TrashedFolders(string destination)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string trash = Path.Combine(destination + "_editor", "trash");
        if (!Directory.Exists(trash))
            return names;
        foreach (string entry in Directory.GetDirectories(trash))
        {
            string name = Path.GetFileName(entry);
            int split = name.IndexOf('_');
            if (split == 15 && split + 1 < name.Length)
                names.Add(name.Substring(split + 1));
        }
        return names;
    }

    private static void CopyPortableSongs(string source, string destination, bool overwrite,
        CopyStats stats)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source))
            CopyPortableSongs(directory, Path.Combine(destination, Path.GetFileName(directory)),
                overwrite, stats);
        foreach (string file in Directory.GetFiles(source))
            CopyFile(file, destination, overwrite, stats);
    }

    private static void CopyFile(string file, string destination, bool overwrite, CopyStats stats)
    {
        if (string.Equals(Path.GetExtension(file), ".meta", StringComparison.OrdinalIgnoreCase))
            return;
        string target = Path.Combine(destination, Path.GetFileName(file));
        if (!overwrite && File.Exists(target))
        {
            stats.Skipped++;
            return;
        }
        File.Copy(file, target, overwrite);
        stats.Copied++;
    }
}
#endif
