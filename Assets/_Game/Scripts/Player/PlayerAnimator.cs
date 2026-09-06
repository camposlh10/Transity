using Transity.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Feeds the character's Animator from the player's actual motion.
    ///
    /// Speed and direction are derived from how far the transform moved, not from input,
    /// because this has to be right on every peer: in first person your own body is only
    /// ever seen as a shadow, and what everyone *else* sees is a remote proxy driven
    /// entirely by NetworkTransform. Reading input would animate the owner correctly and
    /// leave every other player sliding about in a T-pose.
    ///
    /// Grounded and jump are the exception -- they cannot be inferred reliably from
    /// displacement (walking down a slope looks like falling) -- so the owner replicates
    /// those two directly. They are cheap: one bool that changes on landing, and one RPC
    /// per jump.
    /// </summary>
    public sealed class PlayerAnimator : NetworkBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MoveXId = Animator.StringToHash("MoveX");
        static readonly int MoveYId = Animator.StringToHash("MoveY");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int CrouchingId = Animator.StringToHash("Crouching");
        static readonly int JumpId = Animator.StringToHash("Jump");

        [SerializeField] CharacterSkin skin;
        [SerializeField] FirstPersonController movement;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerVitals vitals;
        [SerializeField] PlayerEquipment equipment;

        [Tooltip("How quickly the blend follows a change of speed or direction.")]
        [SerializeField] float smoothing = 12f;

        readonly NetworkVariable<bool> m_Grounded = new(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Crouching is a chosen pose rather than something visible in the motion, so unlike
        // speed it cannot be measured from displacement and has to be told to everyone.
        readonly NetworkVariable<bool> m_Crouching = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Animator m_Animator;
        Vector3 m_LastPosition;
        Vector3 m_Velocity;
        float m_Speed;
        Vector2 m_Direction;
        bool m_WasGrounded = true;
        float m_JumpPressedAt = -10f;

        void Awake()
        {
            if (movement == null) movement = GetComponent<FirstPersonController>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (skin == null) skin = GetComponent<CharacterSkin>();
            if (vitals == null) vitals = GetComponent<PlayerVitals>();
            if (equipment == null) equipment = GetComponent<PlayerEquipment>();

            m_LastPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            RebindAnimator();

            if (skin != null)
            {
                // The body swaps when a player changes character, taking its Animator with it.
                skin.SelectionChanged += _ => RebindAnimator();
            }
        }

        /// <summary>
        /// Finds the Animator on whichever body is currently active. Only one is enabled at
        /// a time, so an inactive search would find the wrong one.
        /// </summary>
        void RebindAnimator()
        {
            m_Animator = null;

            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                if (animator.gameObject.activeInHierarchy)
                {
                    m_Animator = animator;
                    break;
                }
            }
        }

        void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            if (IsOwner)
            {
                PublishGroundState();
            }

            // The active body can change without a selection event (first spawn ordering),
            // so re-find it if what we have has gone away.
            if (m_Animator == null || !m_Animator.gameObject.activeInHierarchy)
            {
                RebindAnimator();
                if (m_Animator == null)
                {
                    return;
                }
            }

            // ---- motion, measured rather than asked for ----
            var delta = transform.position - m_LastPosition;
            m_LastPosition = transform.position;
            delta.y = 0f;

            m_Velocity = Vector3.Lerp(m_Velocity, delta / dt, smoothing * dt);

            var speed = m_Velocity.magnitude;

            // Below a threshold the direction is noise, so hold the last one rather than
            // letting the blend spin while standing still.
            if (speed > 0.15f)
            {
                var local = transform.InverseTransformDirection(m_Velocity.normalized);
                m_Direction = Vector2.Lerp(m_Direction, new Vector2(local.x, local.z), smoothing * dt);

                // Kept on the unit circle. The directional clips all sit on the rim, and a
                // smoothed vector passing near the origin while reversing would otherwise
                // dip through the centre of the blend and stutter into a standing pose
                // halfway through a turn.
                if (m_Direction.sqrMagnitude > 0.0001f)
                {
                    m_Direction = m_Direction.normalized;
                }
            }

            m_Speed = Mathf.Lerp(m_Speed, speed, smoothing * dt);

            // A dead player should not be jogging on the spot while the body settles.
            if (vitals != null && vitals.IsDead)
            {
                m_Speed = 0f;
            }

            m_Animator.SetFloat(SpeedId, m_Speed);
            m_Animator.SetFloat(MoveXId, m_Direction.x);
            m_Animator.SetFloat(MoveYId, m_Direction.y);
            m_Animator.SetBool(GroundedId, m_Grounded.Value);
            m_Animator.SetBool(CrouchingId, m_Crouching.Value);

            UpdateGripLayer(dt);
        }

        /// <summary>
        /// Fades the upper-body grip pose in while something is held. Faded rather than
        /// switched, or the arms would snap to the pose the instant a slot is selected.
        /// </summary>
        void UpdateGripLayer(float dt)
        {
            if (m_Animator.layerCount < 2)
            {
                return;
            }

            // Held is replicated through the inventory, so this is correct on every peer.
            var wantsGrip = equipment != null && equipment.Held != null
                            && (vitals == null || !vitals.IsDead);

            var current = m_Animator.GetLayerWeight(1);
            m_Animator.SetLayerWeight(1, Mathf.MoveTowards(current, wantsGrip ? 1f : 0f, dt * 6f));
        }

        void PublishGroundState()
        {
            if (movement == null)
            {
                return;
            }

            // Remember the press for a moment: the button goes down on one frame and the
            // controller only reports airborne on the next.
            if (input != null && !input.Suppressed && input.JumpPressed)
            {
                m_JumpPressedAt = Time.time;
            }

            var grounded = movement.IsGrounded;

            if (grounded != m_Grounded.Value)
            {
                m_Grounded.Value = grounded;
            }

            if (movement.IsCrouching != m_Crouching.Value)
            {
                m_Crouching.Value = movement.IsCrouching;
            }

            // The jump itself is an event, not a state: by the time anyone reads "not
            // grounded" the push-off has already been missed.
            //
            // Gated on the button rather than just leaving the ground, so stepping off a
            // ledge falls rather than performing a deliberate little hop on the way down.
            if (m_WasGrounded && !grounded && Time.time - m_JumpPressedAt < 0.25f)
            {
                JumpedRpc();
            }

            m_WasGrounded = grounded;
        }

        /// <summary>Fires the jump on every peer, including the owner's own shadow.</summary>
        [Rpc(SendTo.Everyone)]
        void JumpedRpc()
        {
            if (m_Animator != null)
            {
                m_Animator.SetTrigger(JumpId);
            }
        }
    }
}
