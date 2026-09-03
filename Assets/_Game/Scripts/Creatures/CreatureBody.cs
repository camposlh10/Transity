using System;
using Transity.Combat;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// Moves a graybox creature's parts so it reads as alive without a single animation
    /// clip: legs swing with distance travelled, the torso rises and rolls with the gait,
    /// the ribs breathe, the head tracks its target, the whole body coils before a lunge
    /// and flinches when hit. Runs on every peer from the replicated state and the
    /// motion of the replicated transform.
    ///
    /// Everything is driven by two numbers -- speed and stride phase -- plus the state, so
    /// the same code animates a hound, a stilt-walker and a bear-sized brute; only the
    /// part transforms the scaffold builds differ.
    /// </summary>
    public sealed class CreatureBody : MonoBehaviour
    {
        [SerializeField] CreatureBrain brain;
        [SerializeField] Transform torso;
        [SerializeField] Transform head;
        [SerializeField] Transform[] legs = Array.Empty<Transform>();
        [SerializeField] Transform tail;
        [SerializeField] Renderer[] eyes = Array.Empty<Renderer>();
        [SerializeField] float strideLength = 1.4f;

        Vector3 m_LastPosition;
        Vector3 m_Velocity;
        float m_Phase;
        float m_Speed;
        float m_StateChangedAt;
        float m_Flinch;
        float m_Lean;
        float m_Lie;
        Vector3[] m_LegRest;
        Vector3 m_TorsoRest;
        Vector3 m_HeadRest;
        float m_Breath;
        MaterialPropertyBlock m_EyeBlock;
        CreatureState m_LastState;
        int m_LastStep;

        /// <summary>Raised on a footfall, for the audio layer.</summary>
        public event Action Step;

        /// <summary>Client-side speed estimate, metres per second.</summary>
        public float Speed => m_Speed;

        void Awake()
        {
            if (brain == null)
            {
                brain = GetComponent<CreatureBrain>();
            }

            m_LegRest = new Vector3[legs.Length];
            for (var i = 0; i < legs.Length; i++)
            {
                m_LegRest[i] = legs[i] != null ? legs[i].localPosition : Vector3.zero;
            }

            m_TorsoRest = torso != null ? torso.localPosition : Vector3.zero;
            m_HeadRest = head != null ? head.localPosition : Vector3.zero;
            m_LastPosition = transform.position;
            m_EyeBlock = new MaterialPropertyBlock();
        }

        // Fetched directly rather than through the brain: OnEnable here can run before
        // the brain's Awake has cached it.
        Health m_Health;

        void OnEnable()
        {
            if (brain != null)
            {
                brain.StateChanged += HandleStateChanged;
            }

            m_Health = GetComponent<Health>();
            if (m_Health != null)
            {
                m_Health.HitReceived += HandleHit;
            }
        }

        void OnDisable()
        {
            if (brain != null)
            {
                brain.StateChanged -= HandleStateChanged;
            }

            if (m_Health != null)
            {
                m_Health.HitReceived -= HandleHit;
            }
        }

        void HandleStateChanged(CreatureState previous, CreatureState current)
        {
            m_StateChangedAt = Time.time;
        }

        void HandleHit(Vector3 direction, float amount)
        {
            m_Flinch = Mathf.Min(1f, m_Flinch + Mathf.Clamp(amount / 60f, 0.25f, 1f));
        }

        void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // Speed from displacement: true on the server and on every interpolated client.
            var delta = transform.position - m_LastPosition;
            m_LastPosition = transform.position;
            delta.y = 0f;
            m_Velocity = Vector3.Lerp(m_Velocity, delta / dt, 10f * dt);
            m_Speed = m_Velocity.magnitude;

            var state = brain != null ? brain.State : CreatureState.Idle;
            var definition = brain != null ? brain.Definition : null;
            var runSpeed = definition != null ? definition.runSpeed : 6f;

            // ---- stride ----
            var previousPhase = m_Phase;
            m_Phase += m_Speed * dt / Mathf.Max(0.3f, strideLength);
            var stepIndex = Mathf.FloorToInt(m_Phase * 2f);
            if (stepIndex != m_LastStep && m_Speed > 0.3f)
            {
                m_LastStep = stepIndex;
                Step?.Invoke();
            }

            var gait = Mathf.Clamp01(m_Speed / Mathf.Max(0.1f, runSpeed));
            var stride = m_Phase * Mathf.PI * 2f;

            // ---- posture targets by state ----
            var leanTarget = 0f;
            var lieTarget = 0f;
            var crouch = 0f;

            switch (state)
            {
                case CreatureState.Stalk:
                    crouch = 0.35f;
                    break;
                case CreatureState.Attack:
                    var windup = brain != null ? brain.WindupProgress(Time.time - m_StateChangedAt) : 0f;
                    // Coil back through the wind-up, then throw forward.
                    leanTarget = windup < 1f ? -0.6f * windup : 1f;
                    crouch = windup < 1f ? 0.5f * windup : 0f;
                    break;
                case CreatureState.Flee:
                    crouch = 0.15f;
                    break;
                case CreatureState.Recover:
                    crouch = 0.45f;
                    break;
                case CreatureState.Sedated:
                case CreatureState.Dead:
                    lieTarget = 1f;
                    break;
            }

            m_Lean = Mathf.Lerp(m_Lean, leanTarget, 12f * dt);
            m_Lie = Mathf.MoveTowards(m_Lie, lieTarget, dt * (lieTarget > 0f ? 1.6f : 0.8f));
            m_Flinch = Mathf.MoveTowards(m_Flinch, 0f, dt * 3f);
            m_Breath += dt * (state is CreatureState.Chase or CreatureState.Attack ? 3.2f : 1.4f);

            // ---- torso ----
            if (torso != null)
            {
                var bob = Mathf.Abs(Mathf.Sin(stride)) * 0.06f * gait;
                var roll = Mathf.Sin(stride) * 4f * gait;
                var breathe = Mathf.Sin(m_Breath) * 0.012f;
                var flinchOffset = new Vector3(0f, 0f, -0.15f) * m_Flinch;
                var lieOffset = new Vector3(0f, -m_TorsoRest.y * 0.55f, 0f) * m_Lie;

                torso.localPosition = m_TorsoRest + new Vector3(0f, bob - crouch * 0.25f + breathe, m_Lean * 0.25f)
                                      + flinchOffset + lieOffset;
                torso.localRotation = Quaternion.Euler(
                    -m_Lean * 14f + m_Flinch * 8f + crouch * 6f,
                    0f,
                    roll + m_Lie * 78f);
                torso.localScale = new Vector3(1f + breathe * 2f, 1f, 1f + breathe);
            }

            // ---- legs: alternate pairs, lift on the swing half ----
            for (var i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                if (leg == null)
                {
                    continue;
                }

                // Diagonal pairs move together on a quadruped: 0 and 3, 1 and 2.
                var offset = (i == 0 || i == 3) ? 0f : Mathf.PI;
                var swing = Mathf.Sin(stride + offset);
                var lift = Mathf.Max(0f, Mathf.Cos(stride + offset));

                var rest = m_LegRest[i];
                var forwardSwing = swing * 0.28f * gait;
                var raise = lift * 0.16f * gait;

                var splay = m_Lie * (i % 2 == 0 ? -0.3f : 0.3f);
                leg.localPosition = rest + new Vector3(splay, raise - crouch * 0.2f, forwardSwing);
                leg.localRotation = Quaternion.Euler(-swing * 28f * gait, 0f, m_Lie * (i % 2 == 0 ? 60f : -60f));
            }

            // ---- head: track the target, dip when stalking, hang when down ----
            if (head != null)
            {
                var targetRotation = Quaternion.identity;
                var target = TargetPosition();
                if (target.HasValue && m_Lie < 0.5f)
                {
                    var local = transform.InverseTransformPoint(target.Value) - m_HeadRest;
                    if (local.sqrMagnitude > 0.01f)
                    {
                        var look = Quaternion.LookRotation(local.normalized, Vector3.up);
                        // Clamp so it never twists its neck round.
                        var euler = look.eulerAngles;
                        euler.x = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -35f, 35f);
                        euler.y = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.y), -70f, 70f);
                        targetRotation = Quaternion.Euler(euler.x, euler.y, 0f);
                    }
                }

                var stalkDip = Quaternion.Euler(crouch * 25f, 0f, 0f);
                var lieDrop = Quaternion.Euler(0f, 0f, m_Lie * 40f);
                head.localRotation = Quaternion.Slerp(head.localRotation,
                    targetRotation * stalkDip * lieDrop, 8f * dt);
                head.localPosition = m_HeadRest + new Vector3(0f, -crouch * 0.3f - m_Lie * 0.4f, m_Lean * 0.15f);
            }

            // ---- tail ----
            if (tail != null)
            {
                var wag = Mathf.Sin(Time.time * (state == CreatureState.Chase ? 9f : 2.5f)) * (state == CreatureState.Stalk ? 4f : 12f);
                tail.localRotation = Quaternion.Euler(m_Lie * 30f, wag, 0f);
            }

            // ---- eyes ----
            if (eyes.Length > 0 && (state != m_LastState || m_Flinch > 0f))
            {
                var color = state switch
                {
                    CreatureState.Chase or CreatureState.Attack => definition != null ? definition.eyeColor * 6f : Color.red * 6f,
                    CreatureState.Stalk or CreatureState.Investigate => definition != null ? definition.eyeColor * 2.5f : Color.yellow * 2f,
                    CreatureState.Dead => Color.black,
                    CreatureState.Sedated => (definition != null ? definition.eyeColor : Color.white) * 0.3f,
                    _ => definition != null ? definition.eyeColor * 1.2f : Color.white
                };

                m_EyeBlock.SetColor("_EmissionColor", color);
                m_EyeBlock.SetColor("_BaseColor", color * 0.2f);
                foreach (var eye in eyes)
                {
                    if (eye != null)
                    {
                        eye.SetPropertyBlock(m_EyeBlock);
                    }
                }
            }

            m_LastState = state;
        }

        Vector3? TargetPosition()
        {
            if (brain == null || brain.TargetClientId == DamageInfo.NoInstigator)
            {
                return null;
            }

            var target = Player.PlayerVitals.Find(brain.TargetClientId);
            return target != null ? target.transform.position + Vector3.up * 1.4f : null;
        }
    }
}
