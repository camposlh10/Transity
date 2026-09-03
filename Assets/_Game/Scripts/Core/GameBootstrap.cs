using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transity.Core
{
    /// <summary>
    /// Entry point. Lives in the Boot scene, initialises Unity Services, signs in
    /// anonymously and hands off to the main menu.
    ///
    /// Online sign-in is allowed to fail: until the project is linked to a Unity Cloud
    /// project the game still boots, and the menu reports that online play is
    /// unavailable rather than hanging on a spinner.
    /// </summary>
    public sealed class GameBootstrap : PersistentSingleton<GameBootstrap>
    {
        public enum OnlineState { Initialising, Ready, Unavailable }

        [Tooltip("OfflineHost skips the menu entirely and drops straight into the train hub.")]
        [SerializeField] StartupMode startupMode = StartupMode.MainMenu;

        [SerializeField] bool loadMainMenuWhenReady = true;

        /// <summary>
        /// How this session was started. ServerBootstrap reads it to decide whether to open
        /// in the depot or drop straight into a live expedition.
        /// </summary>
        public static StartupMode Mode { get; private set; } = StartupMode.MainMenu;

        public static OnlineState Online { get; private set; } = OnlineState.Initialising;
        public static string OnlineFailureReason { get; private set; }
        public static string PlayerId { get; private set; }

        /// <summary>Raised when sign-in finishes, successfully or not.</summary>
        public static event Action<OnlineState> OnlineStateChanged;

        async void Start()
        {
            Mode = startupMode;

            if (startupMode is StartupMode.OfflineHost or StartupMode.ForestSandbox)
            {
                StartOfflineHost();
                return;
            }

            await InitialiseServicesAsync();

            if (loadMainMenuWhenReady && SceneManager.GetActiveScene().name == SceneCatalog.Boot)
            {
                SceneManager.LoadScene(SceneCatalog.MainMenu);
            }
        }

        /// <summary>
        /// Starts a host on the local transport with no Relay and no sign-in. ServerBootstrap
        /// picks up OnServerStarted exactly as it would online, spawns the mission director
        /// and loads the train hub, so the scene you are looking at is the real networked
        /// one -- just with a single peer.
        /// </summary>
        void StartOfflineHost()
        {
            SetState(OnlineState.Unavailable, "Offline host: online features are skipped.");

            if (NetworkManager.Singleton == null)
            {
                GameLog.Error("Offline host needs a NetworkManager in the Boot scene.");
                return;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                return;
            }

            // Never fight for a fixed port. A leftover editor, a virtual player from
            // Multiplayer Play Mode, or a previous run that has not released its socket will
            // all be sitting on the default 7777, and the bind failure cascades into a
            // transport failure that dumps the player back to the menu.
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                var port = FindFreeLoopbackPort(transport.ConnectionData.Port);

                if (port == 0)
                {
                    GameLog.Error("No free UDP port found for the offline host.");
                    return;
                }

                if (port != transport.ConnectionData.Port)
                {
                    GameLog.Net($"Port {transport.ConnectionData.Port} is busy; using {port} instead.");
                }

                transport.SetConnectionData("127.0.0.1", port);
            }

            if (NetworkManager.Singleton.StartHost())
            {
                GameLog.Net("Offline host started; loading the train hub.");
            }
            else
            {
                GameLog.Error("Offline host failed to start. See the transport error above.");
            }
        }

        /// <summary>
        /// Returns the first loopback UDP port that actually binds, starting at the preferred
        /// one. Probing on 127.0.0.1 matches what UnityTransport does, so a port that passes
        /// here will not fail underneath it.
        /// </summary>
        static ushort FindFreeLoopbackPort(ushort preferred, int attempts = 24)
        {
            for (var i = 0; i < attempts; i++)
            {
                var candidate = (ushort)(preferred + i);

                try
                {
                    using var probe = new System.Net.Sockets.UdpClient(
                        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, candidate));
                    return candidate;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // In use; try the next one.
                }
            }

            return 0;
        }

        public static async Task InitialiseServicesAsync()
        {
            if (Online == OnlineState.Ready)
            {
                return;
            }

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                PlayerId = AuthenticationService.Instance.PlayerId;
                SetState(OnlineState.Ready, null);
                GameLog.Info($"Signed in. PlayerId={PlayerId}");
            }
            catch (Exception e)
            {
                // Most common cause during early development: the project has no Unity
                // Cloud project ID linked yet (Project Settings > Services).
                SetState(OnlineState.Unavailable, e.Message);
                GameLog.Warn($"Online services unavailable: {e.Message}");
            }
        }

        static void SetState(OnlineState state, string reason)
        {
            Online = state;
            OnlineFailureReason = reason;
            OnlineStateChanged?.Invoke(state);
        }
    }
}
