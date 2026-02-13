using Unity.Entities;
using UnityEngine;

public class PlayerStartAuthoring : MonoBehaviour
{
    public Color DebugColor = Color.green;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerStart
            {
                Position = authoring.transform.position,
                Rotation = authoring.transform.rotation
            });
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = DebugColor;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2);
    }
}
