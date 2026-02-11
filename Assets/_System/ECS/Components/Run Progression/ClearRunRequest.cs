using Unity.Entities;
using UnityEngine;

/// <summary>
/// Definies a request to clear a run and destroy run scoped entities before returning to lobby.
/// </summary>
public struct ClearRunRequest : IComponentData { }
