using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PA_Tools;

namespace Editor.HyvernoLeGigaBgDeLaStreet.PlanetCSG
{
    public class CPS_PlanetCSG : EditorWindow
    {
        [SerializeField] private TextAsset jsonFile;
        private GameObject planetObject;
        private bool planetAutoRadiusDetector = true;
        private float planetRadius = 10f;
        private int planetResolution = 40;
        private float deformationStrength = 2.0f;
        
        // New fields for overriding stamps
        private bool useDefaultStamp = false;
        private GameObject defaultStampPrefab;
        
        // Correction settings
        private bool snapToRadius = true;
        private bool swapYZ = false; // PA is often Z-up

        // Fields for PAS converter
        private string pasFilePath;

        [MenuItem("Tools/Planet CSG Generator")]
        public static void ShowWindow()
        {
            GetWindow<CPS_PlanetCSG>("Planet CSG");
        }

        private void OnGUI()
        {
            GUILayout.Label("Planet Generator", EditorStyles.boldLabel);

            planetObject = (GameObject)EditorGUILayout.ObjectField("Target Planet", planetObject, typeof(GameObject), true);
            
            GUILayout.Space(10);
            GUILayout.Label("Generation Settings", EditorStyles.label);
            GUILayout.Label("Generation Settings", EditorStyles.label);
            
            planetAutoRadiusDetector = EditorGUILayout.Toggle("Use Auto Radius Detector", planetAutoRadiusDetector);
            
            if (!planetAutoRadiusDetector)
                planetRadius = EditorGUILayout.FloatField("Radius", planetRadius);
            
            planetResolution = EditorGUILayout.IntField("Resolution", planetResolution);
            deformationStrength = EditorGUILayout.FloatField("Deformation Strength", deformationStrength);

            if (GUILayout.Button("1. Create CubeSphere Base"))
                CreateCubeSphere();

            GUILayout.Space(10);
            GUILayout.Label("CSG Configuration", EditorStyles.boldLabel);
            
            jsonFile = (TextAsset)EditorGUILayout.ObjectField("JSON File", jsonFile, typeof(TextAsset), false);

            GUILayout.Space(5);
            useDefaultStamp = EditorGUILayout.Toggle("Use Default Stamp", useDefaultStamp);
            if (useDefaultStamp)
            {
                defaultStampPrefab = (GameObject)EditorGUILayout.ObjectField("Default Stamp Prefab", defaultStampPrefab, typeof(GameObject), false);
                EditorGUILayout.HelpBox("All CSG operations will use this prefab instead of the one specified in JSON.", MessageType.Info);
            }
            
            GUILayout.Space(5);
            GUILayout.Label("Import Corrections", EditorStyles.boldLabel);
            snapToRadius = EditorGUILayout.Toggle("Snap to Planet Radius", snapToRadius);
            swapYZ = EditorGUILayout.Toggle("Swap Y/Z Coordinates", swapYZ);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("2. Visualize CSG Positions"))
            {
                VisualizeCSGPositions();
            }
            if (GUILayout.Button("3. Apply CSG to Mesh"))
            {
                ApplyCSG();
            }
            GUILayout.EndHorizontal();
            
            if (GUILayout.Button("Clear All"))
            {
                if (planetObject != null) DestroyImmediate(planetObject);
                GameObject debugRoot = GameObject.Find("CSG_Debug_Visuals");
                if (debugRoot != null) DestroyImmediate(debugRoot);
            }

            DrawPasConverter();
        }

        private void DrawPasConverter()
        {
            GUILayout.Space(20);
            GUILayout.Label("PAS to JSON Converter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Converts Planetary Annihilation .pas files to Unity-compatible .json by removing comments and trailing commas.", MessageType.Info);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select .pas File", GUILayout.Width(120)))
            {
                string path = EditorUtility.OpenFilePanel("Select .pas file", "", "pas");
                if (!string.IsNullOrEmpty(path))
                {
                    pasFilePath = path;
                }
            }
            GUILayout.Label(string.IsNullOrEmpty(pasFilePath) ? "No file selected" : Path.GetFileName(pasFilePath));
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(pasFilePath))
            {
                if (GUILayout.Button("Convert and Save as .json"))
                {
                    ConvertPasToJson();
                }
            }
        }

        private void ConvertPasToJson()
        {
            if (!File.Exists(pasFilePath))
            {
                Debug.LogError("File not found: " + pasFilePath);
                return;
            }

            try
            {
                string content = File.ReadAllText(pasFilePath);
                
                // 1. Remove Block comments /* ... */
                content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
                
                // 2. Remove Line comments // ...
                content = Regex.Replace(content, @"//.*", "");

                // 3. Remove Trailing commas
                content = Regex.Replace(content, @",\s*([\]}])", "$1");

                string newPath = Path.ChangeExtension(pasFilePath, ".json");
                File.WriteAllText(newPath, content);
                
                AssetDatabase.Refresh();
                Debug.Log("Successfully converted .pas to .json at: " + newPath);
                
                if (newPath.StartsWith(Application.dataPath))
                {
                    string relativePath = "Assets" + newPath.Substring(Application.dataPath.Length);
                    jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>(relativePath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error converting file: " + e.Message);
            }
        }

        private void CreateCubeSphere()
        {
            if (planetObject != null) DestroyImmediate(planetObject);

            GameObject sphereObj = new GameObject("CubeSphere");
            MeshFilter mf = sphereObj.AddComponent<MeshFilter>();
            MeshRenderer mr = sphereObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Standard"));

            Mesh mesh = GenerateCubeSphereMesh(planetRadius, planetResolution);
            mf.sharedMesh = mesh;
            planetObject = sphereObj;
            
            Selection.activeGameObject = sphereObj;
        }

        private Mesh GenerateCubeSphereMesh(float radius, int resolution)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();

            Vector3[] directions = {
                Vector3.up, Vector3.down,
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back
            };

            foreach (Vector3 dir in directions)
            {
                CreateFace(dir, radius, resolution, vertices, triangles, normals);
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.normals = normals.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateFace(Vector3 normal, float radius, int resolution, List<Vector3> vertices, List<int> triangles, List<Vector3> normals)
        {
            Vector3 axisA = new Vector3(normal.y, normal.z, normal.x);
            Vector3 axisB = Vector3.Cross(normal, axisA);

            int vStart = vertices.Count;

            for (int y = 0; y <= resolution; y++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    Vector2 percent = new Vector2(x, y) / resolution;
                    Vector3 pointOnCube = normal + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                    Vector3 pointOnSphere = pointOnCube.normalized * radius;
                    
                    vertices.Add(pointOnSphere);
                    normals.Add(pointOnSphere.normalized);
                }
            }

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = vStart + x + y * (resolution + 1);
                    
                    // Correct Clockwise Winding (Unity Default)
                    triangles.Add(i);
                    triangles.Add(i + resolution + 1);
                    triangles.Add(i + 1);

                    triangles.Add(i + 1);
                    triangles.Add(i + resolution + 1);
                    triangles.Add(i + resolution + 2);
                }
            }
        }

        private CPSPlanetData LoadData()
        {
            if (jsonFile == null)
            {
                Debug.LogError("No JSON file assigned!");
                return null;
            }

            try 
            {
                // Try to parse as CPSPlanetData directly
                CPSPlanetData data = JsonUtility.FromJson<CPSPlanetData>(jsonFile.text);
                
                // If it's a SolarSystem file, we might need to pick the first planet
                if (data != null || (data.planetCSG != null && data.planet != null))
                {
                    SolarSystemData system = JsonUtility.FromJson<SolarSystemData>(jsonFile.text);
                    if (system != null && system.planets != null && system.planets.Count > 0)
                    {
                        return system.planets[0];
                    }
                }

                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Failed to parse JSON: " + e.Message);
                return null;
            }
        }

        // like calc planet radius, all feat require in Visualize And Apply CSG
        private void CommunFunc()
        {
            if (planetAutoRadiusDetector)
            {
                var hitResult = Physics.Linecast(Vector3.up * 50000, planetObject.transform.position, out var hit, -1);
                // write simple debug.draw
                Debug.DrawLine(Vector3.up * 50000, Vector3.zero, Color.red, 15f);
                if (hitResult)
                    planetRadius = Vector3.Distance(hit.point, planetObject.transform.position);
                else 
                    Debug.LogWarning("please check if ur target have any collider");
            }
        }

        private void VisualizeCSGPositions()
        {
            CPSPlanetData data = LoadData();
            if (data == null || data.planetCSG == null) 
            {
                Debug.LogWarning("Data loaded but planetCSG is null or empty.");
                return;
            }

            CommunFunc();
            
            GameObject root = GameObject.Find("CSG_Debug_Visuals");
            if (root != null) DestroyImmediate(root);
            root = new GameObject("CSG_Debug_Visuals");

            foreach (var item in data.planetCSG)
            {
                GameObject debugObj;

                if (useDefaultStamp && defaultStampPrefab != null)
                {
                    debugObj = (GameObject)PrefabUtility.InstantiatePrefab(defaultStampPrefab);
                }
                else
                {
                    debugObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                }

                debugObj.name = item.spec;
                debugObj.transform.SetParent(root.transform);
                
                CSGOperationHelper op = new CSGOperationHelper(item);
                Matrix4x4 mat = op.GetMatrix(planetRadius, snapToRadius, swapYZ);
                
                debugObj.transform.position = mat.GetColumn(3);
                debugObj.transform.rotation = mat.rotation;
                debugObj.transform.localScale = mat.lossyScale;

                if (!useDefaultStamp)
                {
                    Renderer r = debugObj.GetComponent<Renderer>();
                    if (r != null)
                    {
                        if (IsSubtraction(item.spec))
                            r.sharedMaterial.color = new Color(1, 0, 0, 0.5f);
                        else
                            r.sharedMaterial.color = new Color(0, 1, 0, 0.5f);
                    }
                }
            }
        }

        private bool IsSubtraction(string specName)
        {
            if (string.IsNullOrEmpty(specName)) return false;
            string lower = specName.ToLower();
            return lower.Contains("crater") || lower.Contains("river") || lower.Contains("valley") || lower.Contains("trench") || lower.Contains("sub");
        }

        private void ApplyCSG()
        {
            if (planetObject == null)
            {
                Debug.LogError("No planet object selected!");
                return;
            }

            CommunFunc();

            CPSPlanetData data = LoadData();
            if (data == null || data.planetCSG == null) return;

            MeshFilter mf = planetObject.GetComponent<MeshFilter>();
            if (mf == null) return;

            Mesh mesh = mf.sharedMesh;
            Mesh newMesh = Instantiate(mesh);
            newMesh.name = "DeformedPlanetMesh";
            Vector3[] vertices = newMesh.vertices;
            
            List<Matrix4x4> inverseTransforms = new List<Matrix4x4>();
            List<bool> isSubtractions = new List<bool>();
            
            foreach(var item in data.planetCSG)
            {
                CSGOperationHelper op = new CSGOperationHelper(item);
                Matrix4x4 mat = op.GetMatrix(planetRadius, snapToRadius, swapYZ);
                inverseTransforms.Add(mat.inverse);
                isSubtractions.Add(IsSubtraction(item.spec));
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPt = planetObject.transform.TransformPoint(vertices[i]);
                
                for(int j=0; j<inverseTransforms.Count; j++)
                {
                    Matrix4x4 invMat = inverseTransforms[j];
                    Vector3 localPt = invMat.MultiplyPoint3x4(worldPt);
                    
                    if (Mathf.Abs(localPt.x) <= 0.5f && Mathf.Abs(localPt.y) <= 0.5f && Mathf.Abs(localPt.z) <= 0.5f)
                    {
                        bool isSubtraction = isSubtractions[j];
                        float direction = isSubtraction ? -1.0f : 1.0f;
                        
                        float dist = localPt.magnitude;
                        float falloff = Mathf.Clamp01(1.0f - (dist * 2.0f));
                        
                        vertices[i] += vertices[i].normalized * direction * deformationStrength * falloff;
                    }
                }
            }

            newMesh.vertices = vertices;
            newMesh.RecalculateNormals();
            newMesh.RecalculateBounds();
            mf.mesh = newMesh;
            
            Debug.Log("Applied CSG operations to mesh.");
        }
    }

    public class CSGOperationHelper
    {
        public CsgOperation item;
        public Matrix4x4 rawMatrix;

        public CSGOperationHelper(CsgOperation item)
        {
            this.item = item;
            if (item.transform != null && item.transform.Length == 16)
            {
                Matrix4x4 mat = new Matrix4x4();
                
                // Load Row-Major
                for(int i=0; i<4; i++)
                {
                    mat.SetRow(i, new Vector4(
                        item.transform[i*4 + 0], 
                        item.transform[i*4 + 1], 
                        item.transform[i*4 + 2], 
                        item.transform[i*4 + 3]
                    ));
                }
                this.rawMatrix = mat;
            }
            else if (item.position != null && item.position.Length == 3)
            {
                // Fallback if transform is missing but position exists
                this.rawMatrix = Matrix4x4.TRS(
                    new Vector3(item.position[0], item.position[1], item.position[2]),
                    Quaternion.identity,
                    Vector3.one
                );
            }
            else
            {
                this.rawMatrix = Matrix4x4.identity;
            }
        }
        
        public Matrix4x4 GetMatrix(float targetRadius, bool snapToRadius, bool swapYZ)
        {
            Vector3 pos = rawMatrix.GetColumn(3);
            Quaternion rot = rawMatrix.rotation;
            Vector3 scale = rawMatrix.lossyScale;
            
            if (swapYZ)
            {
                pos = new Vector3(pos.x, pos.z, pos.y);
                rot = Quaternion.Euler(90, 0, 0) * rot; 
            }
            
            if (snapToRadius)
            {
                if (pos.sqrMagnitude > 0.001f)
                {
                    pos = pos.normalized * targetRadius;
                }
            }
            
            return Matrix4x4.TRS(pos, rot, scale);
        }
    }
}

namespace PA_Tools 
{
    [System.Serializable]
    public class SolarSystemData
    {
        public string name;
        public List<CPSPlanetData> planets;
    }

    [System.Serializable]
    public class CPSPlanetData
    {
        public string name;
        public float mass;
        public float position_x;
        public float position_y;
        public float velocity_x;
        public float velocity_y;
        public bool starting_planet;
        public bool respawn;
        public List<CsgOperation> planetCSG;
        public PlanetGeneratorParams planet; 
    }

    [System.Serializable]
    public class CsgOperation
    {
        public string proj;
        public string spec;
        public float[] position; // Changed to float[] to support JSON array
        public float[] scale;    
        public float[] transform;
        public float[] weight;   
    }

    [System.Serializable]
    public class PlanetGeneratorParams
    {
        public int seed;
        public int radius;
        public int heightRange;
        public int waterHeight;
        public string biome;
        public string symmetryType;
    }
}