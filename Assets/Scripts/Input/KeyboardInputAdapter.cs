using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple keyboard adapter that maps physical keys to lane ids and forwards
/// press/release events to JudgmentManager. This is a fallback wiring when
/// a full binding loader is not present.
/// </summary>
[DefaultExecutionOrder(-100)]
public class KeyboardInputAdapter : MonoBehaviour
{
    private static KeyboardInputAdapter _instance;
    public static KeyboardInputAdapter Instance => _instance;
    private const int LaneCount = 28;

    [Tooltip("Per-lane key mapping (Input System Key). Leave as None to disable lane.)")]
    public Key[] laneKeys = new Key[LaneCount];

    [Tooltip("Enable debug logging for key events")]
    public bool debug = false;

    private bool[] pressedState = new bool[LaneCount];

    void OnValidate()
    {
        if (laneKeys == null || laneKeys.Length != LaneCount)
        {
            var tmp = new Key[LaneCount];
            if (laneKeys != null)
            {
                for (int i = 0; i < Mathf.Min(tmp.Length, laneKeys.Length); i++) tmp[i] = laneKeys[i];
            }
            laneKeys = tmp;
        }
    }

    void Start()
    {
        // register singleton for other systems to query lane pressed state
        _instance = this;

        // Try to load mapping from Resources/botton_setting.json (preferred)
        if (TryLoadMappingFromResources("botton_setting"))
        {
            return;
        }

        // If no mapping provided from resources, create a conservative default for first lanes
        bool anyAssigned = false;
        for (int i = 0; i < laneKeys.Length; i++) if (laneKeys[i] != Key.None) { anyAssigned = true; break; }
        if (!anyAssigned)
        {
            // default: map a..z, then 9, 0 (matches botton_setting.json ordering)
            Key[] defaults = new Key[] {
                Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J,
                Key.K, Key.L, Key.M, Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T,
                Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z, Key.Digit9, Key.Digit0
            };
            for (int i = 0; i < LaneCount && i < defaults.Length; i++) laneKeys[i] = defaults[i];
        }
    }

    public bool IsLanePressed(int lane)
    {
        if (lane < 0 || lane >= pressedState.Length) return false;
        return pressedState[lane];
    }

    private bool TryLoadMappingFromResources(string resourceName)
    {
        try
        {
            var ta = Resources.Load<TextAsset>(resourceName);
            if (ta == null || string.IsNullOrWhiteSpace(ta.text)) return false;

            // JSON structure expected: { "bottons": [ { "Name": "...", "botton_setting": [ { "botton_id": 0, "botton_name": "a" }, ... ] } ] }
            var parsed = JsonUtility.FromJson<BottonSettingRoot>(ta.text);
            if (parsed == null || parsed.bottons == null || parsed.bottons.Length == 0) return false;

            // Use first entry's botton_setting by default
            var settings = parsed.bottons[0].botton_setting;
            if (settings == null || settings.Length == 0) return false;

            for (int i = 0; i < settings.Length; i++)
            {
                var e = settings[i];
                if (e == null) continue;
                int id = e.botton_id;
                if (id < 0 || id >= LaneCount) continue;
                Key k;
                if (TryParseKeyFromName(e.botton_name, out k))
                {
                    laneKeys[id] = k;
                }
                else
                {
                    laneKeys[id] = Key.None;
                }
            }

            if (debug) Debug.Log($"[KeyAdapter] Loaded mapping from Resources/{resourceName}.json");
            return true;
        }
        catch (System.Exception ex)
        {
            if (debug) Debug.LogWarning($"[KeyAdapter] Failed to load mapping: {ex}");
            return false;
        }
    }

    [System.Serializable]
    private class BottonSettingRoot
    {
        public BottonGroup[] bottons;
    }

    [System.Serializable]
    private class BottonGroup
    {
        public string Name;
        public BottonEntry[] botton_setting;
    }

    [System.Serializable]
    private class BottonEntry
    {
        public int botton_id;
        public string botton_name;
    }

    private static bool TryParseKeyFromName(string raw, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrEmpty(raw)) return false;
        raw = raw.Trim();

        // single letter a-z
        if (raw.Length == 1)
        {
            char c = raw[0];
            if (char.IsLetter(c))
            {
                string name = char.ToUpperInvariant(c).ToString();
                try { key = (Key)System.Enum.Parse(typeof(Key), name, true); return true; } catch { }
            }
            if (char.IsDigit(c))
            {
                // digits use Digit0..Digit9
                key = (Key)System.Enum.Parse(typeof(Key), "Digit" + c, true);
                return true;
            }
        }

        // common named keys
        if (System.Enum.TryParse<Key>(raw, true, out var parsed))
        {
            key = parsed;
            return true;
        }

        // fallback: try single char uppercase
        if (raw.Length > 0)
        {
            var upper = raw.ToUpperInvariant();
            if (upper.Length == 1 && char.IsLetter(upper[0]))
            {
                try { key = (Key)System.Enum.Parse(typeof(Key), upper, true); return true; } catch { }
            }
        }

        return false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || Judgment.JudgmentManager.Instance == null)
        {
            return;
        }

        var jm = Judgment.JudgmentManager.Instance;
        float songPos = 0f;
        try { songPos = GameManager.Instance != null && GameManager.Instance.Conductor != null ? GameManager.Instance.Conductor.effectiveSongPosition : 0f; } catch { songPos = 0f; }

        for (int lane = 0; lane < LaneCount; lane++)
        {
            var key = laneKeys[lane];
            if (key == Key.None) continue;

            var control = kb[key];
            if (control == null) continue;

            bool wasPressed = control.wasPressedThisFrame;
            bool isPressed = control.isPressed;
            bool wasReleased = control.wasReleasedThisFrame;

            if (wasPressed && !pressedState[lane])
            {
                pressedState[lane] = true;
                try
                {
                    jm.ProcessButtonPress(lane);
                    // Trigger key hit visual (persistent while held)
                    try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(lane); } catch { }
                    if (debug) Debug.Log($"[KeyAdapter] Press lane={lane} key={key}");
                }
                catch { }
            }

            if (wasReleased && pressedState[lane])
            {
                pressedState[lane] = false;
                try
                {
                    jm.HandleHeldKeyRelease(lane, songPos);
                    try { KeyHitEffectManager.Instance.HidePersistentMeshForKeyId(lane); } catch { }
                    if (debug) Debug.Log($"[KeyAdapter] Release lane={lane} key={key} songPos={songPos}");
                }
                catch { }
            }

            // While key is held, allow soft notes to auto-judge when they enter the window (no new press edge needed).
            if (pressedState[lane] && isPressed)
            {
                try { jm.TryAutoSoftOnHold(lane, songPos); } catch { }
            }
        }
    }
}
