using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public class PlanetFoliagePainter : EditorWindow
{
    [Header("Prefab & Planet")]
    public GameObject grassPrefab;
    public GameObject planet;

    [Header("Manual Brush Settings")]
    public float brushRadius = 2f;
    public int brushDensity = 30;
    public float offset = 0.0f; // offset au-dessus de la surface
    public bool eraseMode = false;

    [Header("Foliage Settings")]
    public float randomScale = 0.2f;
    public Vector3 localRotation = Vector3.zero;
    public bool castShadows = true;
    public int proceduralCount = 1000;

    GameObject parentObject;
    bool active;

    [MenuItem("Tools/Planet Foliage Painter")]
    public static void Init()
    {
        GetWindow<PlanetFoliagePainter>("Planet Foliage Painter");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Foliage Painter", EditorStyles.boldLabel);

        grassPrefab = (GameObject)EditorGUILayout.ObjectField("Grass Prefab", grassPrefab, typeof(GameObject), false);
        planet = (GameObject)EditorGUILayout.ObjectField("Planet", planet, typeof(GameObject), true);

        brushRadius = EditorGUILayout.Slider("Brush Radius", brushRadius, 0.1f, 10f);
        brushDensity = EditorGUILayout.IntSlider("Brush Density", brushDensity, 1, 500);
        offset = EditorGUILayout.Slider("Offset", offset, -2f, 2f);

        eraseMode = GUILayout.Toggle(eraseMode, "Erase Mode (brush)");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Foliage Properties", EditorStyles.boldLabel);

        randomScale = EditorGUILayout.Slider("Random Scale", randomScale, 0f, 1f);
        localRotation = EditorGUILayout.Vector3Field("Local Rotation", localRotation);
        castShadows = EditorGUILayout.Toggle("Cast Shadows", castShadows);
        proceduralCount = EditorGUILayout.IntField("Procedural Count", proceduralCount);

        EditorGUILayout.Space();

        if (!active)
        {
            if (GUILayout.Button("Start Manual Painting"))
            {
                active = true;
                SceneView.duringSceneGui += OnSceneGUI;
                EnsureParent();
            }
        }
        else
        {
            if (GUILayout.Button("Stop Manual Painting"))
            {
                active = false;
                SceneView.duringSceneGui -= OnSceneGUI;
            }
        }

        if (GUILayout.Button("Place Procedural Foliage"))
        {
            PlaceProceduralFoliageOnSurface();
        }

        if (GUILayout.Button("Clear All Foliage"))
        {
            ClearAllFoliage();
        }

        if (parentObject != null)
            EditorGUILayout.LabelField($"Total Instances: {parentObject.transform.childCount}");
    }

    void EnsureParent()
    {
        if (parentObject == null)
            parentObject = new GameObject("FoliageParent");
    }

    void OnSceneGUI(SceneView view)
    {
        if (!active || grassPrefab == null) return;

        Event e = Event.current;
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (!TryGetMouseHit(out Vector3 hit, out Vector3 normal)) return;

        Handles.color = eraseMode ? new Color(1, 0, 0, 0.3f) : new Color(0, 1, 0, 0.3f);
        Handles.DrawSolidDisc(hit, normal, brushRadius);

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            if (eraseMode)
                EraseGrass(hit);
            else
                PaintBrush(hit, normal);
            e.Use();
        }
    }

    void PaintBrush(Vector3 center, Vector3 normal)
    {
        EnsureParent();

        for (int i = 0; i < brushDensity; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * brushRadius;
            Vector3 pos = center + new Vector3(rnd.x, 0f, rnd.y) + normal * offset;

            if (Physics.Raycast(pos + normal * 10f, -normal, out RaycastHit hit, 20f))
            {
                Paint(hit.point, hit.normal);
            }
        }
    }

    void Paint(Vector3 position, Vector3 normal)
    {
        GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(grassPrefab) as GameObject;
        g.transform.position = position;

        float s = 1f + Random.Range(-randomScale, randomScale);
        g.transform.localScale = Vector3.one * s;

        // rotation alignée sur la normale + rotation locale + Z aléatoire
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent == Vector3.zero) tangent = Vector3.Cross(normal, Vector3.right);
        Quaternion rot = Quaternion.LookRotation(tangent.normalized, normal);
        rot *= Quaternion.Euler(localRotation);
        rot *= Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
        g.transform.rotation = rot;

        g.transform.parent = parentObject.transform;

        MeshRenderer mr = g.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

        Undo.RegisterCreatedObjectUndo(g, "Paint Foliage");
    }

    void EraseGrass(Vector3 center)
    {
        if (parentObject == null) return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Erase Grass");

        for (int i = parentObject.transform.childCount - 1; i >= 0; i--)
        {
            Transform t = parentObject.transform.GetChild(i);
            if (Vector3.Distance(center, t.position) <= brushRadius)
                Undo.DestroyObjectImmediate(t.gameObject);
        }

        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
    }

    void ClearAllFoliage()
    {
        if (parentObject == null) return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Clear All Foliage");

        for (int i = parentObject.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(parentObject.transform.GetChild(i).gameObject);

        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
    }

    void PlaceProceduralFoliageOnSurface()
    {
        if (grassPrefab == null || planet == null) return;
        EnsureParent();

        MeshFilter mf = planet.GetComponent<MeshFilter>();
        if (mf == null) return;

        Mesh mesh = mf.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        Vector3 planetCenter = planet.transform.position;

        for (int i = 0; i < proceduralCount; i++)
        {
            int triIndex = Random.Range(0, triangles.Length / 3) * 3;
            Vector3 v0 = planet.transform.TransformPoint(vertices[triangles[triIndex + 0]]);
            Vector3 v1 = planet.transform.TransformPoint(vertices[triangles[triIndex + 1]]);
            Vector3 v2 = planet.transform.TransformPoint(vertices[triangles[triIndex + 2]]);

            float r1 = Random.value;
            float r2 = Random.value;
            if (r1 + r2 > 1f)
            {
                r1 = 1f - r1;
                r2 = 1f - r2;
            }
            float r3 = 1f - r1 - r2;

            Vector3 pos = v0 * r1 + v1 * r2 + v2 * r3 + (v0 * r1 + v1 * r2 + v2 * r3 - planetCenter).normalized * offset;
            Vector3 normal = (pos - planetCenter).normalized;

            Paint(pos, normal);
        }
    }

    bool TryGetMouseHit(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }
        return false;
    }
}
