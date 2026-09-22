using System;
using System.IO;
using Newtonsoft.Json.Linq;

// Minimal Unity API surface used by ExternalSongLibrary. This lets the storage
// and import code run as a real command-line integration test without launching
// the Unity editor.
namespace UnityEngine
{
    public static class Application
    {
        public static string persistentDataPath;
    }
}

internal static class ExternalSongLibrarySmokeTest
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: smoke-test <source-song-folder> <isolated-persistent-data>");
            return 2;
        }

        UnityEngine.Application.persistentDataPath = Path.GetFullPath(args[1]);
        string source = Path.GetFullPath(args[0]);

        ExternalSongLibrary.ValidationResult validation = ExternalSongLibrary.ValidateSource(source);
        Assert(validation.IsValid, validation.ToDisplayText());

        Assert(ExternalSongLibrary.Import(source, "Integration Test", out string id, out string importMessage),
            importMessage);
        Assert(!string.IsNullOrWhiteSpace(id), "Import did not return an id.");

        var loaded = ExternalSongLibrary.LoadAll(out var warnings);
        Assert(warnings.Count == 0, string.Join(Environment.NewLine, warnings));
        Assert(loaded.Count == 1, $"Expected one imported song, got {loaded.Count}.");
        Assert(loaded[0].entry.category == "Integration Test", "Imported category was not preserved.");

        JObject metadata = JObject.Parse(loaded[0].metadataJson);
        JArray difficulties = metadata["difficulties"] as JArray;
        Assert(difficulties != null && difficulties.Count > 0, "Imported metadata has no difficulties.");
        foreach (JObject difficulty in difficulties.Children<JObject>())
        {
            AssertExternalFile(difficulty, "chartFileName");
            AssertExternalFile(difficulty, "audioResourcePath");
            AssertExternalFile(difficulty, "coverResourcePath");
        }

        Assert(ExternalSongLibrary.Update(id, "Imported Smoke Test", "Codex", "Verified",
            out string updateMessage), updateMessage);
        loaded = ExternalSongLibrary.LoadAll(out warnings);
        metadata = JObject.Parse(loaded[0].metadataJson);
        Assert((string)metadata["displayName"] == "Imported Smoke Test", "Title update was not saved.");
        Assert((string)metadata["author"] == "Codex", "Author update was not saved.");
        Assert(loaded[0].entry.category == "Verified", "Category update was not saved.");

        Assert(ExternalSongLibrary.Delete(id, out string deleteMessage), deleteMessage);
        loaded = ExternalSongLibrary.LoadAll(out warnings);
        Assert(loaded.Count == 0, "Song remained in the library after deletion.");

        Console.WriteLine("PASS: validate -> import -> load paths -> edit -> delete");
        return 0;
    }

    private static void AssertExternalFile(JObject difficulty, string key)
    {
        string value = (string)difficulty[key];
        Assert(value != null && value.StartsWith(ExternalSongLibrary.ExternalPrefix,
            StringComparison.OrdinalIgnoreCase), $"{key} was not rewritten as an external path.");
        Assert(File.Exists(ExternalSongLibrary.ToLocalPath(value)), $"{key} does not resolve to a file.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
