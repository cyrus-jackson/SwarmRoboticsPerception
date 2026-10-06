#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Displays per-motion recording rules and validates their inspector settings.</summary>
[CustomEditor(typeof(SimRecorder))]
public class SimRecorderEditor : Editor
{
    private static readonly string[] EndConditionLabels =
        { "Fixed Duration", "Target Area Reached", "Density Ratio", "Absolute Hull Area", "Spread Settled",
          "Cluster Count", "Wall Contact Reached", "Average Other Motions" };
    private const int TargetAreaReachedIndex = 1;
    private const int DensityRatioReachedIndex = 2;
    private const int AbsoluteHullAreaIndex = 3;
    private const int SpreadSettledIndex = 4;
    private const int ClusterCountIndex = 5;
    private const int WallContactReachedIndex = 6;
    private const int AverageOtherMotionDurationIndex = 7;
    private static readonly string[] NonRandomEndConditionLabels = new string[AverageOtherMotionDurationIndex];

    static SimRecorderEditor()
    {
        System.Array.Copy(EndConditionLabels, NonRandomEndConditionLabels, NonRandomEndConditionLabels.Length);
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
                EditorGUILayout.PropertyField(startDelay, new GUIContent("Start Delay (s, not recorded)"));

                bool isRandom = swarmType.enumValueIndex == (int)SwarmType.Random;
                if (!isRandom && endCondition.enumValueIndex == AverageOtherMotionDurationIndex)
                    endCondition.enumValueIndex = 0;

                string[] conditionLabels = isRandom ? EndConditionLabels : NonRandomEndConditionLabels;
                endCondition.enumValueIndex = GUILayout.SelectionGrid(
                    endCondition.enumValueIndex,
                    conditionLabels,
                    conditionLabels.Length);

                bool usesAverageDuration = endCondition.enumValueIndex == AverageOtherMotionDurationIndex;
                if (usesAverageDuration)
                {
                    EditorGUILayout.HelpBox(
                        "Random records for the mean motion duration of all other clips in this batch. " +
                        "Combinations and Swarm Type + Parameter batches run Random last automatically. " +
                        "Each completed non-Random clip counts equally, including recorded tails. " +
                        "Warm-up, ending markers, finished cards and automatic reference clips are excluded. " +
                        "The average replaces the duration cap; dwell and an additional tail are not applied. " +
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

                // Both only mean something when a rule can fire. On FixedDuration there is no event
                // to dwell on and no moment to pad, so they are greyed out rather than misleading.
                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex == 0 || usesAverageDuration))
                {
                    EditorGUILayout.PropertyField(dwell, new GUIContent("Dwell Before Ending (s)"));
                    EditorGUILayout.PropertyField(endDelay, new GUIContent("End Delay (s, recorded)"));
                }

                using (new EditorGUI.DisabledScope(usesAverageDuration))
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
