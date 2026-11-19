using Unity.Entities;
using UnityEngine;

/// <summary>
/// Turns an enemy prefab into a boss. Sits next to <see cref="EnemyAuthoring"/> (which bakes all the combat
/// machinery from its Config) and only adds the boss layer: <see cref="Boss"/>, the <see cref="FinalBossTag"/> or
/// <see cref="EliteTag"/>, and the managed <see cref="BossPresentation"/>. Kind, DisplayName and Icon come from the
/// resolved <see cref="BossSO"/> referenced by <see cref="EnemyAuthoring.Config"/>.
/// </summary>
[RequireComponent(typeof(EnemyAuthoring))]
public class BossAuthoring : MonoBehaviour
{
    private class Baker : Baker<BossAuthoring>
    {
        public override void Bake(BossAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            var enemy = GetComponent<EnemyAuthoring>();
            var bossConfig = enemy != null ? enemy.Config as BossSO : null;
            if (bossConfig == null)
                Debug.LogError($"[BossAuthoring] {authoring.name}: EnemyAuthoring.Config is not a BossSO, baking a " +
                               "FinalBoss named after the GameObject.", authoring);

            // Resolved copy (parent chain applied), destroyed at the end of Bake. DependsOn is declared on every link.
            using var resolved = DefinitionResolver.Resolve(bossConfig, this);
            BossSO boss = resolved.Value;

            var kind = boss != null ? boss.Kind : EBossKind.FinalBoss;

            AddComponent(entity, new Boss { Kind = kind });

            if (kind == EBossKind.FinalBoss)
                AddComponent<FinalBossTag>(entity);
            else if (kind == EBossKind.Elite)
                AddComponent<EliteTag>(entity);

            AddComponentObject(entity, new BossPresentation
            {
                DisplayName = boss != null && !string.IsNullOrEmpty(boss.DisplayName)
                    ? boss.DisplayName
                    : authoring.name,
                Icon = boss != null ? boss.Icon : null
            });
        }
    }
}
