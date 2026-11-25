#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Entities;

/// <summary>Sandbox entry. Usable from the main menu, the lobby or a run; no Sandbox entry in any game menu.</summary>
public static class SandboxEntryCommands
{
    [ConsoleCommand("sandbox", "Charge la planète Sandbox (perso : nom ou index ; preset : nom d'un preset JSON ; `-` = perso du preset), sans intro.")]
    private static string Sandbox(string character = "", string preset = "")
    {
        if (!SandboxCommandUtils.TryGetWorld(out _, out var em))
            return SandboxCommandUtils.NoWorld;
        if (GameManager.Instance == null)
            return "Pas de GameManager (scène SC_Main ?).";

        var state = SandboxCommandUtils.GetGameState(em);
        if (state == EGameState.Loading)
            return "Chargement en cours, réessaie après.";

        using (var databases = em.CreateEntityQuery(typeof(PlanetsDatabase)))
        {
            if (databases.IsEmpty)
                return "Scènes de planètes pas encore chargées, réessaie.";
        }
        PlanetSO sandbox = GameManager.Instance.Planets != null ? GameManager.Instance.Planets.Sandbox : null;
        if (sandbox == null)
            return "No Sandbox planet in the planets database (GameManager > Planets).";

        SandboxPreset loaded = null;
        if (!string.IsNullOrWhiteSpace(preset))
        {
            if (!SandboxPresets.TryLoad(preset, out loaded, out var presetError))
                return presetError;
            if (string.IsNullOrWhiteSpace(character) || character == "-")
                character = loaded.Character;
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
        GameManager.Instance.StartRun(sandbox, skipIntro: true, clearCurrentRun: inRun);
        if (loaded != null)
            SandboxRunner.Run(SandboxPresets.Apply(loaded, waitForNewRun: true));
        string withPreset = loaded != null ? $", preset '{preset}' appliqué une fois le joueur prêt" : string.Empty;
        return label != null ? $"Sandbox : chargement avec {label}{withPreset}." : $"Sandbox : chargement{withPreset}.";
    }
}
#endif
