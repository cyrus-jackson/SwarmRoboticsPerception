#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Displays per-motion recording rules and validates their inspector settings.</summary>
[CustomEditor(typeof(SimRecorder))]
public class SimRecorderEditor : Editor
{
    private static readonly string[] EndConditionLabels =
        { "Fixed Duration", "Target Area Reached", "Density Ratio", "Absolute Hull Area", "Spread Settled",
          "Cluster Count", "Wall Contact Reached", "Sample Other Video Length", "Motion Settled", "DBSCAN Count Stable" };
    private const int TargetAreaReachedIndex = 1;
    private const int DensityRatioReachedIndex = 2;
    private const int AbsoluteHullAreaIndex = 3;
    private const int SpreadSettledIndex = 4;
    private const int ClusterCountIndex = 5;
    private const int WallContactReachedIndex = 6;
    private const int SampledOtherMotionDurationIndex = 7;
    private const int MotionSettledIndex = 8;
    private const int ClusterCountStableIndex = 9;
    private static readonly string[] NonRandomEndConditionLabels = new string[EndConditionLabels.Length - 1];
    private static readonly int[] NonRandomEndConditionValues = { 0, 1, 2, 3, 4, 5, 6, MotionSettledIndex, ClusterCountStableIndex };

    static SimRecorderEditor()
    {
        System.Array.Copy(EndConditionLabels, NonRandomEndConditionLabels, SampledOtherMotionDurationIndex);
        NonRandomEndConditionLabels[SampledOtherMotionDurationIndex] = EndConditionLabels[MotionSettledIndex];
        NonRandomEndConditionLabels[MotionSettledIndex] = EndConditionLabels[ClusterCountStableIndex];
    }

    /// <summary>Index of DensityRatioBaseline.NoRandomnessReference in that enum.</summary>
    private const int NoRandomnessReferenceIndex = 1;

    private SerializedProperty motionTypeSettingsProperty;

    private void OnEnable()
    {
        motionTypeSettingsProperty = serializedObject.FindProperty("motionTypeRecordingSettings");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "m_Script", "motionTypeRecordingSettings");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Recording Rules (per motion type)", EditorStyles.boldLabel);

        if (motionTypeSettingsProperty == null)
        {
            EditorGUILayout.HelpBox("Could not find 'motionTypeRecordingSettings' on SimRecorder.", MessageType.Warning);
        }
        else
        {
            for (int i = 0; i < motionTypeSettingsProperty.arraySize; i++)
            {
                SerializedProperty entry = motionTypeSettingsProperty.GetArrayElementAtIndex(i);
                SerializedProperty swarmType = entry.FindPropertyRelative("swarmType");
                SerializedProperty startDelay = entry.FindPropertyRelative("recordingStartDelay");
                SerializedProperty wallsToDisable = entry.FindPropertyRelative("wallsToDisable");
                SerializedProperty endCondition = entry.FindPropertyRelative("endCondition");
                SerializedProperty percent = entry.FindPropertyRelative("goalAreaAgentPercent");
                SerializedProperty wallContactPercent = entry.FindPropertyRelative("wallContactAgentPercent");
                SerializedProperty densityMetric = entry.FindPropertyRelative("densityMetric");
                SerializedProperty densityBaseline = entry.FindPropertyRelative("densityRatioBaseline");
                SerializedProperty densityTargetRatio = entry.FindPropertyRelative("densityTargetRatio");
                SerializedProperty absoluteArea = entry.FindPropertyRelative("absoluteHullAreaTarget");
                SerializedProperty dwell = entry.FindPropertyRelative("endConditionDwellTime");
                SerializedProperty clusterTarget = entry.FindPropertyRelative("clusterCountTarget");
                SerializedProperty minClusterSize = entry.FindPropertyRelative("minClusterSize");
                SerializedProperty settleTolerance = entry.FindPropertyRelative("settleTolerance");
                SerializedProperty settleHoldTime = entry.FindPropertyRelative("settleHoldTime");
                SerializedProperty endDelay = entry.FindPropertyRelative("recordingEndDelay");
                SerializedProperty overrideTime = entry.FindPropertyRelative("overrideRecordingTime");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(swarmType, new GUIContent("Motion Type"));
                bool isDensificationV2 = swarmType.enumValueIndex == (int)SwarmType.DensificationV2;
                if (isDensificationV2)
                {
                    startDelay.floatValue = 0f;
                    endCondition.enumValueIndex = ClusterCountStableIndex;
                    dwell.floatValue = 0f;
                }
                using (new EditorGUI.DisabledScope(isDensificationV2))
                    EditorGUILayout.PropertyField(startDelay, new GUIContent("Start Delay (s, not recorded)"));

                bool isRandom = swarmType.enumValueIndex == (int)SwarmType.Random;
                if (!isRandom && endCondition.enumValueIndex == SampledOtherMotionDurationIndex)
                    endCondition.enumValueIndex = 0;

                string[] conditionLabels = isRandom ? EndConditionLabels : NonRandomEndConditionLabels;
                int selectedCondition = isRandom ? endCondition.enumValueIndex
                    : System.Array.IndexOf(NonRandomEndConditionValues, endCondition.enumValueIndex);
                using (new EditorGUI.DisabledScope(isDensificationV2))
                {
                    int selectedButton = GUILayout.SelectionGrid(
                        Mathf.Max(0, selectedCondition), conditionLabels, conditionLabels.Length);
                    endCondition.enumValueIndex = isRandom ? selectedButton : NonRandomEndConditionValues[selectedButton];
                }

                bool usesClusterStability = endCondition.enumValueIndex == ClusterCountStableIndex;
                if (usesClusterStability)
                {
                    EditorGUILayout.LabelField("DBSCAN Termination Settings", EditorStyles.boldLabel);
                    SerializedProperty useAutoEps = entry.FindPropertyRelative("dbscanUseAutoEps");
                    EditorGUILayout.PropertyField(useAutoEps, new GUIContent("Use Automatic Eps"));
                    if (useAutoEps.boolValue)
                    {
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("dbscanAutoEpsK"),
                            new GUIContent("Auto Eps Neighbour Rank (k)"));
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("dbscanSpacingMultiple"),
                            new GUIContent("Auto Eps Spacing Multiple"));
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("dbscanEps"),
                            new GUIContent("Fixed Eps (world units)"));
                    }
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("dbscanMinPoints"),
                        new GUIContent("Min Points (includes self)"));
                    EditorGUILayout.HelpBox(
                        "Samples DBSCAN from capture t = 0 and ends once its count has stayed unchanged " +
                        "throughout the preceding 3 seconds, after at least 5 seconds of recorded motion. " +
                        "Every count change restarts the window; there is no extra dwell or speed/spacing check. " +
                        "Uses the DBSCAN settings above for both recordings and Play Motion diagnostics. " +
                        "Settings are captured when the run starts. V2 defaults to fixed epsilon 1.5 and " +
                        "Min Points 3. The cluster panel has its own display settings. Noise is excluded from the count. " +
                        "The duration cap remains, with a minimum of 5 seconds.", MessageType.Info);
                }

                bool usesSampledDuration = endCondition.enumValueIndex == SampledOtherMotionDurationIndex;
                if (usesSampledDuration)
                {
                    EditorGUILayout.HelpBox(
                        "Before every Random clip, independently sample one completed non-Random video " +
                        "from this batch and use its recorded motion duration as the stopping time. " +
                        "Combinations and Swarm Type + Parameter batches run Random last automatically. " +
                        "Each eligible video has an equal chance of selection and can be selected again. " +
                        "The sampled duration includes its recorded tail. " +
                        "Warm-up, ending markers, finished cards and automatic reference clips are excluded. " +
                        "The sampled duration replaces the duration cap; dwell and an additional tail are not applied. " +
                        "Include at least one other motion type in the batch.",
                        MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != TargetAreaReachedIndex))
                {
                    EditorGUILayout.Slider(percent, 0f, 100f, new GUIContent("Agents Inside %"));
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != WallContactReachedIndex))
                {
                    EditorGUILayout.Slider(wallContactPercent, 0f, 100f, new GUIContent("Agents Hit Any Wall %"));
                }

                if (endCondition.enumValueIndex == WallContactReachedIndex)
                {
                    EditorGUILayout.HelpBox(
                        "Counts unique agents that touch any wall during capture. Repeated hits and " +
                        "touching different walls count once per agent. Warm-up contacts are cleared; " +
                        "agents still touching a wall when capture starts count. The percentage uses " +
                        "the agents actually spawned. Both collisions and triggers count. Walls use " +
                        "the Wall tag or a name containing 'wall', including parent objects. " +
                        "0 disables the condition and records to the duration cap.",
                        MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != DensityRatioReachedIndex))
                {
                    EditorGUILayout.PropertyField(densityMetric, new GUIContent("Density Metric"));
                    EditorGUILayout.PropertyField(densityBaseline, new GUIContent("Ratio Measured Against"));
                    EditorGUILayout.Slider(densityTargetRatio, 0.05f, 3f, new GUIContent("Target Ratio (<1 shrink)"));

                    // Against the run's own start the ratio begins at exactly 1, so a target of 1
                    // is true before the swarm has moved. Against the reference it is the natural
                    // "as far as the run without randomness", so the warning is baseline-specific.
                    if (endCondition.enumValueIndex == DensityRatioReachedIndex &&
                        densityBaseline.enumValueIndex != NoRandomnessReferenceIndex &&
                        Mathf.Abs(densityTargetRatio.floatValue - 1f) < 0.05f)
                    {
                        EditorGUILayout.HelpBox(
                            "A target of 1.00 against the recording start is already true on the " +
                            "first frame — the ratio starts at 1 by definition — so every clip would " +
                            "end after just the dwell with the swarm still at its spawn. Use a value " +
                            "clearly above 1 to grow or below 1 to shrink, or switch 'Ratio Measured " +
                            "Against' to NoRandomnessReference.",
                            MessageType.Error);
                    }

                    if (endCondition.enumValueIndex == DensityRatioReachedIndex &&
                        densityBaseline.enumValueIndex == NoRandomnessReferenceIndex)
                    {
                        EditorGUILayout.HelpBox(
                            "The ratio is measured against the settled spread of the randomMovement = 0 " +
                            "run from the same layout and motion settings. Combinations batches measure " +
                            "missing references automatically before recording, even with Use Spawn Layouts " +
                            "off and no zero in the parameter values. These reference runs are not saved " +
                            "as clips. The batch stops if a required reference cannot settle within the " +
                            "duration cap. Other recording modes still require a reference recorded first.",
                            MessageType.Info);
                    }
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != AbsoluteHullAreaIndex))
                {
                    EditorGUILayout.PropertyField(absoluteArea, new GUIContent("Target Hull Area (u2)"));
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != ClusterCountIndex))
                {
                    EditorGUILayout.PropertyField(clusterTarget, new GUIContent("Target Group Count"));
                    EditorGUILayout.IntSlider(minClusterSize, 1, 10, new GUIContent("Smallest Group Counted"));
                }

                if (endCondition.enumValueIndex == ClusterCountIndex)
                {
                    EditorGUILayout.HelpBox(
                        "Groups are the connected components of the perception graph: two agents are " +
                        "joined when one is inside the other's perception radius. An agent that sees " +
                        "nobody is a group of one, so 40 scattered agents count as 40 groups.\n\n" +
                        "The direction is taken from the count when recording starts, so a target " +
                        "below it ends the clip when the swarm merges and a target above it ends the " +
                        "clip when it splits. A target equal to the starting count is rejected.",
                        MessageType.Info);
                }

                // Also used by the no-randomness reference run under the reference baseline, so it
                // stays editable when Density Ratio is selected too.
                bool settleRelevant = endCondition.enumValueIndex == SpreadSettledIndex ||
                                      (endCondition.enumValueIndex == DensityRatioReachedIndex &&
                                       densityBaseline.enumValueIndex == NoRandomnessReferenceIndex);

                using (new EditorGUI.DisabledScope(!settleRelevant))
                {
                    EditorGUILayout.Slider(settleTolerance, 0.001f, 0.1f,
                                           new GUIContent("Settle Slope Limit (frac of peak /s)"));
                    EditorGUILayout.PropertyField(settleHoldTime, new GUIContent("Settle Hold (s)"));
                }

                if (endCondition.enumValueIndex == SpreadSettledIndex)
                {
                    EditorGUILayout.HelpBox(
                        "Ends the clip when the spread stops changing rather than when it reaches a " +
                        "level. Dependable only without random movement: with randomness the hull " +
                        "keeps wobbling by several u2/s after the swarm has stopped spreading, so the " +
                        "slope never settles and the clip runs to the timeout.",
                        MessageType.Info);
                }

                bool usesMotionSettling = endCondition.enumValueIndex == MotionSettledIndex;
                if (usesMotionSettling)
                {
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("motionSettleWindowSeconds"),
                        new GUIContent("Spacing Window (s)"));
                    EditorGUILayout.Slider(entry.FindPropertyRelative("motionSettleSpacingTolerance"), 0f, 0.1f,
                        new GUIContent("Spacing Range / Start"));
                    SerializedProperty requireLowSpeed = entry.FindPropertyRelative("motionSettleRequireLowSpeed");
                    EditorGUILayout.PropertyField(requireLowSpeed, new GUIContent("Require Low Speed"));
                    using (new EditorGUI.DisabledScope(!requireLowSpeed.boolValue))
                    {
                        EditorGUILayout.Slider(entry.FindPropertyRelative("motionSettleSpeedFraction"), 0f, 0.1f,
                            new GUIContent("Speed / Max Speed"));
                    }
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("motionSettleHoldSeconds"),
                        new GUIContent("Stability Hold (s)"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("motionSettleMinimumRunSeconds"),
                        new GUIContent("Minimum Run (s)"));
                    EditorGUILayout.Slider(entry.FindPropertyRelative("motionSettleContractionFraction"), 0f, 1f,
                        new GUIContent("Contraction To Label Success"));
                    EditorGUILayout.HelpBox(
                        "Ends when the range of mean 3-neighbour spacing stays within the spacing limit " +
                        "for the stability hold. Require Low Speed additionally checks every agent's speed; " +
                        "leave it off to detect density equilibrium despite drifting groups or random movement. " +
                        "Waits for a full spacing window and the minimum run time. Disconnected groups " +
                        "may settle independently. A spacing reduction at least as large as the contraction " +
                        "threshold is labelled 'after contraction'; other settled runs are labelled " +
                        "'without contraction'. Density equilibrium outcomes use the 'density-settled' prefix. " +
                        "The duration cap still applies. The separate dwell " +
                        "below is ignored for this rule.", MessageType.Info);
                }

                // Both only mean something when a rule can fire. On FixedDuration there is no event
                // to dwell on and no moment to pad, so they are greyed out rather than misleading.
                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex == 0 || usesSampledDuration || usesMotionSettling || usesClusterStability))
                {
                    EditorGUILayout.PropertyField(dwell, new GUIContent("Dwell Before Ending (s)"));
                }
                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex == 0 || usesSampledDuration))
                {
                    EditorGUILayout.PropertyField(endDelay, new GUIContent("End Delay (s, recorded)"));
                }

                using (new EditorGUI.DisabledScope(usesSampledDuration))
                {
                    EditorGUILayout.PropertyField(overrideTime, new GUIContent("Max Time Override (0 = global)"));
                }
                EditorGUILayout.PropertyField(wallsToDisable, new GUIContent("Walls To Disable (by name)"), true);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Motion Type"))
            {
                motionTypeSettingsProperty.arraySize++;
            }
            using (new EditorGUI.DisabledScope(motionTypeSettingsProperty.arraySize == 0))
            {
                if (GUILayout.Button("Remove Last"))
                {
                    motionTypeSettingsProperty.arraySize--;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
