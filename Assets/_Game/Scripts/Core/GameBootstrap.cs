using System;
using System.Threading.Tasks;
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

        [SerializeField] bool loadMainMenuWhenReady = true;

        public static OnlineState Online { get; private set; } = OnlineState.Initialising;
        public static string OnlineFailureReason { get; private set; }
        public static string PlayerId { get; private set; }

        /// <summary>Raised when sign-in finishes, successfully or not.</summary>
        public static event Action<OnlineState> OnlineStateChanged;

        async void Start()
        {
            await InitialiseServicesAsync();

            if (loadMainMenuWhenReady && SceneManager.GetActiveScene().name == SceneCatalog.Boot)
            {
                SceneManager.LoadScene(SceneCatalog.MainMenu);
            }
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
