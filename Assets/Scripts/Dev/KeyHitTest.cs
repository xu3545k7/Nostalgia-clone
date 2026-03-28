using UnityEngine;

// Small helper to manually trigger the key-hit mesh for testing in Editor/Play mode.
// Attach to an empty GameObject and set testKeyId. Press the TestKey (default T) to spawn effect.
public class KeyHitTest : MonoBehaviour
{
    public int testKeyId = 14;
    public KeyCode testKey = KeyCode.T;

    void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            // Key hit effects disabled in this project; no-op.
        }
        if (Input.GetKeyUp(testKey))
        {
            // Key hit effects disabled in this project; no-op.
        }
    }
}
