using UnityEngine;
using Unity.Entities;

public class ExplodeOnDeathAuthoring : MonoBehaviour
{
    public int Damage = 150;
    public float Radius = 1f;
    // public uint TargetLayers;
    public ESpellTag Tags;

    [Space] public GameObject VfxPrefab;

    [Space] public bool StartEnabled = false;

    private class Baker : Baker<ExplodeOnDeathAuthoring>
    {
        public override void Bake(ExplodeOnDeathAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            var explodeOnDeath = new ExplodeOnDeath
            {
                Damage = authoring.Damage,
                Radius = authoring.Radius,
                VfxPrefab = GetEntity(authoring.VfxPrefab, TransformUsageFlags.None),
                // TargetLayers = authoring.TargetLayers,
                Tags = authoring.Tags
            };

            AddComponent<ExplodeOnDeath>(entity, explodeOnDeath);


            if (!authoring.StartEnabled)
                SetComponentEnabled<ExplodeOnDeath>(entity, false);
        }
    }
}

public struct ExplodeOnDeath : IComponentData, IEnableableComponent
{
    public float Damage;
    public float Radius;
    public Entity VfxPrefab;

    public bool IsCrit;

    public uint TargetLayers;

    //public float KnockbackForce;

    public ESpellTag Tags;
}