using System.Linq;
using _System.ECS.Authorings.Ressources;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIPlayerRessourceComponent : UIViewItemBase
{
    [Header("Reference")] [SerializeField] private UI_PlayerRessourceElementComponent _ressourceModel;
    [SerializeField] private Transform _container;

    // todo: implement icon database @hyverno
    [SerializeField] private EnumValues<ERessourceType, Sprite> _ressources;


    private void Start()
    {
        var ressources = _ressources.Where(el => el.Key != ERessourceType.Xp);
        foreach ((var key, var sprite) in ressources)
        {
            var instance = Instantiate(_ressourceModel, _container.transform);
            instance.Refresh((int)key, sprite);
        }
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
    }
}