using UnityEditor;
using UnityEngine;

public class GameStatisticsToggle
{
    private const string DEFINE_SYMBOL = "ENABLE_STATISTICS";

    [MenuItem("Tools/Game Statistics/Toggle Game Statistics")]
    public static void ToggleStatistics()
    {
        var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);

        if (defines.Contains(DEFINE_SYMBOL))
        {
            // Remove the symbol
            if (defines.Contains(";" + DEFINE_SYMBOL))
            {
                defines = defines.Replace(";" + DEFINE_SYMBOL, "");
            }
            else if (defines.Contains(DEFINE_SYMBOL + ";"))
            {
                defines = defines.Replace(DEFINE_SYMBOL + ";", "");
            }
            else
            {
                defines = defines.Replace(DEFINE_SYMBOL, "");
            }
            Debug.Log("Game Statistics Disabled");
        }
        else
        {
            // Add the symbol
            if (string.IsNullOrEmpty(defines))
            {
                defines = DEFINE_SYMBOL;
            }
            else
            {
                defines += ";" + DEFINE_SYMBOL;
            }
            Debug.Log("Game Statistics Enabled");
        }

        PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
    }

    [MenuItem("Tools/Game Statistics/Toggle Game Statistics", true)]
    public static bool ToggleStatisticsValidate()
    {
        var targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
        Menu.SetChecked("Tools/Game Statistics/Toggle Game Statistics", defines.Contains(DEFINE_SYMBOL));
        return true;
    }
}
