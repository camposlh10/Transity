using System;
using System.Threading.Tasks;
using Transity.Core;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transity.Networking
{
    public enum SessionStatus
    {
        Offline,
        Connecting,
        InSession,
        Failed
    }

    /// <summary>
    /// Owns the lifetime of a multiplayer session: create by code, join by code, leave.
    ///
    /// The Multiplayer Services SDK drives NetworkManager for us -- it allocates Relay,
    /// configures the transport and calls StartHost/StartClient internally. Never call
    /// NetworkManager.StartHost() alongside this; you will get a double-start.
    /// </summary>
    public sealed class SessionManager : PersistentSingleton<SessionManager>
    {
        [Header("Session")]
        [SerializeField, Range(1, 8)] int maxPlayers = 4;
        [SerializeField] string sessionNamePrefix = "Expedition";

        ISession m_Session;

        public ISession ActiveSession => m_Session;
        public string JoinCode => m_Session?.Code;
        public bool IsHost => m_Session?.IsHost ?? false;
        public int MaxPlayers => maxPlayers;
        public SessionStatus Status { get; private set; } = SessionStatus.Offline;
        public string LastError { get; private set; }

        /// <summary>Status plus a human-readable reason, for the menu to display.</summary>
        public event Action<SessionStatus, string> StatusChanged;

        // Hooked in Start, not Awake: NetworkManager assigns its singleton during its own
        // Awake and script execution order between two root objects is not guaranteed.
        void Start()
        {
            if (Instance != this)
            {
                return;
            }

            if (NetworkManager.Singleton == null)
            {
                GameLog.Error("No NetworkManager in the scene; hosting and joining will not work.");
                return;
            }

            NetworkManager.Singleton.OnClientStopped += HandleClientStopped;
            NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
        }

        protected override void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped -= HandleClientStopped;
                NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
            }

            base.OnDestroy();
        }

        public async Task<bool> HostAsync(string playerName)
        {
            if (!await EnsureReadyAsync())
            {
                return false;
            }

            SetStatus(SessionStatus.Connecting, "Creating session...");

            try
            {
                var options = new SessionOptions
                {
                    Name = $"{sessionNamePrefix}-{UnityEngine.Random.Range(1000, 9999)}",
                    MaxPlayers = maxPlayers,
                    IsPrivate = true
                }.WithRelayNetwork();

                m_Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                HookSession();

                SetStatus(SessionStatus.InSession, $"Hosting. Join code: {m_Session.Code}");
                GameLog.Net($"Hosted session {m_Session.Id} code={m_Session.Code}");
                return true;
            }
            catch (Exception e)
            {
                Fail($"Could not create session: {Describe(e)}");
                return false;
            }
        }

        public async Task<bool> JoinByCodeAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Fail("Enter a join code first.");
                return false;
            }

            if (!await EnsureReadyAsync())
            {
                return false;
            }

            SetStatus(SessionStatus.Connecting, "Joining session...");

            try
            {
                m_Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim().ToUpperInvariant());
                HookSession();

                SetStatus(SessionStatus.InSession, "Joined session.");
                GameLog.Net($"Joined session {m_Session.Id}");
                return true;
            }
            catch (Exception e)
            {
                Fail($"Could not join: {Describe(e)}");
                return false;
            }
        }

        public async Task LeaveAsync()
        {
            var session = m_Session;
            UnhookSession();
            m_Session = null;

            if (session != null)
            {
                try
                {
                    await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    // Leaving is best-effort: the local teardown below matters more.
                    GameLog.Warn($"LeaveAsync failed, shutting down locally anyway: {e.Message}");
                }
            }

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            SetStatus(SessionStatus.Offline, null);
        }

        static async Task<bool> EnsureReadyAsync()
        {
            await GameBootstrap.InitialiseServicesAsync();
            return GameBootstrap.Online == GameBootstrap.OnlineState.Ready;
        }

        void HookSession()
        {
            if (m_Session != null)
            {
                m_Session.RemovedFromSession += HandleRemovedFromSession;
            }
        }

        void UnhookSession()
        {
            if (m_Session != null)
            {
                m_Session.RemovedFromSession -= HandleRemovedFromSession;
            }
        }

        void HandleRemovedFromSession()
        {
            GameLog.Net("Removed from session.");
            ReturnToMenu("The session ended.");
        }

        /// <summary>
        /// Covers the MVP rule: if the host quits mid-expedition the run is cancelled and
        /// everyone is returned to the menu rather than being left in a dead scene.
        /// </summary>
        void HandleClientStopped(bool wasHost)
        {
            if (Status == SessionStatus.Offline)
            {
                return;
            }

            ReturnToMenu(wasHost ? "You closed the session." : "Disconnected from the host.");
        }

        void HandleTransportFailure()
        {
            // Same guard as HandleClientStopped. Without it, a transport failure during an
            // offline host -- which this manager did not start and does not own -- drags the
            // player out to the main menu.
            if (Status == SessionStatus.Offline)
            {
                GameLog.Warn("Transport failure while offline; staying put.");
                return;
            }

            ReturnToMenu("Connection lost.");
        }

        void ReturnToMenu(string reason)
        {
            UnhookSession();
            m_Session = null;
            SetStatus(SessionStatus.Offline, reason);

            if (SceneManager.GetActiveScene().name != SceneCatalog.MainMenu)
            {
                SceneManager.LoadScene(SceneCatalog.MainMenu);
            }
        }

        void Fail(string reason)
        {
            LastError = reason;
            SetStatus(SessionStatus.Failed, reason);
            GameLog.Warn(reason);
        }

        void SetStatus(SessionStatus status, string message)
        {
            Status = status;
            StatusChanged?.Invoke(status, message);
        }

        static string Describe(Exception e)
        {
            return e is SessionException sessionException
                ? $"{sessionException.Error} ({sessionException.Message})"
                : e.Message;
        }
    }
}
