using UnityEngine;
using UnityEditor;
using System.IO;

public class BillboardMeshCreator : EditorWindow
{
    private GameObject targetObject;
    private string meshName = "BillboardMesh";
    private bool preserveAspectRatio = true;
    private bool createNewMaterial = false;
    private Vector2 billboardSize = Vector2.one;

    [MenuItem("Tools/Billboard/Create Billboard Mesh")]
    public static void ShowWindow()
    {
        GetWindow<BillboardMeshCreator>("Billboard Creator");
    }

    void OnGUI()
    {
        GUILayout.Label("Billboard Mesh Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
        meshName = EditorGUILayout.TextField("Mesh Name", meshName);
        billboardSize = EditorGUILayout.Vector2Field("Billboard Size", billboardSize);
        preserveAspectRatio = EditorGUILayout.Toggle("Preserve Aspect Ratio", preserveAspectRatio);
        createNewMaterial = EditorGUILayout.Toggle("Create New Material", createNewMaterial);

        EditorGUILayout.Space();

        if (GUILayout.Button("Create Billboard"))
        {
            CreateBillboardMesh();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("This will create a quad mesh facing the camera and replace the target object's mesh.", MessageType.Info);
    }

    void CreateBillboardMesh()
    {
        if (targetObject == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a target object.", "OK");
            return;
        }

        MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = targetObject.AddComponent<MeshFilter>();
        }

        MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = targetObject.AddComponent<MeshRenderer>();
        }

        // Create billboard mesh
        Mesh billboardMesh = new Mesh();
        billboardMesh.name = meshName;

        float width = billboardSize.x;
        float height = billboardSize.y;

        // Calculate aspect ratio if preserving
        if (preserveAspectRatio && targetObject.GetComponent<MeshFilter>()?.sharedMesh != null)
        {
            Bounds bounds = targetObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            float aspect = bounds.size.z > 0 ? bounds.size.y / bounds.size.z : 1f;
            height = width * aspect;
        }

        // Vertices for a quad facing positive Z axis
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-width/2, -height/2, 0),
            new Vector3(width/2, -height/2, 0),
            new Vector3(-width/2, height/2, 0),
            new Vector3(width/2, height/2, 0)
        };

        // Triangles (two triangles making a quad)
        int[] triangles = new int[6]
        {
            0, 2, 1, // first triangle
            2, 3, 1  // second triangle
        };

        // UV coordinates for texture mapping
        Vector2[] uv = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        // Normals (all facing forward)
        Vector3[] normals = new Vector3[4]
        {
            Vector3.forward,
            Vector3.forward,
            Vector3.forward,
            Vector3.forward
        };

        billboardMesh.vertices = vertices;
        billboardMesh.triangles = triangles;
        billboardMesh.uv = uv;
        billboardMesh.normals = normals;

        meshFilter.mesh = billboardMesh;

        // Create material if requested
        if (createNewMaterial)
        {
            CreateAndAssignMaterial(meshRenderer);
        }

        // Add Billboard component
        Billboard billboardComponent = targetObject.GetComponent<Billboard>();
        if (billboardComponent == null)
        {
            billboardComponent = targetObject.AddComponent<Billboard>();
        }

        // Save mesh as asset
        SaveMeshAsAsset(billboardMesh);

        Debug.Log("Billboard mesh created for: " + targetObject.name);
    }

    void CreateAndAssignMaterial(MeshRenderer meshRenderer)
    {
        // Try to find a compatible shader
        Shader shader = FindCompatibleShader();
        
        if (shader == null)
        {
            Debug.LogError("No compatible shader found! Material will be purple.");
            return;
        }

        // Create a new material
        Material material = new Material(shader);
        material.name = meshName + "_Material";
        
        // Set default properties based on shader type
        SetupMaterialProperties(material, shader);
        
        // Save the material as an asset
        string materialsPath = "Assets/BillboardMeshes/Materials/";
        if (!Directory.Exists(materialsPath))
        {
            Directory.CreateDirectory(materialsPath);
        }

        string materialPath = materialsPath + material.name + ".mat";
        
        // Check if material already exists
        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existingMaterial != null)
        {
            if (!EditorUtility.DisplayDialog("Material Exists", 
                "A material with this name already exists. Overwrite?", "Yes", "No"))
            {
                // Use existing material instead
                material = existingMaterial;
            }
            else
            {
                // Overwrite: create new asset
                AssetDatabase.CreateAsset(material, materialPath);
            }
        }
        else
        {
            // Create new material asset
            AssetDatabase.CreateAsset(material, materialPath);
        }

        // Assign the material to the renderer
        meshRenderer.material = material;
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"New material created with shader: {shader.name}");
        Debug.Log($"Material saved: {materialPath}");
    }

    Shader FindCompatibleShader()
    {
        // Try different shaders in order of preference
        
        // 1. Try URP Lit shader (most common)
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null) return shader;
        
        // 2. Try Built-in Standard shader
        shader = Shader.Find("Standard");
        if (shader != null) return shader;
        
        // 3. Try HDRP Lit shader
        shader = Shader.Find("HDRP/Lit");
        if (shader != null) return shader;
        
        // 4. Try simple unlit shaders as fallback
        shader = Shader.Find("Unlit/Color");
        if (shader != null) return shader;
        
        shader = Shader.Find("Unlit/Texture");
        if (shader != null) return shader;
        
        // 5. Final fallback - use any available shader
        shader = Shader.Find("Sprites/Default");
        if (shader != null) return shader;
        
        return null;
    }

    void SetupMaterialProperties(Material material, Shader shader)
    {
        string shaderName = shader.name.ToLower();
        
        if (shaderName.Contains("standard") || shaderName.Contains("lit"))
        {
            // Standard or URP/HDRP lit shaders
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness") || material.HasProperty("_Smoothness"))
                material.SetFloat("_Glossiness", 0.1f);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
        }
        else if (shaderName.Contains("unlit/color"))
        {
            // Unlit color shader
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
        }
        else if (shaderName.Contains("unlit/texture"))
        {
            // Unlit texture shader - set white color
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
        }
        else if (shaderName.Contains("sprites/default"))
        {
            // Sprite shader
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
        }
    }

    void SaveMeshAsAsset(Mesh mesh)
    {
        string path = "Assets/BillboardMeshes/";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        string assetPath = path + meshName + ".asset";
        
        // Check if asset already exists
        Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existingMesh != null)
        {
            if (!EditorUtility.DisplayDialog("Mesh Exists", 
                "A mesh with this name already exists. Overwrite?", "Yes", "No"))
            {
                return;
            }
        }

        AssetDatabase.CreateAsset(mesh, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Mesh saved: " + assetPath);
    }
}