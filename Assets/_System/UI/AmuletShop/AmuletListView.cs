using System.Collections.Generic;
using UnityEngine;

public class AmuletListView : MonoBehaviour
{
    [Header("Amulets Container")]
    public Transform AmuletListContainer;

    [Header("Amulet Prefab")] 
    public AmuletUIComponent AmuletUIPrefab;
    
    private AmuletsDatabaseSO _database;
    private AmuletShopUIController _controller;
    private List<AmuletUIComponent> _amuletList = new();
    
    private AmuletUIComponent _selectedAmulet;

    public void Init(AmuletShopUIController controller, AmuletsDatabaseSO database)
    {
        if (database == null)
            return;

        _controller = controller;
        _database = database;
    }

    public void Refresh()
    {
        Clear();

        for (int i = 0; i < _database.Amulets.Length; i++)
        {
            AmuletSO amuletData = _database.Amulets[i];
            AmuletUIComponent amuletComponent = Instantiate(AmuletUIPrefab, AmuletListContainer);

            bool isUnlocked = _controller.IsAmuletUnlocked(i);

            amuletComponent.Init(_controller, i, amuletData, isUnlocked);
            _amuletList.Add(amuletComponent);
        }
    }


    private void Clear()
    {
        foreach (Transform child in AmuletListContainer.transform)
        {
            if (child == null) continue;
            Destroy(child.gameObject);
        }

        _amuletList.Clear();
    }
    
    public void SetSelectedAmulet(int index)
    {
        for (int i = 0; i < _amuletList.Count; i++)
        {
            bool isSelected = (i == index);
            _amuletList[i].SetBorderIcon(isSelected);
        }
    }
}