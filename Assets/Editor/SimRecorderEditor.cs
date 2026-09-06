#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimRecorder))]
public class SimRecorderEditor : Editor
{
    private static readonly string[] EndConditionLabels =
        { "Fixed Duration", "Target Area Reached", "Density Ratio", "Absolute Hull Area", "Spread Settled" };
    private const int TargetAreaReachedIndex = 1;
    private const int DensityRatioReachedIndex = 2;
    private const int AbsoluteHullAreaIndex = 3;
    private const int SpreadSettledIndex = 4;

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
                SerializedProperty densityMetric = entry.FindPropertyRelative("densityMetric");
                SerializedProperty densityBaseline = entry.FindPropertyRelative("densityRatioBaseline");
                SerializedProperty densityTargetRatio = entry.FindPropertyRelative("densityTargetRatio");
                SerializedProperty absoluteArea = entry.FindPropertyRelative("absoluteHullAreaTarget");
                SerializedProperty dwell = entry.FindPropertyRelative("endConditionDwellTime");
                SerializedProperty settleTolerance = entry.FindPropertyRelative("settleTolerance");
                SerializedProperty settleHoldTime = entry.FindPropertyRelative("settleHoldTime");
                SerializedProperty endDelay = entry.FindPropertyRelative("recordingEndDelay");
                SerializedProperty overrideTime = entry.FindPropertyRelative("overrideRecordingTime");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(swarmType, new GUIContent("Motion Type"));
                EditorGUILayout.PropertyField(startDelay, new GUIContent("Start Delay (s, not recorded)"));

                endCondition.enumValueIndex = GUILayout.SelectionGrid(
                    endCondition.enumValueIndex,
                    EndConditionLabels,
                    EndConditionLabels.Length);

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != TargetAreaReachedIndex))
                {
                    EditorGUILayout.Slider(percent, 0f, 100f, new GUIContent("Agents Inside %"));
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
                            "run from the same layout, radius and max speed. That reference is captured " +
                            "as it records, so 0 must come FIRST in Combination Param 1 Values. A run " +
                            "with no reference yet falls back to the duration cap and warns.",
                            MessageType.Info);
                    }
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != AbsoluteHullAreaIndex))
                {
                    EditorGUILayout.PropertyField(absoluteArea, new GUIContent("Target Hull Area (u2)"));
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
                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex == 0))
                {
                    EditorGUILayout.PropertyField(dwell, new GUIContent("Dwell Before Ending (s)"));
                    EditorGUILayout.PropertyField(endDelay, new GUIContent("End Delay (s, recorded)"));
                }

                EditorGUILayout.PropertyField(overrideTime, new GUIContent("Max Time Override (0 = global)"));
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
