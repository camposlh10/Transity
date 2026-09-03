using System;
using Transity.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>Coarse movement state, replicated so creatures can hear and see it.</summary>
    public enum MoveState : byte
    {
        Still = 0,
        Walking = 1,
        Sprinting = 2,
        Crouching = 3
    }

    /// <summary>
    /// Owner-authoritative first person movement. The owning client simulates its own
    /// CharacterController and the NetworkTransform (AuthorityMode = Owner) replicates
    /// the result. That is the right trade for co-op PvE: no rollback machinery, and a
    /// cheating client can only misreport its own position, not damage or rewards --
    /// those stay on the server.
    ///
    /// Stamina lives here, on the owner, for the same reason: it only gates movement.
    /// What the server needs to know -- is this player sprinting, crouching, standing
    /// still -- is one replicated byte, and the server turns it into noise for creatures.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : NetworkBehaviour
    {
        [SerializeField] PlayerInputReader input;

        [Header("Speeds (m/s)")]
        [SerializeField] float walkSpeed = 3.6f;
        [SerializeField] float sprintSpeed = 6.4f;
        [SerializeField] float crouchSpeed = 1.8f;
        [SerializeField] float acceleration = 16f;

        [Header("Stamina")]
        [SerializeField] float maxStamina = 100f;
        [SerializeField] float sprintDrainPerSecond = 16f;
        [SerializeField] float regenPerSecond = 14f;
        [SerializeField] float regenDelay = 1.2f;
        [Tooltip("Cannot start sprinting below this; stops the stutter-sprint at the bottom of the bar.")]
        [SerializeField] float minimumToSprint = 12f;

        [Header("Body")]
        [SerializeField] float standingHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.1f;
        [SerializeField] float crouchLerpSpeed = 10f;

        [Header("Jump / gravity")]
        [SerializeField] float jumpHeight = 1.05f;
        [SerializeField] float gravity = -19.6f;
        [SerializeField] float groundedStick = -2f;
        [SerializeField] float jumpStaminaCost = 10f;

        [Header("Head")]
        [SerializeField] Transform headPivot;
        [SerializeField] float headHeightStanding = 1.65f;
        [SerializeField] float headHeightCrouched = 0.95f;
        [SerializeField] float bobAmplitude = 0.035f;
        [SerializeField] float bobFrequency = 1.9f;
        [SerializeField] float landingDip = 0.12f;

        [Header("Noise")]
        [Tooltip("Metres a creature can hear a footstep from, per state: still, walk, sprint, crouch.")]
        [SerializeField] float[] noiseRadiusByState = { 0f, 9f, 26f, 3f };
        [SerializeField] float footstepInterval = 0.42f;

        CharacterController m_Controller;
        Vector3 m_Velocity;
        Vector3 m_HorizontalVelocity;
        bool m_Crouching;
        bool m_Sprinting;
        float m_Stamina;
        float m_RegenAt;
        float m_BobTime;
        float m_LandingOffset;
        float m_LastFallSpeed;
        bool m_WasGrounded;
        float m_NextFootstepNoise;
        float m_AdrenalineUntil;

        readonly NetworkVariable<MoveState> m_MoveState = new(
            MoveState.Still, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Horizontal speed in m/s.</summary>
        public float CurrentSpeed => m_HorizontalVelocity.magnitude;
        public bool IsCrouching => m_Crouching;
        public bool IsSprinting => m_Sprinting;
        public bool IsGrounded => m_Controller != null && m_Controller.isGrounded;
        public float Stamina01 => maxStamina > 0f ? m_Stamina / maxStamina : 1f;
        public bool IsExhausted => m_Stamina < minimumToSprint;
        public bool HasAdrenaline => Time.time < m_AdrenalineUntil;

        /// <summary>Replicated coarse state; what everyone else -- and every creature -- can tell.</summary>
        public MoveState State => m_MoveState.Value;

        /// <summary>Set by worn gear. 1 is unencumbered.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Set by worn gear. Multiplies footstep noise radius.</summary>
        public float NoiseMultiplier { get; set; } = 1f;

        /// <summary>Raised on the owner when the player lands, with the fall speed.</summary>
        public event Action<float> Landed;

        /// <summary>Raised on the owner on each footstep, for the audio layer.</summary>
        public event Action<bool> Footstep;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Stamina = maxStamina;
        }

        public override void OnNetworkSpawn()
        {
            // Remote proxies are driven entirely by NetworkTransform; simulating them
            // locally would fight the replicated state. The server half stays enabled on
            // the host for noise, and does nothing else there.
            enabled = IsOwner || IsServer;
            m_WasGrounded = true;
        }

        void Update()
        {
            if (IsServer)
            {
                EmitNoise();
            }

            if (!IsOwner || input == null)
            {
                return;
            }

            if (input.Suppressed)
            {
                m_HorizontalVelocity = Vector3.zero;
                m_Sprinting = false;
                ApplyGravityOnly();
                UpdateStamina(false);
                PublishState();
                return;
            }

            UpdateCrouch();
            UpdateMove();
            UpdateStamina(m_Sprinting);
            UpdateHeadBob();
            PublishState();
        }

        // ------------------------------------------------------------------ movement

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

            // Sprint needs a forward component and a little in the tank. Once running you
            // can go until empty, so the bar reads as a real resource, not a hard gate.
            var wantsSprint = input.SprintHeld && wish.y > 0.1f && !m_Crouching;
            var canStart = m_Stamina >= minimumToSprint || HasAdrenaline;
            m_Sprinting = wantsSprint && (m_Sprinting ? m_Stamina > 0f || HasAdrenaline : canStart);

            var targetSpeed = m_Crouching ? crouchSpeed : (m_Sprinting ? sprintSpeed : walkSpeed);
            targetSpeed *= SpeedMultiplier * (HasAdrenaline ? 1.15f : 1f);

            var targetVelocity = desired * targetSpeed;

            m_HorizontalVelocity = Vector3.MoveTowards(
                m_HorizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

            var grounded = m_Controller.isGrounded;

            if (grounded)
            {
                if (!m_WasGrounded)
                {
                    HandleLanding();
                }

                if (m_Velocity.y < 0f)
                {
                    m_Velocity.y = groundedStick;
                }

                if (input.JumpPressed && !m_Crouching && (m_Stamina >= jumpStaminaCost || HasAdrenaline))
                {
                    m_Velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    if (!HasAdrenaline)
                    {
                        m_Stamina = Mathf.Max(0f, m_Stamina - jumpStaminaCost);
                    }

                    m_RegenAt = Time.time + regenDelay;
                }
            }
            else
            {
                m_LastFallSpeed = m_Velocity.y;
            }

            m_WasGrounded = grounded;
            m_Velocity.y += gravity * Time.deltaTime;

            var motion = m_HorizontalVelocity + Vector3.up * m_Velocity.y;
            m_Controller.Move(motion * Time.deltaTime);
        }

        void HandleLanding()
        {
            var impact = Mathf.Max(0f, -m_LastFallSpeed);
            m_LandingOffset = -Mathf.Clamp(impact * 0.02f, 0f, landingDip);
            Landed?.Invoke(impact);
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

        // ------------------------------------------------------------------- stamina

        void UpdateStamina(bool draining)
        {
            if (draining && !HasAdrenaline)
            {
                m_Stamina = Mathf.Max(0f, m_Stamina - sprintDrainPerSecond * Time.deltaTime);
                m_RegenAt = Time.time + regenDelay;
            }
            else if (Time.time >= m_RegenAt)
            {
                m_Stamina = Mathf.Min(maxStamina, m_Stamina + regenPerSecond * Time.deltaTime);
            }
        }

        /// <summary>Owner only. Fills stamina; used by consumables.</summary>
        public void RestoreStamina() => m_Stamina = maxStamina;

        /// <summary>Owner only. Adrenaline: speed up and no drain for a while.</summary>
        public void GrantAdrenaline(float seconds)
        {
            m_AdrenalineUntil = Mathf.Max(m_AdrenalineUntil, Time.time + seconds);
        }

        // ---------------------------------------------------------------------- head

        /// <summary>
        /// A short vertical cycle while moving, faster when sprinting, plus a dip on landing.
        /// Kept small: this is a horror game and the camera should feel like a head, not a
        /// boat. Aim stays untouched -- only the pivot height moves.
        /// </summary>
        void UpdateHeadBob()
        {
            if (headPivot == null)
            {
                return;
            }

            var speed = CurrentSpeed;
            var grounded = m_Controller.isGrounded;
            var moving = grounded && speed > 0.2f;

            if (moving)
            {
                var rate = bobFrequency * (m_Sprinting ? 1.45f : m_Crouching ? 0.7f : 1f);
                var previous = m_BobTime;
                m_BobTime += Time.deltaTime * rate;

                // A footstep lands on each half-cycle.
                if (Mathf.Floor(m_BobTime * 2f) != Mathf.Floor(previous * 2f))
                {
                    Footstep?.Invoke(m_Sprinting);
                }
            }
            else
            {
                m_BobTime = Mathf.MoveTowards(m_BobTime, Mathf.Round(m_BobTime * 2f) * 0.5f, Time.deltaTime * 2f);
            }

            var amplitude = bobAmplitude * (m_Sprinting ? 1.6f : m_Crouching ? 0.5f : 1f) * (moving ? 1f : 0f);
            var bob = -Mathf.Abs(Mathf.Sin(m_BobTime * Mathf.PI * 2f)) * amplitude;

            m_LandingOffset = Mathf.MoveTowards(m_LandingOffset, 0f, Time.deltaTime * 0.9f);

            var targetHeadHeight = (m_Crouching ? headHeightCrouched : headHeightStanding) + bob + m_LandingOffset;
            var local = headPivot.localPosition;
            local.y = Mathf.Lerp(local.y, targetHeadHeight, crouchLerpSpeed * Time.deltaTime);
            headPivot.localPosition = local;
        }

        // ----------------------------------------------------------------- replication

        void PublishState()
        {
            var state = m_Crouching ? MoveState.Crouching
                : m_Sprinting && CurrentSpeed > 0.5f ? MoveState.Sprinting
                : CurrentSpeed > 0.2f ? MoveState.Walking
                : MoveState.Still;

            if (m_MoveState.Value != state)
            {
                m_MoveState.Value = state;
            }
        }

        /// <summary>
        /// Server side. Turns the replicated state into something creatures can hear. The
        /// radius is what the design calls "loud": sprinting carries across a clearing,
        /// crouching barely past your own feet.
        /// </summary>
        void EmitNoise()
        {
            var state = (int)m_MoveState.Value;
            if (state < 0 || state >= noiseRadiusByState.Length || noiseRadiusByState[state] <= 0f)
            {
                return;
            }

            if (Time.time < m_NextFootstepNoise)
            {
                return;
            }

            m_NextFootstepNoise = Time.time + footstepInterval;
            var kind = m_MoveState.Value == MoveState.Sprinting ? NoiseKind.Sprint : NoiseKind.Footstep;
            NoiseBus.Emit(transform.position, noiseRadiusByState[state] * NoiseMultiplier, kind, OwnerClientId);
        }

        /// <summary>Cancels momentum. Called after a teleport so the player does not drift.</summary>
        public void ResetMomentum()
        {
            m_HorizontalVelocity = Vector3.zero;
            m_Velocity = Vector3.zero;
        }
    }
}
