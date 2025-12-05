using Unity.Entities;
using UnityEngine;

public class DamageOnTickAuthoring : MonoBehaviour
{
    class Baker : Baker<DamageOnTickAuthoring>
    {
        public override void Bake(DamageOnTickAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<DamageOnTick>(entity);
        }
    }
}
