using System.Linq;
using _System.ECS.Authorings.Ressources;
using UnityEngine;
using UnityEngine.EventSystems;

public class ResourcesHUDWidget : MonoBehaviour
{
    [Header("Reference")] [SerializeField] private RessourceWidgetItem _ressourceModel;
    [SerializeField] private Transform _container;

    // todo: implement icon database @hyverno
    [SerializeField] private EnumValues<ERessourceType, Sprite> _ressourcesss;


    private void Start()
    {
        var ressources = _ressourcesss.Where(el => el.Key != ERessourceType.Xp);
        foreach ((var key, var sprite) in ressources)
        {
            var instance = Instantiate(_ressourceModel, _container.transform);
            instance.Refresh((int)key, sprite);
        }
    }
    
}