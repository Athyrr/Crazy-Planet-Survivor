using Unity.Collections;
using Unity.Entities;

public struct SpellToIndexMap : IComponentData
{
    public NativeHashMap<SpellKey, int> Map;
}
