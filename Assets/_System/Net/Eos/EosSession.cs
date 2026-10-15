#if !EOS_DISABLE
using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

/// <summary>
/// EOS bootstrap for the net test: SDK init (PlayEveryWare EOSManager), Connect login with a Device ID
/// (no Epic account needed), and a minimal lobby used as matchmaking: the host creates a public lobby in the
/// <see cref="BucketId"/> bucket, clients search that bucket, join, and read the lobby owner's ProductUserId,
/// which is the P2P address to connect netcode to.
///
/// Credentials (ProductId, SandboxId, DeploymentId, ClientId, ClientSecret) are NOT in the repo: they are read by
/// the plugin from Assets/StreamingAssets/EOS/*.json, created locally with Tools > EOS Plugin > EOS Configuration
/// and git-ignored.
/// </summary>
public class EosSession : MonoBehaviour
{
    public const string BucketId = "cps-nettest:global";

    public enum EState { Idle, Initializing, LoggingIn, LoggedIn, CreatingLobby, Searching, Joining, InLobby, Failed }

    public EState State { get; private set; } = EState.Idle;
    public string Status { get; private set; } = "";
    public ProductUserId LocalUserId { get; private set; }
    public ProductUserId HostUserId { get; private set; }
    public string LobbyId { get; private set; }
    public bool IsHost { get; private set; }

    public event Action<ProductUserId> OnHostFound; // client: lobby joined, host PUID known
    public event Action OnLobbyCreated;             // host: lobby is live

    private static EosSession s_instance;

    public static EosSession GetOrCreate()
    {
        if (s_instance != null)
            return s_instance;
        var go = new GameObject("EOS Session");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<EosSession>();
        return s_instance;
    }

    /// <summary>True when the plugin config files exist locally (the IDs from the Epic Developer Portal).</summary>
    public static bool HasLocalConfig()
    {
        string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "EOS");
        return System.IO.File.Exists(System.IO.Path.Combine(dir, "eos_product_config.json"));
    }

    public void Login(string displayName)
    {
        if (State != EState.Idle && State != EState.Failed)
            return;

        if (!HasLocalConfig())
        {
            Fail("EOS config missing: create it with Tools > EOS Plugin > EOS Configuration (ProductId, SandboxId, " +
                 "DeploymentId, ClientId, ClientSecret from the Epic Developer Portal).");
            return;
        }

        State = EState.Initializing;
        Status = "Initializing EOS SDK...";
        if (FindFirstObjectByType<EOSManager>() == null)
        {
            var managerGo = new GameObject("EOSManager");
            DontDestroyOnLoad(managerGo);
            managerGo.AddComponent<EOSManager>(); // Awake initializes the SDK from the local config
        }

        var connect = EOSManager.Instance.GetEOSConnectInterface();
        if (connect == null)
        {
            Fail("EOS platform failed to initialize (check the console / config values).");
            return;
        }

        State = EState.LoggingIn;
        Status = "Creating device id...";
        var createDevice = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel };
        connect.CreateDeviceId(ref createDevice, null, (ref CreateDeviceIdCallbackInfo info) =>
        {
            // DuplicateNotAllowed = this machine already has a device id, which is what we want.
            if (info.ResultCode != Result.Success && info.ResultCode != Result.DuplicateNotAllowed)
            {
                Fail($"CreateDeviceId failed: {info.ResultCode}");
                return;
            }
            Status = "Connect login (device id)...";
            EOSManager.Instance.StartConnectLoginWithDeviceToken(displayName, OnConnectLogin);
        });
    }

    private void OnConnectLogin(LoginCallbackInfo info)
    {
        if (info.ResultCode == Result.Success)
        {
            OnLoggedIn(info.LocalUserId);
            return;
        }

        if (info.ResultCode == Result.InvalidUser && info.ContinuanceToken != null)
        {
            // First login with this device id: create the EOS product user.
            Status = "Creating EOS product user...";
            EOSManager.Instance.CreateConnectUserWithContinuanceToken(info.ContinuanceToken, (CreateUserCallbackInfo created) =>
            {
                if (created.ResultCode != Result.Success)
                    Fail($"CreateUser failed: {created.ResultCode}");
                else
                    OnLoggedIn(created.LocalUserId);
            });
            return;
        }

        Fail($"Connect login failed: {info.ResultCode}");
    }

    private void OnLoggedIn(ProductUserId userId)
    {
        LocalUserId = userId;
        EosP2PSocket.Active?.Dispose();
        EosP2PSocket.Active = new EosP2PSocket(EOSManager.Instance.GetEOSP2PInterface(), userId);
        State = EState.LoggedIn;
        Status = $"Logged in as {userId}";
        Debug.Log($"[EOS] {Status}");
    }

    public void HostLobby(uint maxMembers = 4)
    {
        if (State != EState.LoggedIn)
            return;

        State = EState.CreatingLobby;
        Status = "Creating lobby...";
        IsHost = true;
        HostUserId = LocalUserId;

        var options = new CreateLobbyOptions
        {
            LocalUserId = LocalUserId,
            MaxLobbyMembers = maxMembers,
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            PresenceEnabled = false,
            AllowInvites = false,
            BucketId = BucketId,
            DisableHostMigration = true,
            EnableRTCRoom = false
        };
        EOSManager.Instance.GetEOSLobbyInterface().CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
        {
            if (info.ResultCode != Result.Success)
            {
                Fail($"CreateLobby failed: {info.ResultCode}");
                return;
            }
            LobbyId = info.LobbyId;
            State = EState.InLobby;
            Status = $"Hosting lobby {LobbyId}";
            Debug.Log($"[EOS] {Status} as {LocalUserId}");
            OnLobbyCreated?.Invoke();
        });
    }

    /// <summary>Find the first open lobby in our bucket, join it, and report its owner (the host).</summary>
    public void FindAndJoinLobby()
    {
        if (State != EState.LoggedIn)
            return;

        var lobby = EOSManager.Instance.GetEOSLobbyInterface();
        var searchOptions = new CreateLobbySearchOptions { MaxResults = 10 };
        var result = lobby.CreateLobbySearch(ref searchOptions, out var search);
        if (result != Result.Success)
        {
            Fail($"CreateLobbySearch failed: {result}");
            return;
        }

        var parameter = new LobbySearchSetParameterOptions
        {
            Parameter = new AttributeData { Key = "bucket", Value = BucketId },
            ComparisonOp = ComparisonOp.Equal
        };
        search.SetParameter(ref parameter);

        State = EState.Searching;
        Status = "Searching lobbies...";
        var find = new LobbySearchFindOptions { LocalUserId = LocalUserId };
        search.Find(ref find, null, (ref LobbySearchFindCallbackInfo info) =>
        {
            if (info.ResultCode != Result.Success)
            {
                search.Release();
                Fail($"Lobby search failed: {info.ResultCode}");
                return;
            }

            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            if (count == 0)
            {
                search.Release();
                State = EState.LoggedIn;
                Status = "No lobby found. Is the host running and logged in to the same sandbox/deployment?";
                return;
            }

            var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            search.CopySearchResultByIndex(ref copy, out var details);
            search.Release();
            JoinLobby(details);
        });
    }

    /// <summary>Skip matchmaking: connect straight to a host whose ProductUserId was shared by hand.</summary>
    public bool TryUseHostId(string productUserId)
    {
        if (State != EState.LoggedIn)
            return false;
        var host = ProductUserId.FromString(productUserId?.Trim());
        if (host == null || !host.IsValid())
        {
            Status = "Invalid ProductUserId";
            return false;
        }
        HostUserId = host;
        State = EState.InLobby;
        Status = $"Using host {host}";
        OnHostFound?.Invoke(host);
        return true;
    }

    private void JoinLobby(LobbyDetails details)
    {
        State = EState.Joining;
        Status = "Joining lobby...";

        var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
        var owner = details.GetLobbyOwner(ref ownerOptions);

        var join = new JoinLobbyOptions { LobbyDetailsHandle = details, LocalUserId = LocalUserId, PresenceEnabled = false };
        EOSManager.Instance.GetEOSLobbyInterface().JoinLobby(ref join, null, (ref JoinLobbyCallbackInfo info) =>
        {
            details.Release();
            if (info.ResultCode != Result.Success)
            {
                Fail($"JoinLobby failed: {info.ResultCode}");
                return;
            }
            LobbyId = info.LobbyId;
            HostUserId = owner;
            State = EState.InLobby;
            Status = $"Joined lobby {LobbyId}, host {owner}";
            Debug.Log($"[EOS] {Status}");
            OnHostFound?.Invoke(owner);
        });
    }

    public void LeaveLobby()
    {
        if (string.IsNullOrEmpty(LobbyId) || LocalUserId == null || EOSManager.Instance == null)
            return;

        var lobby = EOSManager.Instance.GetEOSLobbyInterface();
        if (IsHost)
        {
            var destroy = new DestroyLobbyOptions { LocalUserId = LocalUserId, LobbyId = LobbyId };
            lobby?.DestroyLobby(ref destroy, null, (ref DestroyLobbyCallbackInfo _) => { });
        }
        else
        {
            var leave = new LeaveLobbyOptions { LocalUserId = LocalUserId, LobbyId = LobbyId };
            lobby?.LeaveLobby(ref leave, null, (ref LeaveLobbyCallbackInfo _) => { });
        }
        LobbyId = null;
    }

    private void Fail(string message)
    {
        State = EState.Failed;
        Status = message;
        Debug.LogWarning($"[EOS] {message}");
    }

    private void OnDestroy()
    {
        LeaveLobby();
        EosP2PSocket.Active?.Dispose();
        if (s_instance == this)
            s_instance = null;
    }
}
#endif
