#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

[CustomEditor(typeof(TinyPlanetGenerator))]
public class TinyPlanetGeneratorEditor : Editor
{
    private TinyPlanetGenerator generator;
    private SerializedProperty noiseLayers;
    private bool showBaseSettings = true;
    private bool showNoiseSettings = true;
    private bool showPreviewSettings = false;
    private bool showBakeSettings = true;
    private bool[] layerFoldouts;
    private Vector2 scrollPos;
    
    private static readonly Color[] layerColors = {
        new Color(0.8f, 0.4f, 0.4f, 1f),
        new Color(0.4f, 0.8f, 0.4f, 1f),
        new Color(0.4f, 0.4f, 0.8f, 1f),
        new Color(0.8f, 0.8f, 0.4f, 1f),
        new Color(0.8f, 0.4f, 0.8f, 1f),
        new Color(0.4f, 0.8f, 0.8f, 1f)
    };
    
    private void OnEnable()
    {
        generator = (TinyPlanetGenerator)target;
        noiseLayers = serializedObject.FindProperty("noiseLayers");
        
        layerFoldouts = new bool[noiseLayers.arraySize];
        for (int i = 0; i < layerFoldouts.Length; i++)
        {
            layerFoldouts[i] = false;
        }
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        
        EditorGUILayout.Space(10);
        DrawHeader();
        EditorGUILayout.Space(10);
        
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        if (generator.baked)
        {
            DrawBakedWarning();
            EditorGUILayout.Space(10);
        }
        
        DrawBaseMeshSettings();
        EditorGUILayout.Space(15);
        DrawNoiseLayers();
        EditorGUILayout.Space(15);
        DrawPreviewSettings();
        EditorGUILayout.Space(15);
        DrawBakeSettings();
        EditorGUILayout.Space(15);
        DrawActionButtons();
        EditorGUILayout.Space(10);
        DrawStatistics();
        
        EditorGUILayout.EndScrollView();
        
        serializedObject.ApplyModifiedProperties();
    }
    
    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter
        };
        
        GUILayout.Label(generator.baked ? "🌍 BAKED PLANET" : "🌍 TINY PLANET GENERATOR", headerStyle, GUILayout.Height(30));
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(generator.baked ? 
            "Planet is baked with proper UVs and textures" : 
            "Create procedurally generated planets with multiple noise layers", 
            EditorStyles.centeredGreyMiniLabel);
    }
    
    private void DrawBakedWarning()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("⚠️ Planet is Baked", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        EditorGUILayout.LabelField("This planet has been baked with proper UVs.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.LabelField("Modifying parameters will have no effect until you revert the bake.", EditorStyles.wordWrappedMiniLabel);
        
        EditorGUILayout.Space(5);
        
        if (generator.bakedHeightMap != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Height Map:", GUILayout.Width(80));
            EditorGUILayout.ObjectField(generator.bakedHeightMap, typeof(Texture2D), false, GUILayout.Height(50));
            EditorGUILayout.EndHorizontal();
        }
        
        if (generator.bakedNormalMap != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Normal Map:", GUILayout.Width(80));
            EditorGUILayout.ObjectField(generator.bakedNormalMap, typeof(Texture2D), false, GUILayout.Height(50));
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawBaseMeshSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        showBaseSettings = EditorGUILayout.Foldout(showBaseSettings, "Base Mesh Settings", true, EditorStyles.foldoutHeader);
        EditorGUI.BeginDisabledGroup(generator.baked);
        if (GUILayout.Button(generator.baked ? "Baked" : "Generate", GUILayout.Width(80)))
        {
            generator.GeneratePlanet();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        
        if (showBaseSettings)
        {
            EditorGUI.BeginDisabledGroup(generator.baked);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("baseMeshType"), new GUIContent("Base Mesh Type"));
            
            EditorGUILayout.Space(5);
            
            switch (generator.baseMeshType)
            {
                case TinyPlanetGenerator.MeshBaseType.Icosphere:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("resolution"), 
                        new GUIContent("Subdivisions", "Number of subdivision iterations"));
                    EditorGUILayout.HelpBox("Icosahedron subdivided into a sphere. Even vertex distribution.", MessageType.Info);
                    break;
                    
                case TinyPlanetGenerator.MeshBaseType.Cube:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("cubeSubdivisions"), 
                        new GUIContent("Subdivisions", "Subdivisions per cube face (2^N)"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("normalizeCubeVertices"), 
                        new GUIContent("Normalize to Sphere", "Normalize vertices to create a perfect sphere"));
                    EditorGUILayout.HelpBox("Cube subdivided and optionally normalized to sphere. Good for boxy planets.", MessageType.Info);
                    break;
                    
                case TinyPlanetGenerator.MeshBaseType.UVSphere:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("resolution"), 
                        new GUIContent("Resolution", "Number of segments and rings"));
                    EditorGUILayout.HelpBox("Traditional UV sphere. Good for simple planets with poles.", MessageType.Info);
                    break;
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("planetRadius"), 
                new GUIContent("Planet Radius", "Base radius of the planet"));
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Normal Settings", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("normalMode"), 
                new GUIContent("Normal Mode", "How normals are calculated"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("invertNormals"), 
                new GUIContent("Invert Normals", "Flip all normals (useful for cube spheres)"));
            
            EditorGUI.EndDisabledGroup();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawNoiseLayers()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        showNoiseSettings = EditorGUILayout.Foldout(showNoiseSettings, "Noise Layers", true, EditorStyles.foldoutHeader);
        EditorGUI.BeginDisabledGroup(generator.baked);
        if (GUILayout.Button("+ Add Layer", GUILayout.Width(80)))
        {
            AddNoiseLayer();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        
        if (showNoiseSettings)
        {
            EditorGUI.BeginDisabledGroup(generator.baked);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"), 
                new GUIContent("Noise Seed", "Global seed for all noise layers"));
            
            EditorGUILayout.Space(10);
            
            if (noiseLayers.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No noise layers defined. Click '+ Add Layer' to create terrain features.", MessageType.Info);
            }
            else
            {
                if (layerFoldouts.Length != noiseLayers.arraySize)
                {
                    bool[] newFoldouts = new bool[noiseLayers.arraySize];
                    for (int i = 0; i < Mathf.Min(layerFoldouts.Length, newFoldouts.Length); i++)
                    {
                        newFoldouts[i] = layerFoldouts[i];
                    }
                    layerFoldouts = newFoldouts;
                }
                
                for (int i = 0; i < noiseLayers.arraySize; i++)
                {
                    DrawNoiseLayer(i);
                }
            }
            
            EditorGUI.EndDisabledGroup();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawNoiseLayer(int index)
    {
        SerializedProperty layer = noiseLayers.GetArrayElementAtIndex(index);
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        
        SerializedProperty enabled = layer.FindPropertyRelative("enabled");
        enabled.boolValue = EditorGUILayout.Toggle(enabled.boolValue, GUILayout.Width(20));
        
        SerializedProperty layerName = layer.FindPropertyRelative("layerName");
        SerializedProperty debugColor = layer.FindPropertyRelative("debugColor");
        
        debugColor.colorValue = EditorGUILayout.ColorField(GUIContent.none, debugColor.colorValue, 
            false, false, false, GUILayout.Width(30));
        
        GUILayout.Space(5);
        
        string foldoutLabel = $"{layerName.stringValue} (Layer {index + 1})";
        Color originalColor = GUI.color;
        GUI.color = enabled.boolValue ? Color.white : Color.gray;
        layerFoldouts[index] = EditorGUILayout.Foldout(layerFoldouts[index], foldoutLabel, true);
        GUI.color = originalColor;
        
        GUILayout.FlexibleSpace();
        
        EditorGUI.BeginDisabledGroup(index == 0);
        if (GUILayout.Button("▲", GUILayout.Width(25)))
        {
            noiseLayers.MoveArrayElement(index, index - 1);
            bool temp = layerFoldouts[index];
            layerFoldouts[index] = layerFoldouts[index - 1];
            layerFoldouts[index - 1] = temp;
            EditorUtility.SetDirty(target);
            return;
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUI.BeginDisabledGroup(index == noiseLayers.arraySize - 1);
        if (GUILayout.Button("▼", GUILayout.Width(25)))
        {
            noiseLayers.MoveArrayElement(index, index + 1);
            bool temp = layerFoldouts[index];
            layerFoldouts[index] = layerFoldouts[index + 1];
            layerFoldouts[index + 1] = temp;
            EditorUtility.SetDirty(target);
            return;
        }
        EditorGUI.EndDisabledGroup();
        
        if (GUILayout.Button("×", GUILayout.Width(25)))
        {
            noiseLayers.DeleteArrayElementAtIndex(index);
            EditorUtility.SetDirty(target);
            return;
        }
        
        EditorGUILayout.EndHorizontal();
        
        if (layerFoldouts[index])
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(layerName, new GUIContent("Layer Name"));
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("noiseType"), new GUIContent("Noise Type"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("amplitude"), 
                new GUIContent("Amplitude", "Strength of this noise layer"));
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("frequency"), 
                new GUIContent("Frequency", "Scale of the noise pattern"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("octaves"), 
                new GUIContent("Octaves", "Number of noise layers to combine"));
            
            if (layer.FindPropertyRelative("octaves").intValue > 1)
            {
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("persistence"), 
                    new GUIContent("Persistence", "How much each octave contributes"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("lacunarity"), 
                    new GUIContent("Lacunarity", "How much detail is added/diminished"));
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(layer.FindPropertyRelative("maskThreshold"), 
                new GUIContent("Mask Threshold", "Only apply noise above this value"));
            
            DrawNoisePreviewBar(layer);
            
            EditorGUILayout.EndVertical();
        }
        
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
    }
    
    private void DrawNoisePreviewBar(SerializedProperty layer)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 25);
        float width = rect.width;
        
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
        
        for (int i = 1; i < 4; i++)
        {
            float x = rect.x + (width * i / 4);
            EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
        
        float amplitude = layer.FindPropertyRelative("amplitude").floatValue;
        float frequency = layer.FindPropertyRelative("frequency").floatValue;
        int octaves = layer.FindPropertyRelative("octaves").intValue;
        float persistence = layer.FindPropertyRelative("persistence").floatValue;
        float lacunarity = layer.FindPropertyRelative("lacunarity").floatValue;
        
        Color waveColor = layer.FindPropertyRelative("debugColor").colorValue;
        
        for (int x = 0; x < width; x += 2)
        {
            float noiseValue = 0f;
            float amp = 1f;
            float freq = frequency;
            
            for (int oct = 0; oct < octaves; oct++)
            {
                float sample = Mathf.PerlinNoise(x * 0.01f * freq, 0);
                noiseValue += sample * amp;
                amp *= persistence;
                freq *= lacunarity;
            }
            
            float normalizedValue = Mathf.Clamp01(noiseValue * amplitude);
            float yPos = rect.y + rect.height / 2 + (normalizedValue * rect.height / 2 - 0.5f);
            EditorGUI.DrawRect(new Rect(rect.x + x, yPos, 2, 2), waveColor);
        }
        
        float threshold = layer.FindPropertyRelative("maskThreshold").floatValue;
        if (threshold > 0f)
        {
            float thresholdY = rect.y + rect.height / 2 + (threshold * rect.height / 2 - 0.5f);
            EditorGUI.DrawRect(new Rect(rect.x, thresholdY, width, 1), Color.red);
            
            GUI.Label(new Rect(rect.x + 5, thresholdY - 12, 100, 12), 
                $"Threshold: {threshold:F2}", EditorStyles.miniLabel);
        }
        
        GUI.Label(new Rect(rect.x + 5, rect.y + 5, 100, 12), 
            $"Amplitude: {amplitude:F2}", EditorStyles.miniLabel);
    }
    
    private void DrawPreviewSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        showPreviewSettings = EditorGUILayout.Foldout(showPreviewSettings, "Preview & Debug", true, EditorStyles.foldoutHeader);
        
        if (showPreviewSettings)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoUpdate"), 
                new GUIContent("Auto Update", "Update planet automatically when parameters change"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showWireframe"), 
                new GUIContent("Show Wireframe", "Display wireframe overlay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showNormals"), 
                new GUIContent("Show Normals", "Display vertex normals"));
            EditorGUILayout.EndHorizontal();
            
            if (generator.showNormals)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("normalsLength"), 
                    new GUIContent("Normals Length", "Length of normal vectors in viewport"));
            }
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Quick Preview:", EditorStyles.miniBoldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Low Quality", GUILayout.Height(25)))
            {
                SetPreviewQuality(2, 1);
            }
            
            if (GUILayout.Button("Medium Quality", GUILayout.Height(25)))
            {
                SetPreviewQuality(4, 3);
            }
            
            if (GUILayout.Button("High Quality", GUILayout.Height(25)))
            {
                SetPreviewQuality(6, 5);
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            if (GUILayout.Button("Randomize Seed", GUILayout.Height(25)))
            {
                generator.seed = new Vector3(
                    Random.Range(-1000f, 1000f),
                    Random.Range(-1000f, 1000f),
                    Random.Range(-1000f, 1000f)
                );
                if (!generator.baked) generator.GeneratePlanet();
            }
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void SetPreviewQuality(int sphereRes, int cubeRes)
    {
        if (generator.baked) return;
        
        if (generator.baseMeshType == TinyPlanetGenerator.MeshBaseType.Icosphere || 
            generator.baseMeshType == TinyPlanetGenerator.MeshBaseType.UVSphere)
        {
            generator.resolution = sphereRes;
        }
        else if (generator.baseMeshType == TinyPlanetGenerator.MeshBaseType.Cube)
        {
            generator.cubeSubdivisions = cubeRes;
        }
        generator.GeneratePlanet();
    }
    
    private void DrawBakeSettings()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        showBakeSettings = EditorGUILayout.Foldout(showBakeSettings, "Baking Settings", true, EditorStyles.foldoutHeader);
        EditorGUI.BeginDisabledGroup(generator.baked);
        if (GUILayout.Button("Preview Bake", GUILayout.Width(100)))
        {
            PreviewBake();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        
        if (showBakeSettings)
        {
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("Baking creates proper UVs and textures for material application.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(10);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bakeTextureSize"), 
                new GUIContent("Texture Size", "Resolution of generated textures (power of 2 recommended)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("uvBakeMode"), 
                new GUIContent("UV Bake Mode", "Method for generating UVs"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bakeHeightScale"), 
                new GUIContent("Height Scale", "Controls heightmap contrast"));
            
            EditorGUILayout.Space(10);
            
            // Bake buttons
            EditorGUILayout.BeginHorizontal();
            
            if (generator.baked)
            {
                if (GUILayout.Button("Revert Bake", GUILayout.Height(35)))
                {
                    if (EditorUtility.DisplayDialog("Revert Bake", 
                        "This will revert the planet to its unbaked state and you'll lose the baked textures. Continue?", 
                        "Revert", "Cancel"))
                    {
                        generator.RevertBake();
                    }
                }
                
                if (GUILayout.Button("Save Textures", GUILayout.Height(35)))
                {
                    generator.SaveTexturesToFiles();
                }
            }
            else
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f, 1f);
                if (GUILayout.Button("BAKE PLANET", GUILayout.Height(40)))
                {
                    if (EditorUtility.DisplayDialog("Bake Planet", 
                        "This will bake the planet with proper UVs and generate height/normal maps. This process cannot be undone automatically. Continue?", 
                        "Bake", "Cancel"))
                    {
                        generator.BakePlanet();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (!generator.baked)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Baking will: 1) Generate proper UVs, 2) Create heightmap, 3) Create normalmap, 4) Lock planet for material use.", MessageType.Info);
            }
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void PreviewBake()
    {
        if (generator.baked)
        {
            EditorUtility.DisplayDialog("Already Baked", "Planet is already baked. Use 'Revert Bake' to modify.", "OK");
            return;
        }
        
        // Temporary bake for preview
        TinyPlanetGenerator tempGenerator = (TinyPlanetGenerator)target;
        
        // Generate preview textures at lower resolution
        int originalSize = tempGenerator.bakeTextureSize;
        tempGenerator.bakeTextureSize = 256;
        
        Texture2D previewHeight = tempGenerator.GenerateHeightMap();
        Texture2D previewNormal = tempGenerator.GenerateNormalMap(previewHeight);
        
        // Show preview window
        BakePreviewWindow.ShowWindow(previewHeight, previewNormal);
        
        // Restore original size
        tempGenerator.bakeTextureSize = originalSize;
    }
    
    private void DrawActionButtons()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        
        EditorGUILayout.Space(5);
        
        // Main action buttons
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Apply Terrestrial Preset", GUILayout.Height(25)))
        {
            ApplyTerrestrialPreset();
        }
        
        if (GUILayout.Button("Reset All", GUILayout.Height(25)))
        {
            if (EditorUtility.DisplayDialog("Reset All", 
                "Are you sure you want to reset all settings to defaults?", 
                "Reset", "Cancel"))
            {
                generator.ResetToDefault();
            }
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        // Export buttons
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Export Mesh...", GUILayout.Height(30)))
        {
            ExportMesh();
        }
        
        if (GUILayout.Button("Save Preset...", GUILayout.Height(30)))
        {
            SavePreset();
        }
        
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawStatistics()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Mesh Statistics", EditorStyles.miniBoldLabel);
        
        EditorGUILayout.Space(5);
        
        int vertexCount = generator.GetVertexCount();
        int triangleCount = generator.GetTriangleCount();
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Vertices:", GUILayout.Width(80));
        EditorGUILayout.LabelField(vertexCount.ToString("N0"), EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Triangles:", GUILayout.Width(80));
        EditorGUILayout.LabelField(triangleCount.ToString("N0"), EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Status:", GUILayout.Width(80));
        EditorGUILayout.LabelField(generator.baked ? "Baked ✓" : "Unbaked", 
            generator.baked ? EditorStyles.whiteBoldLabel : EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        
        if (generator.baked && generator.bakedHeightMap != null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Textures:", GUILayout.Width(80));
            EditorGUILayout.LabelField($"{generator.bakeTextureSize}x{generator.bakeTextureSize}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void AddNoiseLayer()
    {
        noiseLayers.arraySize++;
        SerializedProperty newLayer = noiseLayers.GetArrayElementAtIndex(noiseLayers.arraySize - 1);
        
        newLayer.FindPropertyRelative("enabled").boolValue = true;
        newLayer.FindPropertyRelative("layerName").stringValue = $"Layer {noiseLayers.arraySize}";
        newLayer.FindPropertyRelative("noiseType").enumValueIndex = 0;
        newLayer.FindPropertyRelative("amplitude").floatValue = 0.1f;
        newLayer.FindPropertyRelative("frequency").floatValue = 1f;
        newLayer.FindPropertyRelative("octaves").intValue = 1;
        newLayer.FindPropertyRelative("persistence").floatValue = 0.5f;
        newLayer.FindPropertyRelative("lacunarity").floatValue = 2f;
        newLayer.FindPropertyRelative("maskThreshold").floatValue = 0f;
        newLayer.FindPropertyRelative("debugColor").colorValue = layerColors[(noiseLayers.arraySize - 1) % layerColors.Length];
        
        bool[] newFoldouts = new bool[noiseLayers.arraySize];
        for (int i = 0; i < layerFoldouts.Length; i++)
        {
            newFoldouts[i] = layerFoldouts[i];
        }
        newFoldouts[noiseLayers.arraySize - 1] = true;
        layerFoldouts = newFoldouts;
        
        serializedObject.ApplyModifiedProperties();
        
        if (generator.autoUpdate && !generator.baked)
        {
            generator.GeneratePlanet();
        }
    }
    
    private void ApplyTerrestrialPreset()
    {
        if (generator.baked)
        {
            EditorUtility.DisplayDialog("Cannot Apply Preset", "Planet is baked. Revert bake first.", "OK");
            return;
        }
        
        generator.baseMeshType = TinyPlanetGenerator.MeshBaseType.Icosphere;
        generator.resolution = 5;
        generator.planetRadius = 1f;
        generator.normalMode = TinyPlanetGenerator.NormalCalculationMode.SphericalNormals;
        generator.invertNormals = false;
        
        noiseLayers.ClearArray();
        
        AddNoiseLayerPreset("Continents", TinyPlanetGenerator.NoiseLayer.NoiseType.Perlin, 
            0.3f, 1f, 4, 0.5f, 2f, 0f, new Color(0.2f, 0.6f, 0.3f));
        
        AddNoiseLayerPreset("Mountains", TinyPlanetGenerator.NoiseLayer.NoiseType.Ridged, 
            0.2f, 3f, 3, 0.4f, 2.5f, 0.3f, new Color(0.8f, 0.6f, 0.4f));
        
        AddNoiseLayerPreset("Details", TinyPlanetGenerator.NoiseLayer.NoiseType.Voronoi, 
            0.05f, 8f, 1, 0.5f, 2f, 0f, new Color(0.4f, 0.3f, 0.2f));
        
        serializedObject.ApplyModifiedProperties();
        generator.GeneratePlanet();
    }
    
    private void AddNoiseLayerPreset(string name, TinyPlanetGenerator.NoiseLayer.NoiseType type, 
        float amplitude, float frequency, int octaves, float persistence, float lacunarity, 
        float threshold, Color color)
    {
        noiseLayers.arraySize++;
        SerializedProperty layer = noiseLayers.GetArrayElementAtIndex(noiseLayers.arraySize - 1);
        
        layer.FindPropertyRelative("enabled").boolValue = true;
        layer.FindPropertyRelative("layerName").stringValue = name;
        layer.FindPropertyRelative("noiseType").enumValueIndex = (int)type;
        layer.FindPropertyRelative("amplitude").floatValue = amplitude;
        layer.FindPropertyRelative("frequency").floatValue = frequency;
        layer.FindPropertyRelative("octaves").intValue = octaves;
        layer.FindPropertyRelative("persistence").floatValue = persistence;
        layer.FindPropertyRelative("lacunarity").floatValue = lacunarity;
        layer.FindPropertyRelative("maskThreshold").floatValue = threshold;
        layer.FindPropertyRelative("debugColor").colorValue = color;
    }
    
    private void ExportMesh()
    {
        Mesh mesh = generator.GetMesh();
        if (mesh == null)
        {
            EditorUtility.DisplayDialog("No Mesh", "Please generate a planet first.", "OK");
            return;
        }
        
        string path = EditorUtility.SaveFilePanel(
            "Export Planet Mesh",
            "",
            $"TinyPlanet_{System.DateTime.Now:yyyyMMdd_HHmmss}.obj",
            "obj"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            ExportMeshToOBJ(mesh, path, generator.vertices, generator.normals, 
                generator.baked ? generator.bakedUVs : generator.uvs, generator.triangles);
            EditorUtility.DisplayDialog("Mesh Exported", $"Mesh saved to:\n{path}", "OK");
        }
    }
    
    private void ExportMeshToOBJ(Mesh mesh, string filePath, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles)
    {
        using (StreamWriter sw = new StreamWriter(filePath))
        {
            sw.WriteLine("# Tiny Planet Generator Export");
            sw.WriteLine("# Created: " + System.DateTime.Now);
            sw.WriteLine("# Vertices: " + vertices.Length);
            sw.WriteLine("# Triangles: " + triangles.Length / 3);
            sw.WriteLine();
            
            foreach (Vector3 v in vertices)
            {
                sw.WriteLine($"v {v.x} {v.y} {v.z}");
            }
            
            sw.WriteLine();
            
            foreach (Vector3 n in normals)
            {
                sw.WriteLine($"vn {n.x} {n.y} {n.z}");
            }
            
            sw.WriteLine();
            
            foreach (Vector2 uv in uvs)
            {
                sw.WriteLine($"vt {uv.x} {uv.y}");
            }
            
            sw.WriteLine();
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int idx1 = triangles[i] + 1;
                int idx2 = triangles[i + 1] + 1;
                int idx3 = triangles[i + 2] + 1;
                
                sw.WriteLine($"f {idx1}/{idx1}/{idx1} {idx2}/{idx2}/{idx2} {idx3}/{idx3}/{idx3}");
            }
        }
    }
    
    private void SavePreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Planet Preset",
            $"PlanetPreset_{System.DateTime.Now:yyyyMMdd_HHmmss}",
            "json",
            "Save planet settings as preset"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            // Create preset data
            PlanetPreset preset = new PlanetPreset
            {
                baseMeshType = generator.baseMeshType.ToString(),
                resolution = generator.resolution,
                planetRadius = generator.planetRadius,
                cubeSubdivisions = generator.cubeSubdivisions,
                normalizeCubeVertices = generator.normalizeCubeVertices,
                normalMode = generator.normalMode.ToString(),
                invertNormals = generator.invertNormals,
                seed = generator.seed,
                bakeTextureSize = generator.bakeTextureSize,
                uvBakeMode = generator.uvBakeMode.ToString(),
                bakeHeightScale = generator.bakeHeightScale,
                noiseLayers = new List<PresetNoiseLayer>()
            };
            
            foreach (TinyPlanetGenerator.NoiseLayer layer in generator.noiseLayers)
            {
                preset.noiseLayers.Add(new PresetNoiseLayer
                {
                    name = layer.layerName,
                    enabled = layer.enabled,
                    noiseType = layer.noiseType.ToString(),
                    amplitude = layer.amplitude,
                    frequency = layer.frequency,
                    octaves = layer.octaves,
                    persistence = layer.persistence,
                    lacunarity = layer.lacunarity,
                    maskThreshold = layer.maskThreshold,
                    debugColor = layer.debugColor
                });
            }
            
            string json = JsonUtility.ToJson(preset, true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            
            EditorUtility.DisplayDialog("Preset Saved", $"Preset saved to:\n{path}", "OK");
        }
    }
    
    [System.Serializable]
    private class PlanetPreset
    {
        public string baseMeshType;
        public int resolution;
        public float planetRadius;
        public int cubeSubdivisions;
        public bool normalizeCubeVertices;
        public string normalMode;
        public bool invertNormals;
        public Vector3 seed;
        public int bakeTextureSize;
        public string uvBakeMode;
        public float bakeHeightScale;
        public List<PresetNoiseLayer> noiseLayers;
    }
    
    [System.Serializable]
    private class PresetNoiseLayer
    {
        public string name;
        public bool enabled;
        public string noiseType;
        public float amplitude;
        public float frequency;
        public int octaves;
        public float persistence;
        public float lacunarity;
        public float maskThreshold;
        public Color debugColor;
    }
    
    [MenuItem("GameObject/3D Object/Tiny Planet Generator", false, 10)]
    private static void CreateTinyPlanetGenerator()
    {
        GameObject planet = new GameObject("Tiny Planet");
        TinyPlanetGenerator generator = planet.AddComponent<TinyPlanetGenerator>();
        planet.AddComponent<MeshCollider>();
        
        Renderer renderer = planet.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material defaultMat = new Material(Shader.Find("Standard"));
            defaultMat.color = new Color(0.3f, 0.6f, 0.4f);
            renderer.material = defaultMat;
        }
        
        generator.noiseLayers.Add(new TinyPlanetGenerator.NoiseLayer
        {
            layerName = "Base Terrain",
            amplitude = 0.15f,
            frequency = 2f,
            octaves = 3,
            persistence = 0.5f,
            lacunarity = 2f,
            debugColor = new Color(0.4f, 0.6f, 0.3f)
        });
        
        generator.GeneratePlanet();
        
        Selection.activeGameObject = planet;
        SceneView.FrameLastActiveSceneView();
    }
}

// Preview window for bake
public class BakePreviewWindow : EditorWindow
{
    private Texture2D heightMap;
    private Texture2D normalMap;
    private Vector2 scrollPos;
    
    public static void ShowWindow(Texture2D height, Texture2D normal)
    {
        BakePreviewWindow window = GetWindow<BakePreviewWindow>("Bake Preview");
        window.heightMap = height;
        window.normalMap = normal;
        window.minSize = new Vector2(400, 500);
    }
    
    void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bake Preview", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("This shows what the baked textures will look like.", EditorStyles.wordWrappedLabel);
        
        EditorGUILayout.Space(20);
        
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        
        if (heightMap != null)
        {
            EditorGUILayout.LabelField("Height Map Preview:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Size: {heightMap.width}x{heightMap.height}", EditorStyles.miniLabel);
            
            Rect rect = GUILayoutUtility.GetRect(300, 150);
            EditorGUI.DrawPreviewTexture(rect, heightMap);
            
            EditorGUILayout.Space(20);
        }
        
        if (normalMap != null)
        {
            EditorGUILayout.LabelField("Normal Map Preview:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Size: {normalMap.width}x{normalMap.height}", EditorStyles.miniLabel);
            
            Rect rect = GUILayoutUtility.GetRect(300, 150);
            EditorGUI.DrawPreviewTexture(rect, normalMap);
        }
        
        EditorGUILayout.EndScrollView();
        
        EditorGUILayout.Space(20);
        
        if (GUILayout.Button("Close", GUILayout.Height(30)))
        {
            Close();
        }
    }
}
#endif