using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Serialization;

#if UNITY_EDITOR

using System.IO;
using UnityEditor;


//https://medium.com/tech-at-wildlife-studios/texture-animation-techniques-1daecb316657
//https://stoyan3d.wordpress.com/2021/07/23/vertex-animation-texture-vat/
public class VATBaker : MonoBehaviour
{
    //───────────────────────────────────────────────────────────────────────────
    //  Public user‑tweakable fields                                            
    //───────────────────────────────────────────────────────────────────────────
    
    public enum BakeOptions
    {
        VatWorld,
        VatWorldWithNormal,
        VatObject,
        VatObjectWithNormal,
        Bat
    }

    public bool BakeNormal()
    {
        return bakeOptions switch
        {
            BakeOptions.VatWorld => false,
            BakeOptions.VatWorldWithNormal => true,
            BakeOptions.VatObject => false,
            BakeOptions.VatObjectWithNormal => true,
            BakeOptions.Bat => false,
            _ => throw new ArgumentOutOfRangeException(nameof(bakeOptions), bakeOptions, null)
        };
    }
    
    public bool BakeObject()
    {
        return bakeOptions switch
        {
            BakeOptions.VatWorld => false,
            BakeOptions.VatWorldWithNormal => false,
            BakeOptions.VatObject => true,
            BakeOptions.VatObjectWithNormal => true,
            BakeOptions.Bat => false,
            _ => throw new ArgumentOutOfRangeException(nameof(bakeOptions), bakeOptions, null)
        };
    }
    
    public BakeOptions bakeOptions = BakeOptions.VatWorldWithNormal;
    
    [Tooltip("Axis‑aligned bounds encompassing *all* frames in world space.\nPopulated automatically after baking.")]
    [FormerlySerializedAs("globalBounds")]
    public Bounds animationBounds;

    [Tooltip("Per‑axis maximum vertex displacement used when *Use Object‑Space\nOffset* is enabled. Filled automatically after bake.")]
    public Vector3 objectOffsetMaxDistance;

    [Header("Animation Source")]
    [Tooltip("Clips baked one under the other into a single atlas, in this order (idle first by convention).")]
    public List<AnimationClip> clips = new();


    [Tooltip("Sampling rate in frames per second. Higher = smoother but larger\ntexture.")]
    public int sampleRate = 30;

    [Tooltip("SkinnedMeshRenderer to bake.")]
    [FormerlySerializedAs("smrs")]
    public SkinnedMeshRenderer smr;

    [Header("Object‑Space Options")]
    [Tooltip("If enabled and Object‑Space Offset is on, the baked reference mesh\n(representing the T‑pose) will replace the original MeshFilter to guarantee\nvertex order consistency at runtime.")]
    [FormerlySerializedAs("replaceMeshIfOffset")]
    public bool replaceMeshIfObjectOffset = false;

    [Tooltip("Target MeshFilter that will receive the baked reference mesh when\nusing Object‑Space Offset.")]
    public MeshFilter destinationMeshFilter;

    [Header("Output")]
    [Tooltip("Model name used in the output names: VATAnimationData_<modelName>.asset and T_VAT_<modelName>.exr.")]
    public string modelName;

    [Tooltip("Output asset. Created in Assets/_Datas/VAT/ on the first bake when empty; the texture is written next to it.")]
    [ContextMenuItem("Ping Texture", nameof(PingTexture))]
    public VATAnimationSO animationData;

    [Tooltip("Materials on SG_VAT_Master that receive the texture and the decoding settings.")]
    public List<Material> targetMaterials = new();

    private const string DefaultDataFolder = "Assets/_Datas/VAT";
    private const int MaxTextureSize = 16384;
    private const float StaticClipThreshold = 1e-4f;


    //───────────────────────────────────────────────────────────────────────────
    //  Private / internal fields                                               
    //───────────────────────────────────────────────────────────────────────────

    private Matrix4x4[][] _bonesAcrossFrames;
    private readonly List<(Vector3[] verts, Vector3[] normals)> _framesAndVerts = new(); // one array per frame
    private Vector3[] _proxyVertices;                         // reference mesh
    private int2 _xy;                                         // texture size in px

    private bool _useObjectSpaceOffset;
    private bool _bakeNormal;
    private bool _useBat;
    
    /// <summary>
    /// Bakes every clip of <see cref="clips"/> one under the other into one RGBA Half EXR (each clip followed by a
    /// copy of its frame 0), writes <see cref="animationData"/> and the decoding settings of every target material.
    /// Synchronous: the files exist when it returns.
    /// </summary>
    [ContextMenu("Bake Vertex Animation Texture (VAT)")]
    public void Bake()
    {
        _useObjectSpaceOffset = BakeObject();
        _bakeNormal = BakeNormal();
        _useBat = bakeOptions == BakeOptions.Bat;
        if (!smr) smr = GetComponentInChildren<SkinnedMeshRenderer>();
        var animator = GetComponent<Animator>();
        if (!ValidateInputs(animator)) return;
        if (_useBat) { BakeBat(); return; }

        _framesAndVerts.Clear();
        var bakedMesh = new Mesh();
        PrepareRestPose();
        var clipInfos = new List<VATClipInfo>();

        // Sampling needs the rig evaluated even when off-screen, and must not leave the prefab posed on the last frame.
        var cullingMode = animator.cullingMode;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var transforms = GetComponentsInChildren<Transform>(true);
        var restPose = new (Vector3 position, Quaternion rotation, Vector3 scale)[transforms.Length];
        for (var i = 0; i < transforms.Length; i++)
            restPose[i] = (transforms[i].localPosition, transforms[i].localRotation, transforms[i].localScale);
        try
        {
            foreach (var clip in clips) clipInfos.Add(SampleClip(clip, animator, bakedMesh));
        }
        finally
        {
            for (var i = 0; i < transforms.Length; i++)
                transforms[i].SetLocalPositionAndRotation(restPose[i].position, restPose[i].rotation);
            for (var i = 0; i < transforms.Length; i++) transforms[i].localScale = restPose[i].scale;
            animator.cullingMode = cullingMode;
        }

        var rowsPerBlock = _framesAndVerts.Count;
        EvaluateBounds();

        var (x, y) = CutTexture(smr.sharedMesh.vertexCount, rowsPerBlock);
        _xy = new int2(x, y);
        var height = _bakeNormal ? y * 2 : y;
        if (x > MaxTextureSize || height > MaxTextureSize)
        {
            Debug.LogError($"[VATBaker] {modelName}: texture {x}x{height} exceeds {MaxTextureSize}. Nothing written.", this);
            return;
        }

        var tex = new Texture2D(x, height, TextureFormat.RGBAHalf, false, true);
        var colors = tex.GetPixels();
        var painters = new Task[rowsPerBlock];
        for (var row = 0; row < rowsPerBlock; row++)
        {
            var r = row;
            painters[row] = _useObjectSpaceOffset
                ? Task.Run(() => PaintInTextureObjectSpaceVat(r, colors))
                : Task.Run(() => PaintInTextureWorldSpaceVat(r, colors));
        }
        Task.WaitAll(painters);
        tex.SetPixels(colors);
        tex.Apply();

        EnsureAnimationData();
        var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(animationData)).Replace('\\', '/');
        var texturePath = $"{folder}/T_VAT_{modelName}.exr";
        var textureAsset = SaveTextureEXR(tex, texturePath);
        DestroyImmediate(tex);
        if (!textureAsset) return;

        WriteAnimationData(textureAsset, rowsPerBlock, clipInfos);
        foreach (var material in targetMaterials)
        {
            animationData.ApplyTo(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        Debug.Log($"[VATBaker] {modelName}: {clipInfos.Count} clips, {rowsPerBlock} rows per block, texture {x}x{height} -> {texturePath}", this);
    }

    private bool ValidateInputs(Animator animator)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(modelName)) errors.Add("modelName is empty");
        if (clips == null || clips.Count == 0 || clips.Contains(null)) errors.Add("clips is empty or holds an empty entry");
        if (!smr) errors.Add("no SkinnedMeshRenderer");
        if (!animator) errors.Add("no Animator on the baker's GameObject");
        if (sampleRate <= 0) errors.Add("sampleRate must be > 0");
        if (targetMaterials == null || targetMaterials.Contains(null)) errors.Add("targetMaterials holds an empty entry");
        if (_useObjectSpaceOffset && replaceMeshIfObjectOffset && !destinationMeshFilter) errors.Add("destinationMeshFilter is required to replace the mesh");
        if (errors.Count == 0) return true;
        Debug.LogError($"[VATBaker] {name}: {string.Join("; ", errors)}. Nothing baked.", this);
        return false;
    }

    private void PrepareRestPose()
    {
        if (!_useObjectSpaceOffset) return;
        var rest = new Mesh();
        smr.BakeMesh(rest, true);
        if (replaceMeshIfObjectOffset)
        {
            _proxyVertices = rest.vertices;
            destinationMeshFilter.sharedMesh = rest;
        }
        else _proxyVertices = smr.sharedMesh.vertices;
    }

    private VATClipInfo SampleClip(AnimationClip clip, Animator animator, Mesh bakedMesh)
    {
        var graph = PlayableGraph.Create("VATBaker");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        var output = AnimationPlayableOutput.Create(graph, "AnimOutput", animator);
        output.SetSourcePlayable(playable);
        graph.Play();

        var frames = Mathf.CeilToInt(clip.length * sampleRate);
        var startRow = _framesAndVerts.Count;
        var maxDisplacementSq = 0f;
        Vector3[] first = null;
        try
        {
            for (var frame = 0; frame < frames; frame++)
            {
                playable.SetTime((float)frame / sampleRate);
                graph.Evaluate(0f);
                smr.BakeMesh(bakedMesh, true);
                var verts = bakedMesh.vertices;
                _framesAndVerts.Add((verts, bakedMesh.normals));
                if (first == null) first = verts;
                else for (var i = 0; i < verts.Length; i++) maxDisplacementSq = Mathf.Max(maxDisplacementSq, (verts[i] - first[i]).sqrMagnitude);
            }
        }
        finally { graph.Destroy(); }
        _framesAndVerts.Add(_framesAndVerts[startRow]);   // buffer row: copy of the clip's frame 0 (clean loop)

        var maxDisplacement = Mathf.Sqrt(maxDisplacementSq);
        Debug.Log($"[VATBaker] {modelName} clip '{clip.name}': {frames} frames, rows {startRow}-{startRow + frames}, max displacement {maxDisplacement:0.####}", this);
        if (maxDisplacement < StaticClipThreshold)
            Debug.LogWarning($"[VATBaker] {modelName} clip '{clip.name}' does not move the mesh: does it bind to this hierarchy?", this);

        return new VATClipInfo { Name = clip.name, StartRow = startRow, FrameCount = frames, Duration = (float)frames / sampleRate, Loop = clip.isLooping };
    }

    private void EvaluateBounds()
    {
        var rows = _framesAndVerts.Count;
        if (!_useObjectSpaceOffset)
        {
            var workers = new Task<Bounds>[rows];
            for (var row = 0; row < rows; row++) { var r = row; workers[row] = Task.Run(() => EvaluateVerticesWorldOffsetVat(r)); }
            Task.WaitAll(workers);
            animationBounds = workers[0].Result;
            for (var i = 1; i < rows; i++) animationBounds.Encapsulate(workers[i].Result);
        }
        else
        {
            var workers = new List<Task<Vector3>>(rows);
            for (var row = 0; row < rows; row++) { var r = row; workers.Add(Task.Run(() => EvaluateVerticesObjectOffsetVat(r))); }
            Task.WaitAll(workers.ToArray());
            objectOffsetMaxDistance = GetBiggestDistanceVat(workers);
        }
    }

    private void EnsureAnimationData()
    {
        if (animationData) return;
        var path = $"{DefaultDataFolder}/VATAnimationData_{modelName}.asset";
        animationData = AssetDatabase.LoadAssetAtPath<VATAnimationSO>(path);
        if (!animationData)
        {
            if (!AssetDatabase.IsValidFolder(DefaultDataFolder)) AssetDatabase.CreateFolder("Assets/_Datas", "VAT");
            animationData = ScriptableObject.CreateInstance<VATAnimationSO>();
            AssetDatabase.CreateAsset(animationData, path);
        }
        EditorUtility.SetDirty(this);
    }

    private Texture2D SaveTextureEXR(Texture2D tex, string path)
    {
        File.WriteAllBytes(path, tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));   // half precision, lossless ZIP
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var size = Mathf.Min(MaxTextureSize, Mathf.NextPowerOfTwo(Mathf.Max(tex.width, tex.height)));
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = size;
        var standalone = importer.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.format = TextureImporterFormat.RGBAHalf;
        standalone.textureCompression = TextureImporterCompression.Uncompressed;
        standalone.maxTextureSize = size;
        importer.SetPlatformTextureSettings(standalone);
        importer.SaveAndReimport();

        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (asset && asset.format != TextureFormat.RGBAHalf)
            Debug.LogError($"[VATBaker] {modelName}: {path} imported as {asset.format}, expected RGBAHalf.", this);
        return asset;
    }

    private void WriteAnimationData(Texture2D texture, int rowsPerBlock, List<VATClipInfo> clipInfos)
    {
        var previous = animationData.Clips ?? Array.Empty<VATClipInfo>();
        for (var i = 0; i < clipInfos.Count; i++)
        {
            var info = clipInfos[i];
            var kept = Array.FindIndex(previous, c => c.Name == info.Name);
            info.NativeSpeed = kept >= 0 ? previous[kept].NativeSpeed : i;   // new clip: distinct placeholder, tuned by hand
            clipInfos[i] = info;
        }

        animationData.Texture = texture;
        animationData.VertexCount = smr.sharedMesh.vertexCount;
        animationData.RowsPerBlock = rowsPerBlock;
        animationData.SampleRate = sampleRate;
        animationData.BoundsSize = animationBounds.extents * 2;
        animationData.ObjectOffsetMaxDistance = objectOffsetMaxDistance;
        animationData.ObjectSpace = _useObjectSpaceOffset;
        animationData.NormalsBaked = _bakeNormal;
        animationData.Clips = clipInfos.ToArray();
        EditorUtility.SetDirty(animationData);
        AssetDatabase.SaveAssetIfDirty(animationData);
    }

    /// <summary>BAT mode (out of scope of ecs-vat-animation): the former Bat branch, unchanged, on the first clip.</summary>
    private void BakeBat()
    {
        var bakedMesh = new Mesh();
        smr.BakeMesh(bakedMesh, true);
            //bakedMesh.bindposes
            var weights = smr.sharedMesh.boneWeights;
            var xBat = bakedMesh.vertexCount;
            var vertexC = xBat;
            var yBat = 2;//un point pour l'id des bones, un point pour les weights
            var originalY = yBat;
            
            //couper ou pas la texture en 2 sur x pour eco de lespace
            (xBat,yBat) = CutTexture(xBat,yBat);
            
            //j'ai pas encore commencé ici
            
            var texBatBones = new Texture2D(
                xBat,
                yBat,
                TextureFormat.RGBAHalf,
                /*mipMaps*/ false,
                /*linear */ true);            // BAT = données, donc linéaire
        
            var colorsBat = texBatBones.GetPixels();

            var tempList = new List<(int, float)>
            {
                Capacity = 4
            };

            for (var i = 0; i < vertexC; i++)
            {
                var pos   = i % xBat;                 // x dans la ligne
                var block = i / xBat;                 // quel “pli” horizontal
                var baseIndex = block * originalY * xBat;     // 2 lignes par bloc (IDs, Weights)

                var idxId = baseIndex + pos;          // ligne IDs
                var idxW  = idxId + xBat;             // ligne Weights

                var result = WeightToColors(weights[i], tempList);
                colorsBat[idxId] = result.id;
                colorsBat[idxW]  = result.wheight;
            }
            
            texBatBones.SetPixels(colorsBat);
            texBatBones.Apply();
            SaveBatTexture(texBatBones, smr.name);
            
            //TODO debloquer
            return;// pour l'instant
    }

    
    private static (Color id, Color wheight) WeightToColors(BoneWeight weight, List<(int, float)> tempList)
    {
        tempList.Clear();

        var totalW = 0f;
        
        //on regarde combien d'os affecte le truc

        //weight valide
        CheckAndAdd(weight.weight0, weight.boneIndex0);
        CheckAndAdd(weight.weight1, weight.boneIndex1);
        CheckAndAdd(weight.weight2, weight.boneIndex2);
        CheckAndAdd(weight.weight3, weight.boneIndex3);
        
        //on fonction de ce qui est add, on normalise.
        var coeff = 1 / totalW;
        for (var i = 0; i < tempList.Count; i++)
        {
            var t = tempList[i];
            t.Item2 *= coeff;
            tempList[i] = t;
        }
        //faire du sorting sur la plus grande weight
        tempList.Sort((a, b) => b.Item2.CompareTo(a.Item2));

        var ids = new Color();
        var colorW = new Color();

        for (var index = 0; index < tempList.Count; index++)
        {
            var (boneIndex, weightVal) = tempList[index];
            ids[index] = boneIndex / 255f;//pour rester entre 0 et 1
            colorW[index] = weightVal;
        }

        return (ids,colorW);

        void CheckAndAdd(float weightPaint, int boneIndex)
        {
            if (weightPaint == 0) return;
            tempList.Add((boneIndex,weightPaint));
            totalW += weightPaint;
        }
    }
    
    private static (int xout, int yout) CutTexture(int x, int y)
    {
        // | Symbole | Nom                                 | Ce qu’il fait sur un entier                                                                                                     |
        // | `<< n`  | **décalage à gauche** (left-shift)  | Décale tous les bits vers la gauche de *n* positions. Chaque décalage multiplie la valeur par 2.                                |
        // | `>> n`  | **décalage à droite** (right-shift) | Décale tous les bits vers la droite de *n* positions. Chaque décalage divise la valeur par 2 (arrondi vers l’entier inférieur). |
        
        var timesCut = 0;
        // tant que la moitié de la largeur reste supérieure à la hauteur
        while (x > (y << 1))          // équiv. à x / 2 > y
        {
            x = (x + 1) >> 1;         // ceil(x / 2) en entier
            y <<= 1;                  // y *= 2
            timesCut++;
        }

        Debug.Log($"cutting texture {timesCut} . x: {x} y: {y}");
        return (x, y);
    }

    #region VAT
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Bounds EvaluateVerticesWorldOffsetVat(int frameIndex)
    {
        var verts = _framesAndVerts[frameIndex].verts;
        var min = verts[0];
        var max = min;
        //plus rapide que encapsulate car on recalcule pas le centre
        for (var i = 1; i < verts.Length; i++)
        {
            var v = verts[i];
            if (v.x < min.x) min.x = v.x; if (v.x > max.x) max.x = v.x;
            if (v.y < min.y) min.y = v.y; if (v.y > max.y) max.y = v.y;
            if (v.z < min.z) min.z = v.z; if (v.z > max.z) max.z = v.z;
        }
        return new Bounds((min + max) * 0.5f, max - min);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3 EvaluateVerticesObjectOffsetVat(int frameIndex)
    {
        var vertsToCompareAgainst = _framesAndVerts[frameIndex].verts;

        var c = _proxyVertices.Length;
        Vector3 maxAbs = Vector3.zero;
        //pour chaque vertice, 
        for (var i = 0; i < c; i++)
        {
            var d = vertsToCompareAgainst[i] - _proxyVertices[i];
            maxAbs.x = Mathf.Max(maxAbs.x, Mathf.Abs(d.x));
            maxAbs.y = Mathf.Max(maxAbs.y, Mathf.Abs(d.y));
            maxAbs.z = Mathf.Max(maxAbs.z, Mathf.Abs(d.z));
        }
        //retourne la distance la plus grande entre un vertice proxy et le notre dans cette frame
        return maxAbs;
    }
    
    /// <summary>
    /// pas la peine de la task en vrai, c pas si violent,
    /// je laisse dans le main thread, de toute façon pas vraiment moyen de parraleliser ce truc
    /// a part découper en plusieurs petites listes ptetre, mais bon pas pertinent a part si l'anim + 1000 frames ou autre a la rigeur
    /// </summary>
    /// <param name="distances"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 GetBiggestDistanceVat(List<Task<Vector3>> distances)
    {
        var count = distances.Count;
        var maxAbs = Vector3.zero;

        for (var i = 0; i < count; i++)
        {
            var d = distances[i].Result;
            maxAbs.x = Mathf.Max(maxAbs.x, Mathf.Abs(d.x));
            maxAbs.y = Mathf.Max(maxAbs.y, Mathf.Abs(d.y));
            maxAbs.z = Mathf.Max(maxAbs.z, Mathf.Abs(d.z));
        }
        
        //le décalage le plus grand dans tout les floats
        return maxAbs;
    }
    
    /// <summary>
    /// Maps a value from some range to the 0 to 1 range
    /// </summary>
    public static float RemapTo01(float value, float min, float max) => (value - min) * 1f / (max - min);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PaintInTextureWorldSpaceVat(int frameIndex, Color[] colors)
    {
        var data = SetupPaintInTexture(frameIndex, out var vertsC, out var yBaseOffset, out var skipLine);

        for (var i = 0; i < vertsC; i++)
        {
            var pixel = data.verts[i];
            var x = RemapTo01(pixel.x, animationBounds.min.x, animationBounds.max.x);
            var y = RemapTo01(pixel.y, animationBounds.min.y, animationBounds.max.y);
            var z = RemapTo01(pixel.z, animationBounds.min.z, animationBounds.max.z);

            var final = GenerateCoordinatesVat(i, skipLine, yBaseOffset);

            if (final >= colors.Length)
            {
                Debug.LogError($"erreur trop gros {final} : {colors.Length}");
                continue;
            }
            colors[final] = new Color(x, y, z, 1);
        }
        
        if(_bakeNormal) PaintInTextureNormalVat(colors, vertsC, yBaseOffset, skipLine, data.normals);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PaintInTextureBat(int frameIndex, Color[] colors)
    {
        var yBaseOffset = frameIndex * _xy.x;
        var skipLine = _xy.x * _framesAndVerts.Count;
        var bones = _bonesAcrossFrames[frameIndex];
        var bonesC = bones.Length;
        
        for (var i = 0; i < bonesC; i++)
        {
            var bone = bones[i];

            //https://github.com/GhislainGir/GameToolsDoc/wiki/Bone-Animation-Textures
            //TODO remap tout ça pour que ça marche en fonction d'un truc, la position de reférence du mesh par exemple
            //première frame de l'anim ou autre
            var pos = bone.GetPosition();
            var rot = bone.rotation;
            var scale = bone.lossyScale;

            var pixel0 = GenerateCoordinatesVat(i, skipLine*3, yBaseOffset);
            var pixel1 = pixel0 + skipLine;
            var pixel2 = pixel1 + skipLine;
            
            colors[pixel0] = new Color(pos.x, pos.y, pos.z, scale.x);
            colors[pixel1] = new Color(scale.y, scale.z, rot.x, 0);
            colors[pixel2] = new Color(rot.y, rot.z, rot.w,0);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PaintInTextureObjectSpaceVat(int frameIndex, Color[] colors)
    {
        var data = SetupPaintInTexture(frameIndex, out var vertsC, out var yBaseOffset, out var skipLine);

        for (var i = 0; i < vertsC; i++)
        {
            var pixel = data.verts[i];
            //remap x y a -1 1 puis a 0 1
            var norm = pixel - _proxyVertices[i];   
            // composante par composante
            norm.x /= objectOffsetMaxDistance.x;
            norm.y /= objectOffsetMaxDistance.y;
            norm.z /= objectOffsetMaxDistance.z;
            norm = (norm + Vector3.one) * 0.5f;
            
            var final = GenerateCoordinatesVat(i, skipLine, yBaseOffset);

            if (final >= colors.Length)
            {
                Debug.LogError($"erreur trop gros {final} : {colors.Length}");
                continue;
            }
            colors[final] = new Color(norm.x, norm.y, norm.z, 1);
            
        }
        
        if(_bakeNormal) PaintInTextureNormalVat(colors, vertsC, yBaseOffset, skipLine, data.normals);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PaintInTextureNormalVat(Color[] colors, int vertsC, int yBaseOffset, int skipLine, Vector3[] normals)
    {
        for (var i = 0; i < vertsC; i++)
        {
            var pixel = normals[i];
            //remap -1 1 0 1
            pixel += Vector3.one;
            pixel /= 2f;

            var final = GenerateCoordinatesVat(i, skipLine, yBaseOffset, true);

            if (final >= colors.Length)
            {
                Debug.LogError($"erreur trop gros {final} : {colors.Length}");
                continue;
            }
            colors[final] = new Color(pixel.x, pixel.y, pixel.z, 1);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (Vector3[] verts, Vector3[] normals) SetupPaintInTexture(int frameIndex, out int vertsC, out int yBaseOffset, out int skipLine)
    {
        var dataOut = _framesAndVerts[frameIndex];
        vertsC = dataOut.verts.Length;
        
        yBaseOffset = frameIndex * _xy.x;
        skipLine = _xy.x * _framesAndVerts.Count;
        return dataOut;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GenerateCoordinatesVat(int i, int skipLine, int yBaseOffset, bool isNormal = false)
    {
        var toAdd = (int)((float)i/_xy.x) * skipLine; //si c'est au desus de un, on doit aller a la ligne du dessous (la pos sury)
        var pos = i%_xy.x;//la pos sur x
        toAdd += yBaseOffset;//on rajoute leccart suite a la frame en cour sur y
        //x pos in percent (si dépasse 1 soulmise a un modulo donc
        var final = toAdd + pos;
        if (_bakeNormal && isNormal)
        {
            final += _xy.x * _xy.y;//comme le _xy.y est pas mis a jour après le * 2, ça marche.
        }
        return final;
    }
    
    #endregion
    
    private void OnDrawGizmosSelected()
    {
        //show bounds
        animationBounds.DrawBounds(transform, Color.blue);
    }


    
    private Texture2D SaveBatTexture(Texture2D tex, string smrName)
    {
        var defaultName = $"{smrName}_BAT_Source.png";

        var path = PromptUserForPath();
        
        if (string.IsNullOrEmpty(path)) return null;
        
        var png = tex.EncodeToPNG();          // besoin de tex.Apply() avant :contentReference[oaicite:0]{index=0}
        File.WriteAllBytes(path, png);
        
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        
        importer.npotScale       = TextureImporterNPOTScale.None;
        importer.maxTextureSize  = Mathf.NextPowerOfTwo(Mathf.Max(tex.width, tex.height));
        
        importer.textureCompression   = TextureImporterCompression.Uncompressed;
        importer.wrapMode             = TextureWrapMode.Clamp;
        importer.filterMode           = FilterMode.Point;
        importer.sRGBTexture          = false;
        importer.alphaSource          = TextureImporterAlphaSource.FromInput;
        importer.SaveAndReimport();

        Debug.Log($"✅ BAT source texture sauvegardée : {path}");

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        string PromptUserForPath()
        {
            path = EditorUtility.SaveFilePanelInProject(
                "Save BAT Source texture",
                defaultName,
                "png",
                "Choose where to save the baked BAT Source texture");
            return path;
        }
    }

    //───────────────────────────────────────────────────────────────────────────
    //  Utility                                                                 
    //───────────────────────────────────────────────────────────────────────────
    private void PingTexture()
    {
        if (animationData && animationData.Texture) EditorGUIUtility.PingObject(animationData.Texture);
    }
}


[CustomEditor(typeof(VATBaker))]
public class VATBakerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var vatBaker = (VATBaker)target;
        if (GUILayout.Button("Bake")) vatBaker.Bake();
    }
}

#endif

public static class VatUtils
{
    /// <summary> Dessine les Bounds locaux d’un transform en Gizmos. </summary>
    public static void DrawBounds(this Bounds localBounds, Transform tr, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(
            tr.TransformPoint(localBounds.center),
            Vector3.Scale(localBounds.size, tr.lossyScale)
        );
    }
    
    public static void ThreadCheck(string fname)
    {
        var isThreadPool = Thread.CurrentThread.IsThreadPoolThread;
        if (!isThreadPool) Debug.LogWarning($"⚠️ {fname} est exécuté sur le main thread (ID {Thread.CurrentThread.ManagedThreadId}) !");
        else Debug.Log($"✅ {fname} sur un thread pool (ID {Thread.CurrentThread.ManagedThreadId})");
    }
}