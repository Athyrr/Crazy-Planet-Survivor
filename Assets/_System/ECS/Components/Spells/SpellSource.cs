using Unity.Entities;

/// <summary>Identifies a spell instance's caster and its ROOT spell (index into SpellsDatabase). DatabaseIndex
/// = -1 means "no source" (enemy or environment explosion): no damage tracking, no life steal.</summary>
public struct SpellSource : IComponentData
{
    public Entity CasterEntity;
    public int DatabaseIndex;
}
