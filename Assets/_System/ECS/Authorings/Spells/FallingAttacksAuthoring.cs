using Unity.Entities;
using UnityEngine;

public class FallingAttackAuthoring : MonoBehaviour
{
    class Baker : Baker<FallingAttackAuthoring>
    {
        public override void Bake(FallingAttackAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<FallingAttack>(entity);

            AddComponent(entity, new OrbitMovement
            {
                OrbitCenterEntity = default,
                OrbitCenterPosition = default,
                AngularSpeed = 0,
                Radius = 0,
                RelativeOffset = default
            });

            //AddComponent<DamageOnContact>(entity);

            AddComponent(entity, new Lifetime { });
        }
    }

}