
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public event Action<Key> OnKeyPressed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        var all = kb.allKeys;
        if (all.Count == 0) return;

        foreach (KeyControl key in all)
        {
            if (key == null) continue;
            try
            {
                if (key.wasPressedThisFrame)
                {
                    OnKeyPressed?.Invoke(key.keyCode);
                }
            }
            catch { }
        }
    }
}
