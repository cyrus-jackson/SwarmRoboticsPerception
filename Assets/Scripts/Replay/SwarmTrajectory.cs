using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Serialisable record of a swarm run: every agent's pose and velocity, frame by frame.
///
/// Frames store a flat float array rather than one object per agent, which keeps the files a
/// fraction of the size and makes them much faster for JsonUtility to parse back. The layout is
/// five floats per agent, in the agent order given by header.agentNames:
///
///     [ x0, y0, rot0, vx0, vy0,  x1, y1, rot1, vx1, vy1,  ... ]
///
/// Use SwarmTrajectory.Stride and the accessor helpers rather than indexing by hand.
/// </summary>
[Serializable]
public class SwarmTrajectory
{
    /// <summary>Floats stored per agent per frame: x, y, rotation, vx, vy.</summary>
    public const int Stride = 5;

    public TrajectoryHeader header = new TrajectoryHeader();
    public List<TrajectoryFrame> frames = new List<TrajectoryFrame>();

    public int FrameCount => frames != null ? frames.Count : 0;
    public int AgentCount => header != null ? header.agentCount : 0;
    public float Duration => FrameCount > 0 ? frames[FrameCount - 1].t : 0f;

    public Vector2 GetPosition(int frame, int agent)
    {
        TrajectoryFrame f = frames[frame];
        int i = agent * Stride;
        return new Vector2(f.v[i], f.v[i + 1]);
    }

    public float GetRotation(int frame, int agent)
    {
        return frames[frame].v[agent * Stride + 2];
    }

    public Vector2 GetVelocity(int frame, int agent)
    {
        TrajectoryFrame f = frames[frame];
        int i = agent * Stride;
        return new Vector2(f.v[i + 3], f.v[i + 4]);
    }

    /// <summary>True when this recording carries wall contact data.</summary>
    public bool HasContactData => header != null && header.contactsRecorded;

    /// <summary>True when this recording carries per-frame hull area.</summary>
    public bool HasHullArea => header != null && header.hullAreaRecorded;

    /// <summary>Recorded trimmed hull area for a frame.</summary>
    public float GetHullArea(int frame)
    {
        if (frame < 0 || frame >= FrameCount) return 0f;
        return frames[frame].a;
    }

    /// <summary>How many agents were touching a wall on a frame.</summary>
    public int ContactCount(int frame)
    {
        if (frame < 0 || frame >= FrameCount) return 0;
        List<int> c = frames[frame].c;
        return c != null ? c.Count : 0;
    }

    /// <summary>Whether a given agent was touching a wall on a frame.</summary>
    public bool IsTouchingWall(int frame, int agent)
    {
        if (frame < 0 || frame >= FrameCount) return false;
        List<int> c = frames[frame].c;
        return c != null && c.Contains(agent);
    }

    /// <summary>Number of distinct agents that touched a wall at any point up to and including a frame.</summary>
    public int UniqueAgentsTouchedBy(int frame)
    {
        if (FrameCount == 0) return 0;

        bool[] touched = new bool[Mathf.Max(AgentCount, 1)];
        int limit = Mathf.Clamp(frame, 0, FrameCount - 1);

        for (int f = 0; f <= limit; f++)
        {
            List<int> c = frames[f].c;
            if (c == null) continue;
            foreach (int agent in c)
            {
                if (agent >= 0 && agent < touched.Length) touched[agent] = true;
            }
        }

        int count = 0;
        foreach (bool t in touched) if (t) count++;
        return count;
    }

    /// <summary>Agent name for a slot, falling back to an index when names were not recorded.</summary>
    public string GetAgentName(int agent)
    {
        if (header != null && header.agentNames != null && agent < header.agentNames.Length)
        {
            return header.agentNames[agent];
        }
        return $"Agent_{agent:D3}";
    }

    /// <summary>
    /// Index of the last frame at or before the given time. Returns -1 when there are no frames.
    /// </summary>
    public int FrameIndexAtTime(float time)
    {
        if (FrameCount == 0) return -1;
        if (time <= frames[0].t) return 0;
        if (time >= frames[FrameCount - 1].t) return FrameCount - 1;

        // Frames are in ascending time order, so binary search.
        int low = 0;
        int high = FrameCount - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (frames[mid].t <= time) low = mid + 1;
            else high = mid - 1;
        }
        return Mathf.Clamp(high, 0, FrameCount - 1);
    }

    /// <summary>
    /// Streams the recording to disk.
    ///
    /// Deliberately not JsonUtility.ToJson on the whole object: that builds the entire document as
    /// one string in memory, roughly double the file size in UTF-16, right at the moment the video
    /// encoder is under most pressure. Writing frame by frame keeps the peak flat.
    ///
    /// Floats are written at limited precision. JsonUtility emits full round-trip precision
    /// (-7.2500000953674316), about 19 bytes per value; three decimals is millimetre resolution in
    /// world units and roughly a third of the size.
    /// </summary>
    public void Save(string path, int decimals = 3)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        header.frameCount = FrameCount;
        header.duration = Duration;

        string format = "0." + new string('#', Mathf.Clamp(decimals, 1, 7));
        CultureInfo invariant = CultureInfo.InvariantCulture;

        using (StreamWriter writer = new StreamWriter(path, false))
        {
            writer.Write("{\"header\":");
            writer.Write(JsonUtility.ToJson(header));
            writer.Write(",\"frames\":[");

            for (int f = 0; f < frames.Count; f++)
            {
                if (f > 0) writer.Write(',');

                TrajectoryFrame frame = frames[f];
                writer.Write("{\"t\":");
                writer.Write(frame.t.ToString(format, invariant));

                writer.Write(",\"a\":");
                writer.Write(frame.a.ToString(format, invariant));

                writer.Write(",\"v\":[");
                for (int i = 0; i < frame.v.Count; i++)
                {
                    if (i > 0) writer.Write(',');
                    writer.Write(frame.v[i].ToString(format, invariant));
                }

                writer.Write("],\"c\":[");
                if (frame.c != null)
                {
                    for (int i = 0; i < frame.c.Count; i++)
                    {
                        if (i > 0) writer.Write(',');
                        writer.Write(frame.c[i].ToString(invariant));
                    }
                }

                writer.Write("]}");
            }

            writer.Write("]}");
        }
    }

    public static SwarmTrajectory Load(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[SwarmTrajectory] No file at {path}");
            return null;
        }

        SwarmTrajectory trajectory = JsonUtility.FromJson<SwarmTrajectory>(File.ReadAllText(path));
        if (trajectory == null || trajectory.frames == null)
        {
            Debug.LogError($"[SwarmTrajectory] Could not parse {path}");
            return null;
        }

        return trajectory;
    }
}

/// <summary>One captured frame: a timestamp and the packed per-agent values.</summary>
[Serializable]
public class TrajectoryFrame
{
    /// <summary>Seconds since capture started.</summary>
    public float t;

    /// <summary>Packed values, SwarmTrajectory.Stride floats per agent.</summary>
    public List<float> v = new List<float>();

    /// <summary>
    /// Indices of the agents touching a wall on this frame. Stored as a sparse index list rather
    /// than a flag per agent, because it is empty on most frames and costs almost nothing there.
    /// </summary>
    public List<int> c = new List<int>();

    /// <summary>
    /// Trimmed convex hull area of the swarm on this frame, computed by SwarmDensityMetrics, the
    /// same code the density end condition uses. Recorded rather than recomputed downstream so
    /// there is one implementation of the measure.
    /// </summary>
    public float a;
}

/// <summary>
/// Everything needed to interpret a recording and to reproduce the run that made it.
/// </summary>
[Serializable]
public class TrajectoryHeader
{
    public string fileName;
    public string sceneName;
    public string recordedAtUtc;
    public string swarmType;

    public int agentCount;           // agents actually captured
    public int requestedAgentCount;  // agents asked for at spawn
    public bool agentSpawnShortfall; // true when fewer spawned than requested
    public int frameCount;
    public float duration;
    public float captureHz;          // 0 means every rendered frame

    // Wall contact capture.
    public bool contactsRecorded;    // false for recordings made before contacts were captured
    public float contactDistance;    // agent radius plus skin, the threshold used
    public float agentRadius;        // radius the threshold was derived from

    // Fixed starting layout, when the run used one.
    public string spawnLayoutId;
    public string spawnLayoutFingerprint;

    // Hull area capture.
    public bool hullAreaRecorded;    // false for recordings made before area was captured
    public float hullTrimFraction;   // fraction of outermost agents dropped before hulling

    public string[] agentNames;

    // Swarm parameters in force during the run.
    public float cohesion;
    public float separation;
    public float alignment;
    public float friction;
    public float randomMovement;
    public float overlapAvoidance;
    public float safetyDistance;
    public float envAvoidance;
    public float obstacleRadius;
    public float perceptionRadius;
    public float maxSpeed;
    public float simulationStep;

    // Scene context.
    public string obstacleName;
    public string goalAreaName;
    public string wallsDisabled;

    // How the run finished, filled in by SimRecorder when the recording window closes.
    public string endReason;         // "goal-area", "density-ratio" or "timeout"
    public string endConditionUsed;  // the rule configured for this motion type
    public float densityTargetRatio;

    /// <summary>
    /// Static scene geometry present during the run: the active obstacle, the active walls and the
    /// goal area. Recorded once because none of it moves while a run is in progress, and it is
    /// what makes a replay legible — agents dodging an invisible obstacle look like noise.
    /// </summary>
    public List<TrajectoryGeometry> geometry = new List<TrajectoryGeometry>();
}

/// <summary>
/// A piece of static scene geometry, captured in world space so the replay scene needs no prefabs
/// and no knowledge of the original scene.
/// </summary>
[Serializable]
public class TrajectoryGeometry
{
    public const string RoleObstacle = "obstacle";
    public const string RoleWall = "wall";
    public const string RoleGoalArea = "goalArea";

    public const string ShapeCircle = "circle";
    public const string ShapeBox = "box";
    public const string ShapePolygon = "polygon";

    public string name;
    public string role;
    public string shape;

    /// <summary>World centre.</summary>
    public float x;
    public float y;

    /// <summary>Z rotation in degrees, used by box shapes.</summary>
    public float rotation;

    /// <summary>Circle radius in world units.</summary>
    public float radius;

    /// <summary>Box size in world units.</summary>
    public float width;
    public float height;

    /// <summary>Polygon outline as flattened world-space x,y pairs.</summary>
    public float[] points;

    public Vector2 Centre => new Vector2(x, y);

    /// <summary>Outline of this shape in world space, ready to feed a LineRenderer.</summary>
    public List<Vector2> BuildOutline(int circleSegments = 48)
    {
        List<Vector2> outline = new List<Vector2>();

        switch (shape)
        {
            case ShapeCircle:
                for (int i = 0; i < circleSegments; i++)
                {
                    float a = (i / (float)circleSegments) * Mathf.PI * 2f;
                    outline.Add(Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                break;

            case ShapePolygon:
                if (points != null)
                {
                    for (int i = 0; i + 1 < points.Length; i += 2)
                    {
                        outline.Add(new Vector2(points[i], points[i + 1]));
                    }
                }
                break;

            default: // box
                float hw = width * 0.5f;
                float hh = height * 0.5f;
                Quaternion q = Quaternion.AngleAxis(rotation, Vector3.forward);
                Vector2[] corners =
                {
                    new Vector2(-hw, -hh), new Vector2(hw, -hh),
                    new Vector2(hw, hh), new Vector2(-hw, hh)
                };
                foreach (Vector2 c in corners)
                {
                    outline.Add(Centre + (Vector2)(q * c));
                }
                break;
        }

        return outline;
    }
}
