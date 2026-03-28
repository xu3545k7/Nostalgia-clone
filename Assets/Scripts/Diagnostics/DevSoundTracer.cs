using System.Collections.Generic;

/// <summary>
/// Development helper to selectively allow SFX/HitSound playback based on callstack signatures.
///
/// Usage: Edit EnabledSignatures in code to add a lowercase substring that matches part of the
/// caller stacktrace you want to enable (e.g. "finalizehold(" or "tryreleaseordestroy").
/// When EnabledSignatures is non-empty, SFX/HitSound playback will only be allowed when the
/// current stacktrace contains ANY of the enabled signatures. This lets you re-enable sources
/// one-by-one to isolate which caller triggers unwanted audio.
///
/// NOTE: This file is for development only and guarded by DEVELOPMENT_BUILD checks where used.
/// </summary>
public static class DevSoundTracer
{
    // Add signatures here (all lower-case). Leave empty to disable dev override and use normal logic.
    // NOTE: This set was intentionally left empty to avoid suppressing normal hit sound playback.
    public static readonly HashSet<string> EnabledSignatures = new HashSet<string>()
    {
        // Dev tracing disabled by default. Populate this set only when actively debugging stray sounds.
    };

    public static bool HasEnabledSignatures()
    {
        return EnabledSignatures != null && EnabledSignatures.Count > 0;
    }

    public static bool StackContainsEnabledSignature(string lowerStackTrace)
    {
        if (!HasEnabledSignatures()) return false;
        foreach (var s in EnabledSignatures)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (lowerStackTrace.Contains(s)) return true;
        }
        return false;
    }
}
