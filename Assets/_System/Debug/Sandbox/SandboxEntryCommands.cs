#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;

/// <summary>Sandbox entry (scene-de-test). Usable from the main menu, the lobby or a run; no Sandbox entry in any game menu.</summary>
public static class SandboxEntryCommands
{
    [ConsoleCommand("sandbox", "Charge la planète Sandbox (perso : nom ou index), sans intro.")]
    private static string Sandbox(string character = "")
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        if (GameManager.Instance == null)
            return "Pas de GameManager (scène SC_Main ?).";

        var state = SandboxCommandUtils.GetGameState(em);
        if (state == EGameState.Loading)
            return "Chargement en cours, réessaie après.";

        using (var scenes = em.CreateEntityQuery(typeof(PlanetSceneRefBufferElement)))
        {
            if (scenes.IsEmpty)
                return "Scènes de planètes pas encore chargées, réessaie.";
            bool found = false;
            foreach (var s in em.GetBuffer<PlanetSceneRefBufferElement>(scenes.GetSingletonEntity(), true))
                found |= s.PlanetID == EPlanetID.Sandbox;
            if (!found)
                return "Pas de scène Sandbox dans Game Scenes Config (rebake SC_Entity_Core).";
        }

        string label = null;
        if (!string.IsNullOrWhiteSpace(character))
        {
            if (!SandboxCommandUtils.TryResolveCharacter(character, out var index, out label, out var error))
                return error;
            using var selection = em.CreateEntityQuery(typeof(SelectedCharacter));
            if (selection.IsEmpty)
                return "Pas de SelectedCharacter (SC_Entity_Core chargée ?).";
            selection.SetSingleton(new SelectedCharacter { DbIndex = index });
        }

        bool inRun = state == EGameState.Running || state == EGameState.RunStarting || state == EGameState.Paused
                     || state == EGameState.UpgradeSelection || state == EGameState.GameOver;

        SandboxSession.ResetBuild();
        GameManager.Instance.StartRun(EPlanetID.Sandbox, skipIntro: true, clearCurrentRun: inRun);
        return label != null ? $"Sandbox : chargement avec {label}." : "Sandbox : chargement.";
    }
}
#endif
