using System;
using System.Collections.Generic;
using _System.ECS.Authorings.Resources;

[Serializable]
public class Save
{
    #region Members

    public bool newSave = true;
    // public SaveClass_GameScore gameScore = new();
    public SaveClass_Ressources ressources = new();
    public SaveClass_MetaUpgrades metaUpgrades = new();
    #endregion
}

#region INTERFACE ISaveClass

public interface ISaveClass
{
    public void StartSaveProcess();
}

#endregion

#region Game Score

/// <summary>
/// Example class use case for save system
/// </summary>
[Serializable]
public class SaveClass_GameScore
{
    #region Constructor

    public SaveClass_GameScore()
    {
        sales = 0;
        trashThrown = 0;
    }

    #endregion

    #region Members

    public int sales;
    public int trashThrown;

    #endregion
}

#endregion

#region Ressources

[Serializable]
public class SaveClass_Ressources
{
    #region Constructor

    public SaveClass_Ressources()
    {
        Ressources = new int[Enum.GetNames(typeof(EResourceType)).Length];
    }

    #endregion

    #region Members
    
    public int[] Ressources; // id == EResourceType

    #endregion
}

#endregion

#region Meta Upgrades

[Serializable]
public class SaveClass_MetaUpgrades
{
    #region Members

    /// <summary>
    /// Stores (stat_name, level) pairs for each purchased meta upgrade.
    /// Empty = no upgrades purchased.
    /// </summary>
    public string[] StatNames = System.Array.Empty<string>();
    public int[] Levels = System.Array.Empty<int>();

    #endregion
}

#endregion
