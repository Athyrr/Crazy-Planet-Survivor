using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

public class UpgradeSelectionUIController : MonoBehaviour
{
    [Header("Interaction")]
    public Camera UICamera;
    public LayerMask UI3DLayer;

    [Header("References")]
    public Transform UpgradesContainer;
    public GameObject UpgradePrefab;

    [Header("Layout Settings")]
    public float Spacing = 3.5f;
    public float ArcHeight = 0.5f;
    public float RotationAmount = 10f;

    [Header("Animation Settings")]
    public float DelayBetweenCards = 0.15f;
    public float PopDuration = 0.4f;
    public AnimationCurve PopCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private List<GameObject> _spawnedCards = new List<GameObject>();
    private UpgradeUIComponent _currentHoveredCard;
    private bool _canInteract = false;

    private RunManager RunManager;
    private EntityManager _entityManager;
    private EntityQuery _upgradeDatabaseQuery;
    private bool _isInitialized = false;

    private void Awake()
    {
        InitDatabase();
    }

    private void InitDatabase()
    {
        if (_isInitialized)
            return;
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _upgradeDatabaseQuery = _entityManager.CreateEntityQuery(typeof(UpgradesDatabase));
        _isInitialized = true;
    }

    private void Update()
    {
        if (!_canInteract)
            return;

        HandleHoverAndClick();
    }

    private void HandleHoverAndClick()
    {
        if (UICamera == null)
            return;

        if (!_canInteract)
            return;

        Vector2 pointerPos = Vector2.zero;
        bool isClicked = false;

        if (Mouse.current != null)
        {
            pointerPos = Mouse.current.position.ReadValue();
            isClicked = Mouse.current.leftButton.wasPressedThisFrame;
        }
        else if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0)
        {
            var touch = Touchscreen.current.touches[0];
            pointerPos = touch.position.ReadValue();
            isClicked = touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began;
        }

        Ray ray = UICamera.ScreenPointToRay(pointerPos);
        UpgradeUIComponent hitCard = null;

        if (Physics.Raycast(ray, out RaycastHit hitInfo, 1000f, UI3DLayer))
            hitCard = hitInfo.collider.GetComponentInParent<UpgradeUIComponent>();

        if (hitCard != _currentHoveredCard)
        {
            if (_currentHoveredCard != null)
                _currentHoveredCard.SetHovered(false);

            if (hitCard != null)
                hitCard.SetHovered(true);

            _currentHoveredCard = hitCard;
        }

        if (isClicked && _currentHoveredCard != null)
            OnUpgradeSelected(_currentHoveredCard.DbIndex);
    }

    public void DisplaySelection(DynamicBuffer<UpgradeSelectionBufferElement> selection)
    {
        InitDatabase();
        ClearSelection();
        _canInteract = false;

        if (_upgradeDatabaseQuery.IsEmptyIgnoreFilter)
            return;

        var dbEntity = _upgradeDatabaseQuery.GetSingletonEntity();
        var blobs = _entityManager.GetComponentData<UpgradesDatabase>(dbEntity).Blobs;
        ref var upgradesDatabase = ref blobs.Value.Upgrades;

        List<Transform> cardsTransforms = new List<Transform>();

        // Spawn
        for (int i = 0; i < selection.Length; i++)
        {
            int dbIndex = selection[i].DatabaseIndex;
            if (dbIndex < 0 || dbIndex >= upgradesDatabase.Length)
                continue;

            ref UpgradeBlob upgradeData = ref upgradesDatabase[dbIndex];

            GameObject cardObj = Instantiate(UpgradePrefab, UpgradesContainer);
            _spawnedCards.Add(cardObj);
            cardsTransforms.Add(cardObj.transform);

            // Setup Data
            var uiComp = cardObj.GetComponent<UpgradeUIComponent>();
            if (uiComp != null)
                uiComp.SetData(ref upgradeData, dbIndex);

            cardObj.transform.localScale = Vector3.zero;
        }

        //Layout
        ApplyLayout(cardsTransforms);

        // Animation
        StartCoroutine(AnimateCardsEntry());
    }

    private void ApplyLayout(List<Transform> cards)
    {
        int count = cards.Count;
        if (count == 0)
            return;

        float totalWidth = (count - 1) * Spacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            Transform card = cards[i];

            // Calcul Position
            float xPos = startX + (i * Spacing);
            float normalizedPos = count > 1 ? (float)i / (count - 1) : 0.5f;

            // Calcul Arc
            float xSym = (normalizedPos - 0.5f) * 2f; // -1 � 1
            float yPos = -Mathf.Abs(xSym) * ArcHeight;

            // Calcul Rotation
            float rotZ = -xSym * RotationAmount;

            card.localPosition = new Vector3(xPos, yPos, 0);
            card.localRotation = Quaternion.Euler(-90, -180, rotZ);
        }
    }

    private IEnumerator AnimateCardsEntry()
    {
        foreach (var card in _spawnedCards)
        {
            StartCoroutine(AnimateSingleCardPop(card.transform));
            yield return new WaitForSecondsRealtime(DelayBetweenCards);
        }
        _canInteract = true;
    }

    private IEnumerator AnimateSingleCardPop(Transform target)
    {
        float elapsed = 0f;
        Vector3 finalScale = Vector3.one;

        while (elapsed < PopDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / PopDuration;
            float scaleValue = PopCurve.Evaluate(t);

            if (target != null)
                target.localScale = finalScale * scaleValue;
            yield return null;
        }
        if (target != null)
            target.localScale = finalScale;
    }

    public void OnUpgradeSelected(int databaseIndex)
    {
        _canInteract = false;

        InitDatabase();
        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(
            requestEntity,
            new ApplyUpgradeRequest { DatabaseIndex = databaseIndex }
        );

        RunManager.TogglePause();
    }

    private void ClearSelection()
    {
        StopAllCoroutines();

        foreach (var obj in _spawnedCards)
            if (obj)
                Destroy(obj);

        _spawnedCards.Clear();
        _currentHoveredCard = null;
    }

    public void Init(RunManager runManager)
    {
        RunManager = runManager;
    }
}
