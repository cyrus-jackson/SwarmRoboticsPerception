using System;

/// <summary>Detects an unchanged cluster count over three seconds, after at least five seconds of capture.</summary>
public class ClusterCountStabilityDetector
{
    public const float WindowSeconds = 3f;
    public const float MinimumRunSeconds = 5f;

    public int ClusterCount { get; private set; }
    public float StableFor { get; private set; }

    private float lastChangeTime;
    private float lastSampleTime;

    public void Reset(int initialClusterCount)
    {
        ClusterCount = initialClusterCount;
        StableFor = 0f;
        lastChangeTime = 0f;
        lastSampleTime = 0f;
    }

    public bool Push(float time, int clusterCount)
    {
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0f || clusterCount < 0)
        {
            StableFor = 0f;
            lastSampleTime = -1f;
            return false;
        }

        if (lastSampleTime < 0f || time < lastSampleTime ||
            time - lastSampleTime > WindowSeconds || clusterCount != ClusterCount)
            lastChangeTime = time;

        ClusterCount = clusterCount;
        lastSampleTime = time;
        StableFor = Math.Max(0f, time - lastChangeTime);

        // Re-evaluate until capture ends: stability before five seconds must not latch the result.
        return time >= MinimumRunSeconds && StableFor >= WindowSeconds;
    }
}
