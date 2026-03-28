using System;
using System.Collections.Generic;
using _System.ECS.Authorings.Ressources;

[Serializable]
public class Save
{
    #region Members

    public bool newSave = true;
    // public SaveClass_GameScore gameScore = new();
    public SaveClass_Ressources ressources = new();
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
        Ressources = new int[Enum.GetNames(typeof(ERessourceType)).Length];
    }

    #endregion

    #region Members
    
    public int[] Ressources; // id == ERessourceType

    #endregion
}

#endregion
