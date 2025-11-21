using UnityEngine;
using UnityEditor;

public class GrassBrushLegacy : EditorWindow
{
    public GameObject grassPrefab;
    public float radius = 2f;
    public int countPerPaint = 30;
    public float randomScale = 0.2f;
    public Vector3 localRotation = Vector3.zero;

    bool active;
    GameObject parentObject;

    [MenuItem("Tools/Grass Brush Legacy")]
    public static void Init()
    {
        GetWindow<GrassBrushLegacy>("Grass Brush Legacy");
    }

    void OnGUI()
    {
        grassPrefab = (GameObject)EditorGUILayout.ObjectField("Grass Prefab", grassPrefab, typeof(GameObject), false);
        radius = EditorGUILayout.Slider("Brush Radius", radius, 0.1f, 10f);
        countPerPaint = EditorGUILayout.IntSlider("Density per Paint", countPerPaint, 1, 500);
        randomScale = EditorGUILayout.Slider("Random Scale", randomScale, 0f, 1f);
        localRotation = EditorGUILayout.Vector3Field("Local Rotation", localRotation);

        if (!active)
        {
            if (GUILayout.Button("Start Painting"))
            {
                active = true;
                SceneView.duringSceneGui += OnSceneGUI;
                if (parentObject == null)
                {
                    parentObject = new GameObject("GrassParent");
                }
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

        if (parentObject != null)
        {
            EditorGUILayout.LabelField($"Total Grass Instances: {parentObject.transform.childCount}");
        }
    }

    void OnSceneGUI(SceneView view)
    {
        Event e = Event.current;

        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (!TryGetMouseHit(out Vector3 hit, out Vector3 normal))
            return;

        Handles.color = new Color(0, 1, 0, 0.3f);
        Handles.DrawSolidDisc(hit, normal, radius);

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt)
        {
            Paint(hit, normal);
            e.Use();
        }
    }

    void Paint(Vector3 center, Vector3 normal)
    {
        if (grassPrefab == null) return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Paint Grass");

        for (int i = 0; i < countPerPaint; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * radius;
            Vector3 pos = center + new Vector3(rnd.x, 5f, rnd.y);

            if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 50f))
            {
                GameObject g = (GameObject)PrefabUtility.InstantiatePrefab(grassPrefab) as GameObject;
                g.transform.position = hit.point;

                float s = 1f + Random.Range(-randomScale, randomScale);
                g.transform.localScale = Vector3.one * s;

                Quaternion rot = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(localRotation);
                rot *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                g.transform.rotation = rot;

                g.transform.parent = parentObject.transform;

                Undo.RegisterCreatedObjectUndo(g, "Paint Grass");
            }
        }

        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
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
