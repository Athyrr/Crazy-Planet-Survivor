using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WFCContent.World;

namespace Editor
{
    // formes booleennes (WFCPlanetShape): creation depuis le menu GameObject/WFC (ou clic droit dans la hierarchie), et
    // recalcul de la planete quand une forme a bouge puis a ete lachee (meme interrupteur que la live preview des regles)
    [InitializeOnLoad]
    public static class WFCShapeTools
    {
        private const double PollSeconds = 0.1;
        // la forme ne bouge plus depuis ce temps (et la souris est lachee): on recalcule
        private const double SettleSeconds = 0.35;
        // taille d'une nouvelle forme, en aretes de la grille
        private const float NewShapeEdges = 3.5f;

        // signature des formes avec laquelle chaque planete a ete construite
        private static readonly Dictionary<GoldbergPolyhedron, int> built = new();
        private static readonly Dictionary<GoldbergPolyhedron, (int signature, double since)> pending = new();
        private static double nextPoll;
        private static bool seeded;

        static WFCShapeTools()
        {
            EditorApplication.update += Tick;
            EditorSceneManager.sceneOpened += (_, _) => Reset();
            EditorSceneManager.sceneClosed += _ => Reset();
            EditorApplication.playModeStateChanged += _ => Reset();
        }

        private static void Reset()
        {
            built.Clear();
            pending.Clear();
            seeded = false;
        }

        #region Live preview

        private static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll)
                return;
            nextPoll = now + PollSeconds;

            // etat de depart: les planetes telles qu'elles sont dans la scene (pas de recalcul a l'ouverture)
            if (!seeded)
            {
                foreach (var planet in Object.FindObjectsByType<GoldbergPolyhedron>())
                    built[planet] = WFCPlanetShape.Signature(planet);
                seeded = true;
                return;
            }

            var planets = new HashSet<GoldbergPolyhedron>(WFCPlanetShape.Active.Where(s => s != null).Select(s => s.Planet).Where(p => p != null));
            foreach (var planet in built.Keys.ToList())
            {
                if (planet == null)
                {
                    built.Remove(planet);
                    pending.Remove(planet);
                }
                else
                    planets.Add(planet);
            }

            foreach (var planet in planets)
            {
                int signature = WFCPlanetShape.Signature(planet);
                if (!built.TryGetValue(planet, out int last))
                {
                    built[planet] = signature;  // planete apparue (copie, nouvel objet): deja dans son etat
                    continue;
                }
                if (signature == last)
                {
                    pending.Remove(planet);
                    continue;
                }
                if (!pending.TryGetValue(planet, out var wait) || wait.signature != signature)
                {
                    pending[planet] = (signature, now);
                    continue;
                }
                // la forme est encore tenue (gizmo, champ de l'inspector en cours de glisse)
                if (now - wait.since < SettleSeconds || GUIUtility.hotControl != 0)
                    continue;

                pending.Remove(planet);
                built[planet] = signature;
                if (!WFCLivePreview.Enabled || !planet.isActiveAndEnabled)
                    continue;
                try
                {
                    planet.Generate();
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e, planet);
                }
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region Creation

        [MenuItem("GameObject/WFC/Trou (sphere)", false, 10)]
        private static void HoleSphere() => Create(WFCPlanetShape.Kind.Sphere, WFCPlanetShape.Operation.Subtract, "Trou");

        [MenuItem("GameObject/WFC/Trou (cylindre)", false, 11)]
        private static void HoleCylinder() => Create(WFCPlanetShape.Kind.Cylinder, WFCPlanetShape.Operation.Subtract, "Trou");

        [MenuItem("GameObject/WFC/Trou (boite)", false, 12)]
        private static void HoleBox() => Create(WFCPlanetShape.Kind.Box, WFCPlanetShape.Operation.Subtract, "Trou");

        [MenuItem("GameObject/WFC/Plateau (sphere)", false, 13)]
        private static void PlateauSphere() => Create(WFCPlanetShape.Kind.Sphere, WFCPlanetShape.Operation.Union, "Plateau");

        [MenuItem("GameObject/WFC/Trou (sphere)", true)]
        [MenuItem("GameObject/WFC/Trou (cylindre)", true)]
        [MenuItem("GameObject/WFC/Trou (boite)", true)]
        [MenuItem("GameObject/WFC/Plateau (sphere)", true)]
        private static bool CanCreate() => !EditorApplication.isPlaying && Object.FindAnyObjectByType<GoldbergPolyhedron>() != null;

        // nouvelle forme, enfant de la planete selectionnee (sinon la plus proche du centre de la vue), posee sur la surface
        // sous le centre de la Scene, axe Y le long de la verticale de la planete
        private static void Create(WFCPlanetShape.Kind kind, WFCPlanetShape.Operation operation, string name)
        {
            var view = SceneView.lastActiveSceneView;
            var planet = TargetPlanet(view);
            if (planet == null)
                return;

            var tr = planet.transform;
            var center = tr.position;
            float scale = tr.lossyScale.x;
            var grid = planet.Grid;
            float surface = grid.Radius * scale;
            var direction = Vector3.up;
            if (view != null)
            {
                var cam = view.camera.transform;
                direction = HitSphere(cam.position, cam.forward, center, surface, out var hit)
                    ? (hit - center).normalized
                    : (cam.position - center).normalized;
            }

            var go = new GameObject($"{name} ({kind})");
            Undo.RegisterCreatedObjectUndo(go, "WFC shape");
            Undo.SetTransformParent(go.transform, tr, "WFC shape");
            go.transform.SetPositionAndRotation(center + direction * surface, Quaternion.FromToRotation(Vector3.up, direction));
            go.transform.localScale = Vector3.one * (grid.MeanEdge * NewShapeEdges);  // repere de la planete: deja a son echelle
            var shape = Undo.AddComponent<WFCPlanetShape>(go);
            shape.kind = kind;
            shape.operation = operation;
            shape.height = operation == WFCPlanetShape.Operation.Union ? planet.Floors : 0;
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        private static GoldbergPolyhedron TargetPlanet(SceneView view)
        {
            var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<GoldbergPolyhedron>() : null;
            if (selected != null)
                return selected;
            var planets = Object.FindObjectsByType<GoldbergPolyhedron>();
            if (planets.Length == 0)
                return null;
            var pivot = view != null ? view.pivot : Vector3.zero;
            return planets.OrderBy(p => (p.transform.position - pivot).sqrMagnitude).First();
        }

        private static bool HitSphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out Vector3 hit)
        {
            var oc = origin - center;
            float b = Vector3.Dot(oc, direction);
            float c = oc.sqrMagnitude - radius * radius;
            float disc = b * b - c;
            hit = Vector3.zero;
            if (disc < 0f)
                return false;
            float t = -b - Mathf.Sqrt(disc);
            if (t < 0f)
                t = -b + Mathf.Sqrt(disc);
            if (t < 0f)
                return false;
            hit = origin + direction * t;
            return true;
        }

        #endregion
    }
}
