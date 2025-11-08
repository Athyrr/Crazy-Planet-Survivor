using Unity.Entities;
using UnityEngine;

public class ProjectileAuthoring : MonoBehaviour
{
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


            AddComponent(entity, new Lifetime { });
        }
    }

}
