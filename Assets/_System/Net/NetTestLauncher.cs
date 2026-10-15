using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Scenes;
using UnityEngine;

/// <summary>
/// Entry point of SC_NetTest: Host / Join UI, transport selection (EOS P2P, or Unity Transport UDP as a dev
/// fallback), enemy-count debug controls and the bandwidth overlay.
///
/// Peer-to-peer listen server only: the host process runs a server world + its own client world (IPC between the
/// two), remote players connect to it. No dedicated server.
///
/// Command line (for automated runs / two instances): -nettest-host | -nettest-join [address],
/// -nettest-transport udp|eos, -nettest-port N, -nettest-enemies N, -nettest-enemy-steps 100,300,500,
/// -nettest-step-seconds S, -nettest-bot, -nettest-log file.csv, -nettest-quit-after S, -nettest-screenshot file.png
/// </summary>
public class NetTestLauncher : MonoBehaviour
{
    public enum ETransport { EOS, UDP }

    [Tooltip("Sub scene holding the planet + NetTestConfig (ghost prefabs). Auto Load must be OFF: it is loaded " +
             "into the server/client worlds once they exist.")]
    [SerializeField] private SubScene _planetSubScene;
    [SerializeField] private ushort _port = 7979;

    private ETransport _transport = ETransport.EOS;
    private string _address = "127.0.0.1";
    private string _listenAddress = "0.0.0.0";
    private string _hostIdInput = "";
    private string _displayName = "Player";
    private bool _started;
    private bool _isHost;
    private int _enemyTarget;
    private string _error;

    // automation
    private int[] _enemySteps;
    private float _stepSeconds = 20f;
    private float _quitAfter = -1f;
    private string _logPath;
    private string _screenshotPath;
    private float _startTime;
    private float _lastLogTime;
    private int _currentStep = -1;
    private bool _screenshotTaken;

    private void Start()
    {
        Application.runInBackground = true; // both instances must keep ticking when unfocused
        _displayName = "Player" + UnityEngine.Random.Range(100, 999);
        ParseCommandLine(out bool autoHost, out bool autoJoin);
        _transport = HasArg("-nettest-transport") ? _transport : (EosAvailable() ? ETransport.EOS : ETransport.UDP);
        if (autoHost) Host();
        else if (autoJoin && _transport == ETransport.UDP) JoinUdp();
        else if (autoJoin) StartEosClient();
    }

    #region World management

    private void Host()
    {
        _isHost = true;
        if (_transport == ETransport.EOS)
        {
#if !EOS_DISABLE
            var session = EosSession.GetOrCreate();
            if (session.State != EosSession.EState.LoggedIn)
            {
                session.Login(_displayName);
                StartCoroutine(WaitForEos(() => session.State == EosSession.EState.LoggedIn, () => Host()));
                return;
            }
            NetworkStreamReceiveSystem.DriverConstructor = new EosDriverConstructor();
            session.HostLobby();
#endif
        }
        else
        {
            NetworkStreamReceiveSystem.DriverConstructor = DefaultDriverBuilder.DefaultDriverConstructor;
        }

        DestroyLocalWorlds();
        var server = ClientServerBootstrap.CreateServerWorld("ServerWorld");
        var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
        World.DefaultGameObjectInjectionWorld = client;
        LoadPlanet(server);
        LoadPlanet(client);

        using (var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(NetworkEndpoint.TryParse(_listenAddress, _port, out var listen) ? listen : NetworkEndpoint.AnyIpv4.WithPort(_port));

        using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, NetworkEndpoint.LoopbackIpv4.WithPort(_port));

        OnStarted();
    }

    private void JoinUdp()
    {
        if (!NetworkEndpoint.TryParse(_address, _port, out var endpoint))
        {
            _error = $"Invalid address {_address}";
            return;
        }
        NetworkStreamReceiveSystem.DriverConstructor = DefaultDriverBuilder.DefaultDriverConstructor;
        StartClient(endpoint);
    }

    private void StartEosClient()
    {
#if !EOS_DISABLE
        var session = EosSession.GetOrCreate();
        if (session.State != EosSession.EState.LoggedIn)
        {
            session.Login(_displayName);
            StartCoroutine(WaitForEos(() => session.State == EosSession.EState.LoggedIn, StartEosClient));
            return;
        }

        session.OnHostFound -= OnEosHostFound;
        session.OnHostFound += OnEosHostFound;
        if (!string.IsNullOrWhiteSpace(_hostIdInput))
            session.TryUseHostId(_hostIdInput);
        else
            session.FindAndJoinLobby();
#endif
    }

#if !EOS_DISABLE
    private void OnEosHostFound(Epic.OnlineServices.ProductUserId host)
    {
        NetworkStreamReceiveSystem.DriverConstructor = new EosDriverConstructor();
        StartClient(EosP2PSocket.Active.EndpointFor(host));
    }

    private System.Collections.IEnumerator WaitForEos(Func<bool> ready, Action then)
    {
        var session = EosSession.GetOrCreate();
        while (!ready() && session.State != EosSession.EState.Failed)
            yield return null;
        if (ready())
            then();
        else
            _error = session.Status;
    }
#endif

    private void StartClient(NetworkEndpoint endpoint)
    {
        _isHost = false;
        DestroyLocalWorlds();
        var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
        World.DefaultGameObjectInjectionWorld = client;
        LoadPlanet(client);

        using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, endpoint);

        OnStarted();
    }

    private void OnStarted()
    {
        _started = true;
        _startTime = Time.realtimeSinceStartup;
        Debug.Log($"[NetTest] Started as {(_isHost ? "HOST" : "CLIENT")} over {_transport}");
        if (_isHost && _enemySteps == null && _enemyTarget > 0)
            SetEnemyTarget(_enemyTarget);
    }

    private void LoadPlanet(World world)
    {
        if (_planetSubScene == null)
        {
            Debug.LogError("[NetTest] Planet sub scene not assigned on NetTestLauncher.");
            return;
        }
        SceneSystem.LoadSceneAsync(world.Unmanaged, _planetSubScene.SceneGUID);
    }

    /// <summary>
    /// The default (single-player) local world is not used by the net test; dispose it so its systems don't run
    /// next to the client/server worlds.
    /// </summary>
    private static void DestroyLocalWorlds()
    {
        foreach (var world in SnapshotWorlds())
        {
            if (world.IsCreated && world.Flags == WorldFlags.Game)
                world.Dispose();
        }
    }

    // World.All can't be enumerated through LINQ (it refuses boxing) and must not change while iterated.
    private static System.Collections.Generic.List<World> SnapshotWorlds()
    {
        var worlds = new System.Collections.Generic.List<World>();
        foreach (var world in World.All)
            worlds.Add(world);
        return worlds;
    }

    private void StopAll()
    {
        foreach (var world in SnapshotWorlds())
        {
            if (world.IsCreated && (world.IsClient() || world.IsServer()))
                world.Dispose();
        }
#if !EOS_DISABLE
        var session = FindFirstObjectByType<EosSession>();
        if (session != null)
            session.LeaveLobby();
#endif
        _started = false;
        _currentStep = -1;
    }

    private void SetEnemyTarget(int count)
    {
        _enemyTarget = count;
        var server = ClientServerBootstrap.ServerWorld;
        if (server == null)
            return;
        using var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetEnemyTarget>());
        if (query.TryGetSingletonRW<NetEnemyTarget>(out var target))
            target.ValueRW.Count = count;
        Debug.Log($"[NetTest] Enemy target = {count}");
    }

    #endregion

    #region Automation + logging

    private void Update()
    {
        if (!_started)
            return;

        float elapsed = Time.realtimeSinceStartup - _startTime;

        if (_isHost && _enemySteps != null)
        {
            int step = Mathf.Min((int)(elapsed / _stepSeconds), _enemySteps.Length - 1);
            if (step != _currentStep)
            {
                _currentStep = step;
                SetEnemyTarget(_enemySteps[step]);
            }
        }
        else if (_isHost)
        {
            // keep re-applying until the server world has created its NetEnemyTarget singleton
            if (_enemyTarget > 0 && Time.frameCount % 30 == 0)
                EnsureEnemyTarget();
        }

        if (Time.realtimeSinceStartup - _lastLogTime >= 1f)
        {
            _lastLogTime = Time.realtimeSinceStartup;
            LogStats(elapsed);
        }

        if (!_screenshotTaken && !string.IsNullOrEmpty(_screenshotPath) && elapsed > Mathf.Max(10f, _quitAfter - 5f))
        {
            _screenshotTaken = true;
            ScreenCapture.CaptureScreenshot(_screenshotPath);
        }

        if (_quitAfter > 0 && elapsed > _quitAfter)
        {
            Debug.Log("[NetTest] quit-after reached");
            Application.Quit();
        }
    }

    private void EnsureEnemyTarget()
    {
        var server = ClientServerBootstrap.ServerWorld;
        if (server == null)
            return;
        using var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetEnemyTarget>());
        if (query.TryGetSingletonRW<NetEnemyTarget>(out var target) && target.ValueRO.Count != _enemyTarget)
            target.ValueRW.Count = _enemyTarget;
    }

    private void LogStats(float elapsed)
    {
        foreach (var s in NetStatsSystem.ByWorld.Values)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:F0},{1},{2},{3},{4},{5},{6},{7:F2},{8:F2},{9:F2},{10:F2},{11:F0},{12:F0},{13:F1},{14}",
                elapsed, _isHost ? "host" : "client", s.World, _transport, s.Connections, s.Players, s.Enemies,
                s.RxKBps, s.TxKBps, s.RemoteRxKBps, s.RemoteTxKBps, s.RxPacketsPerSec, s.TxPacketsPerSec, s.RttMs, s.Drivers);
            Debug.Log("[NetStats] " + line);
            if (!string.IsNullOrEmpty(_logPath))
            {
                if (!File.Exists(_logPath))
                    File.AppendAllText(_logPath, "t,role,world,transport,connections,players,enemies,rxKBps,txKBps,remoteRxKBps,remoteTxKBps,rxPps,txPps,rttMs,drivers\n");
                File.AppendAllText(_logPath, line + "\n");
            }
        }
    }

    private void ParseCommandLine(out bool autoHost, out bool autoJoin)
    {
        var args = Environment.GetCommandLineArgs();
        autoHost = false;
        autoJoin = false;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : null;
            bool nextIsValue = next != null && !next.StartsWith("-");
            switch (args[i])
            {
                case "-nettest-host": autoHost = true; break;
                case "-nettest-join":
                    autoJoin = true;
                    if (nextIsValue) _address = next;
                    break;
                case "-nettest-listen": if (nextIsValue) _listenAddress = next; break;
                case "-nettest-host-id": if (nextIsValue) _hostIdInput = next; break;
                case "-nettest-transport":
                    if (nextIsValue) _transport = next.Equals("eos", StringComparison.OrdinalIgnoreCase) ? ETransport.EOS : ETransport.UDP;
                    break;
                case "-nettest-port": if (nextIsValue && ushort.TryParse(next, out var p)) _port = p; break;
                case "-nettest-enemies": if (nextIsValue) int.TryParse(next, out _enemyTarget); break;
                case "-nettest-enemy-steps":
                    if (nextIsValue) _enemySteps = next.Split(',').Select(int.Parse).ToArray();
                    break;
                case "-nettest-step-seconds": if (nextIsValue) float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out _stepSeconds); break;
                case "-nettest-quit-after": if (nextIsValue) float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out _quitAfter); break;
                case "-nettest-snapshot-size": if (nextIsValue && int.TryParse(next, out var size)) NetGoInGameServerSystem.SnapshotTargetSizeStatic.Data = size; break;
                case "-nettest-bot": NetPlayerInputSystem.BotInput = true; break;
                case "-nettest-log": if (nextIsValue) _logPath = next; break;
                case "-nettest-screenshot": if (nextIsValue) _screenshotPath = next; break;
            }
        }
    }

    private static bool HasArg(string name) => Environment.GetCommandLineArgs().Contains(name);

    private static bool EosAvailable()
    {
#if !EOS_DISABLE
        return EosSession.HasLocalConfig();
#else
        return false;
#endif
    }

    #endregion

    #region UI

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 14;
        GUI.skin.button.fontSize = 14;
        GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20), GUI.skin.box);

        if (!_started)
            DrawMenu();
        else
            DrawInGame();

        if (!string.IsNullOrEmpty(_error))
            GUILayout.Label("<color=#ff7070>" + _error + "</color>");

        GUILayout.EndArea();
    }

    private void DrawMenu()
    {
        GUILayout.Label("<b>Crazy Planet Survivor — network test</b>");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Transport", GUILayout.Width(80));
        if (GUILayout.Toggle(_transport == ETransport.EOS, " EOS P2P")) _transport = ETransport.EOS;
        if (GUILayout.Toggle(_transport == ETransport.UDP, " UDP (dev)")) _transport = ETransport.UDP;
        GUILayout.EndHorizontal();

        if (_transport == ETransport.EOS)
        {
#if !EOS_DISABLE
            if (!EosSession.HasLocalConfig())
                GUILayout.Label("<color=#ffcc66>No EOS config found (Assets/StreamingAssets/EOS). Create it with " +
                                "Tools > EOS Plugin > EOS Configuration, or use UDP.</color>");
            var session = FindFirstObjectByType<EosSession>();
            if (session != null)
                GUILayout.Label("EOS: " + session.Status);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host PUID", GUILayout.Width(80));
            _hostIdInput = GUILayout.TextField(_hostIdInput);
            GUILayout.EndHorizontal();
            GUILayout.Label("<size=11>Empty = join the first lobby found. Otherwise paste the host's ProductUserId.</size>");
#else
            GUILayout.Label("EOS plugin disabled (EOS_DISABLE).");
#endif
        }
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Address", GUILayout.Width(80));
            _address = GUILayout.TextField(_address);
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host")) { _error = null; Host(); }
        if (GUILayout.Button("Join"))
        {
            _error = null;
            if (_transport == ETransport.UDP) JoinUdp(); else StartEosClient();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawInGame()
    {
        GUILayout.Label($"<b>{(_isHost ? "HOST" : "CLIENT")}</b> over {_transport}   (WASD / left stick to move)");

#if !EOS_DISABLE
        var session = FindFirstObjectByType<EosSession>();
        if (session != null && session.LocalUserId != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=11>PUID " + session.LocalUserId + "</size>");
            if (GUILayout.Button("Copy", GUILayout.Width(50))) GUIUtility.systemCopyBuffer = session.LocalUserId.ToString();
            GUILayout.EndHorizontal();
        }
#endif

        if (_isHost)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enemies", GUILayout.Width(60));
            foreach (int n in new[] { 0, 100, 300, 500, 1000 })
                if (GUILayout.Button(n.ToString())) { _enemySteps = null; SetEnemyTarget(n); }
            GUILayout.EndHorizontal();
        }

        var sb = new StringBuilder();
        foreach (var s in NetStatsSystem.ByWorld.Values)
        {
            sb.AppendLine($"<b>{s.World}</b> [{s.Drivers}]  conn {s.Connections}  players {s.Players}  enemies {s.Enemies}" +
                          (s.IsServer ? "" : $"  rtt {s.RttMs:F0} ms"));
            sb.AppendLine($"  in {s.RxKBps:F1} KB/s   out {s.TxKBps:F1} KB/s   ({s.RxPacketsPerSec:F0}/{s.TxPacketsPerSec:F0} pkt/s)");
            if (s.IsServer)
                sb.AppendLine($"  remote peers only: in {s.RemoteRxKBps:F1} KB/s   out {s.RemoteTxKBps:F1} KB/s");
        }
        GUILayout.Label(sb.ToString());

        if (GUILayout.Button("Disconnect"))
            StopAll();
    }

    #endregion

    private void OnDestroy()
    {
        StopAll();
    }
}
