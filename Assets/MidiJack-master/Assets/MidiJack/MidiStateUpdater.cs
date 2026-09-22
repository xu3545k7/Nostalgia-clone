//
// MidiJack - MIDI Input Plugin for Unity
//
// Copyright (C) 2013-2016 Keijiro Takahashi
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
using UnityEngine;

namespace MidiJack
{
    public class MidiStateUpdater : MonoBehaviour
    {
        public delegate void Callback();

        public static void CreateGameObject(Callback callback)
        {
            // Reuse an updater that may already exist in the scene or survive
            // an Enter Play Mode/domain-reload transition. Its delegate is not
            // serialized, so invoking such an instance before rebinding it
            // causes a NullReferenceException every frame.
            var existing = Object.FindFirstObjectByType<MidiStateUpdater>();
            if (existing != null)
            {
                existing._callback = callback;
                existing.enabled = callback != null;
                return;
            }

            var go = new GameObject("MIDI Updater");

            GameObject.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;

            var updater = go.AddComponent<MidiStateUpdater>();
            updater._callback = callback;
        }

        Callback _callback;

        void Update()
        {
            var callback = _callback;
            if (callback == null)
            {
                // A manually placed/duplicated updater has no driver callback.
                // Disable it quietly; MidiDriver will bind or create the valid
                // updater when its singleton is first requested.
                enabled = false;
                return;
            }
            callback();
        }
    }
}
