using System;
using System.Collections.Generic;

/// <summary>Detects sustained stable spacing, optionally requires low speed, and classifies contraction.</summary>
public class MotionSettleDetector
{
    public float windowSeconds = 3f;
    public bool requireLowSpeed = true;
    public float spacingTolerance = 0.01f;
    public float holdSeconds = 3f;
    public float minimumRunSeconds = 5f;
    public float contractionFraction = 0.05f;

    public float Baseline { get; private set; }
    public float Spacing { get; private set; }
    public float SpacingRange { get; private set; }
    public float MaxAgentSpeed { get; private set; }
    public float SpeedLimit { get; private set; }
    public float HeldFor { get; private set; }
    public bool IsSettled { get; private set; }
    public bool HasContracted => Baseline > 0f &&
        Spacing <= Baseline * (1f - Math.Max(0f, Math.Min(1f, contractionFraction)));

    private readonly List<float> times = new List<float>();
    private readonly List<float> spacings = new List<float>();
    private int windowStart;
    private float heldSince = -1f;

    public void Reset(float baseline)
    {
        times.Clear();
        spacings.Clear();
        windowStart = 0;
        heldSince = -1f;
        Baseline = IsFinite(baseline) ? Math.Max(0f, baseline) : 0f;
        Spacing = Baseline;
        SpacingRange = 0f;
        MaxAgentSpeed = 0f;
        SpeedLimit = 0f;
        HeldFor = 0f;
        IsSettled = false;
    }

    public bool Push(float time, float spacing, float maxAgentSpeed, float speedLimit)
    {
        if (IsSettled) return true;

        if (!IsFinite(time) || time < 0f || !IsFinite(spacing) || spacing < 0f ||
            !IsFinite(maxAgentSpeed) || maxAgentSpeed < 0f || !IsFinite(speedLimit) || speedLimit < 0f)
        {
            ClearWindow();
            return false;
        }

        float window = Math.Max(0.01f, windowSeconds);
        if (times.Count > 0)
        {
            float previousTime = times[times.Count - 1];
            if (time == previousTime) return false;
            if (time < previousTime || time - previousTime > window) ClearWindow();
        }

        Spacing = spacing;
        MaxAgentSpeed = maxAgentSpeed;
        SpeedLimit = speedLimit;
        times.Add(time);
        spacings.Add(spacing);

        float cutoff = time - window;
        // Keep the sample at or just before the window boundary to cover a full second.
        while (windowStart + 1 < times.Count && times[windowStart + 1] <= cutoff)
            windowStart++;

        if (windowStart >= 64)
        {
            times.RemoveRange(0, windowStart);
            spacings.RemoveRange(0, windowStart);
            windowStart = 0;
        }

        float minimum = spacing;
        float maximum = spacing;
        for (int i = windowStart; i < spacings.Count; i++)
        {
            minimum = Math.Min(minimum, spacings[i]);
            maximum = Math.Max(maximum, spacings[i]);
        }
        SpacingRange = maximum - minimum;

        // Window range rejects oscillations that have zero net slope across the endpoints.
        bool stable = times[windowStart] <= cutoff &&
            SpacingRange <= Math.Max(0f, spacingTolerance) * Baseline &&
            (!requireLowSpeed || maxAgentSpeed <= speedLimit);
        if (!stable)
        {
            heldSince = -1f;
            HeldFor = 0f;
            return false;
        }

        if (heldSince < 0f) heldSince = time;
        HeldFor = time - heldSince;
        IsSettled = time >= Math.Max(0f, minimumRunSeconds) &&
            HeldFor >= Math.Max(0f, holdSeconds);
        return IsSettled;
    }

    private void ClearWindow()
    {
        times.Clear();
        spacings.Clear();
        windowStart = 0;
        heldSince = -1f;
        HeldFor = 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
