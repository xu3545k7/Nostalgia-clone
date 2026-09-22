using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 製譜器（qt_editor 的「曲庫管理」）改了曲庫之後，遊戲自動重整選曲清單。
///
/// 製譜器每次寫入 UserSongs 都會更新 <c>UserSongs/.editor_revision.json</c>
/// （revision 遞增，外加改動的曲目 id）。這裡每 0.5 秒看一次它的修改時間：
/// 有新的 revision 就記下來，等到選曲畫面可以安全重整時（不在遊玩中、沒有在切歌）
/// 呼叫 <see cref="SongSelectionManager.RefreshSongLibraryAndFocus"/>，並停在剛改的那首。
///
/// 用輪詢檔案而不是 FileSystemWatcher：Mono 的 FileSystemWatcher 在 Windows 上
/// 事件不穩、還會從別的執行緒回呼；一個小檔的修改時間每 0.5 秒讀一次幾乎沒有成本。
/// 遊玩中照樣只讀時間戳，真正的重整延到回選曲畫面才做。
/// </summary>
public sealed class EditorLibraryWatcher : MonoBehaviour
{
    private const string RevisionFileName = ".editor_revision.json";
    private const float PollSeconds = 0.5f;

    private float nextPoll;
    private DateTime lastWriteUtc;
    private long seenRevision = -1;
    private bool pending;
    private string pendingId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<EditorLibraryWatcher>() != null) return;
        var go = new GameObject("EditorLibraryWatcher");
        DontDestroyOnLoad(go);
        go.AddComponent<EditorLibraryWatcher>();
    }

    private void Start()
    {
        // 啟動時已經存在的 revision 不算「新的改動」，免得一開遊戲就重整一次
        if (TryRead(out long revision, out _, out DateTime stamp))
        {
            seenRevision = revision;
            lastWriteUtc = stamp;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + PollSeconds;
            Poll();
        }
        if (pending) TryApply();
    }

    private void Poll()
    {
        string path = RevisionPath();
        if (path == null || !File.Exists(path)) return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch (Exception) { return; }
        if (stamp == lastWriteUtc) return;
        if (!TryRead(out long revision, out string id, out stamp)) return;
        lastWriteUtc = stamp;
        if (revision == seenRevision) return;
        seenRevision = revision;
        pending = true;
        // 連續改好幾首時停在最後改的那首；只有索引變動（刪除、分類）時 id 是空的
        if (!string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pendingId)) pendingId = id;
    }

    private void TryApply()
    {
        SongSelectionManager selection = SongSelectionManager.Instance;
        if (selection == null || !selection.CanRefreshLibraryNow) return;
        GameManager game = GameManager.Instance;
        if (game != null && game.Conductor != null && game.Conductor.isActive) return;

        pending = false;
        string id = pendingId;
        pendingId = null;
        try
        {
            if (string.IsNullOrEmpty(id)) selection.RefreshSongLibrary();
            else selection.RefreshSongLibraryAndFocus(id);
            Debug.Log("[EditorLibraryWatcher] 製譜器改了曲庫，已重整選曲清單" +
                      (string.IsNullOrEmpty(id) ? "" : "（" + id + "）"));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[EditorLibraryWatcher] 重整曲庫失敗：" + ex.Message);
        }
    }

    private static string RevisionPath()
    {
        try { return Path.Combine(ExternalSongLibrary.RootPath, RevisionFileName); }
        catch (Exception) { return null; }
    }

    private static bool TryRead(out long revision, out string id, out DateTime stamp)
    {
        revision = -1;
        id = null;
        stamp = default;
        string path = RevisionPath();
        if (path == null || !File.Exists(path)) return false;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
            JObject data = JObject.Parse(File.ReadAllText(path));
            revision = data.Value<long?>("revision") ?? -1;
            id = data.Value<string>("id");
            return revision >= 0;
        }
        catch (Exception)
        {
            // 製譜器正在寫（先寫暫存檔再換掉，理論上讀不到半份），下一輪再讀
            return false;
        }
    }
}
