using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Names whatever is glowing near the judgment line, once per song.
/// </summary>
/// <remarks>
/// Bloom turns any emissive value past its 1.05 threshold into a soft ball of
/// light that no longer resembles the object producing it, so a screenshot
/// cannot identify the source and reading the code only produces candidates.
/// This lists what is actually there: every visible renderer near the line,
/// ranked by how far past the bloom threshold its emission sits, plus any lit
/// Light in range. The brightest entry is the thing in the picture.
/// </remarks>
public class BrightObjectProbe : MonoBehaviour
{
    /// <summary>Seconds into a song before the sweep runs, so the scene is settled.</summary>
    private const float DelaySeconds = 8f;

    /// <summary>Bloom threshold from ClassicalAtmosphere; below this nothing blooms.</summary>
    private const float BloomThreshold = 1.05f;

    private const int MaxReported = 16;

    private bool reported;
    private float armedAt = -1f;
    private string reportedSong;
    private int reportCount;

    /// <summary>Object paths that were bright in the previous report.</summary>
    /// <remarks>
    /// The fault appears after a video song and then follows every song played
    /// afterwards, so the useful question is not what is bright — that list is
    /// long and mostly innocent — but what is bright *now that was not before*.
    /// Comparing two long lists by eye is error-prone; the probe can just do it.
    /// </remarks>
    private readonly Dictionary<string, float> previousBright = new Dictionary<string, float>();

    /// <summary>Every active object last report, bright or not.</summary>
    /// <remarks>
    /// The bright list cannot answer "what is different about this song": it drops
    /// everything under the bloom threshold, and a plain white material reads as
    /// exactly 1.00 — under it — while its real brightness comes from a texture the
    /// sweep never samples. The track is exactly that case. Diffing the full set
    /// costs one dictionary and misses nothing.
    /// </remarks>
    private readonly Dictionary<string, float> previousAll = new Dictionary<string, float>();
    private readonly Dictionary<string, float> currentAll = new Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("BrightObjectProbe");
        host.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(host);
        host.AddComponent<BrightObjectProbe>();
    }

    private void Update()
    {
        // No early-out on `reported` here. It used to be the first line, which
        // made the song-change re-arm below unreachable: once a report had been
        // printed, Update returned before it could ever notice a new song. The
        // check still happens, after the song has been identified.
        Conductor conductor = null;
        try { conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null; }
        catch { conductor = null; }
        if (conductor == null || !conductor.isPlaying)
        {
            armedAt = -1f;
            return;
        }

        // Re-arm on the song changing, not on playback stopping. Stopping is not
        // reliable between songs — song select previews keep the conductor busy —
        // and the state worth comparing is precisely the *next* song after a
        // video one, which a session-long latch could never capture.
        string song = CurrentSongKey();
        if (song != reportedSong)
        {
            reportedSong = song;
            reported = false;
            armedAt = -1f;
        }
        if (reported) return;

        if (armedAt < 0f) armedAt = Time.unscaledTime;
        if (Time.unscaledTime - armedAt < DelaySeconds) return;

        reported = true;
        reportCount++;
        Report();
    }

    private static string CurrentSongKey()
    {
        try
        {
            var option = SongSelectionManager.Instance != null
                ? SongSelectionManager.Instance.GetSelectedSong() : null;
            if (option != null) return option.displayName + "/" + option.difficultyName;
        }
        catch { }
        return "<unknown>";
    }

    private void Report()
    {
        // Which report this is, and for which song. Two dumps that look alike are
        // otherwise impossible to tell apart from one dump printed twice.
        Debug.Log($"[BrightProbe] report #{reportCount} for '{reportedSong}'");

        GameObject line = GameObject.Find("JudgmentLine");
        Vector3 anchor = line != null ? line.transform.position : Vector3.zero;

        // Keyed by object path, never by the rendered line. The line carries
        // pos= and size=, and a scrolling cue changes those every frame, so a
        // whole-line key reported every moving object as newly appeared — a diff
        // that is pure noise and can only mislead.
        var found = new List<(float score, string key, string text)>();
        currentAll.Clear();

        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            // No proximity filter any more: the first sweep found nothing there,
            // and bloom spreads light well past its source by definition.
            Vector3 centre = renderer.bounds.center;

            float best = 0f;
            string bestProperty = null;
            var mats = renderer.sharedMaterials;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            for (int m = 0; mats != null && m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;
                foreach (string property in new[] { "_EmissionColor", "_BaseColor", "_Color", "_TintColor" })
                {
                    if (!mat.HasProperty(property)) continue;
                    Color c = mat.GetColor(property);
                    // A property block overrides the material, and that is where
                    // every pulse in this project writes.
                    Color overridden = block.GetColor(property);
                    if (overridden != default) c = overridden;
                    float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    if (lum > best) { best = lum; bestProperty = property; }
                }
                // Several shaders carry brightness in a scalar. It is a multiplier
                // on a colour, not a luminance — reporting the raw number made a
                // line whose real output is 0.94 look like the brightest thing in
                // the scene. Multiply it through, the way the shader does.
                foreach (string property in new[] { "_Intensity", "_EdgeGlow", "_Glow", "_Brightness" })
                {
                    if (!mat.HasProperty(property)) continue;
                    float v = mat.GetFloat(property);
                    float overridden = block.GetFloat(property);
                    if (overridden != 0f) v = overridden;
                    if (v <= 0f) continue;

                    Color tint = Color.white;
                    foreach (string source in new[] { "_BaseColor", "_Color" })
                    {
                        if (!mat.HasProperty(source)) continue;
                        tint = mat.GetColor(source);
                        Color tintOverride = block.GetColor(source);
                        if (tintOverride != default) tint = tintOverride;
                        break;
                    }
                    float scaled = (tint.r * 0.299f + tint.g * 0.587f + tint.b * 0.114f)
                        * Mathf.Max(tint.a, 0.0001f) * v;
                    if (scaled > best) { best = scaled; bestProperty = $"{property}x{v:F2}"; }
                }
            }

            currentAll[Path(renderer.transform)] = best;
            if (best < BloomThreshold) continue;
            found.Add((best, Path(renderer.transform),
                $"  {best:F2}  {Path(renderer.transform)}  [{bestProperty}] " +
                $"shader={(renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "?")} " +
                $"pos={centre} size={renderer.bounds.size}"));
        }

        // UI draws through CanvasRenderer, not Renderer, so the sweep above is
        // blind to every Image and RawImage. A RawImage with no texture is a solid
        // white quad, and bloom rounds its corners into something that no longer
        // looks like a rectangle.
        foreach (var graphic in FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
        {
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy) continue;
            Color gc = graphic.color;
            float glum = (gc.r * 0.299f + gc.g * 0.587f + gc.b * 0.114f) * gc.a;
            currentAll["UI " + Path(graphic.transform)] = glum;
            if (glum < 0.9f) continue;
            var raw = graphic as UnityEngine.UI.RawImage;
            string texture = raw != null
                ? (raw.texture != null ? raw.texture.name : "<NULL TEXTURE - draws white>")
                : null;
            // Geometry, not just identity. A UI graphic that is never sized keeps
            // whatever rect the scene authored, and a bright texture in a rect of
            // the wrong shape is indistinguishable from a stray light.
            var uiRect = graphic.transform as RectTransform;
            string shape = "?";
            if (uiRect != null)
            {
                var corners = new Vector3[4];
                uiRect.GetWorldCorners(corners);
                shape = $"rect={uiRect.rect.size} anchoredPos={uiRect.anchoredPosition} " +
                    $"scale={uiRect.lossyScale} screen={corners[0]}..{corners[2]}";
            }
            found.Add((glum, "UI " + Path(graphic.transform),
                $"  UI {glum:F2}  {Path(graphic.transform)}  type={graphic.GetType().Name} {shape}" +
                (texture != null ? $" texture={texture}" : "")));
        }

        // Particles are the last category the sweeps above cannot see: a particle
        // carries its own colour, so the renderer's material says nothing about
        // how bright the thing on screen actually is. An additive system with an
        // HDR tint blooms hard, and a soft radial sprite blooms into a ball —
        // which is the shape being looked for.
        foreach (var system in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            if (system == null || !system.gameObject.activeInHierarchy) continue;
            int count = system.particleCount;
            if (count <= 0) continue;

            int sample = Mathf.Min(count, 64);
            var buffer = new ParticleSystem.Particle[sample];
            int read = system.GetParticles(buffer);
            float peak = 0f;
            Vector3 peakPosition = system.transform.position;
            for (int i = 0; i < read; i++)
            {
                // GetCurrentColor returns Color32, and assigning it to Color already
                // normalises to 0-1. Scaling by 255 as well drove every reading to
                // zero, which read as "no particle is bright" when nothing had
                // actually been measured.
                Color c = buffer[i].GetCurrentColor(system);
                float lum = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) * c.a;
                if (lum <= peak) continue;
                peak = lum;
                peakPosition = system.main.simulationSpace == ParticleSystemSimulationSpace.Local
                    ? system.transform.TransformPoint(buffer[i].position)
                    : buffer[i].position;
            }

            var psRenderer = system.GetComponent<ParticleSystemRenderer>();
            string mode = psRenderer != null && psRenderer.sharedMaterial != null
                ? psRenderer.sharedMaterial.shader.name : "?";
            // Additive particles stack, so many faint ones still blow out.
            found.Add((peak * (count > 8 ? 2f : 1f), "PARTICLES " + Path(system.transform),
                $"  PARTICLES peak={peak:F2} n={count}  {Path(system.transform)}  " +
                $"shader={mode} brightestAt={peakPosition}"));
        }

        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
            if (light.intensity <= 0.001f) continue;
            found.Add((light.intensity, "LIGHT " + Path(light.transform),
                $"  LIGHT {light.intensity:F2}  {Path(light.transform)}  type={light.type} " +
                $"range={light.range} pos={light.transform.position}"));
        }

        ReportLineEnds(anchor);
        ReportVideoState();

        found.Sort((a, b) => b.score.CompareTo(a.score));

        // Appeared, brightened, and gone. "Nothing appeared" is not the same as
        // "nothing changed": an object already on the list can double in
        // brightness between two songs, and a presence-only diff calls that no
        // change — which is exactly the case this was built to catch.
        const float BrightnessDelta = 0.10f;
        var currentBright = new Dictionary<string, float>();
        var appeared = new List<string>();
        var brightened = new List<string>();
        for (int i = 0; i < found.Count; i++)
        {
            string key = found[i].key;
            currentBright[key] = found[i].score;
            if (reportCount <= 1) continue;
            if (!previousBright.TryGetValue(key, out float before))
                appeared.Add(found[i].text.Trim());
            else if (Mathf.Abs(found[i].score - before) > BrightnessDelta)
                brightened.Add($"{before:F2} -> {found[i].score:F2}  {key}");
        }
        var vanished = new List<string>();
        if (reportCount > 1)
        {
            foreach (var pair in previousBright)
                if (!currentBright.ContainsKey(pair.Key))
                    vanished.Add($"{pair.Value:F2}  {pair.Key}");

            var diff = new System.Text.StringBuilder();
            diff.Append($"[BrightProbe] vs report #{reportCount - 1}: {appeared.Count} appeared, " +
                $"{brightened.Count} changed, {vanished.Count} gone");
            for (int i = 0; i < appeared.Count && i < MaxReported; i++)
                diff.Append("\n  + ").Append(appeared[i]);
            for (int i = 0; i < brightened.Count && i < MaxReported; i++)
                diff.Append("\n  ^ ").Append(brightened[i]);
            for (int i = 0; i < vanished.Count && i < MaxReported; i++)
                diff.Append("\n  - ").Append(vanished[i]);
            if (appeared.Count == 0 && brightened.Count == 0 && vanished.Count == 0)
                diff.Append("\n  nothing changed among bright objects.");
            Debug.Log(diff.ToString());

            // The same comparison over everything active, however dim. This is the
            // one that can see a difference the brightness filter throws away — a
            // plain white material reads as exactly 1.00, under the threshold, while
            // its real brightness comes from a texture that is never sampled.
            var allAppeared = new List<string>();
            var allChanged = new List<string>();
            foreach (var pair in currentAll)
            {
                if (!previousAll.TryGetValue(pair.Key, out float before))
                    allAppeared.Add($"{pair.Value:F2}  {pair.Key}");
                else if (Mathf.Abs(pair.Value - before) > BrightnessDelta)
                    allChanged.Add($"{before:F2} -> {pair.Value:F2}  {pair.Key}");
            }
            var allGone = new List<string>();
            foreach (var pair in previousAll)
                if (!currentAll.ContainsKey(pair.Key)) allGone.Add($"{pair.Value:F2}  {pair.Key}");

            var full = new System.Text.StringBuilder();
            full.Append($"[BrightProbe] ALL objects vs #{reportCount - 1}: {allAppeared.Count} appeared, " +
                $"{allChanged.Count} changed, {allGone.Count} gone (of {currentAll.Count} active)");
            for (int i = 0; i < allAppeared.Count && i < 24; i++) full.Append("\n  + ").Append(allAppeared[i]);
            for (int i = 0; i < allChanged.Count && i < 24; i++) full.Append("\n  ^ ").Append(allChanged[i]);
            for (int i = 0; i < allGone.Count && i < 24; i++) full.Append("\n  - ").Append(allGone[i]);
            Debug.Log(full.ToString());
        }
        previousBright.Clear();
        foreach (var pair in currentBright) previousBright[pair.Key] = pair.Value;
        previousAll.Clear();
        foreach (var pair in currentAll) previousAll[pair.Key] = pair.Value;
        // Which input still claims each lit key. Without this the report can say
        // a key is stuck but never which of the four sources is holding it.
        string keyboardState;
        try { keyboardState = IvoryLaneKeyboard.DescribeHeldLanes(); }
        catch { keyboardState = "keyboard=<unavailable>"; }
        Debug.Log($"[BrightProbe] {keyboardState}");
        var report = new System.Text.StringBuilder();
        report.Append($"[BrightProbe] {found.Count} object(s) past the bloom threshold, scene-wide");
        report.Append(line != null ? $" (line at {anchor})" : " (JudgmentLine not found; using origin)");
        for (int i = 0; i < found.Count && i < MaxReported; i++)
        {
            report.Append('\n').Append(found[i].text);
        }
        if (found.Count == 0)
        {
            report.Append("\n  nothing — not an emissive, not a UI graphic, not a light.");
        }
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Lists everything sitting at the two ends of the judgment line, however dim.
    /// </summary>
    /// <remarks>
    /// The brightness sweep cannot find this. It drops anything below the bloom
    /// threshold, so a plain white material — luminance exactly 1.00 — is
    /// filtered out, and brightness carried by a *texture* rather than a colour
    /// is invisible to it entirely: a soft white glow sprite on a white material
    /// reads as unremarkable. Since the two balls sit at fixed, known places, ask
    /// what is there instead of what is bright.
    /// </remarks>
    private static void ReportLineEnds(Vector3 anchor)
    {
        GameObject line = GameObject.Find("JudgmentLine");
        float halfWidth = 52.5f;
        if (line != null)
        {
            var lineRenderer = line.GetComponent<Renderer>();
            if (lineRenderer != null) halfWidth = lineRenderer.bounds.extents.x;
        }

        Vector3[] ends =
        {
            new Vector3(anchor.x - halfWidth, anchor.y, anchor.z),
            new Vector3(anchor.x + halfWidth, anchor.y, anchor.z),
        };

        var report = new System.Text.StringBuilder();
        report.Append($"[BrightProbe] what sits at the line's ends (±{halfWidth:F1} from centre):");
        int listed = 0;
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds bounds = renderer.bounds;
            float nearest = float.MaxValue;
            for (int i = 0; i < ends.Length; i++)
                nearest = Mathf.Min(nearest, Vector3.Distance(bounds.ClosestPoint(ends[i]), ends[i]));
            if (nearest > 4f) continue;
            if (++listed > 20) break;

            var mat = renderer.sharedMaterial;
            string texture = "-";
            if (mat != null && mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                texture = mat.GetTexture("_BaseMap").name;
            else if (mat != null && mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                texture = mat.GetTexture("_MainTex").name;
            Color tint = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : (mat != null && mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear);

            report.Append($"\n  d={nearest:F2}  {Path(renderer.transform)}  " +
                $"shader={(mat != null && mat.shader != null ? mat.shader.name : "?")} " +
                $"tex={texture} tint={tint} size={bounds.size}");
        }
        if (listed == 0) report.Append("\n  nothing is there — the balls are not geometry at that position.");
        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Dumps what the video path switched on, plus the track's cover uniforms.
    /// </summary>
    /// <remarks>
    /// A fault that starts with one video song and then follows every later song
    /// is state nobody cleared, so the useful question is which value is still
    /// set rather than what is bright right now.
    /// </remarks>
    private static void ReportVideoState()
    {
        var background = FindAnyObjectByType<GameBackgroundManager>();
        string state = background != null ? background.DescribeVideoState() : "<no GameBackgroundManager>";

        // The track keeps its own copy of the cover: a released RenderTexture left
        // bound here samples as undefined data rather than as nothing.
        var trackObject = GameObject.Find("Track");
        string track = "<no Track>";
        if (trackObject != null)
        {
            var trackRenderer = trackObject.GetComponent<Renderer>();
            var mat = trackRenderer != null ? trackRenderer.sharedMaterial : null;
            if (mat != null)
            {
                string cover = mat.HasProperty("_CoverTex") && mat.GetTexture("_CoverTex") != null
                    ? mat.GetTexture("_CoverTex").name : "<null>";
                track = $"coverTex={cover} " +
                    $"coverAmount={(mat.HasProperty("_CoverAmount") ? mat.GetFloat("_CoverAmount") : -1f):F2} " +
                    $"screenSpace={(mat.HasProperty("_CoverScreenSpace") ? mat.GetFloat("_CoverScreenSpace") : -1f):F2} " +
                    $"glass={(mat.HasProperty("_Glass") ? mat.GetFloat("_Glass") : -1f):F2} " +
                    $"theme={(mat.HasProperty("_Theme") ? mat.GetFloat("_Theme") : -1f):F0}";
            }
        }
        Debug.Log($"[BrightProbe] video state: {state}\n  track: {track}");
    }

    private static string Path(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
