using _System.ECS.Authorings.Resources;
using System.Linq;
using UnityEngine;

/// <summary>
/// Central database of all resource definitions.
/// Single source of truth for resource icons, names, and types.
/// Referenced by HUD and shop widgets — no more duplicated EnumValues.
/// </summary>
[CreateAssetMenu(menuName = "Game/Resource/Resource Database")]
public class ResourceDatabaseSO : ScriptableObject
{
    [SerializeField] private ResourceSO[] _resources;

    public ResourceSO[] Resources => _resources;

    public ResourceSO GetResource(EResourceType type)
    {
        return _resources.FirstOrDefault(r => r.Type == type);
    }

    public Sprite GetIcon(EResourceType type)
    {
        var resource = GetResource(type);
        return resource != null ? resource.Icon : null;
    }

    public string GetDisplayName(EResourceType type)
    {
        var resource = GetResource(type);
        return resource != null ? resource.DisplayName : type.ToString();
    }
}
