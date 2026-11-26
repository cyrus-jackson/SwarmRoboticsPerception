using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>Integrates the phase dynamics in Equations (4) and (20) of Oscillators that sync and swarm.</summary>
[Serializable]
internal sealed class SwarmalatorLightSync : SwarmLightSync
{
    private enum DisplayMode
    {
        Blink,
        PhaseColor
    }

    private static readonly string[] DisplayModes = { "Blink", "Phase color" };
    private const float MaxCoupling = 2f;
    private const float MaxPhaseNoise = 0.1f;
    private const float MaxFrequencySpread = 0.1f;
    private const float TwoPi = 2f * Mathf.PI;
    private const float MinDistance = 0.1f;
    private const float UnisonSpread = 0.01f; // cycles, peak to peak

    [Tooltip("Equation (4) coupling K. Positive values synchronize phases; negative values push phases apart.")]
    [SerializeField, Range(-MaxCoupling, MaxCoupling)] private float coupling = 1f;
    [Tooltip("Phase diffusion Dθ in Equation (20), in radians squared per second.")]
    [SerializeField, Range(0f, MaxPhaseNoise)] private float phaseNoise;
    [Tooltip("Lorentzian half-width σ of the natural frequencies, in radians per second.")]
    [SerializeField, Range(0f, MaxFrequencySpread)] private float frequencySpread;
    [SerializeField] private DisplayMode displayMode;

    private readonly List<Oscillator> oscillators = new List<Oscillator>();
    private float[] phaseSums = Array.Empty<float>();
    private float displayClock;
    private Random frequencyRandom;
    private Random noiseRandom;

    internal void ResetForSwarm(int seed)
    {
        frequencyRandom = new Random(seed ^ 0x2745391D);
        noiseRandom = new Random(seed ^ 0x59A41EDB);
        ResetDisplayClock();
    }

    internal double SampleFrequencyFactor()
    {
        double uniform;
        do uniform = frequencyRandom.NextDouble();
        while (uniform == 0d);

        // Inverse CDF for a zero-centered Lorentzian with unit half-width.
        return Math.Tan(Math.PI * (uniform - 0.5d));
    }

    internal override void Step(IReadOnlyList<Robot> robots, SwarmManager swarm, float period, float deltaTime)
    {
        oscillators.Clear();
        foreach (Robot robot in robots)
        {
            if (robot.active)
                oscillators.Add(new Oscillator(robot.position, robot.clock, robot.naturalFrequencySample));
        }
        if (coupling < 0f && phaseNoise == 0f && frequencySpread == 0f) PerturbUnison(oscillators);
        Step(oscillators, coupling, deltaTime);

        // The shared display offset keeps the LEDs blinking and preserves their phase differences.
        displayClock = Mathf.Repeat(displayClock + deltaTime / Mathf.Max(period, 0.05f), 1f);
        int index = 0;
        foreach (Robot robot in robots)
        {
            if (robot.active) robot.clock = oscillators[index++].Clock;
        }
    }

    internal override void DriveLight(Robot robot, float dutyCycle)
    {
        if (displayMode == DisplayMode.PhaseColor)
            robot.blinker.DrivePhaseColor(robot.clock);
        else
            robot.blinker.DriveClock(Mathf.Repeat(robot.clock + displayClock, 1f), dutyCycle);
    }

    internal void ResetDisplayClock() => displayClock = 0f;

    internal override string NeighboursLabel => "Coupled neighbours";

    internal override float MeanNeighbours(IReadOnlyList<Robot> robots, SwarmManager swarm, int activeCount) =>
        Mathf.Max(0, activeCount - 1);

    internal override void DrawControls(SwarmLightSyncController panel, SwarmManager swarm)
    {
        panel.Row("Phase coupling K", $"{coupling:F2}");
        float newCoupling = GUILayout.HorizontalSlider(coupling, -MaxCoupling, MaxCoupling);
        if (newCoupling != coupling) coupling = Mathf.Round(newCoupling * 20f) / 20f;

        panel.Row("Phase noise Dθ", $"{phaseNoise:F4} rad²/s");
        phaseNoise = GUILayout.HorizontalSlider(phaseNoise, 0f, MaxPhaseNoise);
        panel.Row("Frequency spread σ", $"{frequencySpread:F4} rad/s");
        frequencySpread = GUILayout.HorizontalSlider(frequencySpread, 0f, MaxFrequencySpread);

        displayMode = (DisplayMode)GUILayout.Toolbar((int)displayMode, DisplayModes);

        if (displayMode == DisplayMode.Blink) panel.DrawBlinkControls();
    }

    internal readonly struct Oscillator
    {
        public Vector2 Position { get; }
        public float Clock { get; }
        public double NaturalFrequencySample { get; }

        public Oscillator(Vector2 position, float clock, double naturalFrequencySample = 0d)
        {
            Position = position;
            Clock = clock;
            NaturalFrequencySample = naturalFrequencySample;
        }

        public Oscillator WithClock(float clock) => new Oscillator(Position, clock, NaturalFrequencySample);
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
            oscillators[i] = oscillators[i].WithClock(Mathf.Repeat(clock + offset, 1f));
        }
    }

    internal void Step(List<Oscillator> oscillators, float coupling, float deltaTime)
    {
        int count = oscillators.Count;
        if (count == 0 || deltaTime <= 0f) return;

        bool hasCoupling = count > 1 && coupling != 0f;
        if (!hasCoupling && phaseNoise == 0f && frequencySpread == 0f) return;

        if (hasCoupling) AccumulatePhaseSums(oscillators);
        float couplingScale = coupling * deltaTime / (TwoPi * count);
        double frequencyScale = (double)frequencySpread * deltaTime / TwoPi;
        // Euler-Maruyama. The phase noise increment has variance 2 Dθ dt in radians.
        double noiseScale = Math.Sqrt(2d * phaseNoise * deltaTime) / TwoPi;

        for (int i = 0; i < count; i++)
        {
            Oscillator oscillator = oscillators[i];
            double clock = oscillator.Clock + (hasCoupling ? couplingScale * phaseSums[i] : 0f);
            clock += frequencyScale * oscillator.NaturalFrequencySample;
            if (noiseScale > 0d) clock += noiseScale * SampleGaussian();

            float wrappedClock = (float)(clock - Math.Floor(clock));
            oscillators[i] = oscillator.WithClock(wrappedClock < 1f ? wrappedClock : 0f);
        }
    }

    private void AccumulatePhaseSums(List<Oscillator> oscillators)
    {
        int count = oscillators.Count;

        if (phaseSums.Length < count) phaseSums = new float[count];
        else Array.Clear(phaseSums, 0, count);

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
    }

    private double SampleGaussian()
    {
        // Box-Muller needs a strictly positive draw for the logarithm.
        double radius = Math.Sqrt(-2d * Math.Log(1d - noiseRandom.NextDouble()));
        return radius * Math.Cos(2d * Math.PI * noiseRandom.NextDouble());
    }
}
