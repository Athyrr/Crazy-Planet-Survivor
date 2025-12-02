using Unity.Entities;
using UnityEngine;

public class OrbitalMovementAuthoring : MonoBehaviour
{
    [SerializeField]
    public bool EnabledOnInit = true;

    class Baker : Baker<OrbitalMovementAuthoring>
    {
        public override void Bake(OrbitalMovementAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<OrbitMovement>(entity);
            if (!authoring.EnabledOnInit)
                SetComponentEnabled<OrbitMovement>(entity, false);
        }
    }
}
