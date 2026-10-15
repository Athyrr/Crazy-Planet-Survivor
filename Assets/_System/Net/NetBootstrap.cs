using Unity.NetCode;
using UnityEngine.Scripting;

/// <summary>
/// Custom netcode bootstrap. Netcode for Entities would otherwise create client + server worlds for every scene,
/// which breaks the single-player game (SC_Main). We always boot a plain local world exactly like the default
/// Entities bootstrap did before netcode was installed. Client/server worlds are only created on demand by
/// <see cref="NetTestLauncher"/> (SC_NetTest) when the user picks Host or Join.
/// </summary>
[Preserve]
public class NetBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 0; // never auto-connect, the launcher drives connections
        CreateLocalWorld(defaultWorldName);
        return true;
    }
}
