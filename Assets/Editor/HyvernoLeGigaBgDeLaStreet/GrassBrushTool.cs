// PlanetFoliagePainterWindow.cs
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlanetFoliagePainterWindow : EditorWindow
{
    [Header("Target")]
    public GameObject planet;
    public Mesh instanceMesh; // mesh rendered (ex: grass mesh)
    public Material instanceMaterial;

    [Header("Brush")]
    public float brushRadius = 2f;
    public int brushDensity = 30;
    public float offset = 0.0f;
    public bool eraseMode = false;

    [Header("Foliage")]
    public float globalScale = 1f;
    public float randomScale = 0.2f;
    public Vector3 localRotation = Vector3.zero;
    public int proceduralCount = 1000;

    [Header("Material Filtering")]
    public bool useMaterialFiltering = false;
    public string[] allowedMaterialNames = new string[0];
    public string[] blockedMaterialNames = new string[0];

    public FoliageData targetData;

    bool active;

    [MenuItem("Tools/Planet Foliage Painter (Indirect)")]
    public static void Open() => GetWindow<PlanetFoliagePainterWindow>("Planet Foliage Painter");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Painter (stores instances only)", EditorStyles.boldLabel);

        planet = (GameObject)EditorGUILayout.ObjectField("Planet", planet, typeof(GameObject), true);
        instanceMesh = (Mesh)EditorGUILayout.ObjectField("Instance Mesh", instanceMesh, typeof(Mesh), false);
        instanceMaterial = (Material)EditorGUILayout.ObjectField("Instance Material", instanceMaterial, typeof(Material), false);

        targetData = (FoliageData)EditorGUILayout.ObjectField("Foliage Data (SO)", targetData, typeof(FoliageData), false);

        EditorGUILayout.Space();
        brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.05f, 50f);
        brushDensity = EditorGUILayout.IntSlider("Brush Density", brushDensity, 1, 200);
        offset = EditorGUILayout.Slider("Offset", offset, -5f, 5f);
        eraseMode = EditorGUILayout.Toggle("Erase Mode", eraseMode);

        EditorGUILayout.Space();
        globalScale = EditorGUILayout.Slider("Global Scale", globalScale, 0f, 10f);
        localRotation = EditorGUILayout.Vector3Field("Local Rotation", localRotation);
        randomScale = EditorGUILayout.Slider("Random Scale", randomScale, 0f, 2f);
        proceduralCount = EditorGUILayout.IntField("Procedural Count", proceduralCount);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Filtering", EditorStyles.boldLabel);
        useMaterialFiltering = EditorGUILayout.Toggle("Use Material Filtering", useMaterialFiltering);
        
        if (useMaterialFiltering)
        {
            EditorGUILayout.LabelField("Allowed Materials (contains):");
            ScriptableObject target = this;
            SerializedObject so = new SerializedObject(target);
            SerializedProperty allowedMaterials = so.FindProperty("allowedMaterialNames");
            EditorGUILayout.PropertyField(allowedMaterials, true);
            
            EditorGUILayout.LabelField("Blocked Materials (contains):");
            SerializedProperty blockedMaterials = so.FindProperty("blockedMaterialNames");
            EditorGUILayout.PropertyField(blockedMaterials, true);
            
            so.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();

        if (!active)
        {
            if (GUILayout.Button("Start Painting (SceneView)"))
            {
                active = true;
                SceneView.duringSceneGui += OnSceneGUI;
                if (targetData == null) CreateOrSelectData();
            }
        }
        else
        {
            if (GUILayout.Button("Stop Painting"))
            {
                active = false;
                SceneView.duringSceneGui -= OnSceneGUI;
            }
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Place Procedural"))
        {
            if (planet != null) PlaceProcedural();
        }
        if (GUILayout.Button("Clear Data"))
        {
            if (targetData != null)
            {
                Undo.RecordObject(targetData, "Clear FoliageData");
                targetData.instances.Clear();
                EditorUtility.SetDirty(targetData);
            }
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Create/Select FoliageData Asset"))
            CreateOrSelectData();

        EditorGUILayout.Space();
        if (targetData != null)
            EditorGUILayout.LabelField($"Instances: {targetData.instances.Count}");

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Painter stores instance data only. Use a runtime FoliageRenderer to draw with DrawMeshInstancedIndirect.", MessageType.Info);
    }

    void CreateOrSelectData()
    {
        string path = "Assets/FoliageData.asset";
        var so = AssetDatabase.LoadAssetAtPath<FoliageData>(path);
        if (so == null)
        {
            so = CreateInstance<FoliageData>();
            AssetDatabase.CreateAsset(so, path);
            AssetDatabase.SaveAssets();
        }
        targetData = so;
        Selection.activeObject = so;
    }

    void OnSceneGUI(SceneView sv)
    {
        if (!active) return;
        if (targetData == null) CreateOrSelectData();
        if (planet == null) return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (!TryGetMouseOnPlanet(out Vector3 hit, out Vector3 normal)) return;

        Handles.color = eraseMode ? new Color(1, 0, 0, 0.25f) : new Color(0, 1, 0, 0.25f);
        Handles.DrawSolidDisc(hit, normal, brushRadius);

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (eraseMode)
                EraseAt(hit);
            else
                PaintBrush(hit, normal);
            e.Use();
        }
    }

    bool TryGetMouseOnPlanet(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero; normal = Vector3.up;
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            // ensure it hits the planet collider
            if (hit.collider != null && planet != null && hit.collider.gameObject == planet)
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
        }

        // fallback: project to planet surface along ray using planet center
        if (planet != null)
        {
            Vector3 center = planet.transform.position;
            // intersect ray with sphere approximated by bounds
            float radius = planet.GetComponent<MeshFilter>()?.sharedMesh?.bounds.max.magnitude * planet.transform.lossyScale.x ?? 1f;
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(ray.direction, oc);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc >= 0)
            {
                float t = -b - Mathf.Sqrt(disc);
                if (t > 0)
                {
                    Vector3 p = ray.origin + ray.direction * t;
                    point = p;
                    normal = (p - center).normalized;
                    return true;
                }
            }
        }

        return false;
    }

    void PaintBrush(Vector3 center, Vector3 normal)
    {
        if (targetData == null) return;
        Undo.RecordObject(targetData, "Paint Foliage");

        for (int i = 0; i < brushDensity; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * brushRadius;
            // generate point in tangent plane
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, normal);

            Vector3 pos = center + right * rnd.x + forward * rnd.y;
            // raycast along radial direction to snap precisely to surface
            Vector3 dir = (pos - planet.transform.position).normalized;
            if (Physics.Raycast(pos + dir * 10f, -dir, out RaycastHit hit, 20f))
            {
                // Check material if filtering is enabled
                if (useMaterialFiltering)
                {
                    Material hitMaterial = GetMaterialAtHit(hit);
                    if (!IsValidMaterialForPlacement(hitMaterial))
                        continue;
                }
                AddInstance(hit.point, hit.normal);
            }
            else
            {
                // fallback: project to sphere surface
                Vector3 bary = pos;
                Vector3 p = (bary - planet.transform.position).normalized * GetPlanetRadiusWorld() + planet.transform.position + (bary - planet.transform.position).normalized * offset;
                AddInstance(p, (p - planet.transform.position).normalized);
            }
        }

        EditorUtility.SetDirty(targetData);
    }

    void AddInstance(Vector3 pos, Vector3 normal)
    {
        FoliageInstance inst = new FoliageInstance
        {
            position = pos,
            normal = normal,
            scale = globalScale + Random.Range(-randomScale, randomScale),
            rotation = normal + localRotation
        };

        Debug.DrawLine(pos, pos + normal, Color.green, 1f);

        targetData.instances.Add(inst);
    }

    void EraseAt(Vector3 center)
    {
        if (targetData == null) return;
        Undo.RecordObject(targetData, "Erase Foliage");
        float r2 = brushRadius * brushRadius;
        targetData.instances.RemoveAll(i => (i.position - center).sqrMagnitude <= r2);
        EditorUtility.SetDirty(targetData);
    }

    float GetPlanetRadiusWorld()
    {
        MeshFilter mf = planet.GetComponent<MeshFilter>();
        if (mf == null) return 1f;
        var mesh = mf.sharedMesh;
        float max = 0f;
        foreach (var v in mesh.vertices) max = Mathf.Max(max, v.magnitude);
        return max * planet.transform.lossyScale.x;
    }

    void PlaceProcedural()
    {
        if (planet == null || targetData == null) return;
        
        // Get all mesh colliders in the planet and its children
        MeshCollider[] meshColliders = planet.GetComponentsInChildren<MeshCollider>();
        if (meshColliders == null || meshColliders.Length == 0) 
        {
            Debug.LogError("Planet or its children require MeshColliders for procedural placement");
            return;
        }
        
        Undo.RecordObject(targetData, "Procedural Foliage");

        int placedCount = 0;
        int attempts = 0;
        int maxAttempts = proceduralCount * 10;

        while (placedCount < proceduralCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Generate random direction from center
            Vector3 randomDirection = Random.onUnitSphere;
            
            // Start ray from outside the planet pointing inward
            Vector3 center = planet.transform.position;
            float planetRadius = GetPlanetRadiusWorld();
            Vector3 rayStart = center + randomDirection * (planetRadius * 2f);
            Vector3 rayDirection = -randomDirection;
            
            
            Ray ray = new Ray(rayStart, rayDirection);
            
            // Raycast against ALL colliders in the planet hierarchy
            List<RaycastHit> hits = Physics.RaycastAll(ray, planetRadius * 3f).ToList();
            hits = hits.OrderBy(el => Vector3.Distance(rayStart, el.point)).ToList();
            bool hitFound = false;
            
            foreach (RaycastHit hit in hits)
            {
                // Check if the hit object is part of the planet hierarchy
                if (hit.collider != null)
                {
                    // Check material if filtering is enabled
                    if (useMaterialFiltering)
                    {
                        Material hitMaterial = GetMaterialAtHit(hit);
                        Debug.Log(hitMaterial.name);
                        if (!IsValidMaterialForPlacement(hitMaterial))
                        {
                            Debug.Log($"{hitMaterial.name} hit rejected by material filtering");
                            break;
                        }
                    }
                    
                    Debug.DrawLine(rayStart, hit.point, Color.red, 2f);

                    Vector3 normal = hit.normal;
                    Vector3 pos = hit.point + Vector3.up * offset;
                    
                    AddInstance(pos, normal);
                    placedCount++;
                    hitFound = true;
                    break; // We found a valid hit, break the loop
                }
            }
            
            // If no valid hit was found, continue to next attempt
            if (!hitFound) continue;
        }

        Debug.Log($"Placed {placedCount} procedural objects out of {proceduralCount} attempted");
        EditorUtility.SetDirty(targetData);
    }

    // Helper method to get material at a specific hit point
    private Material GetMaterialAtHit(RaycastHit hit)
    {
        MeshRenderer renderer = hit.collider.GetComponent<MeshRenderer>();
        if (renderer == null || renderer.sharedMaterials == null) 
            return null;
    
        Mesh mesh = hit.collider.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return null;
    
        // Determine which submesh this triangle belongs to
        int triangleIndex = hit.triangleIndex;
        int subMeshCount = mesh.subMeshCount;
        int accumulatedTriangles = 0;
    
        for (int i = 0; i < subMeshCount; i++)
        {
            int triangleCount = mesh.GetTriangles(i).Length / 3;
            if (triangleIndex < accumulatedTriangles + triangleCount)
            {
                // Return the material for this submesh
                if (i < renderer.sharedMaterials.Length)
                {
                    return renderer.sharedMaterials[i];
                }
                break;
            }
            accumulatedTriangles += triangleCount;
        }
    
        // Fallback to first material
        return renderer.sharedMaterial;
    }

    // Method to validate if placement is allowed on this material
    public bool IsValidMaterialForPlacement(Material material)
    {
        if (!useMaterialFiltering || material == null) 
            return true;
        
        string materialName = material.name;
        
        // Check blocked materials
        if (blockedMaterialNames != null)
        {
            foreach (string blockedName in blockedMaterialNames)
            {
                if (!string.IsNullOrEmpty(blockedName) && materialName.ToLower().Contains(blockedName.ToLower()))
                    return false;
            }
        }
        
        // Check allowed materials (if specified)
        if (allowedMaterialNames != null && allowedMaterialNames.Length > 0)
        {
            foreach (string allowedName in allowedMaterialNames)
            {
                if (!string.IsNullOrEmpty(allowedName) && materialName.ToLower().Contains(allowedName.ToLower()))
                    return true;
            }
            return false; // Material not in allowed list
        }
        
        return true;
    }
}