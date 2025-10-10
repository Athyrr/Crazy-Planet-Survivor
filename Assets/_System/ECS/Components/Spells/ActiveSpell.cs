using Unity.Entities;
/// <summary>
/// Represents a spell in runtime with its coolodwn and datas.
/// </summary>
public struct ActiveSpell : IBufferElementData
{
    public int DatabaseIndex;

    public float CooldownTimer;
    public int Level;

}
