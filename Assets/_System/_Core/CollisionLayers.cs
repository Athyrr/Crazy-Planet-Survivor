public static class CollisionLayers
{
    public const uint Player = 1 << 0;
    public const uint Enemy = 1 << 1;

    public const uint PlayerProjectile = 1 << 2;
    public const uint EnemyProjectile = 1 << 3;

    public const uint Obstacle = 1 << 4;

    public const uint ExpOrb = 1 << 5;

    public const uint Landscape = 1 << 6;

    public const uint Raycast = 1 << 7;
}
