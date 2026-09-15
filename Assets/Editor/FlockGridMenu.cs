using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Creates or opens the dedicated forty-recording flocking grid scene.</summary>
public static class FlockGridMenu
{
    private const string ScenePath = "Assets/Scenes/FlockGrid.unity";

    [MenuItem("Tools/Flocking/Open 40-Recording Grid")]
    public static void OpenGrid()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorSceneManager.OpenScene(ScenePath);
        }
        else
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject manager = new GameObject("FlockGridManager");
            manager.AddComponent<FlockGridManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        FlockGridManager grid = Object.FindFirstObjectByType<FlockGridManager>();
        if (grid != null) Selection.activeGameObject = grid.gameObject;
    }
}
