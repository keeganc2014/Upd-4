using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

// Everything a Quest (Android + Oculus XR) build needs, applied from code so it works headless:
//   - Unity Build Automation (Cloud Build): set "Pre-export method name" to  BuildSetup.PreExport
//   - Local command line:  Unity -batchmode -quit -projectPath . -executeMethod BuildSetup.BuildAndroid
//   - In the editor:       menu  Gtag Test > Configure for Quest (Android)
public static class BuildSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OculusLoader = "Unity.XR.Oculus.OculusLoader";
    const string XRSettingsKey = "com.unity.xr.management.loader_settings";

    // ---- Unity Build Automation entry point ----
    public static void PreExport()
    {
        Apply(false);
    }

    // ---- local batch build entry point ----
    public static void BuildAndroid()
    {
        Apply(true);
        Directory.CreateDirectory("Builds");
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/GtagMapTest.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    // switchTarget = true only for editor/local use; Cloud Build is already on Android.
    public static void Apply(bool switchTarget)
    {
        EnsureScene();
        ConfigurePlayer();
        ConfigureXR();
        AssetDatabase.SaveAssets();

        if (switchTarget && EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        Debug.Log("[BuildSetup] Android / Quest settings applied.");
    }

    static void EnsureScene()
    {
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "TestStudio";
        PlayerSettings.productName = "GtagMapTest";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.test.gtagmaptest");
        PlayerSettings.bundleVersion = "0.1";
        PlayerSettings.Android.bundleVersionCode = Mathf.Max(PlayerSettings.Android.bundleVersionCode, 1);

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;   // Quest is 64-bit only
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });   // no OpenGL
        PlayerSettings.colorSpace = ColorSpace.Linear;                                              // Linear only

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.Android.androidIsGame = true;
        PlayerSettings.gpuSkinning = true;
        PlayerSettings.Android.splitApplicationBinary = false;
        PlayerSettings.Android.forceInternetPermission = true;   // online lobby + voice chat
        PlayerSettings.stripEngineCode = true;   // smaller build
        PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

        // The game reads the old Input Manager on desktop (the XR path uses UnityEngine.XR).
        // 0 = Input Manager (old), 1 = Input System package, 2 = both.
        var psAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (psAssets != null && psAssets.Length > 0)
        {
            var ps = new SerializedObject(psAssets[0]);
            var active = ps.FindProperty("activeInputHandler");
            if (active != null && active.intValue != 0) { active.intValue = 0; ps.ApplyModifiedPropertiesWithoutUndo(); }
        }

        // VR-friendly quality: MSAA on, no vsync (the headset compositor controls the frame rate).
        QualitySettings.antiAliasing = 4;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadows = ShadowQuality.Disable;
    }

    static void ConfigureXR()
    {
        // 1) XR Plug-in Management: Android build target -> Oculus loader
        XRGeneralSettingsPerBuildTarget perTarget = null;
        EditorBuildSettings.TryGetConfigObject(XRSettingsKey, out perTarget);
        if (perTarget == null)
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRSettingsKey, perTarget, true);
        }

        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        XRGeneralSettings general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        general.InitManagerOnStart = true;
        XRManagerSettings manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        if (!XRPackageMetadataStore.AssignLoader(manager, OculusLoader, BuildTargetGroup.Android))
            Debug.LogError("[BuildSetup] Could not assign the Oculus loader. Is com.unity.xr.oculus installed?");

        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(manager);

        // 2) Oculus plugin settings (target devices, stereo mode). Done by reflection so a different
        //    plugin version can never break compilation - worst case you get a warning and defaults.
        try { ConfigureOculusSettings(); }
        catch (Exception e) { Debug.LogWarning("[BuildSetup] Oculus settings left at defaults: " + e.Message); }
    }

    static void ConfigureOculusSettings()
    {
        Type t = Type.GetType("Unity.XR.Oculus.OculusSettings, Unity.XR.Oculus");
        if (t == null) { Debug.LogWarning("[BuildSetup] OculusSettings type not found."); return; }

        UnityEngine.Object settings = null;
        EditorBuildSettings.TryGetConfigObject("Unity.XR.Oculus.Settings", out settings);
        if (settings == null)
        {
            Directory.CreateDirectory("Assets/XR/Settings");
            settings = ScriptableObject.CreateInstance(t);
            AssetDatabase.CreateAsset(settings, "Assets/XR/Settings/OculusSettings.asset");
            EditorBuildSettings.AddConfigObject("Unity.XR.Oculus.Settings", settings, true);
        }

        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        SetBool(settings, t, "TargetQuest", true, F);
        SetBool(settings, t, "TargetQuest2", true, F);
        SetBool(settings, t, "TargetQuestPro", true, F);
        SetBool(settings, t, "TargetQuest3", true, F);
        SetBool(settings, t, "FocusAware", true, F);
        EditorUtility.SetDirty(settings);
    }

    static void SetBool(object o, Type t, string name, bool value, BindingFlags f)
    {
        FieldInfo fi = t.GetField(name, f);
        if (fi != null && fi.FieldType == typeof(bool)) { fi.SetValue(o, value); return; }
        PropertyInfo pi = t.GetProperty(name, f);
        if (pi != null && pi.CanWrite && pi.PropertyType == typeof(bool)) pi.SetValue(o, value, null);
        // not present in this plugin version -> silently skip
    }
}
