using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pulse coupling after Nicky Case's Fireflies (ncase.me/fireflies).
/// The clock nudge is ported from js/index.js in github.com/ncase/fireflies (CC0).
/// </summary>
[Serializable]
internal sealed class FireflyLightSync : SwarmLightSync
{
    private enum RadiusMode
    {
        Perception,
        Custom
    }

    private static readonly string[] RadiusModes = { "Perception radius", "Custom" };

    [Tooltip("Each robot's period differs from the shared period by up to this fraction.")]
    [SerializeField, Range(0f, 0.3f)] private float speedSpread;

    [Tooltip("Lets a flash advance the clocks of nearby robots.")]
    [SerializeField] private bool nudgeNeighbours = true;

    [Tooltip("Fraction by which a nearby flash advances a robot's clock.")]
    [SerializeField, Range(0.01f, 0.1f)] private float nudge = 0.035f;

    [SerializeField] private RadiusMode radiusMode;

    [Tooltip("Custom centre-to-centre distance in world units.")]
    [SerializeField, Range(0.2f, 10f)] private float customRadius = 2f;

    private readonly HashSet<Robot> flashes = new HashSet<Robot>();

    internal override void Step(IReadOnlyList<Robot> robots, SwarmManager swarm, float period, float deltaTime)
    {
        flashes.Clear();

        // Advance every clock before delivering flashes, so the update is independent of robot order.
        foreach (Robot robot in robots)
        {
            if (!robot.active) continue;
            float robotPeriod = Mathf.Max(period, 0.05f) *
                (1f + Mathf.Clamp(speedSpread, 0f, 0.9f) * robot.randomSample);
            robot.clock += deltaTime / robotPeriod;

            if (robot.clock > 1f)
            {
                // Keeping the overshoot preserves the free-running period.
                robot.clock = Mathf.Repeat(robot.clock, 1f);
                flashes.Add(robot);
            }
        }

        if (nudgeNeighbours && flashes.Count > 0) NudgeNeighbours(robots, CurrentRadius(swarm));
    }

    private void NudgeNeighbours(IReadOnlyList<Robot> robots, float radius)
    {
        float radiusSq = radius * radius;
        float gain = 1f + nudge;

        foreach (Robot robot in robots)
        {
            if (!robot.active || flashes.Contains(robot)) continue;

            int seen = 0;
            foreach (Robot other in flashes)
            {
                if ((other.position - robot.position).sqrMagnitude < radiusSq) seen++;
            }

            // Each flash multiplies the clock by the same gain, capped at the next flash.
            if (seen > 0) robot.clock = Mathf.Min(1f, robot.clock * Mathf.Pow(gain, seen));
        }
    }

    internal override string NeighboursLabel => "Mean neighbours";

    internal override float MeanNeighbours(IReadOnlyList<Robot> robots, SwarmManager swarm, int activeCount)
    {
        if (activeCount == 0) return 0f;

        float radius = CurrentRadius(swarm);
        float radiusSq = radius * radius;
        int pairs = 0;
        foreach (Robot robot in robots)
        {
            if (!robot.active) continue;
            foreach (Robot other in robots)
            {
                if (other != robot && other.active && (other.position - robot.position).sqrMagnitude < radiusSq)
                    pairs++;
            }
        }
        return (float)pairs / activeCount;
    }

    private float CurrentRadius(SwarmManager swarm)
    {
        if (radiusMode == RadiusMode.Custom) return customRadius;
        if (swarm == null) return 0f;

        // Perception is measured to the collider edge. Add the body radius for a centre distance.
        Collider2D[] colliders = swarm.AgentColliders;
        if (colliders != null)
        {
            foreach (Collider2D collider in colliders)
            {
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                    return swarm.perceptionRadius + Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y);
            }
        }
        return swarm.perceptionRadius;
    }

    internal override void DrawControls(SwarmLightSyncController panel, SwarmManager swarm)
    {
        panel.Caption("lights that synchronise like ncase.me/fireflies");
        nudgeNeighbours = GUILayout.Toggle(nudgeNeighbours, " Nudge thy neighbour");
        panel.Row("Nudge", $"{nudge:F3}");
        nudge = GUILayout.HorizontalSlider(nudge, 0.01f, 0.1f);

        GUILayout.Space(4);
        radiusMode = (RadiusMode)GUILayout.SelectionGrid((int)radiusMode, RadiusModes, 2);
        panel.Row("Radius", $"{CurrentRadius(swarm):F2}");
        if (radiusMode == RadiusMode.Custom)
            customRadius = GUILayout.HorizontalSlider(customRadius, 0.2f, 10f);

        panel.DrawBlinkControls();
        panel.Row("Speed spread", $"±{speedSpread * 100f:F0}%");
        speedSpread = GUILayout.HorizontalSlider(speedSpread, 0f, 0.3f);
    }
}
