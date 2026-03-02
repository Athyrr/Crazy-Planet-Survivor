using UnityEngine.UI;
using UnityEngine;

/// <summary>
/// UI element for a spell unlocked and active during a run.
/// </summary>
public class UIActiveSpellComponent : MonoBehaviour
{
    public Image Icon;
    public Image Border;

    private int _databaseIndex = -1;

    public int DatabaseIndex => _databaseIndex;

    public void Refresh(SpellDataSO data, int databaseIndex, int level)
    {
        if (data == null)
            return;

        _databaseIndex = databaseIndex;

        if (Icon)
            Icon.sprite = data.Icon;
    }

    public void UpdateLevel(int activeSpellLevel)
    {
    }
}