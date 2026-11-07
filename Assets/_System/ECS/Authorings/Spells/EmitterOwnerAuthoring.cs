using Unity.Entities;
using UnityEngine;

/// <summary>Generic owner of attached emitter children (PF_Spell_EmitterOwner). Values are set at emission.</summary>
public class EmitterOwnerAuthoring : MonoBehaviour
{
    private class Baker : Baker<EmitterOwnerAuthoring>
    {
        public override void Bake(EmitterOwnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EmitterOwner { ChildBaseScale = 1f, ChildScale = 1f });
        }
    }
}
