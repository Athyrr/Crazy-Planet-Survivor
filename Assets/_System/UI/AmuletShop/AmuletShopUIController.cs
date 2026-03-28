using System;
using System.Linq;
using _System.ECS.Authorings.Ressources;
using UnityEngine.UI;
using UnityEngine;
using TMPro;
using Unity.Entities;

public class AmuletShopUIController : MonoBehaviour
{
    [Header("Database")] 
    public AmuletsDatabaseSO Database;

    [Header("Amulet Views")]
    public AmuletListView AmuletListView;
    public AmuletDetailView AmuletDetailView;

    [Header("Equip/Purchase Button")]
    public Button ButtonRef;
    public TMP_Text ButtonText;

    public string PurchaseText = String.Empty;
    public Color PurchaseColor = Color.yellow;

    public string EquipText = String.Empty;
    public Color EquipColor = Color.white;


    private int _currentSelectedAmuletIndex = -1;
    private bool _isCurrentSelectedAmuletUnlocked = false;
    private AmuletSO _currentSelectedAmuletData;

    private EntityManager _entityManager;
    private EntityQuery _gameStateQuery;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _gameStateQuery = _entityManager.CreateEntityQuery(typeof(GameState));

        _currentSelectedAmuletIndex = -1;
        AmuletListView.Init(this, Database);
    }

    public void OpenView()
    {
        AmuletListView.Refresh();

        int startingIndex = 0;

        if (!_gameStateQuery.IsEmpty)
        {
            var gameStateEntity = _gameStateQuery.GetSingletonEntity();

            // Has equipped amulet
            if (_entityManager.HasComponent<EquippedAmulet>(gameStateEntity))
            {
                var equipped = _entityManager.GetComponentData<EquippedAmulet>(gameStateEntity);
                if (equipped.DbIndex >= 0 && equipped.DbIndex < Database.Amulets.Length)
                {
                    startingIndex = equipped.DbIndex;
                }
            }
        }

        // Preview starting amulet
        if (Database != null && Database.Amulets.Length > 0)
        {
            AmuletSO startingAmulet = Database.Amulets[startingIndex];
            bool isUnlocked = IsAmuletUnlocked(startingIndex);

            PreviewAmulet(startingAmulet, startingIndex, isUnlocked);
        }
        else // if no amulets in database, just clear the detail view and disable button
        {
            _currentSelectedAmuletIndex = -1;
            RefreshDetailView();
            RefreshButton();
        }
    }
    
    public void CloseView()
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);
    }
    
    public void PreviewAmulet(AmuletSO data, int index, bool isUnlocked)
    {
        _currentSelectedAmuletIndex = index;
        _currentSelectedAmuletData = data;
        _isCurrentSelectedAmuletUnlocked = isUnlocked;

        AmuletDetailView.Refresh(data, isUnlocked);
        RefreshButton();
        
        AmuletListView.SetSelectedAmulet(index);
    }

    private void RefreshDetailView()
    {
        if (_currentSelectedAmuletIndex == -1)
        {
            AmuletDetailView.Clear();
            if (ButtonRef != null)
                ButtonRef.gameObject.SetActive(false);
        }
        else
        {
            AmuletDetailView.Refresh(_currentSelectedAmuletData, _isCurrentSelectedAmuletUnlocked);
            RefreshButton();
        }
    }
    
    private void RefreshButton()
        {
            if (ButtonRef == null)
                return;
    
            ButtonRef.gameObject.SetActive(true);
            // ButtonRef.image.color = _isCurrentSelectedAmuletUnlocked ? EquipColor : PurchaseColor;
            ButtonText.color = _isCurrentSelectedAmuletUnlocked ? EquipColor : PurchaseColor;
    
            if (ButtonText != null)
                ButtonText.text = _isCurrentSelectedAmuletUnlocked ? EquipText : PurchaseText;
        }
    
    public void PurchaseOrEquipAmulet()
        {
            if (_isCurrentSelectedAmuletUnlocked)
                EquipAmulet();
            else
                PurchaseAmulet();
        }

    private void PurchaseAmulet()
    {
        if (_currentSelectedAmuletIndex < 0 || _currentSelectedAmuletIndex >= Database.Amulets.Length)
            return;

        // todo feedback amulet price
        if (!CanBuyAmulet(out var cost))
            return;

        BuyAmulet(cost);
        
        if (!_gameStateQuery.IsEmpty)
        {
            var gameStateEntity = _gameStateQuery.GetSingletonEntity();
            var unlockedBuffer = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);
            unlockedBuffer.Add(new UnlockedAmulet { DbIndex = _currentSelectedAmuletIndex });
        }

        _isCurrentSelectedAmuletUnlocked = true;

        // Refresh views
        AmuletListView.Refresh();
        RefreshDetailView();
    }

    private void BuyAmulet(int[] cost)
    {
        var ressources = SaveManager.GetCurrentSaveAs<Save>().ressources.Ressources;
        
        foreach (var el in cost.ToList())
        {
            if (el != 0)
                ressources[el] -= _currentSelectedAmuletData.RessourcesPrice[(ERessourceType)el];
        }
    }

    private bool CanBuyAmulet(out int[] cost)
    {
        var ressources = SaveManager.GetCurrentSaveAs<Save>().ressources.Ressources;

        cost = new int[Enum.GetNames(typeof(ERessourceType)).Length];
        var res = true;
        
        foreach (var el in _currentSelectedAmuletData.RessourcesPrice.ToList())
        {
            var idx = (int)el.Key;
            if (ressources[idx] >= el.Value)
            {
                cost[(idx)] = idx;
            }
            else
            {
                res = false;
                break;
            }            
        }

        return res;
    }

    private void EquipAmulet()
    {
        if (_gameStateQuery.IsEmpty)
            return;

        if (_currentSelectedAmuletIndex == -1)
            return;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        _entityManager.SetComponentData(gameStateEntity,
            new EquippedAmulet() { DbIndex = _currentSelectedAmuletIndex });

        GameManager.Instance.ChangeState(EGameState.Lobby);
    }
    
    public bool IsAmuletUnlocked(int index)
    {
        if (_gameStateQuery.IsEmptyIgnoreFilter)
            return false;

        var gameStateEntity = _gameStateQuery.GetSingletonEntity();
        if (!_entityManager.HasBuffer<UnlockedAmulet>(gameStateEntity))
            return false;

        var unlockedAmulets = _entityManager.GetBuffer<UnlockedAmulet>(gameStateEntity);

        foreach (UnlockedAmulet unlockedAmulet in unlockedAmulets)
        {
            if (unlockedAmulet.DbIndex == index)
                return true;
        }

        return false;

    }
}