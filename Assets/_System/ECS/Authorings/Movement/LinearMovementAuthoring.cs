using Unity.Entities;
using UnityEngine;

public class LinearMovementAuthoring : MonoBehaviour
{
    [SerializeField]
    private bool _enabledOnInit = true;

    [SerializeField]
    private bool _hardSnapOnSurface = false;

    class Baker : Baker<LinearMovementAuthoring>
    {
        public override void Bake(LinearMovementAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<LinearMovement>(entity);
            SetComponentEnabled<LinearMovement>(entity, authoring._enabledOnInit);

            if (authoring._hardSnapOnSurface)
                AddComponent<HardSnappedMovement>(entity);
        }
    }
}
