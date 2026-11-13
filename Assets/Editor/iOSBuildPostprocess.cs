#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

/// <summary>
/// iOS 建置後改 Info.plist：讓 App 的 Documents（曲庫 UserSongs 所在）出現在
/// 「檔案」App 和 Windows 版 iTunes 的「檔案共享」裡。
/// </summary>
/// <remarks>
/// 沒有這兩個鍵，曲庫資料夾存在但使用者完全碰不到，歌放不進去。
/// 只在裝了 iOS Build Support 的 Editor 編譯（UnityEditor.iOS.Xcode 只在那裡有）。
/// </remarks>
public sealed class iOSBuildPostprocess : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;

        string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        PlistElementDict root = plist.root;
        root.SetBoolean("UIFileSharingEnabled", true);
        root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
        // 不支援分割畫面／滑動顯示：視窗被擠窄時 28 道會窄到按不準
        root.SetBoolean("UIRequiresFullScreen", true);
        plist.WriteToFile(plistPath);
    }
}
#endif
