using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Opens FlockGrid and configures its motion-combination comparison mode.</summary>
public static class FlockGridMenu
{
    private const string ScenePath = "Assets/Scenes/FlockGrid.unity";

    [MenuItem("Tools/Flocking/Open 32-Video Motion Grid")]
    public static void OpenMotionGrid()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        ConfigureMotionGrid(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void ConfigureMotionGrid(UnityEngine.SceneManagement.Scene scene)
    {
        AllGridManager player = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            player = root.GetComponentInChildren<AllGridManager>(true);
            if (player != null) break;
        }
        if (player == null)
        {
            var root = new GameObject("FlockGridManager");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create motion grid");
            player = Undo.AddComponent<AllGridManager>(root);
        }
        MotionCombinationGridManager manager = player.GetComponent<MotionCombinationGridManager>();
        bool added = manager == null;
        if (added) manager = Undo.AddComponent<MotionCombinationGridManager>(player.gameObject);
        AllGridBatchManager batch = player.GetComponent<AllGridBatchManager>();
        if (batch != null)
        {
            Undo.RecordObject(batch, "Disable sequential batch recording");
            batch.enabled = false;
            if (added && !string.IsNullOrWhiteSpace(batch.folderPath)) manager.folderPath = batch.folderPath;
        }
        FlockGridManager legacy = player.GetComponent<FlockGridManager>();
        if (legacy != null)
        {
            Undo.RecordObject(legacy, "Disable forty-layout grid");
            legacy.enabled = false;
        }
        Undo.RecordObjects(new Object[] { player, manager }, "Configure 32-video motion grid");
        player.enabled = manager.enabled = true;
        player.recordOneIteration = false;
        player.folderPath = manager.folderPath;
        player.columns = MotionCombinationCatalog.CombinationsPerMotion;
        player.expectedLayouts = MotionCombinationCatalog.MotionCount * MotionCombinationCatalog.CombinationsPerMotion;
        Selection.activeGameObject = manager.gameObject;
        EditorSceneManager.MarkSceneDirty(scene);
    }

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
