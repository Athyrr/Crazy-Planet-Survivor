using UnityEngine;
using Unity.Entities;

public class ExplodeOnContactAuthoring : MonoBehaviour
{
    public float Radius = 3.0f;
    public int Damage = 10;
    public GameObject VfxPrefab;

    public bool StartEnabled = false;

    private class Baker : Baker<ExplodeOnContactAuthoring>
    {
        public override void Bake(ExplodeOnContactAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            var explodeOnContact = new ExplodeOnContact
            {
                Radius = authoring.Radius,
                Damage = authoring.Damage,
                VfxPrefab = GetEntity(authoring.VfxPrefab, TransformUsageFlags.None),
                TargetLayers = 0
            };

            AddComponent<ExplodeOnContact>(entity, explodeOnContact);
            
            if (!authoring.StartEnabled)
                SetComponentEnabled<ExplodeOnContact>(entity, false);
        }
    }
}

public struct ExplodeOnContact : IComponentData, IEnableableComponent
{
    public int Damage;
    public float Radius;
    public Entity VfxPrefab;

    public uint TargetLayers;
    //public float KnockbackForce;
}