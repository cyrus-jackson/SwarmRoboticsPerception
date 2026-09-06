using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Captures the pose and velocity of every agent, frame by frame, so a run can be replayed later
/// by SwarmTrajectoryPlayer.
///
/// Two ways to drive it:
///   - Automatically, from SimRecorder: each batch clip gets a matching .json saved next to the
///     mp4 under the same name, so video and data stay paired.
///   - Manually, via StartCapture / StopCapture, or the toggle in the play mode panel, for free
///     play outside of a recording.
///
/// Put this on the SwarmManager object in the simulation scene. UI.cs adds it if it is missing.
/// </summary>
public class SwarmTrajectoryRecorder : MonoBehaviour
{
    [Header("Wiring")]
    public SwarmManager swarmManager;
    public UI uiController;

    [Header("Capture")]
    [Tooltip("Samples per second. 0 captures every rendered frame, which matches the recorder's frame rate.")]
    public float captureHz = 0f;

    [Tooltip("Folder used when capture is started manually rather than by SimRecorder. Relative to Assets.")]
    public string manualSaveFolder = "SimulationRecordings/Trajectories";

    [Tooltip("Stop and discard if a run somehow exceeds this many frames, as a memory guard.")]
    public int maxFrames = 200000;

    [Header("Wall Contacts")]
    [Tooltip("Record which agents are touching a wall on each frame. Contact is geometric: the agent centre within radius + skin of a wall surface. Physics collision events are not usable here because agents are moved by writing to transform.position.")]
    public bool captureWallContacts = true;

    [Tooltip("Extra clearance counted as contact, in world units.")]
    public float contactSkin = 0.02f;

    [Tooltip("Agent radius. Leave at 0 to read it from the first agent's CircleCollider2D.")]
    public float agentRadiusOverride = 0f;

    [Header("Hull Area")]
    [Tooltip("Record the trimmed convex hull area on each frame, so downstream analysis reads the measure rather than reimplementing it.")]
    public bool captureHullArea = true;

    [Tooltip("Fraction of the outermost agents dropped before hulling. Taken from the density monitor when one is present, so the recorded area matches the density end condition exactly.")]
    [Range(0f, 0.5f)]
    public float hullTrimFraction = 0.1f;

    [Tooltip("Record the sizes of the connected groups each frame, largest first. Groups are the connected components of the perception graph, which is how Hénard et al. define swarm fragmentation. Costs a few small integers per frame.")]
    public bool captureClusters = true;

    [Header("Scene Geometry")]
    [Tooltip("Record the active obstacle so the replay shows what the agents were avoiding.")]
    public bool captureObstacle = true;

    [Tooltip("Record the active walls. Walls disabled for this motion type are skipped, matching what the run actually had.")]
    public bool captureWalls = true;

    [Tooltip("Record the goal area for this motion type.")]
    public bool captureGoalArea = true;

    [Header("Status")]
    [SerializeField] private bool isCapturing = false;

    public bool IsCapturing => isCapturing;
    public int CapturedFrames => working != null ? working.FrameCount : 0;
    public float CapturedDuration => working != null ? working.Duration : 0f;

    private SwarmTrajectory working;
    private float captureStartTime;
    private float nextSampleTime;
    private GameObject[] capturedAgents;

    private readonly List<Collider2D> contactWalls = new List<Collider2D>();
    private float contactDistance;
    private float resolvedAgentRadius;

    // Resolved once per run. Looking these up per agent per frame was 40 GetComponent calls a
    // frame for no reason.
    private Transform[] capturedTransforms;
    private SwarmAgent[] capturedScripts;

    private readonly List<Vector2> hullPositionBuffer = new List<Vector2>();

    void LateUpdate()
    {
        // LateUpdate so the sample reflects the positions after SwarmManager has stepped them.
        if (!isCapturing) return;

        if (captureHz > 0f)
        {
            if (Time.time < nextSampleTime) return;
            nextSampleTime = Time.time + (1f / captureHz);
        }

        CaptureFrame();
    }

    /// <summary>
    /// Begins a new capture, snapshotting the current parameters into the header. Any capture
    /// already in progress is discarded.
    /// </summary>
    public void StartCapture(string label)
    {
        if (swarmManager == null)
        {
            Debug.LogError("[SwarmTrajectoryRecorder] No SwarmManager assigned; cannot capture.");
            return;
        }

        capturedAgents = swarmManager.agents;
        if (capturedAgents == null || capturedAgents.Length == 0)
        {
            Debug.LogWarning("[SwarmTrajectoryRecorder] No agents to capture.");
            return;
        }

        capturedTransforms = new Transform[capturedAgents.Length];
        capturedScripts = new SwarmAgent[capturedAgents.Length];
        for (int i = 0; i < capturedAgents.Length; i++)
        {
            if (capturedAgents[i] == null) continue;
            capturedTransforms[i] = capturedAgents[i].transform;
            capturedScripts[i] = capturedAgents[i].GetComponent<SwarmAgent>();
        }

        CacheContactSetup();

        // Match the density end condition's trim exactly, so the recorded area is the same
        // quantity the recorder was deciding on.
        if (swarmManager.densityMonitor != null)
        {
            hullTrimFraction = swarmManager.densityMonitor.hullTrimFraction;
        }

        working = new SwarmTrajectory();
        working.header = BuildHeader(label);

        captureStartTime = Time.time;
        nextSampleTime = Time.time;
        isCapturing = true;

        // Capture the opening frame immediately so the recording starts at t = 0.
        CaptureFrame();

        Debug.Log($"[SwarmTrajectoryRecorder] Capturing '{label}' with {working.header.agentCount} agents.");
    }

    /// <summary>
    /// Adds context known only to the caller: which walls the motion type had disabled, and the
    /// fixed starting layout if the run used one.
    /// </summary>
    public void SetRunContext(string wallsDisabled, string spawnLayoutId = null,
                              string spawnLayoutFingerprint = null)
    {
        if (working == null) return;

        working.header.wallsDisabled = wallsDisabled;
        working.header.spawnLayoutId = spawnLayoutId;
        working.header.spawnLayoutFingerprint = spawnLayoutFingerprint;
    }

    /// <summary>
    /// Records how the run finished, so a replay can say why it stopped without cross-referencing
    /// batch_config.json. Call before saving.
    /// </summary>
    public void SetOutcome(string endReason, string endConditionUsed, float densityTargetRatio)
    {
        if (working == null) return;

        working.header.endReason = endReason;
        working.header.endConditionUsed = endConditionUsed;
        working.header.densityTargetRatio = densityTargetRatio;
    }

    /// <summary>Stops capturing and returns the recording without writing it to disk.</summary>
    public SwarmTrajectory StopCapture()
    {
        if (!isCapturing) return working;

        isCapturing = false;
        if (working != null)
        {
            working.header.frameCount = working.FrameCount;
            working.header.duration = working.Duration;
        }

        return working;
    }

    /// <summary>Stops capturing and writes the recording to the given folder as label.json.</summary>
    public string StopCaptureAndSave(string absoluteFolder, string label)
    {
        SwarmTrajectory trajectory = StopCapture();
        if (trajectory == null || trajectory.FrameCount == 0)
        {
            Debug.LogWarning("[SwarmTrajectoryRecorder] Nothing captured; no file written.");
            return null;
        }

        string safeLabel = string.IsNullOrEmpty(label) ? $"trajectory_{System.DateTime.Now:yyyyMMdd_HHmmss}" : label;
        trajectory.header.fileName = safeLabel;

        string path = Path.Combine(absoluteFolder, safeLabel + ".json");
        trajectory.Save(path);

        Debug.Log($"[SwarmTrajectoryRecorder] Saved {trajectory.FrameCount} frames " +
                  $"({trajectory.Duration:F2}s, {trajectory.AgentCount} agents) to {path}");
        return path;
    }

    /// <summary>Manual save target, used when capture was not started by SimRecorder.</summary>
    public string StopCaptureAndSaveToManualFolder(string label)
    {
        string folder = Path.Combine(Application.dataPath, manualSaveFolder);
        return StopCaptureAndSave(folder, label);
    }

    /// <summary>
    /// Works out the contact threshold and collects the walls that are active for this run. Walls
    /// a motion type disabled are left out, so contacts reflect the arena the run actually had.
    /// </summary>
    private void CacheContactSetup()
    {
        contactWalls.Clear();
        resolvedAgentRadius = 0f;

        if (!captureWallContacts) return;

        if (agentRadiusOverride > 0f)
        {
            resolvedAgentRadius = agentRadiusOverride;
        }
        else
        {
            foreach (GameObject agentObj in capturedAgents)
            {
                if (agentObj == null) continue;

                CircleCollider2D circle = agentObj.GetComponent<CircleCollider2D>();
                if (circle != null)
                {
                    float scale = Mathf.Max(Mathf.Abs(agentObj.transform.lossyScale.x),
                                            Mathf.Abs(agentObj.transform.lossyScale.y));
                    resolvedAgentRadius = circle.radius * scale;
                }
                break;
            }
        }

        contactDistance = resolvedAgentRadius + Mathf.Max(0f, contactSkin);

        if (uiController == null || uiController.walls == null) return;

        foreach (Transform wall in uiController.walls)
        {
            if (wall == null || !wall.gameObject.activeInHierarchy) continue;

            Collider2D collider = wall.GetComponent<Collider2D>();
            if (collider != null && collider.enabled) contactWalls.Add(collider);
        }
    }

    /// <summary>
    /// Geometric contact test. Collision callbacks are not reliable here: agents are moved by
    /// writing to transform.position, so the solver never sees a proper sweep.
    /// </summary>
    private bool TouchesWall(Vector2 position)
    {
        foreach (Collider2D wall in contactWalls)
        {
            if (wall == null || !wall.enabled || !wall.gameObject.activeInHierarchy) continue;

            // ClosestPoint returns the query point itself when it is inside, giving distance 0.
            Vector2 closest = wall.ClosestPoint(position);
            if ((position - closest).sqrMagnitude <= contactDistance * contactDistance) return true;
        }

        return false;
    }

    private void CaptureFrame()
    {
        if (working == null || capturedAgents == null) return;

        if (working.FrameCount >= maxFrames)
        {
            Debug.LogWarning($"[SwarmTrajectoryRecorder] Hit maxFrames ({maxFrames}); stopping capture.");
            isCapturing = false;
            return;
        }

        float timestamp = Time.time - captureStartTime;

        // StartCapture takes the opening frame directly, and the recorder can drive more than one
        // update within the same Time.time. Skip anything that would land on a timestamp already
        // recorded, so frame gaps are never zero.
        if (working.FrameCount > 0 && timestamp <= working.frames[working.FrameCount - 1].t)
        {
            return;
        }

        TrajectoryFrame frame = new TrajectoryFrame
        {
            t = timestamp,
            v = new List<float>(capturedAgents.Length * SwarmTrajectory.Stride)
        };

        hullPositionBuffer.Clear();

        for (int index = 0; index < capturedTransforms.Length; index++)
        {
            Transform agentTransform = capturedTransforms[index];

            if (agentTransform == null)
            {
                // Keep the slot so agent indices stay aligned across frames.
                for (int i = 0; i < SwarmTrajectory.Stride; i++) frame.v.Add(0f);
                continue;
            }

            Vector3 p = agentTransform.position;
            float rotation = agentTransform.eulerAngles.z;

            SwarmAgent agent = capturedScripts[index];
            Vector2 velocity = agent != null ? agent.Velocity : Vector2.zero;

            frame.v.Add(p.x);
            frame.v.Add(p.y);
            frame.v.Add(rotation);
            frame.v.Add(velocity.x);
            frame.v.Add(velocity.y);

            if (captureWallContacts && contactWalls.Count > 0 && TouchesWall(p))
            {
                frame.c.Add(index);
            }

            if (captureHullArea) hullPositionBuffer.Add(p);
        }

        // Same call the density rule makes, so there is one definition of the measure.
        if (captureHullArea)
        {
            frame.a = SwarmDensityMetrics.TrimmedHullArea(hullPositionBuffer, hullTrimFraction);
        }

        // Connected components of the perception graph, the paper's definition of fragmentation.
        // Sampled from the live agents rather than the captured transforms so the neighbour test
        // sees the same colliders SwarmAgent does.
        if (captureClusters && swarmManager != null)
        {
            SwarmClusterMetrics.ClusterSizes(swarmManager.agents, swarmManager.perceptionRadius,
                                             frame.k, swarmManager.AgentColliders);
        }

        working.frames.Add(frame);
    }

    private TrajectoryHeader BuildHeader(string label)
    {
        string[] names = new string[capturedAgents.Length];
        for (int i = 0; i < capturedAgents.Length; i++)
        {
            names[i] = capturedAgents[i] != null ? capturedAgents[i].name : $"Agent_{i:D3}";
        }

        GoalArea goalArea = swarmManager.goalArea;

        return new TrajectoryHeader
        {
            fileName = label,
            sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            recordedAtUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            swarmType = uiController != null ? uiController.SelectedSwarmType.ToString() : null,
            agentCount = capturedAgents.Length,
            requestedAgentCount = uiController != null ? uiController.RequestedAgentCount : capturedAgents.Length,
            agentSpawnShortfall = uiController != null && uiController.AgentSpawnShortfall,
            captureHz = captureHz,
            agentNames = names,

            contactsRecorded = captureWallContacts && contactWalls.Count > 0,
            contactDistance = contactDistance,
            agentRadius = resolvedAgentRadius,

            hullAreaRecorded = captureHullArea,
            hullTrimFraction = hullTrimFraction,
            clustersRecorded = captureClusters && swarmManager != null,
            clusterPerceptionRadius = swarmManager != null ? swarmManager.perceptionRadius : 0f,

            cohesion = swarmManager.cohesionIntensity,
            separation = swarmManager.separationIntensity,
            alignment = swarmManager.alignmentIntensity,
            friction = swarmManager.frictionIntensity,
            randomMovement = swarmManager.randomMovementIntensity,
            overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
            safetyDistance = swarmManager.safetyDistance,
            envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
            obstacleRadius = swarmManager.obstacleAvoidanceRadius,
            perceptionRadius = swarmManager.perceptionRadius,
            maxSpeed = swarmManager.maxSpeed,
            simulationStep = swarmManager.simulationStep,

            obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
            goalAreaName = goalArea != null ? goalArea.name : null,

            geometry = CaptureGeometry(goalArea)
        };
    }

    /// <summary>
    /// Snapshots the static geometry the run took place in. Only active objects are recorded, so a
    /// motion type that disabled some walls produces a replay with those walls genuinely absent.
    /// </summary>
    private List<TrajectoryGeometry> CaptureGeometry(GoalArea goalArea)
    {
        List<TrajectoryGeometry> geometry = new List<TrajectoryGeometry>();

        if (captureObstacle && swarmManager.centralObstacle != null)
        {
            AddGeometry(geometry, swarmManager.centralObstacle.gameObject, TrajectoryGeometry.RoleObstacle);
        }

        if (captureWalls && uiController != null && uiController.walls != null)
        {
            foreach (Transform wall in uiController.walls)
            {
                if (wall == null) continue;
                AddGeometry(geometry, wall.gameObject, TrajectoryGeometry.RoleWall);
            }
        }

        if (captureGoalArea && goalArea != null)
        {
            AddGeometry(geometry, goalArea.gameObject, TrajectoryGeometry.RoleGoalArea);
        }

        return geometry;
    }

    /// <summary>
    /// Reads a collider's world-space shape into a serialisable entry. Inactive objects are
    /// skipped. Falls back to the bounding box when the collider type is not one we handle.
    /// </summary>
    private static void AddGeometry(List<TrajectoryGeometry> into, GameObject source, string role)
    {
        if (source == null || !source.activeInHierarchy) return;

        TrajectoryGeometry entry = new TrajectoryGeometry { name = source.name, role = role };
        Transform t = source.transform;

        CircleCollider2D circle = source.GetComponent<CircleCollider2D>();
        BoxCollider2D box = source.GetComponent<BoxCollider2D>();
        PolygonCollider2D polygon = source.GetComponent<PolygonCollider2D>();

        if (circle != null)
        {
            Vector2 centre = t.TransformPoint(circle.offset);
            float scale = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));

            entry.shape = TrajectoryGeometry.ShapeCircle;
            entry.x = centre.x;
            entry.y = centre.y;
            entry.radius = circle.radius * scale;
        }
        else if (polygon != null && polygon.points != null && polygon.points.Length > 2)
        {
            Vector2[] local = polygon.points;
            float[] world = new float[local.Length * 2];
            for (int i = 0; i < local.Length; i++)
            {
                Vector2 p = t.TransformPoint(local[i] + polygon.offset);
                world[i * 2] = p.x;
                world[i * 2 + 1] = p.y;
            }

            Vector2 c = t.TransformPoint(polygon.offset);
            entry.shape = TrajectoryGeometry.ShapePolygon;
            entry.x = c.x;
            entry.y = c.y;
            entry.points = world;
        }
        else if (box != null)
        {
            Vector2 centre = t.TransformPoint(box.offset);
            entry.shape = TrajectoryGeometry.ShapeBox;
            entry.x = centre.x;
            entry.y = centre.y;
            entry.rotation = t.eulerAngles.z;
            entry.width = box.size.x * Mathf.Abs(t.lossyScale.x);
            entry.height = box.size.y * Mathf.Abs(t.lossyScale.y);
        }
        else
        {
            // No collider we recognise: fall back to the renderer or transform extents.
            Bounds b;
            Collider2D any = source.GetComponent<Collider2D>();
            if (any != null) b = any.bounds;
            else
            {
                Renderer r = source.GetComponent<Renderer>();
                if (r != null) b = r.bounds;
                else b = new Bounds(t.position, t.lossyScale);
            }

            entry.shape = TrajectoryGeometry.ShapeBox;
            entry.x = b.center.x;
            entry.y = b.center.y;
            entry.rotation = 0f;
            entry.width = b.size.x;
            entry.height = b.size.y;
        }

        into.Add(entry);
    }
}
