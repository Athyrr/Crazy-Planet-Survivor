using Unity.Entities;
using UnityEngine;

public class LinearMovementAuthoring : MonoBehaviour
{
    [SerializeField]
    public bool EnabledOnInit = true;

    class Baker : Baker<LinearMovementAuthoring>
    {
        public override void Bake(LinearMovementAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<LinearMovement>(entity);
            SetComponentEnabled<LinearMovement>(entity, authoring.EnabledOnInit);
        }
    }
}
