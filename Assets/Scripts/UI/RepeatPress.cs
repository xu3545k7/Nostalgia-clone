using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace UIHelpers
{
    /// <summary>
    /// Reusable component: invoke <see cref="onRepeat"/> once immediately on pointer down,
    /// then after <see cref="initialDelay"/> repeatedly invoke it every <see cref="repeatRate"/> seconds
    /// while the pointer is held down.
    /// </summary>
    public class RepeatPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Delay before repeating starts (seconds)")]
        public float initialDelay = 0.45f;

        [Tooltip("Interval between repeats once repeating has started (seconds)")]
        public float repeatRate = 0.08f;

        public UnityEvent onRepeat;

        bool _isPressed = false;
        Coroutine _repeatCoroutine = null;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_isPressed) return;
            _isPressed = true;
            // invoke once immediately so a click feels responsive
            onRepeat?.Invoke();
            _repeatCoroutine = StartCoroutine(RepeatCoroutine());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            StopRepeat();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // stop repeating if pointer exits the button area
            StopRepeat();
        }

        IEnumerator RepeatCoroutine()
        {
            // initial delay
            float t = 0f;
            while (t < initialDelay && _isPressed)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            while (_isPressed)
            {
                onRepeat?.Invoke();
                float waited = 0f;
                while (waited < repeatRate && _isPressed)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        void StopRepeat()
        {
            _isPressed = false;
            if (_repeatCoroutine != null)
            {
                StopCoroutine(_repeatCoroutine);
                _repeatCoroutine = null;
            }
        }

        void OnDisable()
        {
            StopRepeat();
        }
    }
    }
