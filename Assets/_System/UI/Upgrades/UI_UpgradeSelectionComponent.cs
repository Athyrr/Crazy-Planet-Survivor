using Unity.Entities;
using UnityEngine.UI;
using UnityEngine;

public class UI_UpgradeSelectionComponent : MonoBehaviour
{
    public GameManager GameManager;
    public GameObject UpgradeUIPrefab;
    public Transform UpgradesContainer;

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

        var upgradeDatabaseEntity = _upgradeDatabaseQuery.GetSingletonEntity();
        _upgradesDatabaseRef = _entityManager.GetComponentData<UpgradesDatabase>(upgradeDatabaseEntity).Blobs;

        ref var upgradesDatabase = ref _upgradesDatabaseRef.Value.Upgrades;

        for (int i = 0; i < selection.Length; i++)
        {
            int dbIndex = selection[i].DatabaseIndex;
            ref UpgradeBlob upgradeData = ref upgradesDatabase[dbIndex];

            GameObject upgradeUIGameObject = Instantiate(UpgradeUIPrefab, UpgradesContainer);

            //@todo Set upgrade data
            var upgradeUIComp = upgradeUIGameObject.GetComponent<UI_UpgradeComponent>();
            upgradeUIComp.SetData(ref upgradeData);

            Button button = upgradeUIGameObject.GetComponent<Button>();
            button.onClick.AddListener(() => OnUpgradeSelected(dbIndex));
        }
    }

    public void OnUpgradeSelected(int databaseIndex)
    {
        InitDatabase();

        var requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new ApplyUpgradeRequest
        {
            DatabaseIndex = databaseIndex
        });

        GameManager.TogglePause();
    }

    private void ClearSelection()
    {
        foreach (Transform child in UpgradesContainer)
        {
            Destroy(child.gameObject);
        }
    }

}
