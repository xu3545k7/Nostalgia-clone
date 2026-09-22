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
    private static long inputEventSequence;

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
        bool loadedFromResources = TryLoadMappingFromResources("botton_setting");

        // If no mapping provided from resources, create a conservative default for first lanes
        bool anyAssigned = false;
        for (int i = 0; i < laneKeys.Length; i++) if (laneKeys[i] != Key.None) { anyAssigned = true; break; }
        if (!loadedFromResources && !anyAssigned)
        {
            // default: map a..z, then 9, 0 (matches botton_setting.json ordering)
            Key[] defaults = new Key[] {
                Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J,
                Key.K, Key.L, Key.M, Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T,
                Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z, Key.Digit9, Key.Digit0
            };
            for (int i = 0; i < LaneCount && i < defaults.Length; i++) laneKeys[i] = defaults[i];
        }

        SyncRealtimeBindings();
    }

    private void OnEnable()
    {
        // The realtime buffer is persistent while gameplay panels can be
        // disabled. Never replay menu/settings key edges as fresh note hits
        // when this adapter becomes active again.
        RealtimeInputBuffer.Instance?.ClearGameplayEvents();
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

    private void SyncRealtimeBindings()
    {
        var rtBuffer = RealtimeInputBuffer.Instance ?? RealtimeInputBuffer.EnsureCreated();
        if (rtBuffer == null) return;

        for (int lane = 0; lane < LaneCount; lane++)
        {
            var key = laneKeys[lane];
            rtBuffer.SetBinding(lane, false, KeyCode.None, key != Key.None, key);
        }
    }

    private void LegacyFrameCollapsedUpdate()
    {
        var kb = Keyboard.current;
        if (kb == null || Judgment.JudgmentManager.Instance == null)
        {
            return;
        }

        var jm = Judgment.JudgmentManager.Instance;
        float songPos = 0f;
        Conductor conductor = null;
        var gm = GameManager.Instance;
        if (gm != null && gm.Conductor != null)
        {
            conductor = gm.Conductor;
            songPos = conductor.effectiveSongPosition;
        }

        // Judgment offset: every timestamp handed to JudgmentManager must live on the same clock as
        // GetSongPositionMs() (effectiveSongPosition + offset). songPos above is the raw audio clock
        // used for sub-frame back-dating; the offset is added only to the FINAL value we forward, so
        // input judgment, auto-miss and hold finalize all share one time base.
        float judgmentOffset = SettingsManager.Instance != null ? SettingsManager.Instance.JudgmentOffsetMs : 0f;

        // RealtimeInputBuffer provides sub-frame hardware timestamps (realtimeSinceStartup).
        // By computing how long ago the key was ACTUALLY pressed relative to now, we can
        // back-date songPos for more accurate judgment timing (avoids up to 16ms frame jitter).
        var rtBuffer = RealtimeInputBuffer.Instance;
        double nowRealtime = Time.realtimeSinceStartupAsDouble;

        for (int lane = 0; lane < LaneCount; lane++)
        {
            var key = laneKeys[lane];
            if (key == Key.None) continue;

            var control = kb[key];
            if (control == null) continue;

            bool wasPressed = rtBuffer != null ? rtBuffer.WasPressedThisFrame(lane) : control.wasPressedThisFrame;
            bool isPressed = rtBuffer != null ? rtBuffer.IsPressed(lane) : control.isPressed;
            bool wasReleased = rtBuffer != null ? rtBuffer.WasReleasedThisFrame(lane) : control.wasReleasedThisFrame;

            if (wasPressed && !pressedState[lane])
            {
                pressedState[lane] = true;
                // Compute sub-frame songPos using hardware timestamp
                float preciseSongPos = float.NaN;
                if (rtBuffer != null && conductor != null)
                {
                    preciseSongPos = TimingMath.ProjectSongPosToEvent(
                        songPos, conductor.TimingSampleRealtime,
                        rtBuffer.GetLastPressTime(lane), maxDeltaMs: 500.0,
                        fallback: float.NaN);
                }
                // Add offset only when we produced a real sub-frame value. When SubFrameSongPos
                // returned the -1 sentinel, ProcessButtonPress falls back to GetSongPositionMs(),
                // which already applies the offset — adding it here too would double-count.
                if (!float.IsNaN(preciseSongPos)) preciseSongPos += judgmentOffset;
                // A computer key reports neither pitch nor force; recording that
                // explicitly stops a previous MIDI press on this lane from being
                // mistaken for this one.
                try { PianoKeysound.RecordInput(lane, -1, -1f); } catch { }
                // 和 MIDI 走同一個輸入幀入口：類原型要把同一幀的鍵一起分派，否則電腦
                // 鍵盤會繞過本家的幀規則。逐鍵模式下它直接轉呼叫 ProcessButtonPress。
                jm.EnqueueInputFramePress(lane, preciseSongPos, NextInputEventId());
                IvoryLaneKeyboard.SetLanePressed(lane, true);
                try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(lane); } catch { }
                if (debug) Debug.Log($"[KeyAdapter] Press lane={lane} key={key}");
            }

            if (wasReleased && pressedState[lane])
            {
                pressedState[lane] = false;
                // Compute sub-frame release songPos using hardware timestamp
                float releaseSongPos = songPos;
                if (rtBuffer != null && conductor != null)
                {
                    float sub = TimingMath.ProjectSongPosToEvent(
                        songPos, conductor.TimingSampleRealtime,
                        rtBuffer.GetLastReleaseTime(lane), maxDeltaMs: 500.0);
                    if (sub >= 0f) releaseSongPos = sub;
                }
                // releaseSongPos is always a concrete value here, so apply the offset unconditionally
                // to match the judgment clock (hold tail / staccato release finalize compare against it).
                jm.HandleHeldKeyRelease(lane, releaseSongPos + judgmentOffset);
                IvoryLaneKeyboard.SetLanePressed(lane, false);
                try { KeyHitEffectManager.Instance.HidePersistentMeshForKeyId(lane); } catch { }
                // Hardcore mode damps on the player's key-up. Without this the
                // computer keyboard has no release at all and every note rings
                // until it times out, which fills the voice pool in seconds.
                try { PianoKeysound.ReleaseLane(lane); } catch { }
                if (debug) Debug.Log($"[KeyAdapter] Release lane={lane} key={key} songPos={releaseSongPos}");
            }

            // While key is held, allow soft notes to auto-judge when they enter the window
            if (pressedState[lane] && isPressed)
            {
                jm.TryAutoSoftOnHold(lane, songPos + judgmentOffset);
            }
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || Judgment.JudgmentManager.Instance == null) return;

        var jm = Judgment.JudgmentManager.Instance;
        float songPos = 0f;
        Conductor conductor = null;
        var gm = GameManager.Instance;
        if (gm != null && gm.Conductor != null)
        {
            conductor = gm.Conductor;
            songPos = conductor.effectiveSongPosition;
        }

        float judgmentOffset = SettingsManager.Instance != null
            ? SettingsManager.Instance.JudgmentOffsetMs
            : 0f;
        var rtBuffer = RealtimeInputBuffer.Instance;
        double nowRealtime = Time.realtimeSinceStartupAsDouble;

        if (rtBuffer != null)
        {
            // Preserve every timestamped edge. A frame can contain more than
            // one press/release pair after a hitch; reducing it to one bool
            // loses valid Trill contacts.
            while (rtBuffer.TryDequeueGameplayEvent(
                out int lane, out bool isPress, out double eventTimestamp))
            {
                if ((uint)lane >= LaneCount || laneKeys[lane] == Key.None) continue;
                if (isPress)
                    ProcessPress(jm, lane, songPos, judgmentOffset,
                        eventTimestamp, conductor);
                else
                    ProcessRelease(jm, lane, songPos, judgmentOffset,
                        eventTimestamp, conductor);
            }

            for (int lane = 0; lane < LaneCount; lane++)
            {
                if (laneKeys[lane] == Key.None) continue;
                if (pressedState[lane] && rtBuffer.IsPressed(lane))
                    jm.TryAutoSoftOnHold(lane, songPos + judgmentOffset);
            }
            return;
        }

        // Fallback for platforms where the realtime event buffer is absent.
        for (int lane = 0; lane < LaneCount; lane++)
        {
            var key = laneKeys[lane];
            if (key == Key.None) continue;
            var control = kb[key];
            if (control == null) continue;

            if (control.wasPressedThisFrame && !pressedState[lane])
                ProcessPress(jm, lane, songPos, judgmentOffset,
                    nowRealtime, null);
            if (control.wasReleasedThisFrame && pressedState[lane])
                ProcessRelease(jm, lane, songPos, judgmentOffset,
                    nowRealtime, null);
            if (pressedState[lane] && control.isPressed)
                jm.TryAutoSoftOnHold(lane, songPos + judgmentOffset);
        }
    }

    private void ProcessPress(Judgment.JudgmentManager jm, int lane,
        float songPos, float judgmentOffset, double eventTimestamp,
        Conductor conductor)
    {
        pressedState[lane] = true;
        float preciseSongPos = float.NaN;
        if (conductor != null)
            preciseSongPos = TimingMath.ProjectSongPosToEvent(
                songPos, conductor.TimingSampleRealtime,
                eventTimestamp, maxDeltaMs: 500.0, fallback: float.NaN);

        if (!float.IsNaN(preciseSongPos)) preciseSongPos += judgmentOffset;
        try { PianoKeysound.RecordInput(lane, -1, -1f); } catch { }
        // 和 MIDI 走同一個輸入幀入口（見上面那一處）。
        jm.EnqueueInputFramePress(lane, preciseSongPos, NextInputEventId());
        IvoryLaneKeyboard.SetLanePressed(lane, true);
        try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(lane); } catch { }
        if (debug)
            Debug.Log($"[KeyAdapter] Press lane={lane} key={laneKeys[lane]} songPos={preciseSongPos}");
    }

    private void ProcessRelease(Judgment.JudgmentManager jm, int lane,
        float songPos, float judgmentOffset, double eventTimestamp,
        Conductor conductor)
    {
        pressedState[lane] = false;
        float releaseSongPos = songPos;
        if (conductor != null)
        {
            float sub = TimingMath.ProjectSongPosToEvent(
                songPos, conductor.TimingSampleRealtime,
                eventTimestamp, maxDeltaMs: 500.0);
            if (sub >= 0f) releaseSongPos = sub;
        }

        jm.HandleHeldKeyRelease(lane, releaseSongPos + judgmentOffset);
        IvoryLaneKeyboard.SetLanePressed(lane, false);
        try { KeyHitEffectManager.Instance.HidePersistentMeshForKeyId(lane); } catch { }
        try { PianoKeysound.ReleaseLane(lane); } catch { }
        if (debug)
            Debug.Log($"[KeyAdapter] Release lane={lane} key={laneKeys[lane]} songPos={releaseSongPos}");
    }

    private long NextInputEventId()
    {
        inputEventSequence++;
        if (inputEventSequence <= 0 || inputEventSequence > (long.MaxValue >> 1))
            inputEventSequence = 1;
        // Odd ids are reserved for keyboard; MIDI uses even ids.
        return (inputEventSequence << 1) | 1L;
    }
}
