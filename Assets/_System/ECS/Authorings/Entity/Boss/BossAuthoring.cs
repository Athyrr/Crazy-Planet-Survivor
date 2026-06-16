using Unity.Entities;
using UnityEngine;
using EasyButtons;

/// <summary>
/// Turns an enemy prefab into a boss. Sits next to <see cref="EnemyAuthoring"/> (which still bakes
/// all the combat machinery) and only adds the boss layer: <see cref="Boss"/>, the
/// <see cref="FinalBossTag"/> for a final boss, and the managed <see cref="BossPresentation"/>.
/// </summary>
[RequireComponent(typeof(EnemyAuthoring))]
public class BossAuthoring : MonoBehaviour
{
    [Tooltip("Boss definition. Press 'Apply Config' to push its combat values into the EnemyAuthoring.")]
    public BossSO Config;

    private class Baker : Baker<BossAuthoring>
    {
        public override void Bake(BossAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            var kind = authoring.Config != null ? authoring.Config.Kind : EBossKind.FinalBoss;

            AddComponent(entity, new Boss { Kind = kind });

            if (kind == EBossKind.FinalBoss)
                AddComponent<FinalBossTag>(entity);

            AddComponentObject(entity, new BossPresentation
            {
                DisplayName = authoring.Config != null && !string.IsNullOrEmpty(authoring.Config.DisplayName)
                    ? authoring.Config.DisplayName
                    : authoring.name,
                Icon = authoring.Config != null ? authoring.Config.Icon : null
            });
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Copies the BossSO's combat values into the sibling EnemyAuthoring so a single baker
    /// (EnemyAuthoring) remains the source of truth for stats/spells.
    /// </summary>
    [Button("Apply Config to EnemyAuthoring")]
    private void ApplyConfig()
    {
        if (Config == null)
        {
            Debug.LogWarning("[BossAuthoring] No BossSO assigned.", this);
            return;
        }

        var enemy = GetComponent<EnemyAuthoring>();
        if (enemy == null)
        {
            Debug.LogWarning("[BossAuthoring] No EnemyAuthoring found on this GameObject.", this);
            return;
        }

        enemy.BaseStats = Config.BaseStats;
        enemy.InitialSpells = Config.InitialSpells;

        UnityEditor.EditorUtility.SetDirty(enemy);
        Debug.Log($"[BossAuthoring] Applied '{Config.DisplayName}' config to EnemyAuthoring.", this);
    }
#endif
}
