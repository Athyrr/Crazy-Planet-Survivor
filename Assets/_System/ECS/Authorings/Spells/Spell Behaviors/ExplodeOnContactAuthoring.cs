using UnityEngine;
using Unity.Entities;

public class ExplodeOnContactAuthoring : MonoBehaviour
{
    public GameObject EffectPrefab;

    private class Baker : Baker<ExplodeOnContactAuthoring>
    {
        public override void Bake(ExplodeOnContactAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new ExplodeOnContact()
            {
                //  EffectPrefab = authoring.EffectPrefab,
            });
        }
    }
}

public struct ExplodeOnContact : IComponentData, IEnableableComponent
{
    public int Damage;

    public float Radius;

//    public float KnockbackForce;
    public Entity EffectPrefab;
}