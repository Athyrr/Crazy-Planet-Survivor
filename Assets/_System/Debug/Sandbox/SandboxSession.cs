#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>What the sandbox tools changed in the current session (scene-de-test), saved into presets: upgrades given
/// (names, in order), stats set, per-spell capability / counter overrides; plus measure results for the window.
/// Reset by `sandbox` and `set_character` (the player restarts from its prefab).</summary>
public static class SandboxSession
{
    public sealed class SpellOverride
    {
        public ESpellID Spell;
        public int Explode = -1; // -1 = unchanged, 0 = off, 1 = on
        public int Emit = -1;
        public int Bounce = -1;  // -1 = unchanged, else the fixed counter
        public int Pierce = -1;
    }

    public static readonly List<string> Upgrades = new List<string>();
    public static readonly List<KeyValuePair<ECharacterStat, float>> FixedStats = new List<KeyValuePair<ECharacterStat, float>>();
    public static readonly List<SpellOverride> SpellOverrides = new List<SpellOverride>();

    public static double MeasureResetTime;
    public static string LastMeasureResult = string.Empty;
    public static string LastTtkResult = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ResetBuild();
        MeasureResetTime = 0;
        LastMeasureResult = string.Empty;
        LastTtkResult = string.Empty;
    }

    public static void ResetBuild()
    {
        Upgrades.Clear();
        FixedStats.Clear();
        SpellOverrides.Clear();
    }

    public static void RecordUpgrade(string name, int count)
    {
        for (int i = 0; i < count; i++)
            Upgrades.Add(name);
    }

    public static void RecordStat(ECharacterStat stat, float value)
    {
        FixedStats.RemoveAll(kv => kv.Key == stat);
        FixedStats.Add(new KeyValuePair<ECharacterStat, float>(stat, value));
    }

    public static void ClearStats() => FixedStats.Clear();

    public static SpellOverride GetOrAddOverride(ESpellID id)
    {
        var o = SpellOverrides.Find(x => x.Spell == id);
        if (o == null)
        {
            o = new SpellOverride { Spell = id };
            SpellOverrides.Add(o);
        }
        return o;
    }

    public static void ForgetSpell(ESpellID id) => SpellOverrides.RemoveAll(x => x.Spell == id);
}
#endif
