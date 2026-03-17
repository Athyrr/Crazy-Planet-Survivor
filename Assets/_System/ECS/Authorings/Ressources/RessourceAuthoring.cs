using Unity.Entities;
using UnityEngine;

public class RessourceAuthoring : MonoBehaviour
{
    class Baker : Baker<RessourceAuthoring>
    {
        public override void Bake(RessourceAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Ressource>(entity);
        }
    }
}