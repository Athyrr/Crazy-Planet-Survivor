// PlanetFoliagePainterWindow.cs
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

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
        MeshFilter mf = planet.GetComponent<MeshFilter>();
        if (mf == null) return;
        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 center = planet.transform.position;

        Undo.RecordObject(targetData, "Procedural Foliage");

        for (int i = 0; i < proceduralCount; i++)
        {
            int triIndex = Random.Range(0, triangles.Length / 3) * 3;
            Vector3 v0 = planet.transform.TransformPoint(vertices[triangles[triIndex + 0]]);
            Vector3 v1 = planet.transform.TransformPoint(vertices[triangles[triIndex + 1]]);
            Vector3 v2 = planet.transform.TransformPoint(vertices[triangles[triIndex + 2]]);

            float r1 = Random.value, r2 = Random.value;
            if (r1 + r2 > 1f) { r1 = 1 - r1; r2 = 1 - r2; }
            float r3 = 1f - r1 - r2;
            Vector3 bary = v0 * r1 + v1 * r2 + v2 * r3;
            Vector3 normal = (bary - center).normalized;
            Vector3 pos = bary + normal * offset;

            AddInstance(pos, normal);
        }

        Debug.Log("hyv; PlaceProcedural");
        EditorUtility.SetDirty(targetData);
    }
}
