using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects, while a run is in progress, that the swarm's spread has stopped changing.
///
/// The question "has it finished spreading?" cannot be answered from the value itself, because the
/// value it settles at is what you are trying to measure. It has to be answered from the rate of
/// change: take the slope over a rolling window and require it to stay near zero for a while.
///
/// This is the same rule as <c>Analysis/steady_state.py find_plateau</c>, run forward one sample at
/// a time instead of over a finished array. Checked against all 130 zero-randomness recordings in
/// the 20260825_014231 batch: it stops each of them within 0.5 u² of the offline plateau, and never
/// while the hull is still below 90% of where it ends up.
///
/// The tolerance is a fraction of the largest value seen so far, not an absolute number, so one
/// setting works whether a swarm settles at 25 u² or 900 u². Using the running peak rather than the
/// final one also makes an early false positive harder: in the first seconds the peak is small, so
/// the tolerance is tight.
///
/// Note this is only dependable when the signal is quiet, which for this project means a run with
/// no random movement. With randomness the hull keeps wobbling by several u²/s after the swarm has
/// stopped spreading, and no absolute tolerance separates that from a real trend.
/// </summary>
public class SpreadSettleDetector
{
    /// <summary>Seconds the slope is measured over. Shorter is noisier, longer blurs the moment.</summary>
    public float windowSeconds = 1f;

    /// <summary>Slope limit, as a fraction of the largest value seen, per second.</summary>
    public float tolerance = 0.02f;

    /// <summary>Seconds the slope must stay inside the limit before this reports settled.</summary>
    public float holdSeconds = 3f;

    private readonly List<float> times = new List<float>();
    private readonly List<float> values = new List<float>();

    // Walks forward with the window rather than searching back each sample.
    private int windowStart;

    private float heldSince = -1f;

    public bool IsSettled { get; private set; }
    public float Peak { get; private set; }
    public float Slope { get; private set; }
    public float SettledAt { get; private set; }
    public float SettledValue { get; private set; }

    /// <summary>Seconds the slope has been continuously inside the limit. 0 when it is not.</summary>
    public float HeldFor { get; private set; }

    public void Reset()
    {
        times.Clear();
        values.Clear();
        windowStart = 0;
        heldSince = -1f;
        HeldFor = 0f;
        Peak = 0f;
        Slope = 0f;
        IsSettled = false;
        SettledAt = 0f;
        SettledValue = 0f;
    }

    /// <summary>
    /// Feeds one sample and returns whether the spread now counts as settled.
    ///
    /// Once settled it stays settled: the caller is ending the recording on it, and letting it flip
    /// back on a single noisy sample would make the stopping moment depend on which frame the check
    /// happened to run.
    /// </summary>
    public bool Push(float time, float value)
    {
        if (IsSettled) return true;

        times.Add(time);
        values.Add(value);
        Peak = Mathf.Max(Peak, value);

        // Advance the window start until it is the oldest sample still inside the window.
        while (windowStart < times.Count - 1 && time - times[windowStart] > windowSeconds)
        {
            windowStart++;
        }

        float span = time - times[windowStart];
        if (span < windowSeconds * 0.5f)
        {
            // Not enough history yet to measure a slope over. Deliberately not treated as settled:
            // at the start of a run the value is flat because nothing has moved, and reporting that
            // as settled would end the clip before the swarm had begun.
            HeldFor = 0f;
            heldSince = -1f;
            return false;
        }

        Slope = (value - values[windowStart]) / span;

        float limit = tolerance * Mathf.Max(Peak, 1e-6f);
        if (Mathf.Abs(Slope) <= limit)
        {
            if (heldSince < 0f) heldSince = time;
            HeldFor = time - heldSince;

            if (HeldFor >= holdSeconds)
            {
                IsSettled = true;
                SettledAt = time;
                SettledValue = value;
            }
        }
        else
        {
            heldSince = -1f;
            HeldFor = 0f;
        }

        return IsSettled;
    }

    /// <summary>Short readout for a log line or the UI.</summary>
    public string Describe()
    {
        if (IsSettled) return $"settled at {SettledAt:F1}s, value {SettledValue:F1}";

        float limit = tolerance * Mathf.Max(Peak, 1e-6f);
        return $"slope {Slope:F2}/s vs limit {limit:F2}, held {HeldFor:F1}s of {holdSeconds:F1}s";
    }
}
