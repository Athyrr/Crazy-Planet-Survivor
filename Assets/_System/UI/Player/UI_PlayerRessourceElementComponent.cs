using TMPro;
using UnityEngine;
using Unity.Entities;
using UnityEngine.UI;

public class UI_PlayerRessourceElementComponent : MonoBehaviour
{
    [SerializeField] private TMP_Text _ressourceCountText;
    [SerializeField] private Image _ressourceImage;

    private int _ressourceType;
    
    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private bool _init;
    
    public void Init(int ressourceType, Sprite ressourceImageTexture)
    {
        _ressourceType = ressourceType;
        _ressourceImage.sprite = ressourceImageTexture;
            
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerRessources>());

        _init = true;
    }

    void Update()
    {
        if (!_init || _playerQuery.IsEmpty)
            return;

        PlayerRessources playerRessources = _playerQuery.GetSingleton<PlayerRessources>();
        _ressourceCountText.text = $"{playerRessources.Ressources[_ressourceType]}";
    }
}
