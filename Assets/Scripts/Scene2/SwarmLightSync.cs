using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A light synchronization model operating on the controller's shared robot clocks.</summary>
[Serializable]
internal abstract class SwarmLightSync
{
    internal sealed class Robot
    {
        internal GameObject agent;
        internal AgentBlinker blinker;
        internal float clock;
        internal float randomSample;
        internal double naturalFrequencySample;
        internal Vector2 position;
        internal bool active;
    }

    internal abstract void Step(IReadOnlyList<Robot> robots, SwarmManager swarm, float period, float deltaTime);

    internal virtual void DriveLight(Robot robot, float dutyCycle) => robot.blinker.DriveClock(robot.clock, dutyCycle);

    internal abstract string NeighboursLabel { get; }

    internal abstract float MeanNeighbours(IReadOnlyList<Robot> robots, SwarmManager swarm, int activeCount);

    internal abstract void DrawControls(SwarmLightSyncController panel, SwarmManager swarm);
}
