using Transity.Train;
using Transity.UI;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Takes the local player out of first-person control while a station screen is open:
    /// blends a dedicated camera to the station's focus point, suppresses movement and
    /// look, and frees the cursor for the UI.
    ///
    /// Deliberately a separate camera rather than moving the player's own. The player
    /// camera is a child of the head pivot that <see cref="PlayerLook"/> drives every
    /// LateUpdate, so animating it directly would mean fighting the controller.
    /// </summary>
    public sealed class StationFocusController : MonoBehaviour
    {
        [SerializeField] PlayerCharacter character;
        [SerializeField] PlayerInputReader input;
        [SerializeField] float blendDuration = 0.35f;

        Camera m_FocusCamera;
        StationTerminal m_Terminal;
        Vector3 m_FromPosition;
        Quaternion m_FromRotation;
        float m_BlendTimer;
        bool m_Blending;
        int m_OpenedFrame = -1;

        public bool IsOpen => m_Terminal != null;
        public StationTerminal Terminal => m_Terminal;

        void Awake()
        {
            if (character == null)
            {
                character = GetComponent<PlayerCharacter>();
            }

            if (input == null)
            {
                input = GetComponent<PlayerInputReader>();
            }
        }

        void Update()
        {
            if (m_Blending)
            {
                UpdateBlend();
            }

            if (!IsOpen)
            {
                return;
            }

            // On a host the owner RPC is delivered in the same frame as the key press, so
            // without this the screen would open and close on the same E.
            if (Time.frameCount == m_OpenedFrame)
            {
                return;
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame ||
                                     keyboard.eKey.wasPressedThisFrame))
            {
                Close();
            }
        }

        public void Open(StationTerminal terminal)
        {
            if (terminal == null || IsOpen)
            {
                return;
            }

            m_Terminal = terminal;
            m_OpenedFrame = Time.frameCount;

            EnsureFocusCamera();

            var playerCamera = character != null ? character.PlayerCamera : null;
            if (playerCamera != null)
            {
                m_FromPosition = playerCamera.transform.position;
                m_FromRotation = playerCamera.transform.rotation;
                playerCamera.enabled = false;
            }
            else
            {
                m_FromPosition = transform.position + Vector3.up * 1.65f;
                m_FromRotation = transform.rotation;
            }

            m_FocusCamera.transform.SetPositionAndRotation(m_FromPosition, m_FromRotation);
            m_FocusCamera.enabled = true;
            m_BlendTimer = 0f;
            m_Blending = true;

            if (input != null)
            {
                input.SetSuppressed(true);
            }

            PlayerCharacter.SetCursorLocked(false);

            if (StationScreenUI.Instance != null)
            {
                StationScreenUI.Instance.Open(terminal, this);
            }
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            m_Terminal = null;
            m_Blending = false;

            if (m_FocusCamera != null)
            {
                m_FocusCamera.enabled = false;
            }

            var playerCamera = character != null ? character.PlayerCamera : null;
            if (playerCamera != null)
            {
                playerCamera.enabled = true;
            }

            if (input != null)
            {
                input.SetSuppressed(false);
            }

            PlayerCharacter.SetCursorLocked(true);

            if (StationScreenUI.Instance != null)
            {
                StationScreenUI.Instance.Close();
            }
        }

        void EnsureFocusCamera()
        {
            if (m_FocusCamera != null)
            {
                return;
            }

            var go = new GameObject("StationFocusCamera");
            m_FocusCamera = go.AddComponent<Camera>();
            m_FocusCamera.nearClipPlane = 0.05f;
            m_FocusCamera.farClipPlane = 400f;
            m_FocusCamera.fieldOfView = 55f;
            m_FocusCamera.enabled = false;
        }

        void UpdateBlend()
        {
            if (m_Terminal == null || m_FocusCamera == null)
            {
                m_Blending = false;
                return;
            }

            m_BlendTimer += Time.deltaTime;
            var t = blendDuration <= 0f ? 1f : Mathf.Clamp01(m_BlendTimer / blendDuration);
            var smooth = t * t * (3f - 2f * t);

            var focus = m_Terminal.FocusPoint;
            var targetPosition = focus.position;
            var targetRotation = m_Terminal.LookTarget != null
                ? Quaternion.LookRotation(
                    (m_Terminal.LookTarget.position - focus.position).normalized, Vector3.up)
                : focus.rotation;

            m_FocusCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(m_FromPosition, targetPosition, smooth),
                Quaternion.Slerp(m_FromRotation, targetRotation, smooth));

            if (t >= 1f)
            {
                m_Blending = false;
            }
        }

        void OnDisable()
        {
            if (IsOpen)
            {
                Close();
            }
        }
    }
}
