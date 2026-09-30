using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace phys1ksUI;

/// <summary>
/// Small animation helpers: values that ease toward a target frame by frame (hover fades, toggles, the segmented pill,
/// the active nav row), timed tweens and pulses. Everything jumps straight to its end
/// with Dalamud's "reduce motion", and a duration of zero or less counts as already finished.
/// </summary>
internal static class Motion
{
    /// <summary>Eased values by ImGui id, with the frame each was last used (stale ones are swept, see <see cref="Sweep"/>).</summary>
    private static readonly Dictionary<uint, (float Value, int Frame)> Values = new();
    private const int StaleFrames = 600;
    private static int lastSweep;

    /// <summary>Frame time, capped so a hitch doesn't make everything jump.</summary>
    private static float DeltaTime => MathF.Min(ImGui.GetIO().DeltaTime, 0.05f);

    /// <summary>Eases the value stored under <paramref name="key"/> toward <paramref name="target"/> (exponential, frame-rate independent).</summary>
    public static float Approach(uint key, float target, float speed = 18f, float snap = 0.002f)
    {
        var frame = ImGui.GetFrameCount();
        Sweep(frame);
        if (Kit.ReducedMotion || !Values.TryGetValue(key, out var current))
        {
            Values[key] = (target, frame);
            return target;
        }
        var next = current.Value + (target - current.Value) * (1f - MathF.Exp(-speed * DeltaTime));
        if (MathF.Abs(next - target) < snap)
            next = target;
        Values[key] = (next, frame);
        return next;
    }

    /// <summary>Drops values no widget has asked for in a while (rows scrolled away, closed pages), so the table doesn't grow forever.</summary>
    private static void Sweep(int frame)
    {
        if (frame - lastSweep < StaleFrames)
            return;
        lastSweep = frame;
        List<uint>? stale = null;
        foreach (var (key, entry) in Values)
            if (frame - entry.Frame > StaleFrames)
                (stale ??= []).Add(key);
        if (stale != null)
            foreach (var key in stale)
                Values.Remove(key);
    }

    /// <summary>0..1: how far a hover highlight has faded in.</summary>
    public static float Hover(uint key, bool hovered) => Approach(key, hovered ? 1f : 0f);

    /// <summary>0..1 over <paramref name="durationMs"/> since <paramref name="startTick"/> (Environment.TickCount64), eased out.</summary>
    public static float Reveal(long startTick, float durationMs)
    {
        if (Kit.ReducedMotion || !(durationMs > 0f))
            return 1f;
        var t = Math.Clamp((Environment.TickCount64 - startTick) / durationMs, 0f, 1f);
        return EaseOutCubic(t);
    }

    /// <summary>
    /// A tween from <paramref name="from"/> to <paramref name="to"/> started at <paramref name="startTick"/>, eased in and
    /// out; <paramref name="fullMs"/> is the time a whole 0→1 run takes (shorter runs take proportionally less).
    /// </summary>
    public static float Tween(float from, float to, long startTick, float fullMs)
    {
        var t = TweenProgress(from, to, startTick, fullMs);
        return t >= 1f ? to : from + (to - from) * EaseInOutCubic(t);
    }

    /// <summary>
    /// Like <see cref="Tween"/> but eased out (fast start, soft landing): folds and reveals that answer a click.
    /// </summary>
    public static float TweenOut(float from, float to, long startTick, float fullMs)
    {
        var t = TweenProgress(from, to, startTick, fullMs);
        return t >= 1f ? to : from + (to - from) * EaseOutCubic(t);
    }

    /// <summary>Linear 0..1 progress of a from→to run; 1 (done) with reduced motion, no distance or no duration.</summary>
    private static float TweenProgress(float from, float to, long startTick, float fullMs)
    {
        var span = MathF.Abs(to - from);
        if (Kit.ReducedMotion || span <= 0.0001f || !(fullMs > 0f))
            return 1f;
        return Math.Clamp((Environment.TickCount64 - startTick) / (fullMs * span), 0f, 1f);
    }

    /// <summary>Smoothstep (0..1, clamped): gentle at both ends. The toggle's knob and track.</summary>
    public static float EaseSmooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float EaseInOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;

    public static float EaseOutCubic(float t)
    {
        var u = 1f - t;
        return 1f - u * u * u;
    }

    /// <summary>
    /// Frames after which an unused entry counts as stale; widgets with their own per-id state (fold cards) sweep with
    /// the same threshold via <see cref="IsStale"/>.
    /// </summary>
    public const int StaleAfterFrames = StaleFrames;

    /// <summary>Whether something last used on <paramref name="lastFrame"/> hasn't been drawn for <see cref="StaleAfterFrames"/>.</summary>
    public static bool IsStale(int lastFrame, int frame) => frame - lastFrame > StaleFrames;

    /// <summary>Alpha for a pulsing dot (0.1..1); steady at 1 with reduced motion.</summary>
    public static float Pulse()
        => Kit.ReducedMotion ? 1f : 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 3f);
}
