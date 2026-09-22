using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// Prevents test scenes from silently becoming the startup scene of a player build.
/// </summary>
public sealed class MainSceneBuildGuard : IPreprocessBuildWithReport
{
    private const string MainScenePath = "Assets/Scenes/main_scene.unity";

    public int callbackOrder => -10000;

    public void OnPreprocessBuild(BuildReport report)
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 1 && enabledScenes[0] == MainScenePath)
        {
            return;
        }

        string actual = enabledScenes.Length == 0
            ? "<none>"
            : string.Join(", ", enabledScenes);

        throw new BuildFailedException(
            $"Build scene list is invalid. Enable only '{MainScenePath}'. Current enabled scenes: {actual}");
    }
}
