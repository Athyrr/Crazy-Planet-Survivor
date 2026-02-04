using System.Collections.Generic;
using System.Collections;
using Unity.Entities;
using UnityEngine.UI;
using UnityEngine;

public class UpgradeSelectionUIController : MonoBehaviour
{
    [Header("Animation")]

    public float DelayBetweenCards = 0.5f;
    public float PopDuration = 0.4f;
    public AnimationCurve PopCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // Courbe (Essaie une courbe "Back Out" pour l'effet rebond)

    [Header("Upgrades Layout")]

    public float Spacing = 3.0f;
    public float ArcHeight = 0.5f;
    public float RotationAmount = 10f;

    private List<GameObject> _spawnedCards = new List<GameObject>();

    [Header("Container")]

    public Transform UpgradesContainer;

    [Header("Prefab")]

    public GameObject UpgradePrefab;

    [Header("Layer Mask")]

    public LayerMask Layer = ~0;

    private RunManager RunManager;

    private EntityManager _entityManager;
    private BlobAssetReference<UpgradeBlobs> _upgradesDatabaseRef;
    private EntityQuery _upgradeDatabaseQuery;

    private bool _isInitialized = false;

    private void InitDatabase()
    {
        if (_isInitialized)
            return;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _upgradeDatabaseQuery = _entityManager.CreateEntityQuery(typeof(UpgradesDatabase));
        _isInitialized = true;
    }

    public void DisplaySelection(DynamicBuffer<UpgradeSelectionBufferElement> selection)
    {
        InitDatabase();
        ClearSelection();

        if (_upgradeDatabaseQuery.IsEmptyIgnoreFilter)
            return;

        var upgradeDatabaseEntity = _upgradeDatabaseQuery.GetSingletonEntity();
        _upgradesDatabaseRef = _entityManager.GetComponentData<UpgradesDatabase>(upgradeDatabaseEntity).Blobs;

        ref var upgradesDatabase = ref _upgradesDatabaseRef.Value.Upgrades;


        List<Transform> cardsTransforms = new List<Transform>();

        for (int i = 0; i < selection.Length; i++)
        {
            int dbIndex = selection[i].DatabaseIndex;
            if (dbIndex < 0 || dbIndex >= upgradesDatabase.Length) continue;

            ref UpgradeBlob upgradeData = ref upgradesDatabase[dbIndex];

            GameObject upgradeObject = Instantiate(UpgradePrefab, UpgradesContainer);
            _spawnedCards.Add(upgradeObject);
            cardsTransforms.Add(upgradeObject.transform);

            // Setup Data
            var upgradeUIComp = upgradeObject.GetComponent<UpgradeUIComponent>();
            if (upgradeUIComp != null) upgradeUIComp.SetData(ref upgradeData);

            // @todo Handle with raycast
            UnityEngine.UI.Button button = upgradeObject.GetComponentInChildren<Button>();
            if (button != null)
            {
                button.interactable = false;
                button.onClick.AddListener(() => OnUpgradeSelected(dbIndex));
            }

            upgradeObject.transform.localScale = Vector3.zero;
        }

        ApplyLayout(cardsTransforms);

        StartCoroutine(AnimateCardsEntry());
    }

    public void OnUpgradeSelected(int databaseIndex)
    {
        InitDatabase();

        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new ApplyUpgradeRequest
        {
            DatabaseIndex = databaseIndex
        });

        RunManager.TogglePause();
    }

    private void ClearSelection()
    {
        StopAllCoroutines();
        foreach (Transform child in UpgradesContainer)
        {
            Destroy(child.gameObject);
        }
        _spawnedCards.Clear();
    }

    public void Init(RunManager runManager)
    {
        RunManager = runManager;
    }

    private IEnumerator AnimateCardsEntry()
    {
        foreach (var card in _spawnedCards)
        {
            StartCoroutine(AnimateSingleCardPop(card.transform));

            //var btn = card.GetComponentInChildren<Button>();
            //if (btn) btn.interactable = true;

            yield return new WaitForSeconds(DelayBetweenCards);
        }
    }
    private IEnumerator AnimateSingleCardPop(Transform target)
    {
        float elapsed = 0f;
        //Vector3 finalScale = new Vector3(1, 1.3f, 0.3f);
        Vector3 finalScale = Vector3.one;

        while (elapsed < PopDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / PopDuration;

            float scaleValue = PopCurve.Evaluate(t);

            if (target != null)
                target.localScale = finalScale * scaleValue;

            yield return null;
        }

        if (target != null)
            target.localScale = finalScale;
    }

    public void ApplyLayout(List<Transform> cards)
    {
        int count = cards.Count;
        if (count == 0) return;

        float totalWidth = (count - 1) * Spacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < count; i++)
        {
            Transform card = cards[i];

            // Horizontal position
            float xPos = startX + (i * Spacing);

            // Arc position and rotation
            float normalizedPos = count > 1 ? (float)i / (count - 1) : 0.5f;
            float xSym = (normalizedPos - 0.5f) * 2f;
            float yPos = -Mathf.Abs(xSym) * ArcHeight;
            float rotZ = -xSym * RotationAmount;
            float rotY = xSym * RotationAmount * 0.5f;
            //float rotY = card.rotation.y + -180 * i;

            card.localPosition = new Vector3(xPos, yPos, 0);
            card.localRotation = Quaternion.Euler(-90, -180, rotZ);
        }
    }
}

