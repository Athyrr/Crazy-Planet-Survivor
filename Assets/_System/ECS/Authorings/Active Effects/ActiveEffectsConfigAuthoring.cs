using _System.Settings;
using Unity.Entities;
using UnityEngine;

/// <summary>Bakes the status-effect VFX prefabs from CpCombatEffectsSettings into ActiveEffectsVfxConfig.
/// The numeric debuff-rule singleton this authoring used to also bake (ActiveEffectsConfig) is superseded
/// by EffectTypeConfigAuthoring — see EffectTypeConfigSO.</summary>
public class ActiveEffectsConfigAuthoring : MonoBehaviour
{
    public CpCombatEffectsSettings Settings;

    private class Baker : Baker<ActiveEffectsConfigAuthoring>
    {
        public override void Bake(ActiveEffectsConfigAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            var s = authoring.Settings;
            if (s != null)
                DependsOn(s);

            AddComponentObject(entity, new ActiveEffectsVfxConfig
            {
                BurnEffectPrefab = s != null ? s.BurnEffectPrefab : null,
                StunEffectPrefab = s != null ? s.StunEffectPrefab : null,
                SlowEffectPrefab = s != null ? s.SlowEffectPrefab : null,
            });
        }
    }
}

public class ActiveEffectsVfxConfig : IComponentData
{
    public GameObject BurnEffectPrefab;
    public GameObject StunEffectPrefab;
    public GameObject SlowEffectPrefab;
}
