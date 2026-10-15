using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

// Network test prototype (SC_NetTest) data. Deliberately separate from the single-player components
// (Player, Enemy, LinearMovement...) so none of the solo gameplay systems pick these entities up.

/// <summary>Networked player ghost (owner predicted). Speed is baked, never changes, so it is not replicated.</summary>
public struct NetPlayer : IComponentData
{
    public float Speed;
}

/// <summary>
/// Player input sent every tick to the server and replayed during prediction. MoveDirection is already a
/// world-space direction (camera relative) computed by the owning client; its length is the stick throttle.
/// </summary>
public struct NetPlayerInput : IInputComponentData
{
    public float3 MoveDirection;
}

/// <summary>Server-simulated enemy, replicated to clients as an interpolated ghost. Only the transform is sent.</summary>
public struct NetEnemy : IComponentData
{
    public float Speed;
    public float StopDistance;
}

/// <summary>Singleton baked in the net test planet sub scene.</summary>
public struct NetTestConfig : IComponentData
{
    public Entity PlayerPrefab;
    public Entity EnemyPrefab;
    public float EnemySpawnMinDistance;
    public float EnemySpawnMaxDistance;
    public int MaxSpawnPerTick;
}

/// <summary>Server-only singleton: number of enemies the server keeps alive (changed by the host debug UI).</summary>
public struct NetEnemyTarget : IComponentData
{
    public int Count;
}

/// <summary>Client -> server RPC sent once the connection is established.</summary>
public struct NetGoInGameRequest : IRpcCommand { }
