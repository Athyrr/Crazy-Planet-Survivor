using _System.ECS.Authorings.Ressources;
using Unity.Entities;
using UnityEngine;

public class RessourceAuthoring : MonoBehaviour
{
    [SerializeField] private ERessourceType _ressourceType;
    [SerializeField] private bool _persistant;
    
    class Baker : Baker<RessourceAuthoring>
    {
        public override void Bake(RessourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Ressource>(entity, new Ressource()
            {
                Type = authoring._ressourceType,
                Value = 0,
                Persistant = authoring._persistant
            });
        }
    }
}