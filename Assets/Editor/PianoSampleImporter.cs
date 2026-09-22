using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the rendered piano sample library.
/// </summary>
/// <remarks>
/// This is a keysound bank, and dense playing keeps dozens of these sounding at
/// once. Anything kept compressed in memory decodes on the audio thread once per
/// voice, so sixty-odd simultaneous notes mean sixty-odd decoders competing for
/// the audio callback — heard as notes dropping out under exactly the passages
/// that need them most.
///
/// Decompressing at load trades that for memory: the whole bank is about 41 MB of
/// 16-bit mono, which is nothing on the machines this runs on, and playback then
/// costs only mixing. ADPCM keeps the files small on disk without affecting that,
/// since the clips live as PCM in memory either way.
///
/// Applied automatically so a re-render (render_piano_samples.py) never silently
/// reverts to defaults.
/// </remarks>
public class PianoSampleImporter : AssetPostprocessor
{
    private const string SampleFolder = "Assets/Resources/PianoSamples/";

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(SampleFolder)) return;

        var importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.ADPCM;
        // Preloading is per-platform in Unity 6; the bank must be resident before
        // the first note rather than loading on the first hit of a song.
        settings.preloadAudioData = true;

        importer.defaultSampleSettings = settings;
        // The renderer already writes mono; forcing it keeps a stray stereo file
        // from doubling the memory of one voice.
        importer.forceToMono = true;
        importer.loadInBackground = false;
    }
}
