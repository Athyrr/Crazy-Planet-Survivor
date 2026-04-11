using TMPro;
using UnityEngine;
using Unity.Entities;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class RessourceWidgetItem : UIViewItemBase
{
    [SerializeField] private TMP_Text _ressourceCountText;
    [SerializeField] private Image _ressourceImage;

    private int _ressourceType;
    
    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private bool _init;
    private bool _ecsContext;
    
    public void Refresh(int ressourceType, Sprite ressourceImageTexture, int defaultValue = -1)
    {
        _ressourceType = ressourceType;
        _ressourceImage.sprite = ressourceImageTexture;
            
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // todo clean this @hyverno
        _playerQuery = default;
        
        if (defaultValue >= 0)
        {
            _ressourceCountText.text = "" + defaultValue;
            _ecsContext = false;
        }
        else
        {
            _playerQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Player>(),
                ComponentType.ReadOnly<PlayerRessources>());
            _ecsContext = true;
        }

        _init = true;
    }

    void Update()
    {
        if (!_init || !_ecsContext || (_ecsContext && _playerQuery.IsEmpty))
            return;

        PlayerRessources playerRessources = _playerQuery.GetSingleton<PlayerRessources>();
        _ressourceCountText.text = $"{playerRessources.Ressources[_ressourceType -1]}";
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        // throw new System.NotImplementedException();
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        // throw new System.NotImplementedException();
    }
}
