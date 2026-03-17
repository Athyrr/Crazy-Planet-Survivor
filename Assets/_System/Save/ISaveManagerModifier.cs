public interface ISaveManagerModifier
{
    public Save CreateSave();
    public Save ReadSaveFromFile(string content);
    public string GetSaveContent();
}