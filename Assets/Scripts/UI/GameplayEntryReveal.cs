using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 開場時把譜面一樣一樣帶進來，而不是整片同時出現。
/// </summary>
/// <remarks>
/// **為什麼分批。** 同時出現的話，玩家在同一瞬間收到七八個東西，眼睛沒有落點，結果
/// 是「畫面亮了」而不是「舞台準備好了」。一樣一樣進來，每一樣都有它自己被看到的那
/// 半秒 —— 而且最後進來的那一個自然會是下一步要看的地方。
///
/// **為什麼方向不一樣。** 軌道和鍵盤從下面上來，因為它們是「立起來」的東西，重量在
/// 下；字卡從上面落下，因為它是掛上去的一張牌。方向一致的話讀起來是整塊平移，方向
/// 相對才讀得出是各自就位。
///
/// **為什麼不淡入 3D 的那兩樣。** 軌道和鍵盤是世界物件，淡入要逐一改材質的 alpha，
/// 而它們的材質是共用的（改一個會影響全部），還有大理石、玻璃、發光好幾種 shader，
/// 每一種的透明通道不在同一個地方。位移加縮放不碰材質，而且在這個鏡頭角度下讀起來
/// 一樣是「升上來」。
///
/// **時間全部走 unscaled。** 這一段跑在倒數裡，而倒數期間 <c>Time.timeScale</c> 不保證
/// 是 1 —— 練習模式會改它。用縮放後的時間，進場的長度會跟著練習速度變。
/// </remarks>
public sealed class GameplayEntryReveal : MonoBehaviour
{
    /// <summary>一樣東西滑進來要多久。</summary>
    private const float SlideSeconds = 0.78f;

    /// <summary>相鄰兩樣之間隔多久開始。</summary>
    /// <remarks>
    /// 比 <see cref="SlideSeconds"/> 短：下一樣在上一樣還在走的時候就起步，看起來是
    /// 一串連起來的動作而不是點名。全長約 0.30×3 + 0.78 ≈ 1.7 秒。
    ///
    /// 第一版是 0.14／0.42，快到讀不出是動畫 —— 東西「出現」了但沒有「進來」。這種
    /// 長度的進場要慢到看得見每一樣各自就位，慢的代價只是倒數的前一秒半被用掉，而
    /// 那一秒半本來就是空的。
    /// </remarks>
    private const float StaggerSeconds = 0.30f;

    private static GameplayEntryReveal instance;

    private readonly List<Step> steps = new List<Step>(8);

    private sealed class Step
    {
        public Transform Target;
        public Vector3 WorldFrom;
        public Vector3 WorldTo;
        public RectTransform Rect;
        public Vector2 UiFrom;
        public Vector2 UiTo;
        public CanvasGroup Group;
        public float Delay;
    }

    /// <summary>
    /// 開場。重複呼叫會把上一次還沒跑完的收掉 —— 重新開始一首就是這個情況。
    /// </summary>
    public static void Play()
    {
        if (instance == null)
        {
            var root = new GameObject("GameplayEntryReveal");
            instance = root.AddComponent<GameplayEntryReveal>();
        }
        instance.StopAllCoroutines();
        instance.Restore();
        instance.Collect();
        if (instance.steps.Count > 0) instance.StartCoroutine(instance.Run());
    }

    // ------------------------------------------------------------------

    private void Collect()
    {
        steps.Clear();
        float at = 0f;

        // 軌道：從下面升上來。偏移用軌道自己的高度去量，不是一個寫死的世界單位
        // —— 不同的鏡頭距離下，固定的公尺數會一下子太多一下子看不見。
        Transform track = GameObject.Find("Track")?.transform;
        if (track != null)
        {
            float rise = Mathf.Max(1.2f, TrackSpan(track) * 0.35f);
            AddWorld(track, Vector3.down * rise, at);
            at += StaggerSeconds;
        }

        // 鍵盤：也從下面。它和軌道是同一個動作的兩段，所以方向一致、只差一個時間差。
        Transform keyboard = FindKeyboard();
        if (keyboard != null && keyboard != track)
        {
            AddWorld(keyboard, Vector3.down * 1.4f, at);
            at += StaggerSeconds;
        }

        // 判定線：最後才亮，它是「可以開始了」的那一條。
        Transform line = GameObject.Find("JudgmentLine")?.transform;
        if (line != null && line != track && !IsChildOf(line, track))
        {
            AddWorld(line, Vector3.down * 0.5f, at);
            at += StaggerSeconds;
        }

        // 字卡：從上面落下來，而且淡入。它是 UI，有 CanvasGroup 可以用。
        RectTransform card = FindCard();
        if (card != null)
        {
            AddUi(card, new Vector2(0f, 90f), at);
            at += StaggerSeconds;
        }
    }

    private void AddWorld(Transform target, Vector3 offset, float delay)
    {
        Vector3 to = target.position;
        steps.Add(new Step
        {
            Target = target,
            WorldTo = to,
            WorldFrom = to + offset,
            Delay = delay,
        });
        target.position = to + offset;
    }

    private void AddUi(RectTransform rect, Vector2 offset, float delay)
    {
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
        Vector2 to = rect.anchoredPosition;
        steps.Add(new Step
        {
            Rect = rect,
            UiTo = to,
            UiFrom = to + offset,
            Group = group,
            Delay = delay,
        });
        rect.anchoredPosition = to + offset;
        group.alpha = 0f;
    }

    private IEnumerator Run()
    {
        float elapsed = 0f;
        float total = 0f;
        for (int i = 0; i < steps.Count; i++)
            total = Mathf.Max(total, steps[i].Delay + SlideSeconds);

        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            for (int i = 0; i < steps.Count; i++)
            {
                Step step = steps[i];
                float k = Mathf.Clamp01((elapsed - step.Delay) / SlideSeconds);
                // 停下來的時候要慢，起步可以快 —— 東西就位的那一刻才是要被看到的。
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                Apply(step, eased);
            }
            yield return null;
        }
        Restore();
    }

    private static void Apply(Step step, float eased)
    {
        if (step.Target != null)
            step.Target.position = Vector3.LerpUnclamped(step.WorldFrom, step.WorldTo, eased);
        if (step.Rect != null)
            step.Rect.anchoredPosition = Vector2.LerpUnclamped(step.UiFrom, step.UiTo, eased);
        if (step.Group != null)
            step.Group.alpha = Mathf.Clamp01(eased * 1.35f);
    }

    /// <summary>
    /// 把每一樣東西放回它該在的地方。
    /// </summary>
    /// <remarks>
    /// 動畫被打斷（重新開始、提早結束）時一定要跑完這個：軌道和判定線的位置是別人
    /// 算命中點用的基準，留在半路上的話，整首歌的判定都會偏掉。
    /// </remarks>
    private void Restore()
    {
        for (int i = 0; i < steps.Count; i++) Apply(steps[i], 1f);
        steps.Clear();
    }

    private void OnDisable() => Restore();

    // ------------------------------------------------------------------

    /// <summary>軌道在世界裡有多長。拿它當位移的尺。</summary>
    private static float TrackSpan(Transform track)
    {
        var renderers = track.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return 0f;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Max(bounds.size.y, bounds.size.z);
    }

    private static Transform FindKeyboard()
    {
        var keyboard = Object.FindFirstObjectByType<IvoryLaneKeyboard>(FindObjectsInactive.Exclude);
        return keyboard != null ? keyboard.transform : null;
    }

    /// <summary>遊玩畫面左上那張曲目資訊卡。</summary>
    private static RectTransform FindCard()
    {
        GameObject card = GameObject.Find("ClassicalSongInfoPanel");
        return card != null ? card.transform as RectTransform : null;
    }

    private static bool IsChildOf(Transform child, Transform parent)
    {
        if (parent == null || child == null) return false;
        return child.IsChildOf(parent);
    }
}
