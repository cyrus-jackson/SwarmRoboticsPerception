using System.Collections.Generic;
using UnityEngine;

/// <summary>Integrates Equation (4) of Oscillators that sync and swarm in the common rotating frame.</summary>
internal sealed class SwarmalatorLightSync
{
    internal const float MaxCoupling = 2f;
    private const float TwoPi = 2f * Mathf.PI;
    private const float MinDistance = 0.1f;
    private const float UnisonSpread = 0.01f; // cycles, peak to peak
    private float[] phaseSums = System.Array.Empty<float>();

    internal readonly struct Oscillator
    {
        public Vector2 Position { get; }
        public float Clock { get; }

        public Oscillator(Vector2 position, float clock)
        {
            Position = position;
            Clock = clock;
        }
    }

    /// <summary>Seeds repulsive coupling when identical phases would otherwise remain in equilibrium.</summary>
    internal static void PerturbUnison(List<Oscillator> oscillators)
    {
        int count = oscillators.Count;
        if (count < 2) return;

        float clock = oscillators[0].Clock;
        for (int i = 1; i < count; i++)
        {
            if (oscillators[i].Clock != clock) return;
        }

        for (int i = 0; i < count; i++)
        {
            float offset = UnisonSpread * (i / (float)(count - 1) - 0.5f);
            oscillators[i] = new Oscillator(oscillators[i].Position, Mathf.Repeat(clock + offset, 1f));
        }
    }

    /// <summary>Clocks store phase in cycles. The sine and coupling in the paper use radians.</summary>
    internal void Step(List<Oscillator> oscillators, float coupling, float deltaTime)
    {
        int count = oscillators.Count;
        if (count < 2 || coupling == 0f) return;

        if (phaseSums.Length < count) phaseSums = new float[count];
        else System.Array.Clear(phaseSums, 0, count);

        for (int i = 0; i < count - 1; i++)
        {
            Oscillator first = oscillators[i];
            for (int j = i + 1; j < count; j++)
            {
                Oscillator second = oscillators[j];
                // The original inverse-distance kernel is singular for overlapping agents.
                float distance = Mathf.Max(Vector2.Distance(first.Position, second.Position), MinDistance);
                float phaseDifference = TwoPi * (second.Clock - first.Clock);
                float interaction = Mathf.Sin(phaseDifference) / distance;
                phaseSums[i] += interaction;
                phaseSums[j] -= interaction;
            }
        }

        float stepScale = coupling * deltaTime / (TwoPi * count);
        for (int i = 0; i < count; i++)
        {
            Oscillator oscillator = oscillators[i];
            float clock = Mathf.Repeat(oscillator.Clock + stepScale * phaseSums[i], 1f);
            oscillators[i] = new Oscillator(oscillator.Position, clock);
        }
    }
}
