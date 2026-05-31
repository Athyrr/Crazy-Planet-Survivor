using UnityEngine;

[CreateAssetMenu(menuName = "BAT/Animation Data", fileName = "BAT_Animation_")]
public class BATAnimationData : ScriptableObject
{
    public string clipName;
    public Texture2D batMap;
    public int boneCount;
    public int columns;
    public int frameRows;
    public int frames;
    public int planeWidth;
    public int planeHeight;
    public int planeGridX;
    public int planeGridY;
    public float fps = 30f;

    public void ApplyToMaterial(Material mat)
    {
        mat.SetTexture("_BATMap", batMap);
        mat.SetFloat("_BATBoneCount", boneCount);
        mat.SetFloat("_BATColumns", columns);
        mat.SetFloat("_BATFrameRows", frameRows);
        mat.SetFloat("_BATFrames", frames);
        mat.SetFloat("_BATPlaneWidth", planeWidth);
        mat.SetFloat("_BATPlaneHeight", planeHeight);
        mat.SetFloat("_BATPlaneGridX", planeGridX);
        mat.SetFloat("_BATPlaneGridY", planeGridY);
        mat.SetFloat("_BAT_FPS", fps);
        mat.SetFloat("_BATFrame", 0);
    }

    public void ApplyToMaterialBlend(Material mat)
    {
        mat.SetTexture("_BATMap2", batMap);
        mat.SetFloat("_BATFrames2", frames);
        mat.SetFloat("_BATFrameRows2", frameRows);
        mat.SetFloat("_BATPlaneHeight2", planeHeight);
        mat.SetFloat("_BAT_FPS_Blend", fps);
        mat.SetFloat("_BATBlendFrame", 0);
    }

    public bool IsValid()
    {
        return batMap != null && boneCount > 0 && frames > 0;
    }
}
