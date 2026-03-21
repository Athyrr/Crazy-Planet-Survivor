using System.Linq;
using _System.ECS.Authorings.Ressources;
using UnityEngine;
using UnityEngine.UI;

public class UI_PlayerRessourceComponent : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private UI_PlayerRessourceElementComponent _ressourceElementComponentRef;
    [SerializeField] private HorizontalLayoutGroup _ressourceLayoutGroup;
    
    // todo: implement icon database @hyverno
    [SerializeField] private EnumValues<ERessourceType, Sprite> _ressources;

    void Start()
    {
        var ressources = _ressources.Where(el => el.Key != ERessourceType.Xp);
        foreach ((var key, var sprite) in ressources)
        {
            var instance = Instantiate(
                _ressourceElementComponentRef,
                _ressourceLayoutGroup.transform
            );

            instance.Init((int)key, sprite);
        }
    }
}
