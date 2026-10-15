using System;
using System.IO;
using System.Linq;
using Unity.NetCode;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Generates the network test content (ghost prefabs, planet sub scene, SC_NetTest) so it can be rebuilt from
/// code, and builds a standalone Windows player containing only SC_NetTest for 2-instance tests.
/// Menu: Crazy Planet / Net Test.
/// </summary>
public static class NetTestSceneBuilder
{
    private const string PrefabDir = "Assets/_Prefabs/Net";
    private const string SceneDir = "Assets/_Scenes/NetTest";
    public const string MainScenePath = SceneDir + "/SC_NetTest.unity";
    private const string PlanetScenePath = SceneDir + "/SC_NetTest_Planet.unity";
    private const string PlanetPrefabPath = "Assets/_Prefabs/Planets/WFC_Earth/PF_WFC_Planet_Earth.prefab";

    [MenuItem("Crazy Planet/Net Test/Rebuild Net Test Content")]
    public static void Rebuild()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(SceneDir);

        var playerMat = CreateMaterial("M_NetPlayer", new Color(0.2f, 0.55f, 1f));
        var enemyMat = CreateMaterial("M_NetEnemy", new Color(0.9f, 0.25f, 0.2f));
        var playerPrefab = CreatePlayerPrefab(playerMat);
        var enemyPrefab = CreateEnemyPrefab(enemyMat);

        CreatePlanetSubScene(playerPrefab, enemyPrefab);
        CreateMainScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NetTest] Content rebuilt: " + MainScenePath);
    }

    private static Material CreateMaterial(string name, Color color)
    {
        string path = $"{PrefabDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static GameObject CreateVisual(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        // No collider: these are pure visuals, Unity Physics must not turn ghosts into physics bodies.
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return go;
    }

    private static GameObject CreatePlayerPrefab(Material mat)
    {
        var root = new GameObject("PF_NetPlayer");
        CreateVisual(PrimitiveType.Capsule, root.transform, new Vector3(0, 1f, 0), Vector3.one, mat, "Body");
        CreateVisual(PrimitiveType.Cube, root.transform, new Vector3(0, 1.4f, 0.55f), new Vector3(0.4f, 0.25f, 0.4f), mat, "Nose");

        var ghost = root.AddComponent<GhostAuthoringComponent>();
        ghost.DefaultGhostMode = GhostMode.OwnerPredicted;
        ghost.SupportedGhostModes = GhostModeMask.All;
        ghost.HasOwner = true;
        ghost.SupportAutoCommandTarget = true;
        ghost.OptimizationMode = GhostOptimizationMode.Dynamic;
        ghost.Importance = 100;
        root.AddComponent<NetPlayerAuthoring>();

        return SavePrefab(root, $"{PrefabDir}/PF_NetPlayer.prefab");
    }

    private static GameObject CreateEnemyPrefab(Material mat)
    {
        var root = new GameObject("PF_NetEnemy");
        CreateVisual(PrimitiveType.Cube, root.transform, new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.9f), mat, "Body");

        var ghost = root.AddComponent<GhostAuthoringComponent>();
        ghost.DefaultGhostMode = GhostMode.Interpolated;
        ghost.SupportedGhostModes = GhostModeMask.Interpolated;
        ghost.HasOwner = false;
        ghost.SupportAutoCommandTarget = false;
        ghost.OptimizationMode = GhostOptimizationMode.Dynamic;
        ghost.Importance = 1;
        root.AddComponent<NetEnemyAuthoring>();

        return SavePrefab(root, $"{PrefabDir}/PF_NetEnemy.prefab");
    }

    private static GameObject SavePrefab(GameObject root, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static void CreatePlanetSubScene(GameObject playerPrefab, GameObject enemyPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var planetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanetPrefabPath);
        if (planetPrefab == null)
            throw new Exception("Planet prefab not found: " + PlanetPrefabPath);
        var planet = (GameObject)PrefabUtility.InstantiatePrefab(planetPrefab, scene);
        planet.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var configGo = new GameObject("NetTestConfig");
        SceneManager.MoveGameObjectToScene(configGo, scene);
        var config = configGo.AddComponent<NetTestConfigAuthoring>();
        config.PlayerPrefab = playerPrefab;
        config.EnemyPrefab = enemyPrefab;

        EditorSceneManager.SaveScene(scene, PlanetScenePath);
    }

    private static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.03f, 0.07f);
        camera.farClipPlane = 2000f;
        cameraGo.AddComponent<UniversalAdditionalCameraData>();
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<NetTestCamera>();
        cameraGo.transform.SetPositionAndRotation(new Vector3(0, 220, -60), Quaternion.Euler(75, 0, 0));

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.shadows = LightShadows.Soft;
        lightGo.AddComponent<UniversalAdditionalLightData>();
        lightGo.transform.rotation = Quaternion.Euler(70f, -30f, 0f);

        var subSceneGo = new GameObject("NetTest Planet (SubScene)");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlanetScenePath);
        subScene.AutoLoadScene = false;

        var launcherGo = new GameObject("NetTestLauncher");
        var launcher = launcherGo.AddComponent<NetTestLauncher>();
        var so = new SerializedObject(launcher);
        so.FindProperty("_planetSubScene").objectReferenceValue = subScene;
        so.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.45f);

        EditorSceneManager.SaveScene(scene, MainScenePath);
    }

    /// <summary>
    /// Builds a Windows player containing only SC_NetTest. Batch mode:
    /// Unity.exe -batchmode -quit -projectPath . -executeMethod NetTestSceneBuilder.BuildWindowsPlayer -nettestBuildPath C:/path/CPS_NetTest.exe
    /// </summary>
    [MenuItem("Crazy Planet/Net Test/Build Windows Player (SC_NetTest only)")]
    public static void BuildWindowsPlayer()
    {
        var args = Environment.GetCommandLineArgs().ToList();
        int index = args.IndexOf("-nettestBuildPath");
        string path = index >= 0 && index + 1 < args.Count ? args[index + 1] : "Builds/NetTest/CPS_NetTest.exe";

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { MainScenePath },
            target = BuildTarget.StandaloneWindows64,
            locationPathName = path,
            options = BuildOptions.None
        });
        Debug.Log($"[NetTest] Build {report.summary.result}: {path} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors)");
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
