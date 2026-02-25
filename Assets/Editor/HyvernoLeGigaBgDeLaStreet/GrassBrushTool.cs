// PlanetFoliagePainterWindow.cs

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlanetFoliagePainterWindow : EditorWindow
{

    #region Member

    

    [Header("Target")] 
    private GameObject _planet;
    private Mesh _instanceMesh; // mesh rendered (ex: grass mesh)
    private Material _instanceMaterial;

    [Header("Brush")] 
    private float _brushRadius = 2f;
    private int _brushDensity = 30;
    private float _offset = 0.0f;
    private bool _eraseMode = false;

    [Header("Foliage")] 
    private float _globalScale = 1f;
    private float _randomScale = 0.2f;
    private Vector3 _localRotation = Vector3.zero;
    private int _proceduralCount = 1000;

    [Header("Material Filtering")] 
    private bool _useMaterialFiltering = false;
    
    // Accessor
    [HideInInspector] public string[] AllowedMaterialNames = new string[0];
    [HideInInspector] public string[] BlockedMaterialNames = new string[0];

    [Header("Extra")] 
    private bool _showRaycast;
    private FoliageData _targetData;
    private bool _active;
    
    #endregion

    [MenuItem("Tools/Planet Foliage Painter (Indirect)")]
    public static void Open() => GetWindow<PlanetFoliagePainterWindow>("Planet Foliage Painter");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Painter (stores instances only)", EditorStyles.boldLabel);

        _planet = (GameObject)EditorGUILayout.ObjectField("Planet", _planet, typeof(GameObject), true);
        _instanceMesh = (Mesh)EditorGUILayout.ObjectField("Instance Mesh", _instanceMesh, typeof(Mesh), false);
        _instanceMaterial =
            (Material)EditorGUILayout.ObjectField("Instance Material", _instanceMaterial, typeof(Material), false);

        _targetData =
            (FoliageData)EditorGUILayout.ObjectField("Foliage Data (SO)", _targetData, typeof(FoliageData), false);

        EditorGUILayout.Space();
        _brushRadius = EditorGUILayout.Slider("Brush Radius", _brushRadius, 0.05f, 50f);
        _brushDensity = EditorGUILayout.IntSlider("Brush Density", _brushDensity, 1, 200);
        _offset = EditorGUILayout.Slider("Offset", _offset, -5f, 5f);
        _eraseMode = EditorGUILayout.Toggle("Erase Mode", _eraseMode);
        
        EditorGUILayout.Space();
        _globalScale = EditorGUILayout.Slider("Global Scale", _globalScale, 0f, 10f);
        _localRotation = EditorGUILayout.Vector3Field("Local Rotation", _localRotation);
        _randomScale = EditorGUILayout.Slider("Random Scale", _randomScale, 0f, 2f);
        _proceduralCount = EditorGUILayout.IntField("Procedural Count", _proceduralCount);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Material Filtering", EditorStyles.boldLabel);
        _useMaterialFiltering = EditorGUILayout.Toggle("Use Material Filtering", _useMaterialFiltering);

        if (_useMaterialFiltering)
        {
            EditorGUILayout.LabelField("Allowed Materials (contains):");
            ScriptableObject target = this;
            var so = new SerializedObject(target);
            var allowedMaterials = so.FindProperty("AllowedMaterialNames");
            if (allowedMaterials == null)
                goto MaterialReferenceNull;
            
            EditorGUILayout.PropertyField(allowedMaterials, true);

            EditorGUILayout.LabelField("Blocked Materials (contains):");
            SerializedProperty blockedMaterials = so.FindProperty("BlockedMaterialNames");
            if (blockedMaterials == null)
                goto MaterialReferenceNull;
            
            EditorGUILayout.PropertyField(blockedMaterials, true);

            so.ApplyModifiedProperties();
        }
        
        MaterialReferenceNull:

        EditorGUILayout.Space();
        
        _eraseMode = EditorGUILayout.Toggle("Show Raycast", _showRaycast);

        
        EditorGUILayout.Space();

        if (!_active)
        {
            if (GUILayout.Button("Start Painting (SceneView)"))
            {
                _active = true;
                SceneView.duringSceneGui += OnSceneGUI;
                if (_targetData == null) CreateOrSelectData();
            }
        }
        else
        {
            if (GUILayout.Button("Stop Painting"))
            {
                _active = false;
                SceneView.duringSceneGui -= OnSceneGUI;
            }
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Place Procedural"))
        {
            if (_planet != null) PlaceProcedural();
        }

        if (GUILayout.Button("Clear Data"))
        {
            if (_targetData != null)
            {
                Undo.RecordObject(_targetData, "Clear FoliageData");
                _targetData.instances.Clear();
                EditorUtility.SetDirty(_targetData);
            }
        }

        GUILayout.EndHorizontal();

        if (GUILayout.Button("Create/Select FoliageData Asset"))
            CreateOrSelectData();

        EditorGUILayout.Space();
        if (_targetData != null)
            EditorGUILayout.LabelField($"Instances: {_targetData.instances.Count}");

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Painter stores instance data only. Use a runtime FoliageRenderer to draw with DrawMeshInstancedIndirect.",
            MessageType.Info);
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

        _targetData = so;
        Selection.activeObject = so;
    }

    void OnSceneGUI(SceneView sv)
    {
        if (!_active) return;
        if (_targetData == null) CreateOrSelectData();
        if (_planet == null) return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (!TryGetMouseOnPlanet(out Vector3 hit, out Vector3 normal)) return;

        Handles.color = _eraseMode ? new Color(1, 0, 0, 0.25f) : new Color(0, 1, 0, 0.25f);
        Handles.DrawSolidDisc(hit, normal, _brushRadius);

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (_eraseMode)
                EraseAt(hit);
            else
                PaintBrush(hit, normal);
            e.Use();
        }
    }

    bool TryGetMouseOnPlanet(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            // ensure it hits the planet collider
            if (hit.collider != null && _planet != null && hit.collider.gameObject == _planet)
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
        }

        // fallback: project to planet surface along ray using planet center
        if (_planet != null)
        {
            Vector3 center = _planet.transform.position;
            // intersect ray with sphere approximated by bounds
            float radius = _planet.GetComponent<MeshFilter>()?.sharedMesh?.bounds.max.magnitude *
                _planet.transform.lossyScale.x ?? 1f;
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
        if (_targetData == null) return;
        Undo.RecordObject(_targetData, "Paint Foliage");

        for (int i = 0; i < _brushDensity; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * _brushRadius;
            // generate point in tangent plane
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, normal);

            Vector3 pos = center + right * rnd.x + forward * rnd.y;
            // raycast along radial direction to snap precisely to surface
            Vector3 dir = (pos - _planet.transform.position).normalized;
            if (Physics.Raycast(pos + dir * 10f, -dir, out RaycastHit hit, 20f))
            {
                // Check material if filtering is enabled
                if (_useMaterialFiltering)
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
                Vector3 p = (bary - _planet.transform.position).normalized * GetPlanetRadiusWorld() +
                            _planet.transform.position + (bary - _planet.transform.position).normalized * _offset;
                AddInstance(p, (p - _planet.transform.position).normalized);
            }
        }

        EditorUtility.SetDirty(_targetData);
    }

    void AddInstance(Vector3 pos, Vector3 normal)
    {
        FoliageInstance inst = new FoliageInstance
        {
            position = pos,
            normal = normal,
            scale = _globalScale + Random.Range(-_randomScale, _randomScale),
            rotation = normal + _localRotation
        };

        Debug.DrawLine(pos, pos + normal, Color.green, 1f);

        _targetData.instances.Add(inst);
    }

    void EraseAt(Vector3 center)
    {
        if (_targetData == null) return;
        Undo.RecordObject(_targetData, "Erase Foliage");
        float r2 = _brushRadius * _brushRadius;
        _targetData.instances.RemoveAll(i => (i.position - center).sqrMagnitude <= r2);
        EditorUtility.SetDirty(_targetData);
    }

    float GetPlanetRadiusWorld()
    {
        MeshFilter mf = _planet.GetComponent<MeshFilter>();
        if (mf == null) return 1f;
        var mesh = mf.sharedMesh;
        float max = 0f;
        foreach (var v in mesh.vertices) max = Mathf.Max(max, v.magnitude);
        return max * _planet.transform.lossyScale.x;
    }

    void PlaceProcedural()
    {
        if (_planet == null || _targetData == null) return;

        // Get all mesh colliders in the planet and its children
        MeshCollider[] meshColliders = _planet.GetComponentsInChildren<MeshCollider>();
        if (meshColliders == null || meshColliders.Length == 0)
        {
            Debug.LogError("Planet or its children require MeshColliders for procedural placement");
            return;
        }

        Undo.RecordObject(_targetData, "Procedural Foliage");

        int placedCount = 0;
        int attempts = 0;
        int maxAttempts = _proceduralCount * 10;

        while (placedCount < _proceduralCount && attempts < maxAttempts)
        {
            attempts++;

            // Generate random direction from center
            Vector3 randomDirection = Random.onUnitSphere;

            // Start ray from outside the planet pointing inward
            Vector3 center = _planet.transform.position;
            float planetRadius = GetPlanetRadiusWorld();
            Vector3 rayStart = center + randomDirection * (planetRadius * 2f);
            Vector3 rayDirection = -randomDirection;
            
            Ray ray = new Ray(rayStart, rayDirection);

            // Raycast against ALL colliders in the planet hierarchy
            List<RaycastHit> hits = Physics.RaycastAll(ray, planetRadius * 3f).ToList();
            hits = hits.OrderBy(el => Vector3.Distance(rayStart, el.point)).ToList();
            bool hitFound = false;

            Debug.Log($"{rayStart}, {rayDirection}");

            foreach (RaycastHit hit in hits)
            {
                // Check if the hit object is part of the planet hierarchy
                if (hit.collider != null)
                {
                    // Check material if filtering is enabled
                    if (_useMaterialFiltering)
                    {
                        Material hitMaterial = GetMaterialAtHit(hit);
                        Debug.Log(hitMaterial.name);
                        if (!IsValidMaterialForPlacement(hitMaterial))
                        {
                            Debug.Log($"{hitMaterial.name} hit rejected by material filtering");
                            break;
                        }
                    }

                    if (_showRaycast)
                        Debug.DrawLine(rayStart, hit.point, Color.red, 2f);

                    Vector3 normal = hit.normal;
                    Vector3 pos = hit.point + Vector3.up * _offset;

                    AddInstance(pos, normal);
                    placedCount++;
                    hitFound = true;
                    break; // We found a valid hit, break the loop
                }
            }

            // If no valid hit was found, continue to next attempt
            if (!hitFound) continue;
        }

        Debug.Log($"Placed {placedCount} procedural objects out of {_proceduralCount} attempted");
        EditorUtility.SetDirty(_targetData);
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
        if (!_useMaterialFiltering || material == null)
            return true;

        string materialName = material.name;

        // Check blocked materials
        if (BlockedMaterialNames != null)
        {
            foreach (string blockedName in BlockedMaterialNames)
            {
                if (!string.IsNullOrEmpty(blockedName) && materialName.ToLower().Contains(blockedName.ToLower()))
                    return false;
            }
        }

        // Check allowed materials (if specified)
        if (AllowedMaterialNames != null && AllowedMaterialNames.Length > 0)
        {
            foreach (string allowedName in AllowedMaterialNames)
            {
                if (!string.IsNullOrEmpty(allowedName) && materialName.ToLower().Contains(allowedName.ToLower()))
                    return true;
            }

            return false; // Material not in allowed list
        }

        return true;
    }
}