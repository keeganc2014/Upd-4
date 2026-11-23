using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 1) Creates Assets/Scenes/Main.unity the first time the project opens (the game builds itself at runtime,
//    so the scene can stay empty) and adds it to Build Settings.
// 2) Menu: Gtag Test > Configure for Quest (Android) applies the player settings a Quest build needs.
[InitializeOnLoad]
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    static ProjectSetup()
    {
        EditorApplication.delayCall += EnsureScene;
    }

    static void EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(ScenePath)) return;

        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.Refresh();
        Debug.Log("[GtagTest] Created " + ScenePath + " and added it to Build Settings.");
    }

    [MenuItem("Gtag Test/Configure for Quest (Android)")]
    public static void ConfigureQuest()
    {
        BuildSetup.Apply(true);
        EditorUtility.DisplayDialog("Gtag Test",
            "Android + Oculus XR settings applied (IL2CPP, ARM64, Vulkan, Linear color space, Oculus loader).\n\nFile > Build Settings > Build to make the APK.",
            "OK");
    }
}
