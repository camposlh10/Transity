using Transity.Interaction;
using Transity.Missions;
using Transity.Networking;
using Transity.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Transity.UI
{
    /// <summary>
    /// In-game overlay: interaction prompt, session code, mission phase and transient
    /// server messages. Binds itself to the local player once it spawns, since the HUD
    /// exists in the scene before anyone connects.
    /// </summary>
    public sealed class HudUI : MonoBehaviour
    {
        [SerializeField] Text promptLabel;
        [SerializeField] Text sessionLabel;
        [SerializeField] Text phaseLabel;
        [SerializeField] Text messageLabel;
        [SerializeField] Image crosshair;
        [SerializeField] float messageDuration = 3f;

        Interactor m_Interactor;
        PlayerFeedback m_Feedback;
        float m_MessageClearAt;

        void Update()
        {
            BindLocalPlayerIfNeeded();
            RefreshSessionLabel();
            RefreshPhaseLabel();
            ExpireMessage();
            HandleCursorToggle();
        }

        void BindLocalPlayerIfNeeded()
        {
            var local = PlayerCharacter.Local;
            if (local == null)
            {
                if (m_Interactor != null)
                {
                    Unbind();
                }

                return;
            }

            if (m_Interactor != null)
            {
                return;
            }

            m_Interactor = local.GetComponent<Interactor>();
            if (m_Interactor != null)
            {
                m_Interactor.TargetChanged += HandleTargetChanged;
            }

            m_Feedback = local.GetComponent<PlayerFeedback>();
            if (m_Feedback != null)
            {
                m_Feedback.MessageReceived += ShowMessage;
            }
        }

        void Unbind()
        {
            if (m_Interactor != null)
            {
                m_Interactor.TargetChanged -= HandleTargetChanged;
                m_Interactor = null;
            }

            if (m_Feedback != null)
            {
                m_Feedback.MessageReceived -= ShowMessage;
                m_Feedback = null;
            }

            SetPrompt(string.Empty);
        }

        void OnDisable() => Unbind();

        void HandleTargetChanged(IInteractable target, string prompt)
        {
            SetPrompt(target != null ? $"[E] {prompt}" : string.Empty);
        }

        void SetPrompt(string text)
        {
            if (promptLabel != null)
            {
                promptLabel.text = text;
            }

            if (crosshair != null)
            {
                crosshair.color = string.IsNullOrEmpty(text)
                    ? new Color(1f, 1f, 1f, 0.35f)
                    : new Color(1f, 0.85f, 0.4f, 0.95f);
            }
        }

        void RefreshSessionLabel()
        {
            if (sessionLabel == null)
            {
                return;
            }

            var code = SessionManager.Exists ? SessionManager.Instance.JoinCode : null;
            sessionLabel.text = string.IsNullOrEmpty(code) ? string.Empty : $"Join code  {code}";
        }

        void RefreshPhaseLabel()
        {
            if (phaseLabel == null)
            {
                return;
            }

            phaseLabel.text = MissionDirector.Instance != null
                ? MissionDirector.Instance.Phase.ToString()
                : string.Empty;
        }

        void ShowMessage(string message)
        {
            if (messageLabel != null)
            {
                messageLabel.text = message;
            }

            m_MessageClearAt = Time.time + messageDuration;
        }

        void ExpireMessage()
        {
            if (messageLabel != null && m_MessageClearAt > 0f && Time.time >= m_MessageClearAt)
            {
                messageLabel.text = string.Empty;
                m_MessageClearAt = 0f;
            }
        }

        /// <summary>
        /// Escape releases the cursor. Essential when running several editor instances
        /// under Multiplayer Play Mode.
        /// </summary>
        static void HandleCursorToggle()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                PlayerCharacter.SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
            }
        }
    }
}
