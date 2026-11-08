using Unity.Entities;
using UnityEngine;

/// <summary>Root of a Summon agent prefab (spec allegeance-summon §2): everything SpellStatsCalculationSystem,
/// SpellActivationSystem, ActiveEffectsSystem (LiveStats) and the Summon systems need. Deliberately NOT Player / Enemy /
/// Health / Destructible and no collider: untouchable, never targeted, never flow-field driven. Owner, group, slot and
/// team are written at spawn by SummonReconcileSystem.</summary>
public class SummonAgentAuthoring : MonoBehaviour
{
    private class Baker : Baker<SummonAgentAuthoring>
    {
        public override void Bake(SummonAgentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Summon { Owner = Entity.Null, SourceDatabaseIndex = -1, Slot = 0, ExpiresAt = 0d });
            AddComponent(entity, new Team { Value = ETeam.Player });
            AddComponent(entity, new CoreStats());
            AddComponent(entity, new LiveStats());
            AddComponent(entity, new SummonFollow());
            AddComponent<RunScope>(entity);

            AddBuffer<ActiveSpell>(entity);
            AddBuffer<SpellStatUpgrade>(entity);
            AddBuffer<CharacterStatBuff>(entity);
            AddBuffer<SpellActivationRequest>(entity);
        }
    }
}
