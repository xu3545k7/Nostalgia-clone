using UnityEngine;

/// <summary>
/// Continuous, self-snapping carousel motion, shared by the song strip and the
/// category strip.  The position is a single float measured in items, so input
/// never has to wait for an animation to finish: it only moves the target and
/// the position glides there.  The target is always an integer, which is what
/// guarantees the strip ends up centred on an item however hard it was spun.
/// </summary>
public sealed class CarouselScroller
{
    public enum Mode { Snap, Drag, Inertia }

    [Tooltip("Seconds for the snap spring to reach the target item.")]
    public float snapSmoothTime = 0.11f;
    [Tooltip("Upper bound on scroll speed, in items per second.")]
    public float maxSpeed = 16f;
    [Tooltip("How quickly a flick loses speed. Larger = shorter coast.")]
    public float flickDamping = 7f;
    [Tooltip("Quiet time after the motion stops before the owner commits the focus.")]
    public float settleDelay = 0.12f;
    [Tooltip("How many items the target may run ahead of the visible position.")]
    public float maxQueuedSteps = 5f;
    [Tooltip("Coasting speed below which a flick hands over to the snap spring.")]
    public float flickSnapThreshold = 1.2f;

    public int Count { get; private set; }
    public float Scroll { get; private set; }
    public float Target { get; private set; }
    public float Velocity { get; private set; }
    public Mode CurrentMode { get; private set; }
    /// <summary>True from the first input until settleDelay after the motion stops.</summary>
    public bool Moving { get; private set; }
    public bool IsDragging { get; private set; }
    /// <summary>Unwrapped round(Scroll); the card layout measures its offsets from this.</summary>
    public int BaseRaw { get; private set; }
    /// <summary>BaseRaw wrapped into [0, Count).</summary>
    public int BaseIndex { get; private set; }
    /// <summary>Signed distance from the nearest item, in [-0.5, 0.5].</summary>
    public float Frac => Scroll - BaseRaw;
    /// <summary>Scroll position when the current drag began, kept valid across wraps.</summary>
    public float DragAnchorScroll { get; private set; }

    private float settleTimer;
    private float wheelAccumulator;

    public int WrapIndex(int index)
    {
        if (Count <= 0) return 0;
        int wrapped = index % Count;
        return wrapped < 0 ? wrapped + Count : wrapped;
    }

    public void SetCount(int count)
    {
        Count = Mathf.Max(0, count);
    }

    /// <summary>Place the strip on an item with no animation and clear all motion state.</summary>
    public void SnapTo(int index)
    {
        Scroll = Target = Count > 0 ? WrapIndex(index) : 0f;
        Velocity = 0f;
        CurrentMode = Mode.Snap;
        Moving = false;
        IsDragging = false;
        settleTimer = 0f;
        wheelAccumulator = 0f;
        DragAnchorScroll = Scroll;
        BaseRaw = Mathf.FloorToInt(Scroll + 0.5f);
        BaseIndex = WrapIndex(BaseRaw);
    }

    /// <summary>
    /// Queue a move of <paramref name="steps"/> items.  Repeated calls stack
    /// instead of being dropped, which is what turns a fast wheel spin into one
    /// continuous glide rather than a series of rejected inputs.
    /// </summary>
    public bool Nudge(int steps)
    {
        if (Count <= 1 || steps == 0) return false;

        if (CurrentMode != Mode.Snap) Target = Mathf.Round(Scroll);
        CurrentMode = Mode.Snap;

        // Cap the lead, otherwise a long spin keeps travelling for seconds after
        // the wheel has stopped.
        float maxLead = Mathf.Max(1f, maxQueuedSteps);
        float lead = Mathf.Clamp(Target + steps - Scroll, -maxLead, maxLead);
        Target = Mathf.Round(Scroll + lead);

        Moving = true;
        settleTimer = 0f;
        return true;
    }

    /// <summary>
    /// Turn a raw wheel delta into whole steps, remembering the fractional part
    /// so high-resolution trackpads still move exactly one item per notch.
    /// </summary>
    public int ConsumeWheel(float delta)
    {
        if (Mathf.Abs(delta) < 0.01f) return 0;

        // Desktop mice report one notch as 120; some backends report 1.
        float notches = Mathf.Abs(delta) >= 10f ? delta / 120f : delta;
        wheelAccumulator += notches;

        int steps = (int)wheelAccumulator;
        if (steps == 0 && Mathf.Abs(wheelAccumulator) > 0.15f)
            steps = wheelAccumulator > 0f ? 1 : -1;
        wheelAccumulator -= steps;
        return steps;
    }

    public void BeginDrag()
    {
        if (Count <= 1) return;
        IsDragging = true;
        DragAnchorScroll = Scroll;
        CurrentMode = Mode.Drag;
        Moving = true;
        settleTimer = 0f;
    }

    /// <summary>Place the strip directly under the pointer and track the throw speed.</summary>
    public void DragTo(float scroll, float deltaTime)
    {
        if (!IsDragging) return;
        float previous = Scroll;
        Scroll = scroll;
        float instant = (Scroll - previous) / Mathf.Max(deltaTime, 0.0001f);
        Velocity = Mathf.Lerp(Velocity, instant, 0.45f);
    }

    public void EndDrag()
    {
        if (!IsDragging) return;
        IsDragging = false;
        CurrentMode = Mode.Inertia;
        Velocity = Mathf.Clamp(Velocity, -maxSpeed, maxSpeed);
    }

    /// <summary>
    /// Advance the motion by one frame.  <paramref name="baseChanged"/> reports
    /// that the nearest item changed, which is when the owner should roll its
    /// card contents; <paramref name="settled"/> fires once when the strip has
    /// come to rest, which is when the expensive work belongs.
    /// </summary>
    public void Tick(float deltaTime, out bool baseChanged, out bool settled)
    {
        baseChanged = false;
        settled = false;
        if (Count <= 0) return;

        if (CurrentMode == Mode.Inertia)
        {
            Scroll += Velocity * deltaTime;
            Velocity *= Mathf.Exp(-flickDamping * deltaTime);
            if (Mathf.Abs(Velocity) < flickSnapThreshold)
            {
                // Aim slightly ahead so a dying flick still lands the way it was thrown.
                Target = Mathf.Round(Scroll + Velocity * 0.16f);
                CurrentMode = Mode.Snap;
            }
        }
        else if (CurrentMode == Mode.Snap)
        {
            float velocity = Velocity;
            Scroll = Mathf.SmoothDamp(Scroll, Target, ref velocity, snapSmoothTime, maxSpeed, deltaTime);
            Velocity = velocity;
            if (Mathf.Abs(Target - Scroll) < 0.002f && Mathf.Abs(Velocity) < 0.08f)
            {
                Scroll = Target;
                Velocity = 0f;
            }
        }

        Wrap();

        BaseRaw = Mathf.FloorToInt(Scroll + 0.5f);
        int index = WrapIndex(BaseRaw);
        if (index != BaseIndex)
        {
            BaseIndex = index;
            baseChanged = true;
        }

        if (!Moving) return;

        bool atRest = CurrentMode == Mode.Snap && !IsDragging && Velocity == 0f && Scroll == Target;
        if (atRest)
        {
            settleTimer += deltaTime;
            if (settleTimer >= settleDelay)
            {
                Moving = false;
                settleTimer = 0f;
                settled = true;
            }
        }
        else
        {
            settleTimer = 0f;
        }
    }

    /// <summary>
    /// Keep the position inside [0, Count) so long spins cannot drift into float
    /// imprecision.  Target and the drag anchor shift by the same amount so the
    /// motion in progress is unaffected.
    /// </summary>
    private void Wrap()
    {
        if (Count <= 0) return;
        while (Scroll < 0f)
        {
            Scroll += Count;
            Target += Count;
            DragAnchorScroll += Count;
        }
        while (Scroll >= Count)
        {
            Scroll -= Count;
            Target -= Count;
            DragAnchorScroll -= Count;
        }
    }
}
