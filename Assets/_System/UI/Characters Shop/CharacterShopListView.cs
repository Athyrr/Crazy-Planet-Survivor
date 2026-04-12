/// <summary>
/// Represents le container of all playables characters displayed in CHaracter Selection UI
/// </summary>
public class CharacterShopListView : ShopListViewBase<CharacterShopViewItem>
{
    public void Init(CharacterShopUIController controller, CharactersDatabaseSO database, System.Func<int, bool> isUnlockedCheck)
    {
        Clear();
        for (int i = 0; i < database.Characters.Length; i++)
        {
            var item = GetOrCreateItem();
            item.Init(controller, i, database.Characters[i], isUnlockedCheck(i));
        }
    }
}