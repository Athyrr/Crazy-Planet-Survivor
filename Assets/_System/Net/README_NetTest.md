# Network test prototype (Netcode for Entities + EOS)

Isolated prototype: solo game (`SC_Main`) is untouched and keeps running in a single local world.
Everything lives in `Assets/_System/Net`, `Assets/_Prefabs/Net`, `Assets/_Scenes/NetTest`.

- Netcode for Entities 1.13.0 / Unity Transport 2.7.2 (versions bundled with Unity 6000.4.6, OK with Entities 6.4)
- PlayEveryWare EOS Plugin for Unity 6.2.0 (EOS SDK 1.19)
- Topology: **peer-to-peer listen server**. The host process runs a server world + its own client world
  (IPC between them); other players connect to the host. No dedicated server, no Unity Relay.
- Transport: **EOS P2P** (Connect interface, Device ID login, NAT relays allowed). Plain UDP over Unity Transport
  is kept only as a dev fallback (LAN/localhost).

## Launch

### In the editor (one instance)
Open `Assets/_Scenes/NetTest/SC_NetTest.unity`, Play, pick a transport, click **Host**.
Use the **Enemies** buttons (0/100/300/500/1000) to change the server-side enemy count. WASD / left stick to move.
Netcode's own tools: *Window > Multiplayer > PlayMode Tools* and *NetDbg* (per ghost type / component bandwidth).

### Two instances
1. Build the test player: menu **Crazy Planet > Net Test > Build Windows Player (SC_NetTest only)**
   (or batch mode, see `NetTestSceneBuilder.BuildWindowsPlayer`). Output: `Builds/NetTest/CPS_NetTest.exe`.
2. Instance A (editor or exe): **Host**.
3. Instance B (exe or editor): **Join**.
   - EOS: leave *Host PUID* empty to join the first lobby of the `cps-nettest:global` bucket, or paste the
     ProductUserId shown (and copyable) in the host's overlay.
   - UDP fallback: address `127.0.0.1` (or the host LAN IP), port 7979.

Command line (exe): `-nettest-host` | `-nettest-join [address]`, `-nettest-transport udp|eos`, `-nettest-port N`,
`-nettest-host-id <PUID>`, `-nettest-enemies N`, `-nettest-enemy-steps 0,100,300,500 -nettest-step-seconds 25`,
`-nettest-bot` (local player wanders by itself), `-nettest-log stats.csv`, `-nettest-quit-after S`,
`-nettest-screenshot shot.png`. Example headless bandwidth run:

```
CPS_NetTest.exe -batchmode -nographics -nettest-host -nettest-transport udp -nettest-enemy-steps 0,100,300,500 -nettest-step-seconds 25 -nettest-bot -nettest-log host.csv -nettest-quit-after 110
CPS_NetTest.exe -batchmode -nographics -nettest-join 127.0.0.1 -nettest-transport udp -nettest-bot -nettest-log client.csv -nettest-quit-after 108
```

## EOS setup (needed once, per developer machine)
1. Epic Developer Portal: create a product. Note **ProductId, SandboxId, DeploymentId**.
   Product Settings > Clients: create a client (policy with P2P + Lobbies + Connect, e.g. "Peer2Peer"),
   note **ClientId / ClientSecret**.
   Product Settings > Identity Providers: Device ID is available by default for Connect.
2. Unity: **Tools > EOS Plugin > EOS Configuration**, fill the IDs, save. The plugin writes them to
   `Assets/StreamingAssets/EOS/*.json` — this folder is **git-ignored** (it contains the client secret).
3. Host/Join with transport *EOS P2P*. The overlay shows each step (SDK init, device id, login, lobby).

## Architecture

| Piece | File | Notes |
|---|---|---|
| Bootstrap | `NetBootstrap.cs` | Always creates the plain local world (solo game unchanged). Client/server worlds are created by the launcher. |
| Launcher / overlay | `NetTestLauncher.cs` | Host/Join, transport choice, enemy count, bandwidth overlay, CLI automation, CSV log. |
| Player ghost | `NetPlayerAuthoring`, `NetPlayerMoveSystem`, `NetPlayerInputSystem` | Owner-predicted. Input = camera-relative world direction (`IInputComponentData`). Raycast terrain follow, deterministic for prediction. |
| Enemy ghosts | `NetEnemyAuthoring`, `NetEnemySystems.cs` | Server-only simulation, interpolated on clients, chase the **nearest** player. `NetEnemyTarget` (server singleton) = count to keep alive. |
| Connection flow | `NetGoInGameSystems.cs` | GoInGame RPC, player spawned per connection, linked to the connection entity. |
| Camera | `NetTestCamera.cs` | Follows `GhostOwnerIsLocal` player, parallel-transported screen up. |
| Bandwidth | `NetStatsSystem.cs` | Unity Transport driver statistics per world, once a second (same numbers for UDP and EOS). |
| EOS transport | `Eos/EosNetworkInterface.cs`, `Eos/EosP2PSocket.cs` | `INetworkInterface` over EOS P2P: unreliable-unordered packets, 1170 B MTU, ProductUserId <-> fake endpoint mapping, main-thread send/receive. |
| EOS session | `Eos/EosSession.cs` | EOSManager init, Device ID Connect login (creates the product user on first run), lobby create / search / join, or manual host PUID. |
| Content generator | `Editor/NetTestSceneBuilder.cs` | Rebuilds prefabs + scenes, builds the test player. |

`NetCodePhysicsConfig` (Always Run) sits in the planet sub scene: netcode otherwise skips the physics group when no
predicted physics ghost exists, and all movement raycasts the planet collider.

## Porting the real game (not done in this prototype)
Single-player assumptions to remove before the real gameplay can run in client/server worlds:

- `GetSingletonEntity<Player>` / `TryGetSingletonEntity<Player>`: ActiveEffects, AreaAttack, Avoidance,
  CameraTargetUpdater, Collision (x2), EnemiesSpawner, EnemyTargeting, FlowField, HazardZone, CharacterSelection,
  LobbyInteraction, PlayerProgression, RunInitialization, SpellCasting, UpgradeSelection.
  - local-only logic (camera, HUD, upgrade UI): query `Player` + `GhostOwnerIsLocal` on the client.
  - per-player logic (spells, progression, collisions): iterate all players on the server.
  - enemy targeting / flow field: nearest player (see `NetEnemyMoveSystem`), or one flow field seeded by all players.
- `GetSingleton<InputData>` (PlayerInputSystem, LobbyInteraction): becomes an `IInputComponentData` on each player ghost.
- `GameState` singleton: `UpgradeSelection` pauses the whole simulation — in multiplayer the server can't pause for
  one player; needs per-player upgrade selection (or a vote/pause decided by the server and replicated).
- ~28 MonoBehaviours read `World.DefaultGameObjectInjectionWorld`: point them at the client world (the launcher sets
  `DefaultGameObjectInjectionWorld` to the client world) and keep them presentation-only.
- Systems without `[WorldSystemFilter]` run in both client and server worlds: gameplay must be server-only (or
  predicted), presentation client-only.
