#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class CliffGeneratorTool : EditorWindow
{
    [MenuItem("Tools/Planet/Cliff Generator")]
    public static void ShowWindow()
    {
        GetWindow<CliffGeneratorTool>("Cliff Generator");
    }

    // SIMPLE NOISE - ÇA MARCHE
    private float SimpleNoise(Vector3 point, float scale, int octaves, float persistance, float lacunarity)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;
        
        for (int i = 0; i < octaves; i++)
        {
            // Utilise directement Mathf.PerlinNoise avec les coordonnées X et Z
            float noise = Mathf.PerlinNoise(
                point.x * frequency * scale + 1000f, 
                point.z * frequency * scale + 1000f
            );
            
            value += noise * amplitude;
            maxValue += amplitude;
            amplitude *= persistance;
            frequency *= lacunarity;
        }
        
        return value / maxValue;
    }

    // Variables
    private GameObject targetPlanet;
    private float planetRadius = 50f;
    private int resolution = 128;
    
    private float noiseScale = 0.1f;
    private float threshold = 0.5f;
    private int noiseOctaves = 3;
    private float noisePersistance = 0.5f;
    private float noiseLacunarity = 2f;
    
    private GameObject cliffMeshPrefab;
    private float cliffHeight = 5f;
    private float cliffWidth = 2f;
    private float placementOffset = 0.5f;
    private float meshSpacing = 3f;
    
    private string parentName = "Generated_Cliffs";
    private bool clearPrevious = true;
    
    private Texture2D previewTexture;
    private bool showPreview = true;
    private Vector2 scrollPos;

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("CLIFF GENERATOR - SIMPLE VERSION", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        // 1. PLANET SETTINGS
        EditorGUILayout.LabelField("1. Planet Settings", EditorStyles.boldLabel);
        targetPlanet = (GameObject)EditorGUILayout.ObjectField("Target Planet", targetPlanet, typeof(GameObject), true);
        
        if (targetPlanet != null)
        {
            // Essayer d'obtenir le rayon automatiquement
            MeshFilter mf = targetPlanet.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                planetRadius = mf.sharedMesh.bounds.extents.magnitude;
            }
            else
            {
                planetRadius = targetPlanet.transform.lossyScale.x * 0.5f;
            }
        }
        
        planetRadius = EditorGUILayout.FloatField("Planet Radius", planetRadius);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 32, 256);
        
        EditorGUILayout.Space(15);

        // 2. NOISE SETTINGS
        EditorGUILayout.LabelField("2. Noise Settings", EditorStyles.boldLabel);
        noiseScale = EditorGUILayout.Slider("Scale", noiseScale, 0.01f, 1f);
        threshold = EditorGUILayout.Slider("Threshold", threshold, 0f, 1f);
        noiseOctaves = EditorGUILayout.IntSlider("Octaves", noiseOctaves, 1, 6);
        noisePersistance = EditorGUILayout.Slider("Persistance", noisePersistance, 0f, 1f);
        noiseLacunarity = EditorGUILayout.Slider("Lacunarity", noiseLacunarity, 1f, 3f);
        
        EditorGUILayout.Space(15);

        // 3. CLIFF SETTINGS
        EditorGUILayout.LabelField("3. Cliff Settings", EditorStyles.boldLabel);
        cliffMeshPrefab = (GameObject)EditorGUILayout.ObjectField("Cliff Prefab", cliffMeshPrefab, typeof(GameObject), false);
        
        if (cliffMeshPrefab == null)
        {
            EditorGUILayout.HelpBox("Create a simple cube prefab first!", MessageType.Warning);
            if (GUILayout.Button("Create Test Cube Prefab"))
            {
                CreateTestPrefab();
            }
        }
        
        cliffHeight = EditorGUILayout.FloatField("Height", cliffHeight);
        cliffWidth = EditorGUILayout.FloatField("Width", cliffWidth);
        placementOffset = EditorGUILayout.FloatField("Offset", placementOffset);
        meshSpacing = EditorGUILayout.Slider("Spacing", meshSpacing, 1f, 10f);
        
        EditorGUILayout.Space(15);

        // 4. PREVIEW
        EditorGUILayout.LabelField("4. Preview", EditorStyles.boldLabel);
        showPreview = EditorGUILayout.Toggle("Show Preview", showPreview);
        
        if (showPreview)
        {
            if (GUILayout.Button("Generate Noise Preview"))
            {
                GenerateSimplePreview();
            }
            
            if (previewTexture != null)
            {
                EditorGUILayout.LabelField($"Threshold: {threshold:F2} (Red Line)");
                Rect rect = GUILayoutUtility.GetRect(256, 256);
                EditorGUI.DrawPreviewTexture(rect, previewTexture);
                
                // Ligne de seuil
                float thresholdPos = rect.y + rect.height * (1 - threshold);
                EditorGUI.DrawRect(new Rect(rect.x, thresholdPos, rect.width, 2), Color.red);
            }
        }
        
        EditorGUILayout.Space(20);

        // 5. GENERATE BUTTON
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("TEST NOISE ONLY", GUILayout.Height(30)))
        {
            TestNoiseOnly();
        }
        
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("GENERATE CLIFFS", GUILayout.Height(40)))
        {
            GenerateCliffsSimple();
        }
        GUI.backgroundColor = Color.white;
        
        if (GUILayout.Button("CLEAR ALL", GUILayout.Height(30)))
        {
            ClearAllCliffs();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(10);
        
        EditorGUILayout.HelpBox("Steps:\n1. Assign a planet\n2. Create/assign a cliff prefab\n3. Adjust noise settings\n4. Click 'Generate Noise Preview'\n5. Click 'Generate Cliffs'", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    void CreateTestPrefab()
    {
        // Créer un cube simple
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cliff_Cube_Prefab";
        cube.transform.localScale = new Vector3(2f, 5f, 1f);
        
        // Sauvegarder en prefab
        string path = "Assets/Cliff_Cube_Prefab.prefab";
        PrefabUtility.SaveAsPrefabAsset(cube, path);
        
        DestroyImmediate(cube);
        
        // Charger et assigner le prefab
        cliffMeshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        
        EditorUtility.DisplayDialog("Success", "Test prefab created at: " + path, "OK");
    }

    void GenerateSimplePreview()
    {
        int size = 256;
        previewTexture = new Texture2D(size, size);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Coordonnées UV
                float u = (float)x / (size - 1);
                float v = (float)y / (size - 1);
                
                // Point sur la sphère (pour visualisation)
                Vector3 spherePoint = UVToSpherePoint(u, v);
                
                // Générer du bruit SIMPLE
                float noise = SimpleNoise(spherePoint, noiseScale, noiseOctaves, noisePersistance, noiseLacunarity);
                
                // Visualiser
                Color color = noise > threshold ? Color.white : Color.black;
                previewTexture.SetPixel(x, y, color);
            }
        }
        
        previewTexture.Apply();
        Repaint();
        
        Debug.Log($"Preview generated. Max/Min noise values should be between 0 and 1.");
    }

    Vector3 UVToSpherePoint(float u, float v)
    {
        // Convertir UV en point sur sphère unitaire
        float theta = u * Mathf.PI * 2f; // Longitude
        float phi = v * Mathf.PI; // Latitude
        
        return new Vector3(
            Mathf.Sin(phi) * Mathf.Cos(theta),
            Mathf.Cos(phi),
            Mathf.Sin(phi) * Mathf.Sin(theta)
        );
    }

    void TestNoiseOnly()
    {
        // Test simple pour voir si le bruit fonctionne
        List<float> testValues = new List<float>();
        
        for (int i = 0; i < 10; i++)
        {
            Vector3 testPoint = new Vector3(Random.value, Random.value, Random.value);
            float noise = SimpleNoise(testPoint, noiseScale, noiseOctaves, noisePersistance, noiseLacunarity);
            testValues.Add(noise);
            
            Debug.Log($"Test {i}: Point {testPoint} -> Noise: {noise:F3}");
        }
        
        float min = Mathf.Min(testValues.ToArray());
        float max = Mathf.Max(testValues.ToArray());
        
        EditorUtility.DisplayDialog("Noise Test Results", 
            $"Min noise: {min:F3}\nMax noise: {max:F3}\nThreshold: {threshold:F3}\n\nIf min/max are both 0 or 1, adjust noise scale.", 
            "OK");
    }

    void GenerateCliffsSimple()
    {
        if (targetPlanet == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a target planet!", "OK");
            return;
        }

        if (cliffMeshPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a cliff mesh prefab!", "OK");
            return;
        }

        // Nettoyer les anciens
        if (clearPrevious)
        {
            ClearAllCliffs();
        }

        // Créer parent
        GameObject parentObj = GameObject.Find(parentName);
        if (parentObj == null)
        {
            parentObj = new GameObject(parentName);
            parentObj.transform.position = targetPlanet.transform.position;
        }

        // Générer les positions
        List<Vector3> cliffPositions = new List<Vector3>();
        int edgeCount = 0;
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // UV
                float u = (float)x / (resolution - 1);
                float v = (float)y / (resolution - 1);
                
                // Point sur la sphère
                Vector3 spherePoint = UVToSpherePoint(u, v);
                Vector3 worldPoint = spherePoint * planetRadius + targetPlanet.transform.position;
                
                // Bruit à ce point
                float noise = SimpleNoise(spherePoint, noiseScale, noiseOctaves, noisePersistance, noiseLacunarity);
                
                // Décision cliff/non-cliff
                bool isCliff = noise > threshold;
                
                // Détecter si c'est un bord
                if (isCliff && IsEdge(x, y, resolution))
                {
                    cliffPositions.Add(worldPoint);
                    edgeCount++;
                }
            }
            
            // Progress bar
            if (y % 20 == 0)
            {
                float progress = (float)y / resolution;
                EditorUtility.DisplayProgressBar("Finding cliff edges", 
                    $"Found {edgeCount} edges...", progress);
            }
        }
        
        EditorUtility.ClearProgressBar();
        
        if (cliffPositions.Count == 0)
        {
            EditorUtility.DisplayDialog("Warning", "No cliff edges found! Try adjusting threshold or noise scale.", "OK");
            return;
        }
        
        // Placer les meshes
        int placed = 0;
        for (int i = 0; i < cliffPositions.Count; i += Mathf.RoundToInt(meshSpacing))
        {
            Vector3 pos = cliffPositions[i];
            Vector3 normal = (pos - targetPlanet.transform.position).normalized;
            
            // Décalage
            pos += normal * placementOffset;
            
            // Créer instance
            GameObject cliff = null;
            if (PrefabUtility.IsPartOfPrefabAsset(cliffMeshPrefab))
            {
                cliff = (GameObject)PrefabUtility.InstantiatePrefab(cliffMeshPrefab, parentObj.transform);
            }
            else
            {
                cliff = Instantiate(cliffMeshPrefab, parentObj.transform);
            }
            
            cliff.transform.position = pos;
            
            // Orientation (regarde vers l'extérieur)
            cliff.transform.up = normal;
            
            // Rotation pour être tangent à la sphère
            cliff.transform.Rotate(0, Random.Range(0, 360), 0);
            
            // Échelle
            cliff.transform.localScale = new Vector3(cliffWidth, cliffHeight, 1f);
            
            cliff.name = $"Cliff_{placed:0000}";
            placed++;
            
            // Progress
            if (placed % 50 == 0)
            {
                float progress = (float)placed / (cliffPositions.Count / meshSpacing);
                EditorUtility.DisplayProgressBar("Placing cliffs", 
                    $"Placed {placed} cliffs...", progress);
            }
        }
        
        EditorUtility.ClearProgressBar();
        Debug.Log($"✅ SUCCESS! Placed {placed} cliff meshes on planet.");
        EditorUtility.DisplayDialog("Success", $"Placed {placed} cliff meshes!\nCheck the '{parentName}' GameObject.", "OK");
        
        Selection.activeGameObject = parentObj;
    }

    bool IsEdge(int x, int y, int res)
    {
        // Vérifie si c'est un bord (pour simplifier, toujours vrai pour le test)
        // Tu peux améliorer cette logique plus tard
        return true;
        
        /* Logique d'edge detection de base :
        if (x <= 1 || x >= res - 2 || y <= 1 || y >= res - 2) 
            return true;
        return Random.value > 0.5f; // Random pour test
        */
    }

    void ClearAllCliffs()
    {
        GameObject parent = GameObject.Find(parentName);
        if (parent != null)
        {
            int childCount = parent.transform.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(parent.transform.GetChild(i).gameObject);
            }
            Debug.Log($"Cleared {childCount} cliff meshes");
        }
    }
}
#endif