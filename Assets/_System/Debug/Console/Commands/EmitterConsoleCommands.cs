using Unity.Entities;

public static class EmitterConsoleCommands
{
    [ConsoleCommand("emitter_stats", "Print emitter counters (emitted, frame-cap and lineage-budget drops).")]
    private static string EmitterStatsCommand()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return "No ECS world.";
        using var query = world.EntityManager.CreateEntityQuery(typeof(EmitterStats));
        if (query.IsEmpty)
            return "No EmitterStats singleton (SpellEmissionSystem not running).";
        var s = query.GetSingleton<EmitterStats>();
        return $"emitted last frame: {s.EmittedLastFrame} | peak per frame: {s.PeakEmittedPerFrame} | " +
               $"dropped (frame cap): {s.DroppedFrameCap} | dropped (lineage budget): {s.DroppedLineageBudget} | " +
               $"dropped (invalid): {s.DroppedInvalid}";
    }
}
