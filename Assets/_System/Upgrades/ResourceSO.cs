using _System.ECS.Authorings.Resources;
using UnityEngine;

/// <summary>
/// Defines a resource type: its enum type, display icon, display name, and drop orb prefab.
/// Used by both HUD/shop widgets (via ResourceDatabaseSO) and the loot database for spawning orbs.
/// </summary>
[CreateAssetMenu(menuName = "Game/Resource/Resource Definition")]
public class ResourceSO : ScriptableObject
{
    [SerializeField] private EResourceType _type;
    [SerializeField] private Sprite _icon;
    [Tooltip("Tint applied to the (white) icon sprite wherever this resource is displayed.")]
    [SerializeField] private Color _color = Color.white;
    [SerializeField] private string _displayName;
    [SerializeField] private GameObject _orbPrefab;

    public EResourceType Type => _type;
    public Sprite Icon => _icon;
    public Color Color => _color;
    public string DisplayName => _displayName;
    public GameObject OrbPrefab => _orbPrefab;
}
