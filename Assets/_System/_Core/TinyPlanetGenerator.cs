using UnityEngine;
using System.Collections.Generic;
using System.IO;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteInEditMode]
public class TinyPlanetGenerator : MonoBehaviour
{
    [Header("Base Mesh Settings")]
    public MeshBaseType baseMeshType = MeshBaseType.Icosphere;
    [Range(1, 8)] public int resolution = 5;
    [Range(0.5f, 5f)] public float planetRadius = 1f;
    
    [Header("Cube Sphere Settings")]
    [Range(1, 6)] public int cubeSubdivisions = 3;
    public bool normalizeCubeVertices = true;
    
    [Header("Normal Settings")]
    public NormalCalculationMode normalMode = NormalCalculationMode.Auto;
    public bool invertNormals = false;
    
    [Header("Noise Settings")]
    public Vector3 seed = Vector3.zero;
    public List<NoiseLayer> noiseLayers = new List<NoiseLayer>();
    
    [Header("Baking Settings")]
    public bool baked = false;
    public Texture2D bakedHeightMap;
    public Texture2D bakedNormalMap;
    public int bakeTextureSize = 1024;
    public UVBakeMode uvBakeMode = UVBakeMode.Spherical;
    public float bakeHeightScale = 1.0f;
    
    [Header("Preview")]
    public bool autoUpdate = true;
    public bool showWireframe = false;
    public bool showNormals = false;
    [Range(0.1f, 1f)] public float normalsLength = 0.2f;
    
    private Mesh mesh;
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uvs;
    public Vector2[] bakedUVs;
    public int[] triangles;
    
    // Original data for revert
    private Vector3[] originalVertices;
    private Vector2[] originalUVs;
    private bool hasOriginalData = false;
    
    public enum MeshBaseType
    {
        Icosphere,
        Cube,
        UVSphere
    }
    
    public enum NormalCalculationMode
    {
        Auto,
        FaceNormals,
        VertexNormals,
        SphericalNormals
    }
    
    public enum UVBakeMode
    {
        Spherical,
        Equirectangular,
        Cubic,
        Planar
    }
    
    [System.Serializable]
    public class NoiseLayer
    {
        public bool enabled = true;
        public string layerName = "New Layer";
        public NoiseType noiseType = NoiseType.Perlin;
        [Range(0f, 2f)] public float amplitude = 0.1f;
        [Range(0.1f, 20f)] public float frequency = 1f;
        [Range(1, 8)] public int octaves = 1;
        [Range(0f, 1f)] public float persistence = 0.5f;
        [Range(0f, 5f)] public float lacunarity = 2f;
        [Range(0f, 1f)] public float maskThreshold = 0f;
        public Color debugColor = Color.white;
        
        public enum NoiseType
        {
            Perlin,
            Ridged,
            Voronoi,
            Billow,
            Simplex
        }
    }
    
    void OnValidate()
    {
        if (autoUpdate && !baked)
        {
            GeneratePlanet();
        }
    }
    
    void Start()
    {
        if (mesh == null)
        {
            GeneratePlanet();
        }
    }
    
    [ContextMenu("Generate Planet")]
    public void GeneratePlanet()
    {
        if (baked)
        {
            Debug.LogWarning("Planet is baked. Use 'Revert Bake' to modify.");
            return;
        }
        
        InitializeMesh();
        GenerateBaseMesh();
        ApplyNoiseLayers();
        CalculateNormals();
        GenerateUVs();
        UpdateMesh();
        
        // Update collider if exists
        MeshCollider collider = GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.sharedMesh = mesh;
        }
    }
    
    [ContextMenu("Bake Planet")]
    public void BakePlanet()
    {
        if (mesh == null || vertices == null || vertices.Length == 0)
        {
            Debug.LogError("No mesh to bake. Generate planet first.");
            return;
        }
        
        // Save original data for revert
        SaveOriginalData();
        
        // Generate proper baked UVs
        GenerateBakedUVs();
        
        // Generate height map
        bakedHeightMap = GenerateHeightMap();
        
        // Generate normal map from height map
        bakedNormalMap = GenerateNormalMap(bakedHeightMap);
        
        // Apply baked UVs to mesh
        mesh.uv = bakedUVs;
        
        // Update mesh with baked data
        UpdateMesh();
        
        baked = true;
        
        Debug.Log("Planet baked successfully. Heightmap and Normalmap generated.");
    }
    
    [ContextMenu("Revert Bake")]
    public void RevertBake()
    {
        if (!hasOriginalData)
        {
            Debug.LogWarning("No original data to revert to.");
            return;
        }
        
        // Restore original vertices and UVs
        vertices = originalVertices;
        bakedUVs = originalUVs;
        
        // Regenerate normals
        CalculateNormals();
        
        // Update mesh
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = originalUVs;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        
        baked = false;
        bakedHeightMap = null;
        bakedNormalMap = null;
        
        Debug.Log("Bake reverted to original state.");
    }
    
    void SaveOriginalData()
    {
        originalVertices = (Vector3[])vertices.Clone();
        originalUVs = (Vector2[])uvs.Clone();
        hasOriginalData = true;
    }
    
    void GenerateBakedUVs()
    {
        bakedUVs = new Vector2[vertices.Length];
        
        switch (uvBakeMode)
        {
            case UVBakeMode.Spherical:
                GenerateSphericalUVs(bakedUVs);
                break;
                
            case UVBakeMode.Equirectangular:
                GenerateEquirectangularUVs(bakedUVs);
                break;
                
            case UVBakeMode.Cubic:
                GenerateCubicUVs(bakedUVs);
                break;
                
            case UVBakeMode.Planar:
                GeneratePlanarUVs(bakedUVs);
                break;
        }
    }
    
    void GenerateSphericalUVs(Vector2[] targetUVs)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = vertices[i].normalized;
            
            // Spherical mapping
            float u = 0.5f + Mathf.Atan2(normal.z, normal.x) / (2f * Mathf.PI);
            float v = 0.5f - Mathf.Asin(normal.y) / Mathf.PI;
            
            // Fix seam wrapping
            if (u < 0.001f) u = 0.001f;
            if (u > 0.999f) u = 0.999f;
            if (v < 0.001f) v = 0.001f;
            if (v > 0.999f) v = 0.999f;
            
            targetUVs[i] = new Vector2(u, v);
        }
    }
    
    void GenerateEquirectangularUVs(Vector2[] targetUVs)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = vertices[i].normalized;
            
            // Equirectangular projection (better for textures)
            float u = Mathf.Atan2(normal.z, normal.x);
            u = (u + Mathf.PI) / (2f * Mathf.PI);
            
            float v = Mathf.Asin(normal.y);
            v = (v / Mathf.PI) + 0.5f;
            
            targetUVs[i] = new Vector2(u, v);
        }
    }
    
    void GenerateCubicUVs(Vector2[] targetUVs)
    {
        // For cube sphere, generate proper cubic UVs
        if (baseMeshType == MeshBaseType.Cube)
        {
            int vertsPerFace = vertices.Length / 6;
            for (int face = 0; face < 6; face++)
            {
                Vector3 faceNormal = GetFaceNormal(face);
                
                for (int i = 0; i < vertsPerFace; i++)
                {
                    int idx = face * vertsPerFace + i;
                    if (idx >= vertices.Length) break;
                    
                    Vector3 vertex = vertices[idx];
                    Vector3 localPos = Quaternion.Inverse(Quaternion.LookRotation(faceNormal)) * vertex;
                    
                    // Map to 0-1 within face bounds
                    float u = (localPos.x + planetRadius) / (2f * planetRadius);
                    float v = (localPos.y + planetRadius) / (2f * planetRadius);
                    
                    // Adjust for face arrangement
                    switch (face)
                    {
                        case 0: // +X
                            targetUVs[idx] = new Vector2(0.666f + u * 0.333f, 0.5f + v * 0.5f);
                            break;
                        case 1: // -X
                            targetUVs[idx] = new Vector2(u * 0.333f, 0.5f + v * 0.5f);
                            break;
                        case 2: // +Y
                            targetUVs[idx] = new Vector2(0.333f + u * 0.333f, v * 0.5f);
                            break;
                        case 3: // -Y
                            targetUVs[idx] = new Vector2(0.333f + u * 0.333f, 0.5f + v * 0.5f);
                            break;
                        case 4: // +Z
                            targetUVs[idx] = new Vector2(0.333f + u * 0.333f, 0.5f + v * 0.5f);
                            break;
                        case 5: // -Z
                            targetUVs[idx] = new Vector2(0.666f + u * 0.333f, v * 0.5f);
                            break;
                    }
                }
            }
        }
        else
        {
            // Fallback to spherical for other mesh types
            GenerateSphericalUVs(targetUVs);
        }
    }
    
    void GeneratePlanarUVs(Vector2[] targetUVs)
    {
        // Simple planar projection from top
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            float u = (vertex.x + planetRadius) / (2f * planetRadius);
            float v = (vertex.z + planetRadius) / (2f * planetRadius);
            targetUVs[i] = new Vector2(u, v);
        }
    }

    public Texture2D GenerateHeightMap()
    {
        Texture2D heightMap = new Texture2D(bakeTextureSize, bakeTextureSize, TextureFormat.RGBA32, false);
        
        // Find min and max height for normalization
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        
        foreach (Vector3 vertex in vertices)
        {
            float height = vertex.magnitude;
            if (height < minHeight) minHeight = height;
            if (height > maxHeight) maxHeight = height;
        }
        
        // Create height data grid
        float[,] heightGrid = new float[bakeTextureSize, bakeTextureSize];
        int[,] sampleCount = new int[bakeTextureSize, bakeTextureSize];
        
        // Initialize grids
        for (int x = 0; x < bakeTextureSize; x++)
        {
            for (int y = 0; y < bakeTextureSize; y++)
            {
                heightGrid[x, y] = 0;
                sampleCount[x, y] = 0;
            }
        }
        
        // Sample vertices into grid
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 uv = bakedUVs[i];
            int x = Mathf.FloorToInt(uv.x * (bakeTextureSize - 1));
            int y = Mathf.FloorToInt(uv.y * (bakeTextureSize - 1));
            
            x = Mathf.Clamp(x, 0, bakeTextureSize - 1);
            y = Mathf.Clamp(y, 0, bakeTextureSize - 1);
            
            float height = vertices[i].magnitude;
            heightGrid[x, y] += height;
            sampleCount[x, y]++;
        }
        
        // Average and normalize heights
        for (int x = 0; x < bakeTextureSize; x++)
        {
            for (int y = 0; y < bakeTextureSize; y++)
            {
                if (sampleCount[x, y] > 0)
                {
                    float avgHeight = heightGrid[x, y] / sampleCount[x, y];
                    float normalizedHeight = Mathf.Clamp01((avgHeight - minHeight) / (maxHeight - minHeight));
                    
                    // Apply height scale
                    normalizedHeight = Mathf.Pow(normalizedHeight, bakeHeightScale);
                    
                    Color color = new Color(normalizedHeight, normalizedHeight, normalizedHeight, 1f);
                    heightMap.SetPixel(x, y, color);
                }
                else
                {
                    // Fill empty pixels with nearest neighbor
                    heightMap.SetPixel(x, y, Color.black);
                }
            }
        }
        
        // Apply simple blur to smooth the heightmap
        heightMap = ApplyHeightmapBlur(heightMap, 1);
        
        heightMap.Apply();
        return heightMap;
    }
    
    Texture2D ApplyHeightmapBlur(Texture2D source, int iterations)
    {
        Texture2D blurred = new Texture2D(source.width, source.height, source.format, false);
        
        for (int i = 0; i < iterations; i++)
        {
            for (int x = 0; x < source.width; x++)
            {
                for (int y = 0; y < source.height; y++)
                {
                    Color sum = Color.black;
                    int count = 0;
                    
                    // 3x3 kernel
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = Mathf.Clamp(x + dx, 0, source.width - 1);
                            int ny = Mathf.Clamp(y + dy, 0, source.height - 1);
                            
                            sum += source.GetPixel(nx, ny);
                            count++;
                        }
                    }
                    
                    blurred.SetPixel(x, y, sum / count);
                }
            }
            
            // Swap for next iteration
            if (i < iterations - 1)
            {
                var temp = source;
                source = blurred;
                blurred = new Texture2D(source.width, source.height, source.format, false);
            }
        }
        
        blurred.Apply();
        return blurred;
    }

    public Texture2D GenerateNormalMap(Texture2D heightMap)
    {
        Texture2D normalMap = new Texture2D(heightMap.width, heightMap.height, TextureFormat.RGBA32, false);
        
        float strength = 1.0f;
        
        for (int x = 0; x < heightMap.width; x++)
        {
            for (int y = 0; y < heightMap.height; y++)
            {
                // Get heights from surrounding pixels
                float left = heightMap.GetPixel(Mathf.Max(x - 1, 0), y).grayscale;
                float right = heightMap.GetPixel(Mathf.Min(x + 1, heightMap.width - 1), y).grayscale;
                float up = heightMap.GetPixel(x, Mathf.Min(y + 1, heightMap.height - 1)).grayscale;
                float down = heightMap.GetPixel(x, Mathf.Max(y - 1, 0)).grayscale;
                
                // Calculate normal using Sobel filter
                float dX = (right - left) * strength;
                float dY = (up - down) * strength;
                
                Vector3 normal = new Vector3(-dX, -dY, 1).normalized;
                
                // Convert to color space (normal maps store X in R, Y in G, Z in B)
                Color normalColor = new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f,
                    1f
                );
                
                normalMap.SetPixel(x, y, normalColor);
            }
        }
        
        normalMap.Apply();
        return normalMap;
    }
    
    void InitializeMesh()
    {
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "Tiny Planet";
        }
        GetComponent<MeshFilter>().mesh = mesh;
    }
    
    void GenerateBaseMesh()
    {
        switch (baseMeshType)
        {
            case MeshBaseType.Icosphere:
                GenerateIcosphere();
                break;
            case MeshBaseType.Cube:
                GenerateCubeSphere();
                break;
            case MeshBaseType.UVSphere:
                GenerateUVSphere();
                break;
        }
    }
    
    void GenerateIcosphere()
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        
        verts.Add(new Vector3(-1, t, 0).normalized * planetRadius);
        verts.Add(new Vector3(1, t, 0).normalized * planetRadius);
        verts.Add(new Vector3(-1, -t, 0).normalized * planetRadius);
        verts.Add(new Vector3(1, -t, 0).normalized * planetRadius);
        
        verts.Add(new Vector3(0, -1, t).normalized * planetRadius);
        verts.Add(new Vector3(0, 1, t).normalized * planetRadius);
        verts.Add(new Vector3(0, -1, -t).normalized * planetRadius);
        verts.Add(new Vector3(0, 1, -t).normalized * planetRadius);
        
        verts.Add(new Vector3(t, 0, -1).normalized * planetRadius);
        verts.Add(new Vector3(t, 0, 1).normalized * planetRadius);
        verts.Add(new Vector3(-t, 0, -1).normalized * planetRadius);
        verts.Add(new Vector3(-t, 0, 1).normalized * planetRadius);
        
        int[] icoTris = {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        
        tris.AddRange(icoTris);
        
        for (int i = 0; i < resolution; i++)
        {
            List<int> newTris = new List<int>();
            Dictionary<string, int> middlePointCache = new Dictionary<string, int>();
            
            for (int j = 0; j < tris.Count; j += 3)
            {
                int a = tris[j];
                int b = tris[j + 1];
                int c = tris[j + 2];
                
                int ab = GetMiddlePoint(a, b, verts, middlePointCache, planetRadius);
                int bc = GetMiddlePoint(b, c, verts, middlePointCache, planetRadius);
                int ca = GetMiddlePoint(c, a, verts, middlePointCache, planetRadius);
                
                newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
            }
            
            tris = newTris;
        }
        
        vertices = verts.ToArray();
        triangles = tris.ToArray();
    }
    
    void GenerateCubeSphere()
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvsList = new List<Vector2>();
        
        for (int face = 0; face < 6; face++)
        {
            Vector3 normal = GetFaceNormal(face);
            Vector3 axisA = new Vector3(normal.y, normal.z, normal.x);
            Vector3 axisB = Vector3.Cross(normal, axisA);
            
            int gridSize = 1 << cubeSubdivisions;
            int faceStartIndex = verts.Count;
            
            for (int y = 0; y <= gridSize; y++)
            {
                for (int x = 0; x <= gridSize; x++)
                {
                    Vector2 percent = new Vector2(x, y) / gridSize;
                    Vector3 pointOnUnitCube = normal + (percent.x - 0.5f) * 2 * axisA + (percent.y - 0.5f) * 2 * axisB;
                    
                    if (normalizeCubeVertices)
                    {
                        pointOnUnitCube = pointOnUnitCube.normalized;
                    }
                    
                    verts.Add(pointOnUnitCube * planetRadius);
                    
                    Vector2 uv = new Vector2(
                        (face % 3 == 0 ? percent.x : face % 3 == 1 ? 1 - percent.y : percent.x),
                        (face < 3 ? percent.y : 1 - percent.x)
                    );
                    uvsList.Add(uv);
                }
            }
            
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    int i = faceStartIndex + x + y * (gridSize + 1);
                    
                    tris.Add(i);
                    tris.Add(i + gridSize + 1);
                    tris.Add(i + gridSize + 2);
                    
                    tris.Add(i);
                    tris.Add(i + gridSize + 2);
                    tris.Add(i + 1);
                }
            }
        }
        
        vertices = verts.ToArray();
        triangles = tris.ToArray();
        uvs = uvsList.ToArray();
    }
    
    void GenerateUVSphere()
    {
        int segments = Mathf.Max(3, resolution * 4);
        int rings = Mathf.Max(2, resolution * 2);
        
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvsList = new List<Vector2>();
        
        for (int ring = 0; ring <= rings; ring++)
        {
            float v = (float)ring / rings;
            float phi = v * Mathf.PI;
            
            for (int segment = 0; segment <= segments; segment++)
            {
                float u = (float)segment / segments;
                float theta = u * 2 * Mathf.PI;
                
                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Cos(phi);
                float z = Mathf.Sin(phi) * Mathf.Sin(theta);
                
                verts.Add(new Vector3(x, y, z) * planetRadius);
                uvsList.Add(new Vector2(u, v));
            }
        }
        
        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int current = ring * (segments + 1) + segment;
                int next = current + segments + 1;
                
                tris.Add(current);
                tris.Add(next + 1);
                tris.Add(next);
                
                tris.Add(current);
                tris.Add(current + 1);
                tris.Add(next + 1);
            }
        }
        
        vertices = verts.ToArray();
        triangles = tris.ToArray();
        uvs = uvsList.ToArray();
    }
    
    Vector3 GetFaceNormal(int face)
    {
        switch (face)
        {
            case 0: return Vector3.up;
            case 1: return Vector3.down;
            case 2: return Vector3.left;
            case 3: return Vector3.right;
            case 4: return Vector3.forward;
            case 5: return Vector3.back;
            default: return Vector3.up;
        }
    }
    
    int GetMiddlePoint(int p1, int p2, List<Vector3> vertices, Dictionary<string, int> cache, float radius)
    {
        string key = p1 < p2 ? p1 + "_" + p2 : p2 + "_" + p1;
        
        if (cache.TryGetValue(key, out int ret))
        {
            return ret;
        }
        
        Vector3 point1 = vertices[p1];
        Vector3 point2 = vertices[p2];
        Vector3 middle = Vector3.Lerp(point1, point2, 0.5f).normalized * radius;
        
        vertices.Add(middle);
        cache.Add(key, vertices.Count - 1);
        
        return vertices.Count - 1;
    }
    
    void CalculateNormals()
    {
        normals = new Vector3[vertices.Length];
        
        switch (normalMode)
        {
            case NormalCalculationMode.FaceNormals:
                CalculateFaceNormals();
                break;
                
            case NormalCalculationMode.VertexNormals:
                CalculateVertexNormals();
                break;
                
            case NormalCalculationMode.SphericalNormals:
                CalculateSphericalNormals();
                break;
                
            case NormalCalculationMode.Auto:
            default:
                CalculateAutoNormals();
                break;
        }
        
        if (invertNormals)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i];
            }
        }
    }
    
    void CalculateFaceNormals()
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            
            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];
            
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            
            normals[i0] = normal;
            normals[i1] = normal;
            normals[i2] = normal;
        }
    }
    
    void CalculateVertexNormals()
    {
        Vector3[] tempNormals = new Vector3[vertices.Length];
        
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            
            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];
            
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            
            tempNormals[i0] += normal;
            tempNormals[i1] += normal;
            tempNormals[i2] += normal;
        }
        
        for (int i = 0; i < tempNormals.Length; i++)
        {
            normals[i] = tempNormals[i].normalized;
        }
    }
    
    void CalculateSphericalNormals()
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            normals[i] = vertices[i].normalized;
        }
    }
    
    void CalculateAutoNormals()
    {
        if (baseMeshType == MeshBaseType.Cube && !normalizeCubeVertices)
        {
            CalculateFaceNormals();
        }
        else
        {
            CalculateSphericalNormals();
        }
    }
    
    void ApplyNoiseLayers()
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            Vector3 normalizedPos = vertex.normalized;
            float originalDistance = vertex.magnitude;
            float noiseValue = 0f;
            
            foreach (NoiseLayer layer in noiseLayers)
            {
                if (!layer.enabled) continue;
                
                float layerNoise = 0f;
                float amplitude = 1f;
                float frequency = layer.frequency;
                
                for (int oct = 0; oct < layer.octaves; oct++)
                {
                    Vector3 samplePoint = normalizedPos * frequency + seed;
                    float sample = 0f;
                    
                    switch (layer.noiseType)
                    {
                        case NoiseLayer.NoiseType.Perlin:
                            sample = Perlin3D(samplePoint);
                            break;
                        case NoiseLayer.NoiseType.Ridged:
                            sample = 1f - Mathf.Abs(Perlin3D(samplePoint));
                            break;
                        case NoiseLayer.NoiseType.Voronoi:
                            sample = VoronoiNoise(samplePoint);
                            break;
                        case NoiseLayer.NoiseType.Billow:
                            sample = Mathf.Abs(Perlin3D(samplePoint));
                            break;
                        case NoiseLayer.NoiseType.Simplex:
                            sample = SimplexNoise3D(samplePoint);
                            break;
                    }
                    
                    layerNoise += sample * amplitude;
                    amplitude *= layer.persistence;
                    frequency *= layer.lacunarity;
                }
                
                if (layerNoise > layer.maskThreshold)
                {
                    noiseValue += layerNoise * layer.amplitude;
                }
            }
            
            vertices[i] = normalizedPos * (originalDistance + noiseValue);
        }
    }
    
    float Perlin3D(Vector3 point)
    {
        float xy = Mathf.PerlinNoise(point.x, point.y);
        float xz = Mathf.PerlinNoise(point.x, point.z);
        float yz = Mathf.PerlinNoise(point.y, point.z);
        
        return (xy + xz + yz) / 3f;
    }
    
    float SimplexNoise3D(Vector3 point)
    {
        float noise = 0f;
        float scale = 1f;
        
        for (int i = 0; i < 3; i++)
        {
            noise += Mathf.PerlinNoise(point.x * scale, point.y * scale) * 
                     Mathf.PerlinNoise(point.y * scale, point.z * scale) * 
                     Mathf.PerlinNoise(point.z * scale, point.x * scale);
            scale *= 2f;
        }
        
        return noise / 3f;
    }
    
    float VoronoiNoise(Vector3 point)
    {
        Vector3 i = new Vector3(
            Mathf.Floor(point.x),
            Mathf.Floor(point.y),
            Mathf.Floor(point.z)
        );
        
        Vector3 f = new Vector3(
            point.x - i.x,
            point.y - i.y,
            point.z - i.z
        );
        
        float minDist = float.MaxValue;
        
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 neighbor = new Vector3(x, y, z);
                    Vector3 random = RandomVector3(i + neighbor);
                    Vector3 diff = neighbor + random - f;
                    float dist = diff.sqrMagnitude;
                    
                    if (dist < minDist)
                    {
                        minDist = dist;
                    }
                }
            }
        }
        
        return Mathf.Clamp01(Mathf.Sqrt(minDist));
    }
    
    Vector3 RandomVector3(Vector3 p)
    {
        return new Vector3(
            RandomValue(p.x, p.y),
            RandomValue(p.y, p.z),
            RandomValue(p.z, p.x)
        );
    }
    
    float RandomValue(float x, float y)
    {
        return Mathf.Abs(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f) % 1f;
    }
    
    void GenerateUVs()
    {
        if (baseMeshType == MeshBaseType.Cube && uvs != null && uvs.Length == vertices.Length)
            return;
            
        uvs = new Vector2[vertices.Length];
        
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 normal = vertices[i].normalized;
            
            float u = 0.5f + Mathf.Atan2(normal.z, normal.x) / (2f * Mathf.PI);
            float v = 0.5f - Mathf.Asin(normal.y) / Mathf.PI;
            
            uvs[i] = new Vector2(u, v);
        }
    }
    
    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        
        if (baked && bakedUVs != null)
        {
            mesh.uv = bakedUVs;
        }
        else if (uvs != null && uvs.Length == vertices.Length)
        {
            mesh.uv = uvs;
        }
        else
        {
            GenerateUVs();
            mesh.uv = uvs;
        }
        
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        
        if (showWireframe)
        {
            GetComponent<MeshRenderer>().material = new Material(Shader.Find("Unlit/Color"))
            {
                color = Color.gray
            };
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (vertices == null || !showWireframe) return;
        
        Gizmos.color = Color.white;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v1 = transform.TransformPoint(vertices[triangles[i]]);
            Vector3 v2 = transform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 v3 = transform.TransformPoint(vertices[triangles[i + 2]]);
            
            Gizmos.DrawLine(v1, v2);
            Gizmos.DrawLine(v2, v3);
            Gizmos.DrawLine(v3, v1);
        }
        
        if (showNormals && vertices != null && normals != null)
        {
            Gizmos.color = Color.blue;
            for (int i = 0; i < vertices.Length; i += Mathf.Max(1, vertices.Length / 100))
            {
                Vector3 start = transform.TransformPoint(vertices[i]);
                Vector3 end = start + transform.TransformDirection(normals[i]) * normalsLength;
                Gizmos.DrawLine(start, end);
            }
        }
    }
    
    public int GetVertexCount()
    {
        return vertices != null ? vertices.Length : 0;
    }
    
    public int GetTriangleCount()
    {
        return triangles != null ? triangles.Length / 3 : 0;
    }
    
    public Mesh GetMesh()
    {
        return mesh;
    }
    
    [ContextMenu("Reset to Default")]
    public void ResetToDefault()
    {
        baseMeshType = MeshBaseType.Icosphere;
        resolution = 5;
        planetRadius = 1f;
        cubeSubdivisions = 3;
        normalizeCubeVertices = true;
        normalMode = NormalCalculationMode.Auto;
        invertNormals = false;
        seed = Vector3.zero;
        baked = false;
        bakeTextureSize = 1024;
        uvBakeMode = UVBakeMode.Spherical;
        bakeHeightScale = 1.0f;
        autoUpdate = true;
        showWireframe = false;
        showNormals = false;
        normalsLength = 0.2f;
        
        bakedHeightMap = null;
        bakedNormalMap = null;
        hasOriginalData = false;
        
        noiseLayers.Clear();
        
        noiseLayers.Add(new NoiseLayer
        {
            enabled = true,
            layerName = "Base Terrain",
            noiseType = NoiseLayer.NoiseType.Perlin,
            amplitude = 0.15f,
            frequency = 2f,
            octaves = 3,
            persistence = 0.5f,
            lacunarity = 2f,
            maskThreshold = 0f,
            debugColor = new Color(0.4f, 0.6f, 0.3f)
        });
        
        GeneratePlanet();
    }
    
    [ContextMenu("Save Textures to Files")]
    public void SaveTexturesToFiles()
    {
        if (bakedHeightMap == null || bakedNormalMap == null)
        {
            Debug.LogError("No baked textures to save. Bake the planet first.");
            return;
        }
        
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderPath = Path.Combine(Application.dataPath, "GeneratedPlanets", $"Planet_{timestamp}");
        
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        
        // Save heightmap
        byte[] heightmapBytes = bakedHeightMap.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(folderPath, $"HeightMap_{timestamp}.png"), heightmapBytes);
        
        // Save normalmap
        byte[] normalmapBytes = bakedNormalMap.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(folderPath, $"NormalMap_{timestamp}.png"), normalmapBytes);
        
        // Save mesh
        string meshPath = Path.Combine(folderPath, $"Mesh_{timestamp}.obj");
        SaveMeshAsOBJ(meshPath);
        
        // Save settings
        string settingsPath = Path.Combine(folderPath, $"Settings_{timestamp}.json");
        SaveSettings(settingsPath);
        
        Debug.Log($"Textures and mesh saved to: {folderPath}");
    }
    
    void SaveMeshAsOBJ(string path)
    {
        using (StreamWriter sw = new StreamWriter(path))
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
            
            Vector2[] currentUVs = baked ? bakedUVs : uvs;
            foreach (Vector2 uv in currentUVs)
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
    
    void SaveSettings(string path)
    {
        PlanetSettings settings = new PlanetSettings
        {
            baseMeshType = baseMeshType.ToString(),
            resolution = resolution,
            planetRadius = planetRadius,
            cubeSubdivisions = cubeSubdivisions,
            normalizeCubeVertices = normalizeCubeVertices,
            normalMode = normalMode.ToString(),
            invertNormals = invertNormals,
            seed = seed,
            bakeTextureSize = bakeTextureSize,
            uvBakeMode = uvBakeMode.ToString(),
            bakeHeightScale = bakeHeightScale,
            noiseLayers = new List<SerializableNoiseLayer>()
        };
        
        foreach (NoiseLayer layer in noiseLayers)
        {
            settings.noiseLayers.Add(new SerializableNoiseLayer
            {
                name = layer.layerName,
                enabled = layer.enabled,
                noiseType = layer.noiseType.ToString(),
                amplitude = layer.amplitude,
                frequency = layer.frequency,
                octaves = layer.octaves,
                persistence = layer.persistence,
                lacunarity = layer.lacunarity,
                maskThreshold = layer.maskThreshold
            });
        }
        
        string json = JsonUtility.ToJson(settings, true);
        File.WriteAllText(path, json);
    }
    
    [System.Serializable]
    private class PlanetSettings
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
        public List<SerializableNoiseLayer> noiseLayers;
    }
    
    [System.Serializable]
    private class SerializableNoiseLayer
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
    }
}