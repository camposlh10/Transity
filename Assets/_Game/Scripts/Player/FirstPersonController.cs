using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Owner-authoritative first person movement. The owning client simulates its own
    /// CharacterController and the NetworkTransform (AuthorityMode = Owner) replicates
    /// the result. That is the right trade for co-op PvE: no rollback machinery, and a
    /// cheating client can only misreport its own position, not damage or rewards --
    /// those stay on the server.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : NetworkBehaviour
    {
        [SerializeField] PlayerInputReader input;

        [Header("Speeds (m/s)")]
        [SerializeField] float walkSpeed = 3.6f;
        [SerializeField] float sprintSpeed = 6.2f;
        [SerializeField] float crouchSpeed = 1.8f;
        [SerializeField] float acceleration = 14f;

        [Header("Body")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.1f;
        [SerializeField] float crouchLerpSpeed = 10f;

        [Header("Jump / gravity")]
        [SerializeField] float jumpHeight = 1.05f;
        [SerializeField] float gravity = -19.6f;
        [SerializeField] float groundedStick = -2f;

        [Header("Head")]
        [SerializeField] Transform headPivot;
        [SerializeField] float headHeightStanding = 1.65f;
        [SerializeField] float headHeightCrouched = 0.95f;

        CharacterController m_Controller;
        Vector3 m_Velocity;
        Vector3 m_HorizontalVelocity;
        bool m_Crouching;

        /// <summary>Horizontal speed in m/s. Read by the noise system so sprinting is loud.</summary>
        public float CurrentSpeed => m_HorizontalVelocity.magnitude;
        public bool IsCrouching => m_Crouching;
        public bool IsGrounded => m_Controller != null && m_Controller.isGrounded;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // Remote proxies are driven entirely by NetworkTransform; simulating them
            // locally would fight the replicated state.
            enabled = IsOwner;
        }

        void Update()
        {
            if (!IsOwner || input == null)
            {
                return;
            }

            if (input.Suppressed)
            {
                m_HorizontalVelocity = Vector3.zero;
                ApplyGravityOnly();
                return;
            }

            UpdateCrouch();
            UpdateMove();
        }

        void UpdateCrouch()
        {
            var wantsCrouch = input.CrouchHeld;

            // Refuse to stand up under geometry.
            if (m_Crouching && !wantsCrouch && !HasHeadroom())
            {
                wantsCrouch = true;
            }

            m_Crouching = wantsCrouch;

            var targetHeight = m_Crouching ? crouchHeight : standingHeight;
            m_Controller.height = Mathf.Lerp(m_Controller.height, targetHeight, crouchLerpSpeed * Time.deltaTime);
            m_Controller.center = new Vector3(0f, m_Controller.height * 0.5f, 0f);

            if (headPivot != null)
            {
                var targetHeadHeight = m_Crouching ? headHeightCrouched : headHeightStanding;
                var local = headPivot.localPosition;
                local.y = Mathf.Lerp(local.y, targetHeadHeight, crouchLerpSpeed * Time.deltaTime);
                headPivot.localPosition = local;
            }
        }

        bool HasHeadroom()
        {
            var origin = transform.position + Vector3.up * (m_Controller.radius + 0.05f);
            var distance = standingHeight - m_Controller.radius * 2f;
            return !Physics.SphereCast(origin, m_Controller.radius * 0.95f, Vector3.up, out _, distance,
                ~0, QueryTriggerInteraction.Ignore);
        }

        void UpdateMove()
        {
            var wish = input.Move;
            var desired = transform.right * wish.x + transform.forward * wish.y;
            desired = Vector3.ClampMagnitude(desired, 1f);

            var targetSpeed = m_Crouching ? crouchSpeed : (input.SprintHeld ? sprintSpeed : walkSpeed);
            var targetVelocity = desired * targetSpeed;

            m_HorizontalVelocity = Vector3.MoveTowards(
                m_HorizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

            if (m_Controller.isGrounded)
            {
                if (m_Velocity.y < 0f)
                {
                    m_Velocity.y = groundedStick;
                }

                if (input.JumpPressed && !m_Crouching)
                {
                    m_Velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
            }

            m_Velocity.y += gravity * Time.deltaTime;

            var motion = m_HorizontalVelocity + Vector3.up * m_Velocity.y;
            m_Controller.Move(motion * Time.deltaTime);
        }

        void ApplyGravityOnly()
        {
            if (m_Controller.isGrounded && m_Velocity.y < 0f)
            {
                m_Velocity.y = groundedStick;
            }

            m_Velocity.y += gravity * Time.deltaTime;
            m_Controller.Move(Vector3.up * (m_Velocity.y * Time.deltaTime));
        }

        /// <summary>Cancels momentum. Called after a teleport so the player does not drift.</summary>
        public void ResetMomentum()
        {
            m_HorizontalVelocity = Vector3.zero;
            m_Velocity = Vector3.zero;
        }
    }
}
