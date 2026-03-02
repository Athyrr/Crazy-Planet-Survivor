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
public class VatBaker : MonoBehaviour
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
    [Tooltip("AnimationClip to analyse. If left null, the first clip from the\nAnimator Controller is used.")]
    public AnimationClip clip;

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

    [Header("Output Material")]
    [Tooltip("Material that uses the VAT shader / ShaderGraph. The bake process\nupdates all relevant textures and uniforms.")]
    [ContextMenuItem("Ping Texture", nameof(PingTexture))]
    public Material materialToChange;

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
    /// Bake the VAT texture based on the current inspector settings. Writes
    /// the texture on disk and updates <see cref="materialToChange"/>.
    /// </summary>
    [ContextMenu("Bake Vertex Animation Texture (VAT)")]
    public async void Bake()
    {
        _useObjectSpaceOffset = BakeObject();
        _bakeNormal = BakeNormal();
        _useBat = bakeOptions == BakeOptions.Bat;
        
        var (graph, playable, bakedMesh, frames) = BakeSetup();
        
        if (_useBat)
        {
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
        
        var evaluateVerticesObjectOffsetWorkers = new List<Task<Vector3>>();
        var evaluateVerticesWorldOffsetWorkers = new List<Task<Bounds>>();

        _bonesAcrossFrames = new Matrix4x4[frames+1][];

        _framesAndVerts.Clear();
        
        var bones = smr.bones;
        
        for (var frameIndex = 0; frameIndex < frames; frameIndex++)
        {
            var t = (float)frameIndex / sampleRate;
            playable.SetTime(t);
            graph.Evaluate(0f);          // force l’état du squelette
            
            smr.BakeMesh(bakedMesh, true);
            
            var verts = bakedMesh.vertices;
            var normals = bakedMesh.normals;
            
            //on va réut ça ensuite
            _framesAndVerts.Add((verts, normals));
            
            var frameIndexCached = frameIndex;
            if (_useBat)
            {
                var arrayMatrices = new Matrix4x4[bones.Length+1];
                
                arrayMatrices[0] = smr.rootBone.localToWorldMatrix;//root bone

                for (var boneIndex = 1; boneIndex < bones.Length; boneIndex++) 
                    arrayMatrices[boneIndex] = bones[boneIndex].localToWorldMatrix;
                
                _bonesAcrossFrames[frameIndex] = arrayMatrices;
            }
            else if (!_useObjectSpaceOffset) evaluateVerticesWorldOffsetWorkers.Add(Task.Run(() => EvaluateVerticesWorldOffsetVat(frameIndexCached))); //giga vitesse
            else evaluateVerticesObjectOffsetWorkers.Add(Task.Run(() => EvaluateVerticesObjectOffsetVat(frameIndexCached)));
        }


        if (!_useBat)
        {
            if (!_useObjectSpaceOffset)
            {
                await Task.WhenAll(evaluateVerticesWorldOffsetWorkers);
                
                await Task.Run(() =>
                {
                    var workersC = evaluateVerticesWorldOffsetWorkers.Count;
                    if(workersC == 0) return;
                    animationBounds = evaluateVerticesWorldOffsetWorkers[0].Result;
                    if (workersC == 1) return;
                    for (var i = 1; i < workersC; i++) 
                        animationBounds.Encapsulate(evaluateVerticesWorldOffsetWorkers[i].Result);
                });
                //on a le world offset
            }
            else
            {
                await Task.WhenAll(evaluateVerticesObjectOffsetWorkers);
                objectOffsetMaxDistance = GetBiggestDistanceVat(evaluateVerticesObjectOffsetWorkers);
                //on a l'object offset
            }
        }
        
        // après avoir échantillonné toutes les vraies frames…
        if(!_useBat) _framesAndVerts.Add(_framesAndVerts[0]);   // ligne-tampon (boucle propre)
        else _bonesAcrossFrames[^1] = _bonesAcrossFrames[0];
        
        var texFrames  = frames + 1;  // nb de lignes réelles dans la texture (le +1 c pour la marge quand on tente de CutTexture ça évite les artefacts)
        
        graph.Destroy();

        var workersTexture = new List<Task>();
        
        var x = _useBat ? bones.Length : smr.sharedMesh.vertexCount;
        var y = texFrames; //hauteur frames + 1

        //couper ou pas la texture en 2 sur x pour eco de lespace
        (x,y) = CutTexture(x,y);

        _xy.x = x;
        _xy.y = y;

        var trueY = _xy.y;
        if (_bakeNormal)
        {
            //le y est * 2 pour pack la normal en dessous
            trueY *= 2;
        }
        
        var tex = new Texture2D(
            _xy.x,   // width  = nb de sommets
            trueY,                       // height = nb de frames
            TextureFormat.RGBAHalf,
            /*mipMaps*/ false,
            /*linear */ true);            // VAT = données, donc linéaire
        
        var colors = tex.GetPixels();

        if (_useBat)
        {
            for (var i = 0; i < texFrames; i++)
            {
                var i1 = i;
                workersTexture.Add(Task.Run(() => PaintInTextureBat(i1, colors))); //viouuuuuu
            }
        }
        else if (!_useObjectSpaceOffset)
        {
            for (var i = 0; i < texFrames; i++)
            {
                var i1 = i;
                workersTexture.Add(Task.Run(() => PaintInTextureWorldSpaceVat(i1, colors))); //viouuuuuu
            }
        }
        else
        {
            for (var i = 0; i < texFrames; i++)
            {
                var i1 = i;
                workersTexture.Add(Task.Run(() => PaintInTextureObjectSpaceVat(i1, colors))); //viouuuuuu
            }
        }
        
        await Task.WhenAll(workersTexture);
        
        tex.SetPixels(colors);
        tex.Apply();
        var textureAsset = SaveTexture(tex, smr.name);
        
        //set du mat
        
        materialToChange.SetFloat("_totalVertices", smr.sharedMesh.vertexCount);
        if(textureAsset) materialToChange.SetTexture("_Texture2D", textureAsset);
        materialToChange.SetFloat("_frames", frames);
        materialToChange.SetVector("_bounds", animationBounds.extents*2);
        //materialToChange.SetFloat("_timesCut", _timesCut); //retrouvé dans le shader
        materialToChange.SetFloat("_addPositionMode", _useObjectSpaceOffset ? 1f : 0f);
        materialToChange.SetFloat("_normalIsBaked", _bakeNormal ? 1f : 0f);
        materialToChange.SetVector("_objectOffsetMaxDistance", objectOffsetMaxDistance);
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
    
    private (PlayableGraph graph, AnimationClipPlayable playable, Mesh bakedMesh, int frames) BakeSetup()
    {
        if (!smr) smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (!clip) clip = GetComponent<Animator>().runtimeAnimatorController.animationClips[0];//prend le permier de la liste (faudra un truc pour for sur tout quoi)
        
        //on commence
        
        // 1. Graph Playables minimal
        var graph = PlayableGraph.Create("BoundsPreview");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);

        var output = AnimationPlayableOutput.Create(graph, "AnimOutput", GetComponent<Animator>());
        output.SetSourcePlayable(playable);

        graph.Play();

        var bakedMesh = new Mesh();
        smr.BakeMesh(bakedMesh, true);//premier pour init quoi
        if (!_useObjectSpaceOffset)
        {
            // 2. Préparer la mesh tampon
            animationBounds = new Bounds(bakedMesh.vertices[0], Vector3.zero); // sera initialisé au 1er vertex
        }
        else
        {
            if (replaceMeshIfObjectOffset)
            {
                _proxyVertices = bakedMesh.vertices;
                 destinationMeshFilter.sharedMesh = bakedMesh;
            }
            else _proxyVertices = smr.sharedMesh.vertices;
        }
        
        return (graph, playable, bakedMesh, Mathf.CeilToInt(clip.length * sampleRate));
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


    private Texture2D SaveTexture(Texture2D tex, string smrName, string additionalName = "VAT")
    {
        var defaultName = $"{smrName}_{additionalName}.png";

        string path;
        if (materialToChange)
        {
            var t = materialToChange.GetTexture("_Texture2D");

            path = !t ? 
                PromptUserForPath() : 
                AssetDatabase.GetAssetPath(t);
        }else path = PromptUserForPath();
        
        if (string.IsNullOrEmpty(path)) return null;
        
        var png = tex.EncodeToPNG();          // besoin de tex.Apply() avant :contentReference[oaicite:0]{index=0}
        File.WriteAllBytes(path, png);
        
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        
        importer.npotScale       = TextureImporterNPOTScale.None;
        importer.maxTextureSize  = Mathf.NextPowerOfTwo(Mathf.Max(tex.width, tex.height));
        
        importer.textureCompression   = TextureImporterCompression.Uncompressed;
        importer.wrapMode             = TextureWrapMode.Clamp;
        importer.filterMode           = FilterMode.Bilinear;
        importer.sRGBTexture          = false;
        importer.alphaSource          = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();

        Debug.Log($"✅ VAT texture sauvegardée : {path}");

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        string PromptUserForPath()
        {
            path = EditorUtility.SaveFilePanelInProject(
                "Save VAT texture",
                defaultName,
                "png",
                "Choose where to save the baked VAT texture");
            return path;
        }
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
        if (materialToChange)
        {
            var t = materialToChange.GetTexture("_Texture2D");
            EditorGUIUtility.PingObject(t);
        }
    }
}


[CustomEditor(typeof(VatBaker))]
public class VatBakerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var vatBaker = (VatBaker)target;
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