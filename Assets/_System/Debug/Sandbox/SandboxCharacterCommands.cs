#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;
using Unity.Transforms;

/// <summary>Hot character swap (scene-de-test). The new player restarts from its prefab: starting spells, level 1, XP 0.</summary>
public static class SandboxCharacterCommands
{
    [ConsoleCommand("set_character", "Remplace le joueur par ce perso, au même endroit (repart de son prefab).")]
    private static string SetCharacter(string character)
    {
        if (!SandboxCommandUtils.TryResolveCharacter(character, out var index, out var label, out var error))
            return error;
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;

        using (var selection = em.CreateEntityQuery(typeof(SelectedCharacter)))
        {
            if (selection.IsEmpty)
                return "Pas de SelectedCharacter (SC_Entity_Core chargée ?).";
            selection.SetSingleton(new SelectedCharacter { DbIndex = index });
        }

        if (!SandboxCommandUtils.TryGetPlayer(out _, out var player))
            return $"Perso sélectionné : {label} (pas de joueur à remplacer).";

        var transform = em.GetComponentData<LocalTransform>(player);

        // The old player's FireOrbs / beam owners are not cleaned by anyone when their caster disappears (Impact §3).
        int destroyed = SandboxCommandUtils.DestroySpellInstances(em, player, -1);

        // Immediate destroy (never an ECB): PlayerSpawnerSystem must never see two players.
        em.DestroyEntity(player);
        var force = em.CreateEntity();
        em.AddComponentData(force, new ForceSpawnPosition { Position = transform.Position, Rotation = transform.Rotation });

        SandboxSession.ResetBuild();
        return $"Joueur remplacé par {label} ({destroyed} instance(s) de l'ancien détruite(s) ; ses summons sont retirés par SummonReconcileSystem).";
    }
}
#endif
