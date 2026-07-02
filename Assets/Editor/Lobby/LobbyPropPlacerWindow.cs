using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Editor tool to drop prefabs onto any spherical planet, snapping each placement to the surface and
/// aligning it to the surface normal ("standing up" on the sphere).
///
/// The surface sphere is read from the planet's <see cref="PlanetDataAuthoring"/> (the exact center/radius
/// the game bakes) so it works on any planet — lobby, Volcanus, etc. — regardless of how its renderers are
/// nested. It falls back to the largest renderer's bounds when no authoring is present, and to a classic
/// collider raycast when "Snap To Colliders" is enabled (true bumpy terrain if a MeshCollider exists).
///
/// Click the planet in the Scene view to place the active prefab; use "Snap Selection" to re-stick
/// already-placed objects.
/// </summary>
public class LobbyPropPlacerWindow : EditorWindow
{
    [MenuItem("Tools/Planet/Prop Placer")]
    public static void Open() => GetWindow<LobbyPropPlacerWindow>("Planet Prop Placer");

    // --- Target ---
    private GameObject _planet;
    private Transform _parent;
    [Tooltip("0 = auto from the planet renderer bounds.")]
    private float _radiusOverride = 0f;
    [Tooltip("The lobby planet has no classic collider (DOTS PhysicsShape), so analytic sphere snapping is the default. Enable only if you add a MeshCollider for true bumpy-terrain snapping.")]
    private bool _snapToColliders = false;

    // --- Prefab selection ---
    private GameObject _prefab;
    private string _paletteFolder = "Assets/_Prefabs/Lobby";
    private readonly List<GameObject> _palette = new();
    private Vector2 _paletteScroll;

    // --- Placement options ---
    private bool _alignToNormal = true;
    private float _surfaceOffset = 0f;
    private bool _randomYaw = false;
    private float _scaleJitter = 0f;

    // --- Scene state ---
    private bool _placing;
    private bool _hasHit;
    private Vector3 _hitPoint, _hitNormal;

    private void OnEnable()
    {
        if (_planet == null)
            AutoFindPlanet();
        RefreshPalette();
    }

    private void OnDisable() => StopPlacing();

    // ----------------------------------------------------------------- GUI

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        _planet = (GameObject)EditorGUILayout.ObjectField("Planet", _planet, typeof(GameObject), true);
        if (_planet == null)
        {
            EditorGUILayout.HelpBox("Assign the planet you want to decorate (its root, or any object under it). Auto-Find picks the planet defined by a PlanetDataAuthoring in the open scene(s).", MessageType.Warning);
            if (GUILayout.Button("Try Auto-Find Planet"))
                AutoFindPlanet();
        }
        else if (_planet.GetComponentInParent<PlanetDataAuthoring>(true) == null
                 && _planet.GetComponentInChildren<PlanetDataAuthoring>(true) == null)
        {
            EditorGUILayout.HelpBox("No PlanetDataAuthoring found on this object — falling back to the largest renderer's bounds for the surface sphere. Set 'Radius Override' if the detected radius looks wrong.", MessageType.Info);
        }
        _parent = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Place Under", "Optional parent for placed objects (e.g. a '--- Props ---' container in SC_Lobby)."),
            _parent, typeof(Transform), true);
        _radiusOverride = EditorGUILayout.FloatField(new GUIContent("Radius Override", "0 = auto from the planet renderer bounds."), _radiusOverride);
        _snapToColliders = EditorGUILayout.Toggle(new GUIContent("Snap To Colliders", "Raycast classic colliders first (true bumpy surface); falls back to the analytic sphere."), _snapToColliders);

        if (_planet != null && TryGetPlanetSphere(out _, out float r))
            EditorGUILayout.LabelField("Detected radius", r.ToString("0.##"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
        _prefab = (GameObject)EditorGUILayout.ObjectField("Active Prefab", _prefab, typeof(GameObject), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            _paletteFolder = EditorGUILayout.TextField("Palette Folder", _paletteFolder);
            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                RefreshPalette();
        }
        DrawPalette();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        _alignToNormal = EditorGUILayout.Toggle(new GUIContent("Align To Normal", "Stand the object up along the surface normal."), _alignToNormal);
        _surfaceOffset = EditorGUILayout.Slider(new GUIContent("Surface Offset", "Push along the normal (sink/raise)."), _surfaceOffset, -5f, 5f);
        _randomYaw = EditorGUILayout.Toggle(new GUIContent("Random Yaw", "Random spin around the normal on placement."), _randomYaw);
        _scaleJitter = EditorGUILayout.Slider(new GUIContent("Scale Jitter", "±% random scale on placement."), _scaleJitter, 0f, 1f);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_planet == null || _prefab == null))
        {
            if (!_placing)
            {
                if (GUILayout.Button("Start Placing  (click the planet in Scene)", GUILayout.Height(26)))
                    StartPlacing();
            }
            else
            {
                var bg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
                if (GUILayout.Button("● Placing… click to drop, Esc to stop", GUILayout.Height(26)))
                    StopPlacing();
                GUI.backgroundColor = bg;
            }
        }

        using (new EditorGUI.DisabledScope(_planet == null))
        {
            if (GUILayout.Button("Snap Selection To Surface"))
                SnapSelection();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Left-click the planet to drop the active prefab; keep clicking to place more. Esc or the button stops. 'Snap Selection' re-sticks the objects you have selected.", MessageType.Info);

        if (AssetPreview.IsLoadingAssetPreviews())
            Repaint();
    }

    private void DrawPalette()
    {
        if (_palette.Count == 0)
        {
            EditorGUILayout.LabelField("(no prefabs found in folder)");
            return;
        }

        const int cols = 4;
        _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(180));
        for (int i = 0; i < _palette.Count;)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int c = 0; c < cols && i < _palette.Count; c++, i++)
                {
                    var p = _palette[i];
                    var tex = AssetPreview.GetAssetPreview(p);
                    var content = new GUIContent("\n" + p.name, tex);
                    var bg = GUI.backgroundColor;
                    if (p == _prefab)
                        GUI.backgroundColor = Color.cyan;
                    if (GUILayout.Button(content, GUILayout.Width(82), GUILayout.Height(82)))
                        _prefab = p;
                    GUI.backgroundColor = bg;
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void RefreshPalette()
    {
        _palette.Clear();
        if (!AssetDatabase.IsValidFolder(_paletteFolder))
            return;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { _paletteFolder }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null)
                _palette.Add(go);
        }
    }

    private void AutoFindPlanet()
    {
        // Preferred: any planet defined by a PlanetDataAuthoring (works on every planet). If something
        // planet-ish is selected, prefer that one; otherwise take the first found.
        var authorings = FindObjectsByType<PlanetDataAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (authorings != null && authorings.Length > 0)
        {
            var sel = Selection.activeGameObject;
            if (sel != null)
            {
                foreach (var a in authorings)
                {
                    if (a.gameObject == sel || a.transform.IsChildOf(sel.transform) || sel.transform.IsChildOf(a.transform))
                    {
                        _planet = a.gameObject;
                        return;
                    }
                }
            }
            _planet = authorings[0].gameObject;
            return;
        }

        // Fallback: match by name (any planet, not just the lobby).
        foreach (var go in FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.name.Contains("Planet"))
            {
                _planet = go;
                return;
            }
        }
    }

    // ----------------------------------------------------------------- Scene placement

    private void StartPlacing()
    {
        if (_placing)
            return;
        _placing = true;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void StopPlacing()
    {
        if (!_placing)
            return;
        _placing = false;
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.RepaintAll();
    }

    private void OnSceneGUI(SceneView sv)
    {
        if (!_placing || _planet == null || _prefab == null)
            return;

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            StopPlacing();
            e.Use();
            return;
        }

        // Take control so clicks don't box-select / deselect.
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        _hasHit = TryGetMouseOnPlanet(out _hitPoint, out _hitNormal);
        if (e.type == EventType.MouseMove)
            sv.Repaint();

        if (!_hasHit)
            return;

        float size = HandleUtility.GetHandleSize(_hitPoint) * 0.3f;
        Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Handles.DrawWireDisc(_hitPoint, _hitNormal, size);
        Handles.DrawLine(_hitPoint, _hitPoint + _hitNormal * size * 2f);
        Handles.Label(_hitPoint + _hitNormal * size * 2.2f, _prefab.name);

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            PlaceAt(_hitPoint, _hitNormal);
            e.Use();
        }
    }

    private void PlaceAt(Vector3 point, Vector3 normal)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(_prefab);
        if (go == null)
            return;

        Undo.RegisterCreatedObjectUndo(go, "Place Lobby Prop");
        if (_parent != null)
            go.transform.SetParent(_parent, true);

        ApplySnap(go.transform, point, normal, applyJitter: true);

        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    private void SnapSelection()
    {
        if (!TryGetPlanetSphere(out Vector3 center, out float radius))
            return;

        var transforms = Selection.transforms;
        if (transforms == null || transforms.Length == 0)
            return;

        Undo.RecordObjects(transforms, "Snap To Planet Surface");
        foreach (var t in transforms)
        {
            if (!RaycastRadial(center, radius, t.position, out Vector3 point, out Vector3 normal))
            {
                Vector3 dir = t.position - center;
                normal = dir.sqrMagnitude < 1e-6f ? Vector3.up : dir.normalized;
                point = center + normal * radius;
            }

            ApplySnap(t, point, normal, applyJitter: false);
            EditorUtility.SetDirty(t);
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }

    /// <summary>Places <paramref name="t"/> on the surface and tilts it so its up matches the normal,
    /// preserving its heading (minimal rotation). Optional random yaw / scale jitter on placement.</summary>
    private void ApplySnap(Transform t, Vector3 point, Vector3 normal, bool applyJitter)
    {
        t.position = point + normal * _surfaceOffset;

        if (_alignToNormal)
        {
            Quaternion rotation = Quaternion.FromToRotation(t.up, normal) * t.rotation;
            if (applyJitter && _randomYaw)
                rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * rotation;
            t.rotation = rotation;
        }

        if (applyJitter && _scaleJitter > 0f)
            t.localScale *= 1f + Random.Range(-_scaleJitter, _scaleJitter);
    }

    // ----------------------------------------------------------------- Surface queries

    private bool TryGetMouseOnPlanet(out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);

        if (_snapToColliders && Physics.Raycast(ray, out RaycastHit hit, 100000f) && IsPlanetCollider(hit.collider))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        return TryGetPlanetSphere(out Vector3 center, out float radius)
            && RaySphere(ray, center, radius, out point, out normal);
    }

    /// <summary>Radial raycast onto the planet surface (for re-snapping an existing object beneath it).</summary>
    private bool RaycastRadial(Vector3 center, float radius, Vector3 from, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;
        if (!_snapToColliders)
            return false;

        Vector3 dir = from - center;
        if (dir.sqrMagnitude < 1e-6f)
            return false;
        dir.Normalize();

        Vector3 start = center + dir * (radius * 2f);
        if (Physics.Raycast(start, -dir, out RaycastHit hit, radius * 3f) && IsPlanetCollider(hit.collider))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }
        return false;
    }

    private bool IsPlanetCollider(Collider col)
    {
        if (col == null || _planet == null)
            return false;
        return col.transform == _planet.transform || col.transform.IsChildOf(_planet.transform);
    }

    private bool TryGetPlanetSphere(out Vector3 center, out float radius)
    {
        center = Vector3.zero;
        radius = 0f;
        if (_planet == null)
            return false;

        // Preferred source: the same PlanetDataAuthoring the game bakes from. This gives the exact
        // surface sphere for any planet regardless of how its renderers are nested (Volcanus in
        // particular has a nested renderer prefab that the old "first child renderer" logic missed).
        var authoring = _planet.GetComponent<PlanetDataAuthoring>()
                        ?? _planet.GetComponentInParent<PlanetDataAuthoring>(true)
                        ?? _planet.GetComponentInChildren<PlanetDataAuthoring>(true);

        if (authoring != null)
        {
            Unity.Mathematics.float3 c = authoring.WorldCenter;
            center = new Vector3(c.x, c.y, c.z);
            radius = _radiusOverride > 0f ? _radiusOverride : authoring.WorldRadius;
            return radius > 0f;
        }

        // Fallback: pick the largest renderer under the planet root (encompassing bounds), not just the
        // first one found. On multi-renderer prefabs (rings, decals, atmospheres) "first" is often wrong.
        center = _planet.transform.position;

        if (TryGetEncompassingRendererBounds(_planet, out Bounds b))
            center = b.center;

        if (_radiusOverride > 0f)
        {
            radius = _radiusOverride;
            return true;
        }

        if (b.size.sqrMagnitude > 0f)
        {
            Vector3 ext = b.extents;
            radius = Mathf.Max(ext.x, ext.y, ext.z);
        }
        return radius > 0f;
    }

    /// <summary>
    /// Returns an encompassing world-space bounds over the largest renderer(s) under <paramref name="root"/>.
    /// Picks the renderer with the biggest bounding volume as the "planet" and merges anything similarly-
    /// sized (within 20%) so multi-mesh planets (e.g. rocky shells) contribute their full extent.
    /// </summary>
    private static bool TryGetEncompassingRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        Renderer biggest = null;
        float biggestVol = -1f;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            Vector3 s = r.bounds.size;
            float vol = s.x * s.y * s.z;
            if (vol > biggestVol)
            {
                biggestVol = vol;
                biggest = r;
            }
        }

        if (biggest == null)
            return false;

        bounds = biggest.bounds;
        float threshold = biggestVol * 0.8f;
        foreach (var r in renderers)
        {
            if (r == null || r == biggest) continue;
            Vector3 s = r.bounds.size;
            float vol = s.x * s.y * s.z;
            if (vol >= threshold)
                bounds.Encapsulate(r.bounds);
        }
        return true;
    }

    /// <summary>Nearest ray-vs-sphere intersection (near side; far side if the camera is inside).</summary>
    private static bool RaySphere(Ray ray, Vector3 center, float radius, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        Vector3 m = ray.origin - center;
        float b = Vector3.Dot(m, ray.direction);
        float c = Vector3.Dot(m, m) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f)
            return false;

        float sqrt = Mathf.Sqrt(disc);
        float t = -b - sqrt;
        if (t < 0f)
            t = -b + sqrt; // origin inside the sphere → use the far hit
        if (t < 0f)
            return false;

        point = ray.origin + ray.direction * t;
        normal = (point - center).normalized;
        return true;
    }
}
