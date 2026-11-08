using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Row of the player's equipped spells shown inside the upgrade selection screen. Built when a selection
/// opens, highlights the spells the hovered upgrade card affects. Place it as a child of the selection view.
/// </summary>
public class UpgradeSpellStripView : MonoBehaviour
{
    public UpgradeSelectionView SelectionView;
    public Transform Container;
    public ActiveSpellWidgetItem ItemPrefab;

    private readonly Dictionary<int, ActiveSpellWidgetItem> _items = new Dictionary<int, ActiveSpellWidgetItem>();

    private void OnEnable()
    {
        if (SelectionView == null)
            SelectionView = GetComponentInParent<UpgradeSelectionView>(true);
        if (SelectionView == null)
            return;

        SelectionView.SelectionShown += Build;
        SelectionView.HighlightChanged += Apply;
    }

    private void OnDisable()
    {
        if (SelectionView == null)
            return;

        SelectionView.SelectionShown -= Build;
        SelectionView.HighlightChanged -= Apply;
    }

    private void Build(UpgradeDisplayContext context)
    {
        if (Container == null)
            Container = transform;
        foreach (Transform child in Container)
            Destroy(child.gameObject);
        _items.Clear();

        if (context.ActiveSpells == null || context.SpellsDatabase == null || context.SpellsDatabase.Spells == null)
            return;

        var spells = context.SpellsDatabase.Spells;
        foreach (var active in context.ActiveSpells)
        {
            int idx = active.DatabaseIndex;
            if (idx < 0 || idx >= spells.Length || spells[idx] == null || _items.ContainsKey(idx))
                continue;

            var item = Instantiate(ItemPrefab, Container);
            item.Refresh(spells[idx], idx, active.Level);
            item.HideCooldown();
            _items.Add(idx, item);
        }
    }

    private void Apply(UpgradeHighlightTargets targets)
    {
        foreach (var pair in _items)
            pair.Value.SetHighlighted(targets.SpellIndices.Contains(pair.Key));
    }
}
