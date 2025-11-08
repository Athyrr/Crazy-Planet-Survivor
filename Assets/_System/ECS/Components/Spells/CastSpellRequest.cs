using Unity.Entities;

public struct CastSpellRequest : IComponentData
{
    public Entity Caster;
    public Entity Target;

    public int DatabaseIndex;
}

public struct FireballRequestTag : IComponentData { }
public struct IceBoltRequestTag : IComponentData { }
public struct LightningStrikeRequestTag : IComponentData { }
public struct MagicMissileRequestTag : IComponentData { }
public struct RichochetShotRequestTag : IComponentData { }