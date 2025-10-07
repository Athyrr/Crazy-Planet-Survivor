using Unity.Entities;
using UnityEngine;

public class ProjectileAuthoring : MonoBehaviour
{

    //@todo lifetime + damage on collision component

    class Baker : Baker<ProjectileAuthoring>
    {
        public override void Bake(ProjectileAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Projectile>(entity);

            AddComponent(entity, new LinearMovement
            {
                Speed = 1,
                Direction = Vector3.forward
            });

            //AddComponent<DamageOnContact>(entity); 

            AddComponent(entity, new Lifetime { });
        }
    }

}
