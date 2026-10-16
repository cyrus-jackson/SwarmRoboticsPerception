using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates or opens the separate scene for titled motion-comparison video sequences.</summary>
public static class MotionPresentationMenu
{
    public const string ScenePath = "Assets/Scenes/MotionPresentation.unity";

    [MenuItem("Tools/Flocking/Open Motion Presentation")]
    public static void OpenPresentation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateSceneIfMissing();
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        Selection.activeGameObject = scene.GetRootGameObjects()[0];
    }

    public static void CreateSceneIfMissing()
    {
        if (File.Exists(ScenePath)) return;
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("Motion Presentation Manager", typeof(MotionPresentationManager));
        SceneManager.MoveGameObjectToScene(root, scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new IOException($"Could not save {ScenePath}.");
        EditorSceneManager.CloseScene(scene, true);
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
    }
}
