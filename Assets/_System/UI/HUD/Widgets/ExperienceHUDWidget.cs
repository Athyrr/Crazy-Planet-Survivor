using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

public class ExperienceHUDWidget : MonoBehaviour
{
    public Slider ExpSlider;
    public TMP_Text ExpText;
    public TMP_Text LevelText;

    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private bool _hasQuery;

    // Cached last-displayed values so we only rebuild the (allocating) text strings when they change.
    private int _lastLevel = int.MinValue;
    private float _lastExp = float.NaN;
    private int _lastReq = int.MinValue;

    void Awake()
    {
        if (ExpSlider == null)
            ExpSlider = GetComponent<Slider>();
    }

    void OnEnable()
    {
        TryCreateQuery();
    }

    void OnDisable()
    {
        DisposeQuery();
    }

    void Update()
    {
        if (!_hasQuery)
        {
            TryCreateQuery();
            if (!_hasQuery)
                return;
        }

        if (_playerQuery.IsEmpty)
            return;

        PlayerExperience playerExperience = _playerQuery.GetSingleton<PlayerExperience>();

        if (playerExperience.Experience != _lastExp ||
            playerExperience.NextLevelExperienceRequired != _lastReq)
        {
            _lastExp = playerExperience.Experience;
            _lastReq = playerExperience.NextLevelExperienceRequired;

            float ratio = Mathf.Clamp01(playerExperience.Experience / playerExperience.NextLevelExperienceRequired);
            ExpSlider.value = ratio;
            ExpText.text = $"{playerExperience.Experience} / {playerExperience.NextLevelExperienceRequired}";
        }

        if (playerExperience.Level != _lastLevel)
        {
            _lastLevel = playerExperience.Level;
            LevelText.text = $"{playerExperience.Level}";
        }
    }

    private void TryCreateQuery()
    {
        if (_hasQuery)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;
        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerExperience>());
        _hasQuery = true;
    }

    private void DisposeQuery()
    {
        if (!_hasQuery)
            return;

        var world = _entityManager.World;
        if (world != null && world.IsCreated)
            _playerQuery.Dispose();

        _hasQuery = false;
    }
}
