using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Shows the name of the interactable building the player is close enough to interact with.
/// </summary>
public class LobbyInteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _label;

    [Header("Building names (per interaction type)")]
    [SerializeField] private string _planetSelectionName = "Galaxy";
    [SerializeField] private string _characterSelectionName = "Characters";
    [SerializeField] private string _amuletShopName = "Amulets";
    [SerializeField] private string _metaProgressionName = "Upgrades";

    private EntityManager _entityManager;
    private EntityQuery _inRangeQuery;
    private EntityQuery _playerQuery;
    private bool _initialized;
    private bool _shown;

    private void Awake()
    {
        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(includeInactive: true);
    }

    private void OnEnable() => Hide();

    private void OnDisable() => Hide();

    private void EnsureInit()
    {
        if (_initialized)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;

        // Including the enableable tag in the query means only in-range buildings are matched.
        _inRangeQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Interactable>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<InteractableInRangeTag>());

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<LocalTransform>());

        _initialized = true;
    }

    private void Update()
    {
        // The prompt only exists while walking the lobby.
        if (GameManager.Instance == null || GameManager.Instance.GetGameState() != EGameState.Lobby)
        {
            Hide();
            return;
        }

        EnsureInit();
        if (!_initialized)
            return;

        // Nothing enabled in range → nothing to prompt.
        if (_inRangeQuery.IsEmpty)
        {
            Hide();
            return;
        }

        if (TryGetNearestInteractable(out Interactable nearest))
            Show(ResolveName(nearest.InteractionType));
        else
            Hide();
    }

    /// <summary>Picks the in-range building closest to the player (falls back to the first when no player).</summary>
    private bool TryGetNearestInteractable(out Interactable nearest)
    {
        nearest = default;

        var entities = _inRangeQuery.ToEntityArray(Allocator.Temp);
        var interactables = _inRangeQuery.ToComponentDataArray<Interactable>(Allocator.Temp);
        var transforms = _inRangeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        bool found = false;
        try
        {
            if (entities.Length == 0)
                return false;

            bool hasPlayer = _playerQuery.CalculateEntityCount() == 1;
            float3 playerPos = float3.zero;
            if (hasPlayer)
                playerPos = _entityManager.GetComponentData<LocalTransform>(_playerQuery.GetSingletonEntity()).Position;

            float bestDistSq = float.MaxValue;
            for (int i = 0; i < entities.Length; i++)
            {
                float distSq = hasPlayer ? math.distancesq(playerPos, transforms[i].Position) : 0f;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = interactables[i];
                    found = true;
                }
            }
        }
        finally
        {
            entities.Dispose();
            interactables.Dispose();
            transforms.Dispose();
        }

        return found;
    }

    private string ResolveName(EInteractionType type)
    {
        switch (type)
        {
            case EInteractionType.PlanetSelection: return _planetSelectionName;
            case EInteractionType.CharacterSelection: return _characterSelectionName;
            case EInteractionType.AmuletShop: return _amuletShopName;
            case EInteractionType.MetaProgression: return _metaProgressionName;
            default: return type.ToString();
        }
    }

    private void Show(string text)
    {
        if (_label == null)
            return;

        if (!_shown)
        {
            _label.gameObject.SetActive(true);
            _shown = true;
        }

        if (_label.text != text)
            _label.text = text;
    }

    private void Hide()
    {
        if (_label == null)
            return;

        if (_shown || _label.gameObject.activeSelf)
        {
            _label.gameObject.SetActive(false);
            _shown = false;
        }
    }
}
