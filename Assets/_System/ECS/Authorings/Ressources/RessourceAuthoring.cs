using _System.ECS.Authorings.Ressources;
using Unity.Entities;
using UnityEngine;

public class RessourceAuthoring : MonoBehaviour
{
    [SerializeField] private ERessourceType _ressourceType;
    [SerializeField] private bool _persistant;
    
    [Tooltip("If true, the orb will snap perfectly to the ground when attracted.")]
    [SerializeField]private bool _hardSnapToGround;
    
    class Baker : Baker<RessourceAuthoring>
    {
        public override void Bake(RessourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Ressource>(entity, new Ressource()
            {
                Type = authoring._ressourceType,
                Value = 0,
            });
            
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
            
            if (authoring._hardSnapToGround)
                AddComponent<HardSnappedMovement>(entity);
        }
    }
}