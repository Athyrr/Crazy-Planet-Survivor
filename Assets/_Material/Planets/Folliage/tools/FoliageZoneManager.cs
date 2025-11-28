using System;
using System.Collections.Generic;
using UnityEngine;

public class FoliageZoneManager : MonoBehaviour
{
    [System.Serializable]
    public class FoliageZone
    {
        public Transform zoneTransform;
        public float radius = 5f;
        public Color zoneColor = Color.green;
        public float blendSmoothness = 2f;
        
        // Pour détecter les changements en runtime
        private Vector3 lastPosition;
        private float lastRadius;
        private Color lastColor;
        private float lastBlend;

        public FoliageZone(Transform transform, float rad, Color color, float blend)
        {
            zoneTransform = transform;
            radius = rad;
            zoneColor = color;
            blendSmoothness = blend;
            lastPosition = transform.position;
            lastRadius = rad;
            lastColor = color;
            lastBlend = blend;
        }

        public bool HasChanged()
        {
            bool changed = zoneTransform.position != lastPosition || 
                          !Mathf.Approximately(radius, lastRadius) ||
                          zoneColor != lastColor ||
                          !Mathf.Approximately(blendSmoothness, lastBlend);
            
            if (changed)
            {
                lastPosition = zoneTransform.position;
                lastRadius = radius;
                lastColor = zoneColor;
                lastBlend = blendSmoothness;
            }
            
            return changed;
        }
    }

    public List<FoliageZone> foliageZones = new List<FoliageZone>();
    public Material foliageMaterial;
    
    private Vector4[] zonePositions;
    private float[] zoneRadii;
    private Vector4[] zoneColors;
    private float[] zoneBlends;

    private bool needsUpdate = false;

    void Start()
    {
        InitializeShaderArrays();
        UpdateShaderZones();
    }

    private void OnValidate()
    {
        // En éditeur seulement
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UpdateShaderZones();
        }
        #endif
    }

    void Update()
    {
        // Vérifier les changements en runtime
        if (Application.isPlaying && CheckForChanges())
        {
            UpdateShaderZones();
        }
    }

    public void AddZone(Transform zoneTransform, float radius, Color color, float blendSmoothness = 2f)
    {
        RemoveZone(zoneTransform);
        
        FoliageZone newZone = new FoliageZone(zoneTransform, radius, color, blendSmoothness);
        foliageZones.Add(newZone);
        UpdateShaderZones();
    }

    public void UpdateZone(Transform zoneTransform, float radius = -1f, Color color = default, float blendSmoothness = -1f)
    {
        var zone = foliageZones.Find(el => el.zoneTransform == zoneTransform);

        if (zone == null)
        {
            AddZone(zoneTransform, 
                   radius > 0 ? radius : 5f, 
                   color != default ? color : Color.green, 
                   blendSmoothness > 0 ? blendSmoothness : 2f);
            return;
        }

        if (radius > 0) zone.radius = radius;
        if (color != default) zone.zoneColor = color;
        if (blendSmoothness > 0) zone.blendSmoothness = blendSmoothness;

        UpdateShaderZones();
    }

    public void RemoveZone(Transform zoneTransform)
    {
        foliageZones.RemoveAll(zone => zone.zoneTransform == zoneTransform);
        UpdateShaderZones();
    }

    private void InitializeShaderArrays()
    {
        zonePositions = new Vector4[32];
        zoneRadii = new float[32];
        zoneColors = new Vector4[32];
        zoneBlends = new float[32];
    }

    private void UpdateShaderZones()
    {
        if (foliageMaterial == null)
        {
            Debug.LogWarning("Foliage Material non assigné!", this);
            return;
        }

        if (zonePositions == null) InitializeShaderArrays();

        int zoneCount = Mathf.Min(foliageZones.Count, 32);
Debug.Log("hyv; update truc");
        // Réinitialiser les tableaux
        for (int i = 0; i < 32; i++)
        {
            if (i < zoneCount && foliageZones[i].zoneTransform != null)
            {
                FoliageZone zone = foliageZones[i];
                zonePositions[i] = new Vector4(
                    zone.zoneTransform.position.x,
                    zone.zoneTransform.position.y,
                    zone.zoneTransform.position.z,
                    1f // placeholder
                );
                zoneRadii[i] = zone.radius;
                zoneColors[i] = new Vector4(zone.zoneColor.r, zone.zoneColor.g, zone.zoneColor.b, zone.zoneColor.a);
                zoneBlends[i] = zone.blendSmoothness;
            }
            else
            {
                // Désactiver les zones non utilisées
                zonePositions[i] = Vector4.zero;
                zoneRadii[i] = 0f;
                zoneColors[i] = Vector4.zero;
                zoneBlends[i] = 0f;
            }
        }

        // Passer les données au shader
        foliageMaterial.SetInt("_ZoneCount", zoneCount);
        foliageMaterial.SetVectorArray("_ZonePositions", zonePositions);
        foliageMaterial.SetFloatArray("_ZoneRadii", zoneRadii);
        foliageMaterial.SetVectorArray("_ZoneColors", zoneColors);
        foliageMaterial.SetFloatArray("_ZoneBlends", zoneBlends);
    }

    private bool CheckForChanges()
    {
        foreach (var zone in foliageZones)
        {
            if (zone.HasChanged())
            {
                return true;
            }
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (foliageZones != null)
        {
            foreach (var zone in foliageZones)
            {
                if (zone.zoneTransform != null)
                {
                    Gizmos.color = zone.zoneColor;
                    Gizmos.DrawWireSphere(zone.zoneTransform.position, zone.radius);
                    
                    Gizmos.color = new Color(zone.zoneColor.r, zone.zoneColor.g, zone.zoneColor.b, 0.1f);
                    Gizmos.DrawSphere(zone.zoneTransform.position, zone.radius);
                }
            }
        }
    }
}