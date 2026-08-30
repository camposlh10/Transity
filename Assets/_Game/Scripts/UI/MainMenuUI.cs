using Transity.Core;
using Transity.Networking;
using Transity.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Transity.UI
{
    /// <summary>
    /// Graybox front end: host a private session, or join one with a code. Intentionally
    /// plain -- it exists so the loop can be tested, and gets replaced in the polish pass.
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        [SerializeField] Button hostButton;
        [SerializeField] Button joinButton;
        [SerializeField] Button quitButton;
        [SerializeField] InputField codeField;
        [SerializeField] Text statusLabel;

        void OnEnable()
        {
            PlayerCharacter.SetCursorLocked(false);

            if (hostButton != null)
            {
                hostButton.onClick.AddListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.AddListener(OnJoinClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(OnQuitClicked);
            }

            GameBootstrap.OnlineStateChanged += HandleOnlineStateChanged;

            if (SessionManager.Exists)
            {
                SessionManager.Instance.StatusChanged += HandleSessionStatusChanged;
            }

            RefreshStatus();
        }

        void OnDisable()
        {
            if (hostButton != null)
            {
                hostButton.onClick.RemoveListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(OnJoinClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
            }

            GameBootstrap.OnlineStateChanged -= HandleOnlineStateChanged;

            if (SessionManager.Exists)
            {
                SessionManager.Instance.StatusChanged -= HandleSessionStatusChanged;
            }
        }

        void HandleOnlineStateChanged(GameBootstrap.OnlineState _) => RefreshStatus();

        void HandleSessionStatusChanged(SessionStatus _, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        void RefreshStatus()
        {
            switch (GameBootstrap.Online)
            {
                case GameBootstrap.OnlineState.Initialising:
                    SetStatus("Connecting to Unity services...");
                    SetButtonsInteractable(false);
                    break;
                case GameBootstrap.OnlineState.Ready:
                    SetStatus("Ready.");
                    SetButtonsInteractable(true);
                    break;
                case GameBootstrap.OnlineState.Unavailable:
                    SetStatus($"Online unavailable: {GameBootstrap.OnlineFailureReason}\n" +
                              "Link a Unity Cloud project in Project Settings > Services.");
                    SetButtonsInteractable(false);
                    break;
            }
        }

        void SetButtonsInteractable(bool value)
        {
            if (hostButton != null)
            {
                hostButton.interactable = value;
            }

            if (joinButton != null)
            {
                joinButton.interactable = value;
            }
        }

        async void OnHostClicked()
        {
            if (!SessionManager.Exists)
            {
                return;
            }

            SetButtonsInteractable(false);
            await SessionManager.Instance.HostAsync(null);
            SetButtonsInteractable(GameBootstrap.Online == GameBootstrap.OnlineState.Ready);
        }

        async void OnJoinClicked()
        {
            if (!SessionManager.Exists)
            {
                return;
            }

            SetButtonsInteractable(false);
            await SessionManager.Instance.JoinByCodeAsync(codeField != null ? codeField.text : string.Empty);
            SetButtonsInteractable(GameBootstrap.Online == GameBootstrap.OnlineState.Ready);
        }

        static void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetStatus(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }
        }
    }
}
