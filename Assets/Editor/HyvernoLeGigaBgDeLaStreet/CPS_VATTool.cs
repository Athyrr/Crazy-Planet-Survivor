using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class FixedVATTool : EditorWindow
{
    private const int MAX_TEXTURE_SIZE = 4096;
    
    [SerializeField] private AnimationClip clip;
    [SerializeField] private GameObject targetObject;
    [SerializeField] private SkinnedMeshRenderer skinnedMeshRenderer;
    [SerializeField] private float samplingRate = 60.0f;
    [SerializeField] private bool enforcePowerOfTwo = true;
    
    private bool hasResults = false;
    private Texture2D lastGeneratedTexture;
    private float lastDuration;
    private Vector3 lastMinBounds;
    private Vector3 lastMaxBounds;
    
    [MenuItem("Tools/CPS VAT Tool (by brrbrrpatapims)")]
    static void Init()
    {
        var window = GetWindow<FixedVATTool>("VAT Generator (by brrbrrpatapims)");
        window.minSize = new Vector2(400, 500);
        window.Show();
    }
    
    private void OnEnable()
    {
        // Load saved values
        var data = EditorPrefs.GetString("VATTool_Settings", JsonUtility.ToJson(this, false));
        if (!string.IsNullOrEmpty(data))
            JsonUtility.FromJsonOverwrite(data, this);
    }
    
    private void OnDisable()
    {
        // Save settings
        EditorPrefs.SetString("VATTool_Settings", JsonUtility.ToJson(this, false));
    }
    
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Vertex Animation Texture Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        
        // Input fields with validation
        clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", clip, typeof(AnimationClip), false);
        targetObject = (GameObject)EditorGUILayout.ObjectField("Animated Object", targetObject, typeof(GameObject), true);
        
        // Auto-fill skinned mesh renderer if target object is provided
        if (targetObject != null && skinnedMeshRenderer == null)
        {
            skinnedMeshRenderer = targetObject.GetComponentInChildren<SkinnedMeshRenderer>();
        }
        
        skinnedMeshRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
            "Skinned Mesh Renderer", skinnedMeshRenderer, typeof(SkinnedMeshRenderer), true);
        
        // Validate the skinned mesh renderer belongs to the target object
        if (skinnedMeshRenderer != null && targetObject != null)
        {
            if (!skinnedMeshRenderer.transform.IsChildOf(targetObject.transform) && skinnedMeshRenderer.gameObject != targetObject)
            {
                EditorGUILayout.HelpBox("Skinned Mesh Renderer does not belong to the target GameObject!", MessageType.Warning);
            }
        }
        
        EditorGUILayout.Space();
        samplingRate = Mathf.Max(1.0f, EditorGUILayout.FloatField("Sampling Rate (FPS)", samplingRate));
        enforcePowerOfTwo = EditorGUILayout.Toggle("Enforce Power of Two", enforcePowerOfTwo);
        
        EditorGUILayout.Space();
        
        // Validation and Generate button
        bool isValid = ValidateInputs();
        GUI.enabled = isValid;
        
        if (GUILayout.Button("Generate Vertex Animation Texture", GUILayout.Height(40)))
        {
            GenerateVAT();
        }

        if (GUILayout.Button("Generate Animated Material (only if not exist in project pls UwU)", GUILayout.Height(40)))
        {
            GenerateExampleShader();    
        }
        
        GUI.enabled = true;
        
        if (!isValid)
        {
            EditorGUILayout.HelpBox(GetValidationMessage(), MessageType.Info);
        }
        
        EditorGUILayout.Space(20);
        
        // Results section
        if (hasResults)
        {
            DrawResults();
        }
    }
    
    private bool ValidateInputs()
    {
        if (clip == null) return false;
        if (targetObject == null) return false;
        if (skinnedMeshRenderer == null) return false;
        if (skinnedMeshRenderer.sharedMesh == null) return false;
        if (samplingRate <= 0) return false;
        
        // Check if object has necessary components
        var animator = targetObject.GetComponent<Animator>();
        var animation = targetObject.GetComponent<Animation>();
        if (animator == null && animation == null)
        {
            return false;
        }
        
        return true;
    }
    
    private string GetValidationMessage()
    {
        if (clip == null) return "Assign an Animation Clip";
        if (targetObject == null) return "Assign a GameObject";
        if (skinnedMeshRenderer == null) return "Assign a Skinned Mesh Renderer";
        if (skinnedMeshRenderer.sharedMesh == null) return "Skinned Mesh has no mesh data";
        if (samplingRate <= 0) return "Sampling rate must be positive";
        
        var animator = targetObject.GetComponent<Animator>();
        var animation = targetObject.GetComponent<Animation>();
        if (animator == null && animation == null) return "GameObject needs Animator or Animation component";
        
        return "All inputs are valid";
    }
    
    private void GenerateVAT()
    {
        try
        {
            // Calculate texture dimensions
            int vertexCount = skinnedMeshRenderer.sharedMesh.vertexCount;
            int frameCount = Mathf.CeilToInt(clip.length * samplingRate);
            
            int textureHeight = vertexCount;
            int textureWidth = frameCount;
            
            if (enforcePowerOfTwo)
            {
                textureHeight = NextPowerOfTwo(textureHeight);
                textureWidth = NextPowerOfTwo(textureWidth);
            }
            
            // Validate texture size
            if (textureWidth > MAX_TEXTURE_SIZE || textureHeight > MAX_TEXTURE_SIZE)
            {
                EditorUtility.DisplayDialog("Error", 
                    $"Texture size would be {textureWidth}x{textureHeight}, exceeding maximum of {MAX_TEXTURE_SIZE}x{MAX_TEXTURE_SIZE}.\n" +
                    "Reduce sampling rate, animation length, or vertex count.", "OK");
                return;
            }
            
            // Get rest pose vertices (in world space)
            Vector3[] restPoseVertices = GetRestPoseVertices();
            
            // First pass: Calculate bounds
            EditorUtility.DisplayProgressBar("Generating VAT", "Calculating bounds...", 0.1f);
            CalculateBounds(restPoseVertices, out Vector3 minBounds, out Vector3 maxBounds);
            
            // Create texture
            Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBAFloat, false);
            
            // Fill texture with initial color (zero offset)
            Color[] clearColors = new Color[textureWidth * textureHeight];
            for (int i = 0; i < clearColors.Length; i++)
            {
                clearColors[i] = Color.black;
            }
            texture.SetPixels(clearColors);
            
            // Second pass: Sample animation and fill texture
            SampleAnimationToTexture(texture, restPoseVertices, minBounds, maxBounds);
            
            // Apply texture changes
            texture.Apply();
            
            // Save texture
            SaveTexture(texture, minBounds, maxBounds);
            
            // Cleanup
            DestroyImmediate(texture);
            EditorUtility.ClearProgressBar();
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Error", $"Failed to generate VAT: {e.Message}", "OK");
            Debug.LogError($"VAT Generation Error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private Vector3[] GetRestPoseVertices()
    {
        // Store original transform
        Vector3 originalPosition = targetObject.transform.position;
        Quaternion originalRotation = targetObject.transform.rotation;
        Vector3 originalScale = targetObject.transform.localScale;
        
        try
        {
            // Reset to rest pose
            targetObject.transform.position = Vector3.zero;
            targetObject.transform.rotation = Quaternion.identity;
            targetObject.transform.localScale = Vector3.one;
            
            // Get vertices in rest pose (world space)
            Mesh bakedMesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(bakedMesh);
            Vector3[] vertices = bakedMesh.vertices;
            DestroyImmediate(bakedMesh);
            
            return vertices;
        }
        finally
        {
            // Restore original transform
            targetObject.transform.position = originalPosition;
            targetObject.transform.rotation = originalRotation;
            targetObject.transform.localScale = originalScale;
        }
    }
    
    private void CalculateBounds(Vector3[] restPoseVertices, out Vector3 minBounds, out Vector3 maxBounds)
    {
        minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        
        int frameCount = Mathf.CeilToInt(clip.length * samplingRate);
        Mesh bakedMesh = new Mesh();
        
        // Enable animation mode for safe sampling
        AnimationMode.StartAnimationMode();
        
        try
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                float progress = frame / (float)frameCount;
                EditorUtility.DisplayProgressBar("Calculating Bounds", 
                    $"Frame {frame + 1}/{frameCount}", progress * 0.8f + 0.1f);
                
                float time = (frame / (float)frameCount) * clip.length;
                
                // Sample animation safely
                AnimationMode.SampleAnimationClip(targetObject, clip, time);
                
                // Bake current pose
                skinnedMeshRenderer.BakeMesh(bakedMesh);
                Vector3[] currentVertices = bakedMesh.vertices;
                
                // Calculate offsets and update bounds
                for (int i = 0; i < currentVertices.Length; i++)
                {
                    Vector3 offset = currentVertices[i] - restPoseVertices[i];
                    
                    minBounds.x = Mathf.Min(minBounds.x, offset.x);
                    minBounds.y = Mathf.Min(minBounds.y, offset.y);
                    minBounds.z = Mathf.Min(minBounds.z, offset.z);
                    
                    maxBounds.x = Mathf.Max(maxBounds.x, offset.x);
                    maxBounds.y = Mathf.Max(maxBounds.y, offset.y);
                    maxBounds.z = Mathf.Max(maxBounds.z, offset.z);
                }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            DestroyImmediate(bakedMesh);
        }
    }
    
    private void SampleAnimationToTexture(Texture2D texture, Vector3[] restPoseVertices, Vector3 minBounds, Vector3 maxBounds)
    {
        int vertexCount = restPoseVertices.Length;
        int frameCount = Mathf.CeilToInt(clip.length * samplingRate);
        Mesh bakedMesh = new Mesh();
        
        AnimationMode.StartAnimationMode();
        
        try
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                float progress = frame / (float)frameCount;
                EditorUtility.DisplayProgressBar("Generating Texture", 
                    $"Frame {frame + 1}/{frameCount}", progress * 0.8f + 0.1f);
                
                float time = (frame / (float)frameCount) * clip.length;
                
                // Sample animation
                AnimationMode.SampleAnimationClip(targetObject, clip, time);
                
                // Bake current pose
                skinnedMeshRenderer.BakeMesh(bakedMesh);
                Vector3[] currentVertices = bakedMesh.vertices;
                
                // Write vertex offsets to texture
                for (int vertexIndex = 0; vertexIndex < Mathf.Min(vertexCount, texture.height); vertexIndex++)
                {
                    Vector3 offset = currentVertices[vertexIndex] - restPoseVertices[vertexIndex];
                    
                    // Normalize offset to 0-1 range per component
                    Color pixelColor = new Color(
                        Mathf.InverseLerp(minBounds.x, maxBounds.x, offset.x),
                        Mathf.InverseLerp(minBounds.y, maxBounds.y, offset.y),
                        Mathf.InverseLerp(minBounds.z, maxBounds.z, offset.z),
                        1.0f
                    );
                    
                    texture.SetPixel(frame, vertexIndex, pixelColor);
                }
            }
            
            texture.Apply();
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            DestroyImmediate(bakedMesh);
        }
    }
    
    private void SaveTexture(Texture2D texture, Vector3 minBounds, Vector3 maxBounds)
    {
        // ugly hack prefere regex command (la meme j'ai la flemme, mais tibo va le faire bientot <3)
        string defaultName = $"VAT_{clip.name.Replace(" ", "_")}_{skinnedMeshRenderer.sharedMesh.name.Replace(" ", "_")}";
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Vertex Animation Texture",
            defaultName,
            "png",
            "Save the generated vertex animation texture",
            "Assets");
        
        if (string.IsNullOrEmpty(path))
            return;
        
        // Encode to PNG
        byte[] pngData = texture.EncodeToPNG();
        if (pngData == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to encode texture to PNG", "OK");
            return;
        }
        
        // Write file
        File.WriteAllBytes(path, pngData);
        
        // Import and configure texture
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            
            if (enforcePowerOfTwo)
            {
                importer.npotScale = TextureImporterNPOTScale.ToNearest;
            }
            
            importer.SaveAndReimport();
        }
        
        // Refresh and load
        AssetDatabase.Refresh();
        
        // Wait for import to complete
        EditorApplication.delayCall += () =>
        {
            lastGeneratedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            lastDuration = clip.length;
            lastMinBounds = minBounds;
            lastMaxBounds = maxBounds;
            hasResults = true;
            
            // Create a simple shader helper file with the bounds
            CreateShaderHelperFile(path, defaultName, minBounds, maxBounds);
            
            EditorUtility.DisplayDialog("Success", 
                $"Vertex Animation Texture generated successfully!\n\n" +
                $"Texture: {texture.width}x{texture.height}\n" +
                $"Frames: {Mathf.CeilToInt(clip.length * samplingRate)}\n" +
                $"Vertices: {skinnedMeshRenderer.sharedMesh.vertexCount}\n" +
                $"Bounds: {minBounds} to {maxBounds}", 
                "OK");
        };
    }
    
    private void CreateShaderHelperFile(string texturePath, string defaultName, Vector3 minBounds, Vector3 maxBounds)
    {
        string shaderPath = texturePath.Replace(".png", "_Data.cs");
        string shaderCode = $@"// Vertex Animation Texture Data
        // Generated from: {clip.name}
        // Texture: {Path.GetFileName(texturePath)}
        // Frame Count: {Mathf.CeilToInt(clip.length * samplingRate)}
        // Vertex Count: {skinnedMeshRenderer.sharedMesh.vertexCount}

        using UnityEngine;

        public class {defaultName} : MonoBehaviour
        {{
            // Texture bounds data (ugly hack but flemme de regarder comment faire autrement)
            public static readonly Vector3 MinBounds = new Vector3({minBounds.x.ToString().Replace(",", ".")}f, {minBounds.y.ToString().Replace(",", ".")}f, {minBounds.z.ToString().Replace(",", ".")}f);
            public static readonly Vector3 MaxBounds = new Vector3({maxBounds.x.ToString().Replace(",", ".")}f, {maxBounds.y.ToString().Replace(",", ".")}f, {maxBounds.z.ToString().Replace(",", ".")}f);
            public static readonly Vector3 BoundsSize = MaxBounds - MinBounds;
            
            // Animation data
            public static readonly float Duration = {clip.length}f;
            public static readonly float FrameRate = {samplingRate}f;
            public static readonly int TotalFrames = {Mathf.CeilToInt(clip.length * samplingRate)};
            
            // Shader property IDs (for optimization)
            public static readonly int VATTextureID = Shader.PropertyToID(""_VATTexture"");
            public static readonly int VATMinBoundsID = Shader.PropertyToID(""_VATMinBounds"");
            public static readonly int VATMaxBoundsID = Shader.PropertyToID(""_VATMaxBounds"");
            public static readonly int VATFrameID = Shader.PropertyToID(""_VATFrame"");
            public static readonly int VATSpeedID = Shader.PropertyToID(""_VATSpeed"");
        }}
        ";
        
        File.WriteAllText(defaultName, shaderCode);
        AssetDatabase.ImportAsset(shaderPath);
    }
    
    private void DrawResults()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generation Results", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.LabelField("Generated Texture:", EditorStyles.boldLabel);
        EditorGUILayout.ObjectField(lastGeneratedTexture, typeof(Texture2D), false);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation Data:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Duration: {lastDuration:F2} seconds");
        EditorGUILayout.LabelField($"Sampling Rate: {samplingRate} FPS");
        EditorGUILayout.LabelField($"Total Frames: {Mathf.CeilToInt(lastDuration * samplingRate)}");
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Vertex Offset Bounds:", EditorStyles.boldLabel);
        EditorGUILayout.Vector3Field("Minimum", lastMinBounds);
        EditorGUILayout.Vector3Field("Maximum", lastMaxBounds);
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Copy Bounds to Clipboard"))
        {
            string boundsData = $"Min: {lastMinBounds}\nMax: {lastMaxBounds}";
            EditorGUIUtility.systemCopyBuffer = boundsData;
            Debug.Log("Bounds copied to clipboard");
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private int NextPowerOfTwo(int value)
    {
        if (value < 1) return 1;
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
    
    // Example shader for using the VAT
    private void GenerateExampleShader()
    {
        string shaderCode = @"
Shader ""Custom/VAT/ExampleShader""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
        _VATTexture (""VAT Texture"", 2D) = ""white"" {}
        _VATMinBounds (""Min Bounds"", Vector) = (0,0,0,0)
        _VATMaxBounds (""Max Bounds"", Vector) = (1,1,1,0)
        _VATFrame (""Current Frame"", Float) = 0
        _VATSpeed (""Animation Speed"", Float) = 1
    }
    
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1; // Vertex ID as UV
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _VATTexture;
            float4 _VATTexture_ST;
            float3 _VATMinBounds;
            float3 _VATMaxBounds;
            float _VATFrame;
            float _VATSpeed;
            
            v2f vert (appdata v)
            {
                v2f o;
                
                // Calculate normalized time (0 to 1)
                float time = frac(_Time.y * _VATSpeed);
                
                // Get vertex offset from VAT texture
                // x = frame/time, y = vertex ID
                float2 vatUV = float2(time, v.uv2.y);
                float3 offsetColor = tex2Dlod(_VATTexture, float4(vatUV, 0, 0)).rgb;
                
                // Convert from normalized color back to offset
                float3 offset = lerp(_VATMinBounds, _VATMaxBounds, offsetColor);
                
                // Apply offset to vertex position
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                worldPos.xyz += offset;
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
";
        
        string path = EditorUtility.SaveFilePanelInProject("Save Example Shader", "VAT_ExampleShader", "shader", "Save example shader");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, shaderCode);
            AssetDatabase.ImportAsset(path);
        }
    }
}