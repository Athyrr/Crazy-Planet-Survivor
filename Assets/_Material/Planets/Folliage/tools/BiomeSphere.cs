using System;
using UnityEngine;

public class BiomeSphere : MonoBehaviour
{
    [Header("Biome Settings")]
    public float radius = 5f;
    public Color zoneColor = Color.green;
    public float blendSmoothness = 2f;

    [Header("References")]
    public FoliageZoneManager zoneManager;

    /*private void OnDrawGizmosSelected() // todo @hyverno caca mais il a la flemme de coder donc il va dormir 
    {
        #if UNITY_EDITOR
        if (zoneManager == null)
            zoneManager = FindFirstObjectByType<FoliageZoneManager>();
        
        zoneManager.UpdateZone(transform, radius, zoneColor, blendSmoothness);

        #endif
    }*/

    void Start()
    {
        if (zoneManager == null)
            zoneManager = FindObjectOfType<FoliageZoneManager>();

        if (zoneManager != null)
            zoneManager.AddZone(this.transform, radius, zoneColor, blendSmoothness);
    }

    void OnDestroy()
    {
        if (zoneManager != null)
        {
            zoneManager.RemoveZone(this.transform);
        }
    }

    // Pour mettre à jour les paramètres en runtime
    public void UpdateZoneParameters(float newRadius, Color newColor, float newBlend)
    {
        radius = newRadius;
        zoneColor = newColor;
        blendSmoothness = newBlend;

        if (zoneManager != null && Application.isPlaying)
        {
            zoneManager.UpdateZone(transform, radius, zoneColor, blendSmoothness);
        }
    }

    public void Update()
    {
        throw new NotImplementedException();
    }

    // Visualisation dans l'éditeur
    void OnDrawGizmos()
    {
        Gizmos.color = zoneColor;
        Gizmos.DrawWireSphere(transform.position, radius);
        
        // Sphere semi-transparente pour mieux visualiser
        Gizmos.color = new Color(zoneColor.r, zoneColor.g, zoneColor.b, 0.1f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}