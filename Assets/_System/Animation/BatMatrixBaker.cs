#if UNITY_EDITOR
using System;
using System.IO;
using EasyButtons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class BatMatrixBaker : MonoBehaviour
{
    public AnimationClip clip;
    public Animator animator;
    public SkinnedMeshRenderer smr;
    public Material materialToChange;
    public int sampleRate = 30;
    public MeshFilter outputMeshFilter;

    public BatLayout layout;

    private Vector2Int _xy;

    [Button]
    [ContextMenu("Bake BAT Matrix")]
    public void BakeBatMatrix()
    {
        if (!smr) smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!animator) animator = GetComponentInParent<Animator>();
        if (!clip) clip = animator.runtimeAnimatorController.animationClips[0];

        if (!outputMeshFilter)
        {
            Debug.LogError("Missing outputMeshFilter");
            return;
        }
        if (!materialToChange)
        {
            Debug.LogError("Missing materialToChange");
            return;
        }
        if (!smr || !animator || !clip)
        {
            Debug.LogError("Missing animation references");
            return;
        }
        
        var graph = PlayableGraph.Create("BAT Bake");
        try
        {
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);

            var output = AnimationPlayableOutput.Create(graph, "AnimOutput", animator);
            output.SetSourcePlayable(playable);
            graph.Play();

            var bones = smr.bones;
            var bindPoses = smr.sharedMesh.bindposes;
            var frames = Mathf.CeilToInt(clip.length * sampleRate);
            var frameRows = frames + 1;

            layout = ComputeBestLayout(bones.Length, frameRows, 3);

            _xy = new Vector2Int(layout.totalWidth, layout.totalHeight);
            
            var occupied = new bool[layout.totalWidth * layout.totalHeight];

            void CheckWrite(int idx)
            {
                if (idx < 0 || idx >= occupied.Length)
                    Debug.LogError($"BAT index out of range: {idx}");

                if (occupied[idx])
                    Debug.LogError($"BAT collision at pixel {idx}");

                occupied[idx] = true;
            }

            var tex = new Texture2D(
                layout.totalWidth,
                layout.totalHeight,
                TextureFormat.RGBAHalf,
                false,
                true
            );

            var colors = new Color[layout.totalWidth * layout.totalHeight];
            var meshWorldToLocal = outputMeshFilter.transform.worldToLocalMatrix;
            
            for (int frameIndex = 0; frameIndex < frames; frameIndex++)
            {
                float t = (float) frameIndex / sampleRate;
                playable.SetTime(t);
                graph.Evaluate(0f);

                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    Matrix4x4 skin =
                        meshWorldToLocal *
                        bones[boneIndex].localToWorldMatrix *
                        bindPoses[boneIndex];

                    int p0 = GetPixelIndex(boneIndex, frameIndex, 0);
                    int p1 = GetPixelIndex(boneIndex, frameIndex, 1);
                    int p2 = GetPixelIndex(boneIndex, frameIndex, 2);
                    CheckWrite(p0);
                    CheckWrite(p1);
                    CheckWrite(p2);

                    colors[p0] = new Color(skin.m00, skin.m01, skin.m02, skin.m03);
                    colors[p1] = new Color(skin.m10, skin.m11, skin.m12, skin.m13);
                    colors[p2] = new Color(skin.m20, skin.m21, skin.m22, skin.m23);
                }
            }

            // duplicate first frame at end for looping
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                int src0 = GetPixelIndex(boneIndex, 0, 0);
                int src1 = GetPixelIndex(boneIndex, 0, 1);
                int src2 = GetPixelIndex(boneIndex, 0, 2);

                int dst0 = GetPixelIndex(boneIndex, frames, 0);
                int dst1 = GetPixelIndex(boneIndex, frames, 1);
                int dst2 = GetPixelIndex(boneIndex, frames, 2);

                colors[dst0] = colors[src0];
                colors[dst1] = colors[src1];
                colors[dst2] = colors[src2];
            }

            tex.SetPixels(colors);
            tex.Apply();

            var asset = SaveTexture(tex, smr.name + "_BATMatrix.png");

            if (materialToChange && asset)
            {
                materialToChange.SetFloat("_BATFrames", frames);
                materialToChange.SetFloat("_BATFrameRows", layout.frameRows);
                materialToChange.SetFloat("_BATColumns", layout.columns);
                materialToChange.SetFloat("_BATBoneCount", bones.Length);

                materialToChange.SetFloat("_BATPlaneWidth", layout.planeWidth);
                materialToChange.SetFloat("_BATPlaneHeight", layout.planeHeight);
                materialToChange.SetFloat("_BATPlaneGridX", layout.planeGridX);
                materialToChange.SetFloat("_BATPlaneGridY", layout.planeGridY);

                materialToChange.SetTexture("_BATMap", asset);
            }

            outputMeshFilter.sharedMesh = smr.sharedMesh;
        }
        finally
        {
            if(graph.IsValid()) graph.Destroy();
        }
    }

    private int GetPixelIndex(int boneIndex, int frameIndex, int plane)
    {
        int col = boneIndex % layout.columns;
        int rowBlock = boneIndex / layout.columns;

        int planeX = plane % layout.planeGridX;
        int planeY = plane / layout.planeGridX;

        int x = planeX * layout.planeWidth + col;
        int y = planeY * layout.planeHeight + rowBlock * layout.frameRows + frameIndex;

        return y * layout.totalWidth + x;
    }

    [Serializable]
    public struct BatLayout
    {
        public int columns;
        public int rowBlocks;

        public int frameRows;

        public int planeWidth;
        public int planeHeight;

        public int planeGridX;
        public int planeGridY;

        public int totalWidth;
        public int totalHeight;
    }

    private static BatLayout ComputeBestLayout(int boneCount, int frameRows, int planes = 3)
    {
        if (boneCount <= 0)
        {
            return new BatLayout
            {
                columns = 1,
                rowBlocks = 0,
                frameRows = frameRows,
                planeWidth = 1,
                planeHeight = 0,
                planeGridX = 1,
                planeGridY = 1,
                totalWidth = 1,
                totalHeight = 0
            };
        }

        BatLayout best = default;
        long bestScore = long.MaxValue;
        int bestWaste = int.MaxValue;
        long bestArea = long.MaxValue;

        // Grilles possibles pour 3 plans
        Span<Vector2Int> grids = stackalloc Vector2Int[]
        {
            new Vector2Int(1, 3),
            new Vector2Int(3, 1),
            new Vector2Int(2, 2),
        };

        for (int columns = 1; columns <= boneCount; columns++)
        {
            int rowBlocks = (boneCount + columns - 1) / columns;
            int planeWidth = columns;
            int planeHeight = rowBlocks * frameRows;
            int waste = columns * rowBlocks - boneCount;

            for (int i = 0; i < grids.Length; i++)
            {
                int gridX = grids[i].x;
                int gridY = grids[i].y;

                if (gridX * gridY < planes)
                    continue;

                int totalWidth = planeWidth * gridX;
                int totalHeight = planeHeight * gridY;

                long squareness = Math.Abs((long)totalWidth - totalHeight);
                long area = (long)totalWidth * totalHeight;

                bool isBetter =
                    squareness < bestScore ||
                    (squareness == bestScore && waste < bestWaste) ||
                    (squareness == bestScore && waste == bestWaste && area < bestArea);

                if (!isBetter)
                    continue;

                bestScore = squareness;
                bestWaste = waste;
                bestArea = area;

                best.columns = columns;
                best.rowBlocks = rowBlocks;
                best.frameRows = frameRows;
                best.planeWidth = planeWidth;
                best.planeHeight = planeHeight;
                best.planeGridX = gridX;
                best.planeGridY = gridY;
                best.totalWidth = totalWidth;
                best.totalHeight = totalHeight;
            }
        }

        return best;
    }

    private Texture2D SaveTexture(Texture2D tex, string defaultName)
    {

        var t = materialToChange.GetTexture("_BATMap");
        string path;
        if (t)
            path = AssetDatabase.GetAssetPath(t);
        else
        {
            path = EditorUtility.SaveFilePanelInProject(
                "Save BAT texture",
                defaultName.Replace(".png", ".exr"),
                "exr",
                "Choose where to save the baked BAT texture");
        }
        
        if (string.IsNullOrEmpty(path))
            return null;

        var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(tex.width, tex.height));
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif