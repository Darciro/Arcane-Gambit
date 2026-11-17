using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArcaneGambit.Rules;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace ArcaneGambit.Game
{
    public enum NetState { Offline, Creating, Hosting, Joining, Guest, Countdown, Playing, Failed, Lost }

    /// <summary>
    /// A private two-player duel. A Multiplayer Services session supplies the join code and the Relay connection;
    /// Netcode for GameObjects carries named messages over it. The host's match is the authoritative one: the guest
    /// sends its commands to the host, and the host relays every command it accepts, in order, for the guest to replay.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        public const int MaxPlayers = 2;
        public const int CodeLength = 6;
        public const float CountdownSeconds = 3f, BattleCrySeconds = 0.8f;
        public static readonly int[] TurnOptions = { 60, 90, 120 };
        const int DefaultTurnSeconds = 90;
        const string HelloMessage = "ag.hello", WelcomeMessage = "ag.welcome", ConfigMessage = "ag.config",
            StartMessage = "ag.start", CommandMessage = "ag.command", RejectMessage = "ag.reject";

        public readonly struct Inbound
        {
            public readonly Command Command;
            public readonly Side By;
            public readonly int Fingerprint;
            public readonly bool Rejected;

            public Inbound(Command command, Side by, int fingerprint, bool rejected = false)
            {
                Command = command; By = by; Fingerprint = fingerprint; Rejected = rejected;
            }
        }

        static NetSession s_instance;
        static Task s_signIn;

        NetworkManager _network;
        ISession _session;
        CancellationTokenSource _cancel;
        ulong _guest = ulong.MaxValue;
        float _countdownEnd;
        string _notice;
        readonly Queue<Inbound> _inbox = new Queue<Inbound>();

        public NetState State { get; private set; }
        /// <summary>Loc key of the last failure, shown while <see cref="State"/> is Failed.</summary>
        public string Error { get; private set; }
        /// <summary>Whether the online services answered the last sign-in; null until one is tried.</summary>
        public bool? Reachable { get; private set; }
        public string Code => _session != null ? _session.Code : null;
        public bool IsHost => _session != null && _session.IsHost;
        public bool OpponentPresent => State == NetState.Guest || State == NetState.Countdown
            || (State == NetState.Hosting && _guest != ulong.MaxValue);
        public int Seed { get; private set; }
        public Side LocalSide { get; private set; }
        public int TurnSeconds { get; private set; } = DefaultTurnSeconds;
        /// <summary>Seconds until the battle loads; the last <see cref="BattleCrySeconds"/> are the call to arms.</summary>
        public float CountdownLeft => State == NetState.Countdown ? Mathf.Max(0f, _countdownEnd - Time.unscaledTime) : 0f;
        public event Action Changed;

        public static NetSession Instance => s_instance != null ? s_instance : s_instance = Create();

        /// <summary>The session of the match being played online, or null when playing offline.</summary>
        public static NetSession Online => s_instance != null && s_instance.State is NetState.Playing or NetState.Lost
            ? s_instance : null;

        public static Side HostSide(int seed) => new System.Random(seed).Next(2) == 0 ? Side.White : Side.Black;

        /// <summary>Codes are typed loosely: case, spaces and hyphens do not matter.</summary>
        public static string CleanCode(string raw)
        {
            var chars = new List<char>(CodeLength);
            foreach (char c in raw ?? "")
                if (char.IsLetterOrDigit(c) && c < 128 && chars.Count < CodeLength) chars.Add(char.ToUpperInvariant(c));
            return new string(chars.ToArray());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_instance = null;
            s_signIn = null;
        }

        // Built inactive so NetworkManager wakes up already configured.
        static NetSession Create()
        {
            var go = new GameObject("Network");
            go.SetActive(false);
            var transport = go.AddComponent<UnityTransport>();
            // Browsers only speak WebSockets, so every platform uses them: an editor can then host a browser guest.
            transport.UseWebSockets = true;
            var network = go.AddComponent<NetworkManager>();
            network.NetworkConfig ??= new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.EnableSceneManagement = false;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            var session = go.AddComponent<NetSession>();
            session._network = network;
            go.SetActive(true);
            DontDestroyOnLoad(go);

            network.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = network.ConnectedClientsIds.Count < MaxPlayers;
                response.CreatePlayerObject = false;
                if (!response.Approved) response.Reason = "full";
            };
            network.OnServerStarted += session.Bind;
            network.OnClientStarted += session.Bind;
            network.OnClientConnectedCallback += session.OnClientConnected;
            network.OnClientDisconnectCallback += session.OnClientDisconnected;
            network.OnTransportFailure += session.OnConnectionLost;
            return session;
        }

        void Update()
        {
            if (State == NetState.Countdown && Time.unscaledTime >= _countdownEnd) Begin();
        }

        /// <summary>Signs in ahead of time, so the War Hall can tell at once whether the services answer.</summary>
        public async Task CheckAsync()
        {
            Reachable = null;
            if (State == NetState.Failed) SetState(NetState.Offline);
            else Changed?.Invoke();
            try
            {
                await SignInAsync();
                Reachable = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Online services unavailable: {e.Message}");
                Reachable = false;
            }
            Changed?.Invoke();
        }

        public async Task CreateAsync()
        {
            var token = await Restart(NetState.Creating);
            try
            {
                await SignInAsync();
                token.ThrowIfCancellationRequested();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }
                    .WithRelayNetwork()
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS });
                var session = await MultiplayerService.Instance.CreateSessionAsync(options, token);
                if (!Attach(session, token)) return;
                Seed = Environment.TickCount;
                LocalSide = HostSide(Seed);
                TurnSeconds = DefaultTurnSeconds;
                Reachable = true;
                SetState(NetState.Hosting);
            }
            catch (Exception e) { Fail(e, token, "ui.mp.error.create"); }
        }

        public async Task JoinAsync(string code)
        {
            var token = await Restart(NetState.Joining);
            try
            {
                await SignInAsync();
                token.ThrowIfCancellationRequested();
                var options = new JoinSessionOptions()
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS });
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(CleanCode(code), options, token);
                Attach(session, token);
                Reachable = true;
            }
            catch (Exception e) { Fail(e, token, "ui.mp.error.join"); }
        }

        public void Leave() => _ = Restart(NetState.Offline);

        /// <summary>The notice left by the last thing that happened to the room, read once.</summary>
        public string TakeNotice()
        {
            var notice = _notice;
            _notice = null;
            return notice;
        }

        public void SetTurnSeconds(int seconds)
        {
            if (State != NetState.Hosting || Array.IndexOf(TurnOptions, seconds) < 0) return;
            TurnSeconds = seconds;
            if (_guest != ulong.MaxValue)
            {
                using var writer = new FastBufferWriter(8, Allocator.Temp);
                writer.WriteValueSafe(TurnSeconds);
                Post(ConfigMessage, _guest, writer);
            }
            Changed?.Invoke();
        }

        public void StartBattle()
        {
            if (State != NetState.Hosting || _guest == ulong.MaxValue) return;
            using (var writer = new FastBufferWriter(4, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)0);
                Post(StartMessage, _guest, writer);
            }
            SetLocked(true);
            StartCountdown();
        }

        /// <summary>Guest only: asks the host to apply a command; it comes back through the inbox once accepted.</summary>
        public void Send(Command command)
        {
            using var writer = new FastBufferWriter(64, Allocator.Temp, 1024);
            Write(writer, command);
            Post(CommandMessage, NetworkManager.ServerClientId, writer);
        }

        /// <summary>Host only: hands an applied command to the guest, with the fingerprint its match should reach.</summary>
        public void Relay(Command command, Side by, int fingerprint)
        {
            using var writer = new FastBufferWriter(64, Allocator.Temp, 1024);
            writer.WriteValueSafe((byte)by);
            writer.WriteValueSafe(fingerprint);
            Write(writer, command);
            Post(CommandMessage, _guest, writer);
        }

        public void Reject()
        {
            using var writer = new FastBufferWriter(4, Allocator.Temp);
            writer.WriteValueSafe((byte)0);
            Post(RejectMessage, _guest, writer);
        }

        public bool TryReceive(out Inbound message) => _inbox.TryDequeue(out message);

        async Task<CancellationToken> Restart(NetState next)
        {
            _cancel?.Cancel();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            var previous = _session;
            _session = null;
            _guest = ulong.MaxValue;
            _inbox.Clear();
            Error = null;
            SetState(next);
            if (previous != null) await Close(previous);
            return token;
        }

        async Task Close(ISession session)
        {
            session.PlayerHasLeft -= OnPlayerLeft;
            session.RemovedFromSession -= OnConnectionLost;
            session.Deleted -= OnConnectionLost;
            try
            {
                if (session.IsHost) await session.AsHost().DeleteAsync();
                else await session.LeaveAsync();
            }
            catch (Exception e) { Debug.LogWarning($"Leaving the session failed: {e.Message}"); }
        }

        bool Attach(ISession session, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                _ = Close(session);
                return false;
            }
            _session = session;
            session.PlayerHasLeft += OnPlayerLeft;
            session.RemovedFromSession += OnConnectionLost;
            session.Deleted += OnConnectionLost;
            return true;
        }

        void Fail(Exception e, CancellationToken token, string fallback)
        {
            if (token.IsCancellationRequested) return;
            Debug.LogWarning($"Online session failed: {e}");
            Error = ErrorKey(e) ?? fallback;
            if (Error == "ui.mp.error.offline") Reachable = false;
            var session = _session;
            _session = null;
            _guest = ulong.MaxValue;
            if (session != null) _ = Close(session);
            SetState(NetState.Failed);
        }

        static string ErrorKey(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
            {
                if (x is LobbyServiceException lobby)
                    switch (lobby.Reason)
                    {
                        case LobbyExceptionReason.LobbyFull:
                        case LobbyExceptionReason.LobbyLocked: return "ui.mp.error.full";
                        case LobbyExceptionReason.LobbyNotFound:
                        case LobbyExceptionReason.InvalidJoinCode:
                        case LobbyExceptionReason.ValidationError: return "ui.mp.error.notFound";
                    }
                if (x is SessionException { Error: SessionError.SessionNotFound }) return "ui.mp.error.notFound";
                // The SDK turns a full lobby into an Unknown session error, keeping only the message.
                if (x is SessionException && (x.Message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0
                    || x.Message.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)) return "ui.mp.error.full";
                if (x is AuthenticationException || x is ServicesInitializationException
                    || x is RequestFailedException { ErrorCode: CommonErrorCodes.TransportError
                        or CommonErrorCodes.Timeout or CommonErrorCodes.ServiceUnavailable })
                    return "ui.mp.error.offline";
            }
            return null;
        }

        // A fresh profile per launch makes every window its own anonymous player, so an editor and a browser
        // tab on the same machine can face each other.
        static Task SignInAsync()
        {
            if (s_signIn == null || s_signIn.IsFaulted || s_signIn.IsCanceled) s_signIn = SignIn();
            return s_signIn;

            static async Task SignIn()
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync(new InitializationOptions()
                        .SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 20)));
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        // Handlers belong to the messaging manager of each run, so they are bound again whenever the network starts.
        void Bind()
        {
            var messages = _network.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(HelloMessage, OnHello);
            messages.RegisterNamedMessageHandler(WelcomeMessage, OnWelcome);
            messages.RegisterNamedMessageHandler(ConfigMessage, OnConfig);
            messages.RegisterNamedMessageHandler(StartMessage, OnStart);
            messages.RegisterNamedMessageHandler(CommandMessage, OnCommand);
            messages.RegisterNamedMessageHandler(RejectMessage, OnReject);
        }

        // The guest greets once connected, so the host never writes to a guest that cannot read yet.
        void OnClientConnected(ulong clientId)
        {
            if (!_network.IsServer && clientId == _network.LocalClientId && State == NetState.Joining)
            {
                using var writer = new FastBufferWriter(4, Allocator.Temp);
                writer.WriteValueSafe((byte)0);
                Post(HelloMessage, NetworkManager.ServerClientId, writer);
            }
        }

        void OnHello(ulong sender, FastBufferReader reader)
        {
            if (!_network.IsServer || State != NetState.Hosting || _guest != ulong.MaxValue) return;
            _guest = sender;
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(Seed);
                writer.WriteValueSafe(TurnSeconds);
                Post(WelcomeMessage, _guest, writer);
            }
            Changed?.Invoke();
        }

        void OnWelcome(ulong sender, FastBufferReader reader)
        {
            if (_network.IsServer || State != NetState.Joining) return;
            reader.ReadValueSafe(out int seed);
            reader.ReadValueSafe(out int turnSeconds);
            Seed = seed;
            LocalSide = HostSide(seed).Other();
            TurnSeconds = turnSeconds;
            SetState(NetState.Guest);
        }

        void OnConfig(ulong sender, FastBufferReader reader)
        {
            if (_network.IsServer || State != NetState.Guest) return;
            reader.ReadValueSafe(out int turnSeconds);
            TurnSeconds = turnSeconds;
            Changed?.Invoke();
        }

        void OnStart(ulong sender, FastBufferReader reader)
        {
            if (!_network.IsServer && State == NetState.Guest) StartCountdown();
        }

        // Both sides count down on their own clock from the same message, so they reach the battle together.
        void StartCountdown()
        {
            _countdownEnd = Time.unscaledTime + CountdownSeconds + BattleCrySeconds;
            SetState(NetState.Countdown);
        }

        void Begin()
        {
            _inbox.Clear();
            SetState(NetState.Playing);
            SceneTransition.Load(GameManager.SceneName);
        }

        void OnCommand(ulong sender, FastBufferReader reader)
        {
            if (State != NetState.Playing) return;
            if (_network.IsServer)
            {
                if (sender != _guest) return;
                _inbox.Enqueue(new Inbound(Read(reader), LocalSide.Other(), 0));
                return;
            }
            reader.ReadValueSafe(out byte by);
            reader.ReadValueSafe(out int fingerprint);
            _inbox.Enqueue(new Inbound(Read(reader), (Side)by, fingerprint));
        }

        void OnReject(ulong sender, FastBufferReader reader)
        {
            if (State == NetState.Playing && !_network.IsServer) _inbox.Enqueue(new Inbound(default, LocalSide, 0, rejected: true));
        }

        void OnPlayerLeft(string playerId)
        {
            if (IsHost) OnGuestGone();
            else OnConnectionLost();
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_network.IsServer)
            {
                if (clientId == _guest) OnGuestGone();
                return;
            }
            if (State == NetState.Joining)
                Fail(new Exception("Disconnected while joining"), CancellationToken.None, "ui.mp.error.join");
            else OnConnectionLost();
        }

        // The room outlives its guest: it reopens with the same code until the host closes it.
        void OnGuestGone()
        {
            if (State == NetState.Playing)
            {
                SetState(NetState.Lost);
                return;
            }
            if (State is not (NetState.Hosting or NetState.Countdown) || _guest == ulong.MaxValue) return;
            _guest = ulong.MaxValue;
            if (State == NetState.Countdown) SetLocked(false);
            _notice = "ui.mp.notice.opponentLeft";
            SetState(NetState.Hosting);
        }

        void OnConnectionLost()
        {
            switch (State)
            {
                case NetState.Playing:
                    SetState(NetState.Lost);
                    break;
                case NetState.Guest:
                case NetState.Countdown when !IsHost:
                    _notice = "ui.mp.notice.hostClosed";
                    Leave();
                    break;
                case NetState.Hosting:
                case NetState.Countdown:
                case NetState.Joining:
                    Fail(new Exception("Connection lost"), CancellationToken.None, "ui.mp.error.lost");
                    break;
            }
        }

        void SetLocked(bool locked)
        {
            if (_session == null || !_session.IsHost) return;
            var host = _session.AsHost();
            host.IsLocked = locked;
            _ = SaveQuietly(host);
        }

        static async Task SaveQuietly(IHostSession host)
        {
            try { await host.SavePropertiesAsync(); }
            catch (Exception e) { Debug.LogWarning($"Updating the session failed: {e.Message}"); }
        }

        void Post(string message, ulong clientId, FastBufferWriter writer)
        {
            var messages = _network != null && _network.IsListening ? _network.CustomMessagingManager : null;
            if (messages == null || clientId == ulong.MaxValue) return;
            messages.SendNamedMessage(message, clientId, writer, NetworkDelivery.ReliableSequenced);
        }

        void SetState(NetState state)
        {
            State = state;
            Changed?.Invoke();
        }

        static void Write(FastBufferWriter writer, Command c)
        {
            writer.WriteValueSafe((byte)c.Kind);
            writer.WriteValueSafe((byte)c.Unit);
            writer.WriteValueSafe(c.Index);
            WriteCoord(writer, c.To);
            writer.WriteValueSafe(c.Card.HasValue);
            writer.WriteValueSafe(c.Card.HasValue ? (int)c.Card.Value : 0);
            writer.WriteValueSafe((byte)c.Picks.Length);
            foreach (var pick in c.Picks) WriteCoord(writer, pick);
        }

        static void WriteCoord(FastBufferWriter writer, Coord c)
        {
            writer.WriteValueSafe((sbyte)c.X);
            writer.WriteValueSafe((sbyte)c.Y);
        }

        static Command Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out byte unit);
            reader.ReadValueSafe(out int index);
            var to = ReadCoord(reader);
            reader.ReadValueSafe(out bool hasCard);
            reader.ReadValueSafe(out int card);
            reader.ReadValueSafe(out byte count);
            var picks = new Coord[count];
            for (int i = 0; i < count; i++) picks[i] = ReadCoord(reader);
            return new Command((CommandKind)kind, (UnitType)unit, index, to, hasCard ? (CardId)card : null, picks);
        }

        static Coord ReadCoord(FastBufferReader reader)
        {
            reader.ReadValueSafe(out sbyte x);
            reader.ReadValueSafe(out sbyte y);
            return new Coord(x, y);
        }
    }
}
