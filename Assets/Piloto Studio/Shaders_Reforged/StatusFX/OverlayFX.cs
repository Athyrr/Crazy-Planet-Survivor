using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class OverlayFX : MonoBehaviour
{
    public Material overlayMaterial;
    public Renderer targetRenderer;
    public List<ParticleSystem> particleSystems = new();

    private float particleSizeMultiplier = 0.15f;
    public Vector3 rendererTrueForward = Vector3.zero;

#if UNITY_EDITOR
    private static readonly HashSet<Renderer> s_PreviewInjected = new();
    private static readonly HashSet<Renderer> s_RuntimeInjected = new();
    private bool IsSelected =>
        overlayMaterial &&
        (Selection.Contains(gameObject) || Selection.Contains(transform));
#endif

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EnsurePreviewOverlay(true);
#endif
        if (Application.isPlaying)
        {
            StripRuntimeOverlay(); // 💡 Fix: ensure stale runtime overlays are removed
            EnsureRuntimeOverlay(); // 💡 Fix: reinject on enable
        }

        SyncParticleSystems();
    }

    private void OnValidate() => SyncParticleSystems();

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EnsurePreviewOverlay(false);
#endif
        SyncParticleSystems();
    }

#if UNITY_EDITOR
    private void OnDisable() => StripPreviewOverlay();
    private void OnDestroy() => StripPreviewOverlay();
#endif

#if UNITY_EDITOR
    private void EnsurePreviewOverlay(bool force)
    {
        if ((!force && !IsSelected) || overlayMaterial == null) return;
        var rend = targetRenderer;
        if (rend == null) return;

        foreach (var m in rend.sharedMaterials)
            if (IsOverlayMatch(m)) return;

        var mats = rend.sharedMaterials;
        int slot = System.Array.FindIndex(mats, m => m == null);
        bool willAppend = slot == -1;

        if (!s_PreviewInjected.Contains(rend))
        {
            var preview = Object.Instantiate(overlayMaterial);
            preview.name = overlayMaterial.name + " (Preview)";
            preview.hideFlags = HideFlags.HideAndDontSave;
            UpdateBounds(preview, rend);

            if (willAppend)
            {
                var list = new List<Material>(mats) { preview };
                rend.sharedMaterials = list.ToArray();
            }
            else
            {
                mats[slot] = preview;
                rend.sharedMaterials = mats;
            }
            s_PreviewInjected.Add(rend);
        }
    }

    private static void OnSelectionChanged()
    {
        if (Selection.activeObject != null) return;
        foreach (var rend in new List<Renderer>(s_PreviewInjected))
        {
            if (rend == null) continue;
            var mats = rend.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && (mats[i].hideFlags & HideFlags.DontSave) != 0)
                {
                    mats[i] = null;
                    changed = true;
                }
            if (changed) rend.sharedMaterials = mats;
        }
        s_PreviewInjected.Clear();
    }

    private void StripPreviewOverlay()
    {
        var rend = targetRenderer;
        if (rend == null) return;
        var mats = rend.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] && (mats[i].hideFlags & HideFlags.DontSave) != 0)
            {
                mats[i] = null;
                changed = true;
            }
        if (changed) rend.sharedMaterials = mats;
        s_PreviewInjected.Remove(rend);
    }
#endif
    private bool IsOverlayMatch(Material m) =>
        m && m.shader == overlayMaterial?.shader &&
        m.name.StartsWith(overlayMaterial.name);


    private void EnsureRuntimeOverlay()
    {
        if (overlayMaterial == null) return;
        var rend = targetRenderer;
        if (rend == null) return;

#if UNITY_EDITOR
        if (s_RuntimeInjected.Contains(rend)) return;
#endif

        foreach (var m in rend.sharedMaterials)
            if (IsOverlayMatch(m)) return;

        var mats = rend.sharedMaterials;
        int slot = System.Array.FindIndex(mats, m => m == null);
        bool append = slot == -1;

        var runtime = Object.Instantiate(overlayMaterial);
        runtime.name = overlayMaterial.name + " (Runtime)";
        runtime.hideFlags = HideFlags.HideAndDontSave;
        UpdateBounds(runtime, rend);

        if (append)
        {
            var list = new List<Material>(mats) { runtime };
            rend.sharedMaterials = list.ToArray();
        }
        else
        {
            mats[slot] = runtime;
            rend.sharedMaterials = mats;
        }

#if UNITY_EDITOR
        s_RuntimeInjected.Add(rend);
#endif
    }

    private void StripRuntimeOverlay()
    {
        var rend = targetRenderer;
        if (rend == null) return;
        var mats = rend.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] && mats[i].name.EndsWith("(Runtime)"))
            {
                mats[i] = null;
                changed = true;
            }
        }

        if (changed) rend.sharedMaterials = mats;

#if UNITY_EDITOR
        s_RuntimeInjected.Remove(rend);
#endif
    }

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearRuntimeFlag() => s_RuntimeInjected.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StripRuntimeOverlaysBeforePlay()
    {
        foreach (var rend in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var mats = rend.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.EndsWith("(Runtime)"))
                {
                    mats[i] = null;
                    changed = true;
                }
            if (changed) rend.sharedMaterials = mats;
        }
        s_RuntimeInjected.Clear();
    }
#endif

    private void SyncParticleSystems()
    {
        if (targetRenderer == null) return;

        Vector3 worldSize = Vector3.one;
        if (targetRenderer is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            worldSize = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
        else if (targetRenderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            worldSize = Vector3.Scale(smr.sharedMesh.bounds.size, smr.transform.lossyScale);

        float scale = Mathf.Max(worldSize.x, worldSize.y, worldSize.z) * particleSizeMultiplier;

        foreach (var ps in particleSystems)
        {
            if (!ps) continue;
            var shape = ps.shape;
            shape.enabled = true;

            if (targetRenderer is MeshRenderer meshR)
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = meshR;
            }
            else if (targetRenderer is SkinnedMeshRenderer skinnedR)
            {
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = skinnedR;
            }

            var main = ps.main;
            main.startSizeMultiplier = scale;
        }
    }

    private void UpdateBounds(Material mat, Renderer rend)
    {
        var b = rend.localBounds;
        mat.SetVector("_LocalBoundsMinimum", b.min);
        mat.SetVector("_LocalBoundsMaximum", b.max);

        if (rend.TryGetComponent(out SkinnedMeshRenderer skinned))
            rendererTrueForward = skinned.rootBone.transform.rotation.eulerAngles;

        if (rendererTrueForward.x <= 90f)
        {
            ShaderKeywordController.SetGradientAxis(mat, GradientAxis.Y);
            ShaderKeywordController.SetUvDirection(mat, UvDirection.X);
        }
        else if (rendererTrueForward.x >= 270f)
        {
            ShaderKeywordController.SetGradientAxis(mat, GradientAxis.Z);
            ShaderKeywordController.SetUvDirection(mat, UvDirection.Z);
            foreach (var ps in particleSystems)
            {
                if (ps == null) continue;
                var vel = ps.velocityOverLifetime;
                if (!vel.enabled) continue;

                if (vel.orbitalX.mode == ParticleSystemCurveMode.Constant &&
                    vel.orbitalY.mode == ParticleSystemCurveMode.Constant &&
                    vel.orbitalZ.mode == ParticleSystemCurveMode.Constant)
                {
                    float combined = Mathf.Max(vel.orbitalX.constant, vel.orbitalY.constant, vel.orbitalZ.constant);
                    vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
                    vel.orbitalY = new ParticleSystem.MinMaxCurve(0f);
                    vel.orbitalZ = new ParticleSystem.MinMaxCurve(combined);
                }
            }
        }
    }

    public enum GradientAxis { X, Y, Z }
    public enum UvDirection { X, Y, Z }
    public static class ShaderKeywordController
    {
        private static readonly string[] GradientAxisKeywords = { "_GRADIENT_AXIS_X", "_GRADIENT_AXIS_Y", "_GRADIENT_AXIS_Z" };
        private static readonly string[] UvDirectionKeywords = { "_UV_DIRECTION_X", "_UV_DIRECTION_Y", "_UV_DIRECTION_Z" };
        public static void SetGradientAxis(Material mat, GradientAxis axis)
        {
            for (int i = 0; i < GradientAxisKeywords.Length; i++)
                if (i == (int)axis) mat.EnableKeyword(GradientAxisKeywords[i]);
                else mat.DisableKeyword(GradientAxisKeywords[i]);
        }
        public static void SetUvDirection(Material mat, UvDirection uv)
        {
            for (int i = 0; i < UvDirectionKeywords.Length; i++)
                if (i == (int)uv) mat.EnableKeyword(UvDirectionKeywords[i]);
                else mat.DisableKeyword(UvDirectionKeywords[i]);
        }
    }
}
