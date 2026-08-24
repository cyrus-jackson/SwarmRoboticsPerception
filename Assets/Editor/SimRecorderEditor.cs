#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimRecorder))]
public class SimRecorderEditor : Editor
{
    private static readonly string[] EndConditionLabels =
        { "Fixed Duration", "Target Area Reached", "Density Ratio", "Absolute Hull Area" };
    private const int TargetAreaReachedIndex = 1;
    private const int DensityRatioReachedIndex = 2;
    private const int AbsoluteHullAreaIndex = 3;

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
                SerializedProperty densityTargetRatio = entry.FindPropertyRelative("densityTargetRatio");
                SerializedProperty absoluteArea = entry.FindPropertyRelative("absoluteHullAreaTarget");
                SerializedProperty dwell = entry.FindPropertyRelative("endConditionDwellTime");
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
                    EditorGUILayout.Slider(densityTargetRatio, 0.05f, 3f, new GUIContent("Target Ratio (<1 shrink)"));
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex != AbsoluteHullAreaIndex))
                {
                    EditorGUILayout.PropertyField(absoluteArea, new GUIContent("Target Hull Area (u2)"));
                }

                using (new EditorGUI.DisabledScope(endCondition.enumValueIndex == 0))
                {
                    EditorGUILayout.PropertyField(dwell, new GUIContent("Dwell Before Ending (s)"));
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
