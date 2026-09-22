using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Event-driven keyboard buffer that captures input as soon as it reaches the
/// Unity Input System and exposes low-latency queries for gameplay systems.
/// </summary>
[DefaultExecutionOrder(-500)]
public class RealtimeInputBuffer : MonoBehaviour
{
    private const int LaneCount = 28;
    private const float PressThreshold = 0.5f;

    public static RealtimeInputBuffer Instance { get; private set; }

    [Tooltip("Desired keyboard polling frequency (Hz). Higher values reduce latency but may increase CPU use.")]
    [SerializeField] private float targetPollingFrequency = 1000f;

    [Header("Noise Filtering")]
    [Tooltip("Minimum time (ms) the key must remain pressed before counting as a stable press. Set to 0 to disable.")]
    [SerializeField, Range(0f, 5f)] private float pressConfirmWindowMs = 2f;

    [Tooltip("Minimum time (ms) the key must remain released before counting as an actual release. Extends holds slightly to absorb jitter.")]
    [SerializeField, Range(0f, 20f)] private float releaseConfirmWindowMs = 4f;

    [Tooltip("Minimum interval (ms) allowed between two presses on the same lane. Filters out key-bounce double hits.")]
    [SerializeField, Range(0f, 15f)] private float minPressIntervalMs = 3f;

    private readonly bool[] pressed = new bool[LaneCount];
    private readonly uint[] downSequence = new uint[LaneCount];
    private readonly uint[] upSequence = new uint[LaneCount];

    private readonly bool[] hasKeyCode = new bool[LaneCount];
    private readonly KeyCode[] keyCodes = new KeyCode[LaneCount];
    private readonly bool[] hasInputKey = new bool[LaneCount];
    private readonly Key[] inputKeys = new Key[LaneCount];
    private readonly KeyControl[] keyControls = new KeyControl[LaneCount];

    private readonly bool[] pendingActive = new bool[LaneCount];
    private readonly bool[] pendingTarget = new bool[LaneCount];
    private readonly double[] pendingStartTime = new double[LaneCount];
    private readonly double[] lastPressTime = new double[LaneCount];
    private readonly double[] lastReleaseTime = new double[LaneCount];
    private readonly double[] lastStableChangeTime = new double[LaneCount];
    private struct BufferedLaneEvent
    {
        public int lane;
        public bool pressed;
        public double timestamp;
    }
    // Preserve every stable edge in arrival order. A single "pressed this
    // frame" bit cannot represent press/release/press during one long frame,
    // which is especially destructive for 0.125-second Trill slots.
    private readonly Queue<BufferedLaneEvent> gameplayEvents =
        new Queue<BufferedLaneEvent>(64);

    /// <summary>
    /// Returns the hardware event timestamp (realtimeSinceStartup) of the last stable press
    /// on the given lane, or double.NegativeInfinity if never pressed.
    /// Use this instead of frame-time songPos for sub-frame judgment accuracy.
    /// </summary>
    public double GetLastPressTime(int lane)
    {
        if ((uint)lane >= LaneCount) return double.NegativeInfinity;
        return lastPressTime[lane];
    }

    /// <summary>
    /// Returns the hardware event timestamp of the last stable release on the given lane.
    /// </summary>
    public double GetLastReleaseTime(int lane)
    {
        if ((uint)lane >= LaneCount) return double.NegativeInfinity;
        return lastReleaseTime[lane];
    }

    public bool TryDequeueGameplayEvent(out int lane, out bool isPressed, out double timestamp)
    {
        if (gameplayEvents.Count == 0)
        {
            lane = -1;
            isPressed = false;
            timestamp = 0.0;
            return false;
        }

        BufferedLaneEvent inputEvent = gameplayEvents.Dequeue();
        lane = inputEvent.lane;
        isPressed = inputEvent.pressed;
        timestamp = inputEvent.timestamp;
        return true;
    }

    public void ClearGameplayEvents()
    {
        gameplayEvents.Clear();
    }

    private uint currentSequence;
    private uint frameSequence;
    private int frameMarker = -1;
    private float? previousPollingFrequency;

    /// <summary>
    /// Ensure there is a singleton instance available in the scene.
    /// </summary>
    public static RealtimeInputBuffer EnsureCreated()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var existing = FindFirstObjectByType<RealtimeInputBuffer>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("RealtimeInputBuffer");
        DontDestroyOnLoad(go);
        return go.AddComponent<RealtimeInputBuffer>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ResetState();
        RefreshKeyControls();
    }

    private void OnEnable()
    {
        InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
        InputSystem.onEvent += OnInputEvent;
        InputSystem.onDeviceChange += OnDeviceChange;

        previousPollingFrequency = InputSystem.pollingFrequency;
        if (targetPollingFrequency > 0f)
        {
            InputSystem.pollingFrequency = Mathf.Max(targetPollingFrequency, InputSystem.pollingFrequency);
        }

        ResetState();
        RefreshKeyControls();
    }

    private void OnDisable()
    {
        InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
        InputSystem.onEvent -= OnInputEvent;
        InputSystem.onDeviceChange -= OnDeviceChange;

        if (previousPollingFrequency.HasValue)
        {
            InputSystem.pollingFrequency = previousPollingFrequency.Value;
            previousPollingFrequency = null;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnBeforeInputUpdate()
    {
        var updateType = InputState.currentUpdateType;
        if (updateType == InputUpdateType.Dynamic ||
            updateType == InputUpdateType.Fixed ||
            updateType == InputUpdateType.BeforeRender)
        {
            currentSequence++;
        }

        ProcessPending(InputState.currentTime);
    }

    private void Update()
    {
        ProcessPending(GetRealtimeSinceStartup());
    }

    private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (device is not Keyboard keyboard)
        {
            return;
        }

        if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
        {
            return;
        }

        if (Keyboard.current != keyboard)
        {
            RefreshKeyControls();
        }

        // onEvent executes as Unity receives the hardware state event, before
        // gameplay Update. Stamp that arrival directly on Unity's realtime
        // clock. InputEvent.time/InputState.currentTime use the Input Runtime
        // clock; deriving an "age" between that clock and realtime produced a
        // stable 20-30 ms over-backdate for keyboard-emulated MIDI devices.
        // Arrival time still preserves sub-frame precision without that bias.
        double timestamp = GetRealtimeSinceStartup();

        for (int i = 0; i < LaneCount; i++)
        {
            var control = keyControls[i];
            if (control == null)
            {
                continue;
            }

            if (!control.ReadValueFromEvent(eventPtr, out float value))
            {
                continue;
            }

            bool newPressed = value >= PressThreshold;
            QueueStateChange(i, newPressed, timestamp);
        }

        ProcessPending(timestamp);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Keyboard && (change == InputDeviceChange.Added ||
                                   change == InputDeviceChange.Removed ||
                                   change == InputDeviceChange.Reconnected ||
                                   change == InputDeviceChange.Enabled))
        {
            RefreshKeyControls();
        }
    }

    private void RefreshKeyControls()
    {
        var keyboard = Keyboard.current;
        for (int i = 0; i < LaneCount; i++)
        {
            keyControls[i] = (keyboard != null && hasInputKey[i]) ? keyboard[inputKeys[i]] : null;
        }
    }

    private void SyncFrame()
    {
        int frame = Time.frameCount;
        if (frameMarker == frame)
        {
            return;
        }

        frameMarker = frame;
        frameSequence = currentSequence;
    }

    private void ResetState()
    {
        Array.Clear(pressed, 0, pressed.Length);
        Array.Clear(downSequence, 0, downSequence.Length);
        Array.Clear(upSequence, 0, upSequence.Length);
        Array.Clear(pendingActive, 0, pendingActive.Length);
        Array.Clear(pendingTarget, 0, pendingTarget.Length);
        Array.Clear(pendingStartTime, 0, pendingStartTime.Length);
        gameplayEvents.Clear();
        for (int i = 0; i < LaneCount; i++)
        {
            lastPressTime[i] = double.NegativeInfinity;
            lastReleaseTime[i] = double.NegativeInfinity;
            lastStableChangeTime[i] = double.NegativeInfinity;
        }
        currentSequence = 0;
        frameSequence = 0;
        frameMarker = -1;
    }

    private void QueueStateChange(int lane, bool newPressed, double timestamp)
    {
        if ((uint)lane >= LaneCount)
        {
            return;
        }

        bool stable = pressed[lane];
        if (pendingActive[lane])
        {
            bool target = pendingTarget[lane];
            if (target == newPressed)
            {
                // Keep the timestamp of the first edge. Full keyboard state
                // events can repeat an unchanged control when another key
                // moves; resetting here could postpone confirmation forever
                // during dense chords.
                return;
            }

            if (target == false && newPressed && stable)
            {
                pendingActive[lane] = false;
                double releaseTime = pendingStartTime[lane] > 0.0 ? pendingStartTime[lane] : timestamp;
                ApplyStableState(lane, false, releaseTime);
                stable = pressed[lane];
            }
            else if (target == true && !newPressed && !stable)
            {
                pendingActive[lane] = false;
                double pressTime = pendingStartTime[lane] > 0.0 ? pendingStartTime[lane] : timestamp;
                ApplyStableState(lane, true, pressTime);
                stable = pressed[lane];
            }
            else if (newPressed == stable)
            {
                pendingActive[lane] = false;
                return;
            }
            else
            {
                pendingActive[lane] = false;
            }
        }

        if (newPressed == stable)
        {
            return;
        }

        double threshold = GetThresholdSeconds(newPressed);
        if (threshold <= 0.0)
        {
            ApplyStableState(lane, newPressed, timestamp);
            return;
        }

        pendingActive[lane] = true;
        pendingTarget[lane] = newPressed;
        pendingStartTime[lane] = timestamp;
    }

    private void ProcessPending(double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now))
        {
            now = GetRealtimeSinceStartup();
        }

        for (int i = 0; i < LaneCount; i++)
        {
            if (!pendingActive[i])
            {
                continue;
            }

            bool target = pendingTarget[i];
            double threshold = GetThresholdSeconds(target);
            if (now - pendingStartTime[i] < threshold)
            {
                continue;
            }

            pendingActive[i] = false;
            // 用實際硬體事件時間（pendingStartTime）作為穩定時間戳，而非確認當下的幀時間 now。
            // now 只負責判斷「是否已等夠 threshold」；若在這裡寫入 now，lastPressTime 會被記成
            // 最晚可達一整幀之後的幀時間，抵銷本 buffer 設計的 sub-frame 精度，導致判定系統性偏晚。
            ApplyStableState(i, target, pendingStartTime[i]);
        }
    }

    private void ApplyStableState(int lane, bool newPressed, double timestamp)
    {
        if ((uint)lane >= LaneCount)
        {
            return;
        }

        if (newPressed)
        {
            double minInterval = minPressIntervalMs > 0f ? minPressIntervalMs / 1000.0 : 0.0;
            double sinceLastPress = timestamp - lastPressTime[lane];
            if (sinceLastPress < minInterval)
            {
                return;
            }

            pressed[lane] = true;
            downSequence[lane] = currentSequence;
            lastPressTime[lane] = timestamp;
        }
        else
        {
            pressed[lane] = false;
            upSequence[lane] = currentSequence;
            lastReleaseTime[lane] = timestamp;
        }

        lastStableChangeTime[lane] = timestamp;
        gameplayEvents.Enqueue(new BufferedLaneEvent
        {
            lane = lane,
            pressed = newPressed,
            timestamp = timestamp
        });
    }

    private double GetThresholdSeconds(bool isPress)
    {
        double ms = isPress ? pressConfirmWindowMs : releaseConfirmWindowMs;
        return ms > 0f ? ms / 1000.0 : 0.0;
    }

    private static double GetRealtimeSinceStartup()
    {
#if UNITY_2020_2_OR_NEWER
        return Time.realtimeSinceStartupAsDouble;
#else
        return Time.realtimeSinceStartup;
#endif
    }

    public void SetBinding(int lane, bool hasKeyCodeBinding, KeyCode keyCode,
        bool hasInputKeyBinding, Key inputKey)
    {
        if ((uint)lane >= LaneCount)
        {
            return;
        }

        hasKeyCode[lane] = hasKeyCodeBinding;
        keyCodes[lane] = keyCode;

        bool resolvedHasInputKey = hasInputKeyBinding;
        Key resolvedInputKey = inputKey;

        if (!resolvedHasInputKey && hasKeyCodeBinding && TryGetKeyFromKeyCode(keyCode, out var mappedKey))
        {
            resolvedHasInputKey = true;
            resolvedInputKey = mappedKey;
        }

        hasInputKey[lane] = resolvedHasInputKey;
        inputKeys[lane] = resolvedInputKey;
        RefreshKeyControls();
    }

    public bool WasPressedThisFrame(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }

        SyncFrame();
        if (downSequence[lane] == frameSequence)
        {
            return true;
        }

        var control = keyControls[lane];
        if (control != null)
        {
            return control.wasPressedThisFrame;
        }

        return false;
    }

    public bool WasReleasedThisFrame(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }

        SyncFrame();
        if (upSequence[lane] == frameSequence)
        {
            return true;
        }

        var control = keyControls[lane];
        if (control != null)
        {
            return control.wasReleasedThisFrame;
        }

        return false;
    }

    public bool IsPressed(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }

        if (pressed[lane])
        {
            return true;
        }

        var control = keyControls[lane];
        if (control != null)
        {
            return control.isPressed;
        }

        return false;
    }

    private static bool TryGetKeyFromKeyCode(KeyCode keyCode, out Key key)
    {
        switch (keyCode)
        {
            case KeyCode.A: key = Key.A; return true;
            case KeyCode.B: key = Key.B; return true;
            case KeyCode.C: key = Key.C; return true;
            case KeyCode.D: key = Key.D; return true;
            case KeyCode.E: key = Key.E; return true;
            case KeyCode.F: key = Key.F; return true;
            case KeyCode.G: key = Key.G; return true;
            case KeyCode.H: key = Key.H; return true;
            case KeyCode.I: key = Key.I; return true;
            case KeyCode.J: key = Key.J; return true;
            case KeyCode.K: key = Key.K; return true;
            case KeyCode.L: key = Key.L; return true;
            case KeyCode.M: key = Key.M; return true;
            case KeyCode.N: key = Key.N; return true;
            case KeyCode.O: key = Key.O; return true;
            case KeyCode.P: key = Key.P; return true;
            case KeyCode.Q: key = Key.Q; return true;
            case KeyCode.R: key = Key.R; return true;
            case KeyCode.S: key = Key.S; return true;
            case KeyCode.T: key = Key.T; return true;
            case KeyCode.U: key = Key.U; return true;
            case KeyCode.V: key = Key.V; return true;
            case KeyCode.W: key = Key.W; return true;
            case KeyCode.X: key = Key.X; return true;
            case KeyCode.Y: key = Key.Y; return true;
            case KeyCode.Z: key = Key.Z; return true;

            case KeyCode.Alpha0: key = Key.Digit0; return true;
            case KeyCode.Alpha1: key = Key.Digit1; return true;
            case KeyCode.Alpha2: key = Key.Digit2; return true;
            case KeyCode.Alpha3: key = Key.Digit3; return true;
            case KeyCode.Alpha4: key = Key.Digit4; return true;
            case KeyCode.Alpha5: key = Key.Digit5; return true;
            case KeyCode.Alpha6: key = Key.Digit6; return true;
            case KeyCode.Alpha7: key = Key.Digit7; return true;
            case KeyCode.Alpha8: key = Key.Digit8; return true;
            case KeyCode.Alpha9: key = Key.Digit9; return true;

            case KeyCode.F1: key = Key.F1; return true;
            case KeyCode.F2: key = Key.F2; return true;
            case KeyCode.F3: key = Key.F3; return true;
            case KeyCode.F4: key = Key.F4; return true;
            case KeyCode.F5: key = Key.F5; return true;
            case KeyCode.F6: key = Key.F6; return true;
            case KeyCode.F7: key = Key.F7; return true;
            case KeyCode.F8: key = Key.F8; return true;
            case KeyCode.F9: key = Key.F9; return true;
            case KeyCode.F10: key = Key.F10; return true;
            case KeyCode.F11: key = Key.F11; return true;
            case KeyCode.F12: key = Key.F12; return true;

            case KeyCode.UpArrow: key = Key.UpArrow; return true;
            case KeyCode.DownArrow: key = Key.DownArrow; return true;
            case KeyCode.LeftArrow: key = Key.LeftArrow; return true;
            case KeyCode.RightArrow: key = Key.RightArrow; return true;

            case KeyCode.Space: key = Key.Space; return true;
            case KeyCode.Escape: key = Key.Escape; return true;
            case KeyCode.Tab: key = Key.Tab; return true;
            case KeyCode.BackQuote: key = Key.Backquote; return true;
            case KeyCode.Minus: key = Key.Minus; return true;
            case KeyCode.Equals: key = Key.Equals; return true;
            case KeyCode.LeftBracket: key = Key.LeftBracket; return true;
            case KeyCode.RightBracket: key = Key.RightBracket; return true;
            case KeyCode.Semicolon: key = Key.Semicolon; return true;
            case KeyCode.Quote: key = Key.Quote; return true;
            case KeyCode.Comma: key = Key.Comma; return true;
            case KeyCode.Period: key = Key.Period; return true;
            case KeyCode.Slash: key = Key.Slash; return true;
            case KeyCode.Backslash: key = Key.Backslash; return true;
            case KeyCode.Backspace: key = Key.Backspace; return true;
            case KeyCode.Return: key = Key.Enter; return true;
            case KeyCode.KeypadEnter: key = Key.NumpadEnter; return true;
            case KeyCode.Insert: key = Key.Insert; return true;
            case KeyCode.Delete: key = Key.Delete; return true;
            case KeyCode.Home: key = Key.Home; return true;
            case KeyCode.End: key = Key.End; return true;
            case KeyCode.PageUp: key = Key.PageUp; return true;
            case KeyCode.PageDown: key = Key.PageDown; return true;

            case KeyCode.CapsLock: key = Key.CapsLock; return true;
            case KeyCode.LeftShift: key = Key.LeftShift; return true;
            case KeyCode.RightShift: key = Key.RightShift; return true;
            case KeyCode.LeftControl: key = Key.LeftCtrl; return true;
            case KeyCode.RightControl: key = Key.RightCtrl; return true;
            case KeyCode.LeftAlt: key = Key.LeftAlt; return true;
            case KeyCode.RightAlt: key = Key.RightAlt; return true;

            case KeyCode.Keypad0: key = Key.Numpad0; return true;
            case KeyCode.Keypad1: key = Key.Numpad1; return true;
            case KeyCode.Keypad2: key = Key.Numpad2; return true;
            case KeyCode.Keypad3: key = Key.Numpad3; return true;
            case KeyCode.Keypad4: key = Key.Numpad4; return true;
            case KeyCode.Keypad5: key = Key.Numpad5; return true;
            case KeyCode.Keypad6: key = Key.Numpad6; return true;
            case KeyCode.Keypad7: key = Key.Numpad7; return true;
            case KeyCode.Keypad8: key = Key.Numpad8; return true;
            case KeyCode.Keypad9: key = Key.Numpad9; return true;
            case KeyCode.KeypadDivide: key = Key.NumpadDivide; return true;
            case KeyCode.KeypadMultiply: key = Key.NumpadMultiply; return true;
            case KeyCode.KeypadMinus: key = Key.NumpadMinus; return true;
            case KeyCode.KeypadPlus: key = Key.NumpadPlus; return true;
            case KeyCode.KeypadPeriod: key = Key.NumpadPeriod; return true;

            default:
                key = default;
                return false;
        }
    }
}
