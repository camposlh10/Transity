using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Transity.Player
{
    /// <summary>
    /// Alt+5 pulls the camera back behind the player and shows their own body.
    ///
    /// This is an inspection view, not a second way to play: it exists so animation,
    /// equipment and creature work can be judged on the character rather than on a shadow.
    /// Aim still comes from the head, so shooting from back here is not meant to be fair
    /// and is not balanced for.
    ///
    /// The camera is already a child of the head pivot, which <see cref="PlayerLook"/>
    /// pitches, so pulling straight back along local -Z gives an orbit that follows look
    /// for free -- no separate rig, and nothing fighting the controller.
    /// </summary>
    public sealed class ThirdPersonView : NetworkBehaviour
    {
        [SerializeField] PlayerCharacter character;
        [SerializeField] CharacterSkin skin;
        [SerializeField] FirstPersonBody firstPersonBody;
        [SerializeField] StationFocusController stationFocus;

        [Header("Framing")]
        [SerializeField] float distance = 3.2f;
        [SerializeField] float shoulderOffset = 0.5f;
        [SerializeField] float heightOffset = 0.15f;
        [SerializeField] float followSpeed = 14f;

        [Tooltip("What the camera will not pass through when finding room behind the player.")]
        [SerializeField] LayerMask collisionMask = 1;

        Transform m_Camera;
        Vector3 m_FirstPersonPosition;
        float m_CurrentDistance;
        bool m_Active;
        bool m_CapturedBase;

        public bool IsActive => m_Active;

        void Awake()
        {
            if (character == null) character = GetComponent<PlayerCharacter>();
            if (skin == null) skin = GetComponent<CharacterSkin>();
            if (firstPersonBody == null) firstPersonBody = GetComponent<FirstPersonBody>();
            if (stationFocus == null) stationFocus = GetComponent<StationFocusController>();
        }

        public override void OnNetworkSpawn()
        {
            // Purely local: nobody else needs to know how you are looking at yourself.
            enabled = IsOwner;

            if (!IsOwner || character == null || character.PlayerCamera == null)
            {
                return;
            }

            m_Camera = character.PlayerCamera.transform;
        }

        void Update()
        {
            if (m_Camera == null)
            {
                return;
            }

            if (WasTogglePressed())
            {
                SetActive(!m_Active);
            }

            if (!m_Active)
            {
                return;
            }

            // A station screen drives its own camera; leaving the view on would fight it.
            if (stationFocus != null && stationFocus.IsOpen)
            {
                SetActive(false);
                return;
            }

            UpdateCamera();
        }

        static bool WasTogglePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            // Alt is what keeps this off the hotbar: 1-4 select slots, and a bare 5 should
            // stay free for a fifth one.
            var alt = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
            return alt && keyboard.digit5Key.wasPressedThisFrame;
        }

        void SetActive(bool active)
        {
            m_Active = active;

            if (active)
            {
                // Captured here rather than at spawn: FirstPersonBody nudges the camera off
                // the pivot for the body view, and reading it too early would bake in the
                // pre-nudge value and shift the eye position every time this is toggled.
                if (!m_CapturedBase)
                {
                    m_FirstPersonPosition = m_Camera.localPosition;
                    m_CapturedBase = true;
                }

                m_CurrentDistance = 0f;
            }
            else
            {
                m_Camera.localPosition = m_FirstPersonPosition;
            }

            // The body is already visible in first person; what changes is the head, which
            // is collapsed so the camera is not sitting inside it. Seen from behind you
            // want it back.
            skin?.SetVisibleToOwner(true);
            firstPersonBody?.SetFirstPerson(!active);
        }

        void UpdateCamera()
        {
            var pivot = m_Camera.parent;
            if (pivot == null)
            {
                return;
            }

            var origin = pivot.position;
            var back = -pivot.forward;

            // Find room behind the player. Sphere rather than ray so the camera does not
            // slip through a tree trunk it only clips the edge of.
            var wanted = distance;
            if (Physics.SphereCast(origin, 0.25f, back, out var hit, distance,
                    collisionMask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(0.4f, hit.distance - 0.15f);
            }

            // Easing out feels like a camera; snapping in feels like a bug. Pulling away
            // is smoothed, pushing in to avoid a wall is immediate.
            m_CurrentDistance = wanted < m_CurrentDistance
                ? wanted
                : Mathf.Lerp(m_CurrentDistance, wanted, followSpeed * Time.deltaTime);

            m_Camera.localPosition = m_FirstPersonPosition
                                     + new Vector3(shoulderOffset, heightOffset, -m_CurrentDistance);
        }

        public override void OnNetworkDespawn()
        {
            if (m_Active)
            {
                SetActive(false);
            }
        }
    }
}
