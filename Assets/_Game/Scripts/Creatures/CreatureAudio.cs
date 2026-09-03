using Transity.Audio;
using Transity.Player;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// A creature's voice and footfalls. Fear is mostly heard: the growl before the eyes
    /// are seen, the footsteps behind, the screech that means it has committed. All of
    /// it is driven by replicated state, so every peer hears the same thing.
    /// </summary>
    [RequireComponent(typeof(CreatureBrain), typeof(CreatureBody))]
    public sealed class CreatureAudio : MonoBehaviour
    {
        CreatureBrain m_Brain;
        CreatureBody m_Body;
        Combat.Health m_Health;
        float m_NextBreathAt;

        void Awake()
        {
            m_Brain = GetComponent<CreatureBrain>();
            m_Body = GetComponent<CreatureBody>();
            m_Health = GetComponent<Combat.Health>();
        }

        void OnEnable()
        {
            m_Brain.StateChanged += HandleStateChanged;
            m_Body.Step += HandleStep;
            if (m_Health != null)
            {
                m_Health.HitReceived += HandleHit;
            }
        }

        void OnDisable()
        {
            m_Brain.StateChanged -= HandleStateChanged;
            m_Body.Step -= HandleStep;
            if (m_Health != null)
            {
                m_Health.HitReceived -= HandleHit;
            }
        }

        float Pitch => (m_Brain.Definition != null ? m_Brain.Definition.voicePitch : 1f) * AudioPool.Vary(0.06f);

        void HandleStateChanged(CreatureState previous, CreatureState current)
        {
            var definition = m_Brain.Definition;
            var position = transform.position + Vector3.up;

            switch (current)
            {
                case CreatureState.Chase when previous != CreatureState.Attack:
                    AudioPool.PlayAt(definition != null ? definition.alarmCall : SoundKind.Screech, position, 1f, Pitch, 120f);
                    break;
                case CreatureState.Attack:
                    // The tell. Loud enough to react to.
                    AudioPool.PlayAt(SoundKind.Lunge, position, 1f, Pitch, 60f);
                    break;
                case CreatureState.Stalk:
                    AudioPool.PlayAt(SoundKind.Rustle, position, 0.7f, AudioPool.Vary(), 40f);
                    break;
                case CreatureState.Investigate when previous is CreatureState.Roam or CreatureState.Idle:
                    AudioPool.PlayAt(definition != null ? definition.voice : SoundKind.GrowlLow, position, 0.6f, Pitch, 70f);
                    break;
                case CreatureState.Flee:
                    AudioPool.PlayAt(SoundKind.Bark, position, 0.9f, Pitch * 1.2f, 80f);
                    break;
                case CreatureState.Dead:
                    AudioPool.PlayAt(SoundKind.Death, position, 1f, Pitch * 0.8f, 90f);
                    break;
                case CreatureState.Sedated:
                    AudioPool.PlayAt(SoundKind.Collapse, position, 0.9f, Pitch * 0.9f, 60f);
                    break;
                case CreatureState.Rooted:
                    AudioPool.PlayAt(SoundKind.Screech, position, 0.8f, Pitch * 1.3f, 90f);
                    break;
            }
        }

        void HandleStep()
        {
            var definition = m_Brain.Definition;
            var volume = (definition != null ? definition.footstepVolume : 0.8f) * Mathf.Clamp01(0.4f + m_Body.Speed / 6f);
            AudioPool.PlayAt(SoundKind.CreatureStep, transform.position, volume, AudioPool.Vary(0.12f), 45f);
        }

        void HandleHit(Vector3 direction, float amount)
        {
            AudioPool.PlayAt(SoundKind.Hurt, transform.position + Vector3.up, 0.8f, Pitch * 0.9f, 60f);
        }

        /// <summary>Called by the brain's periodic call RPC.</summary>
        public void Call(bool aggressive)
        {
            var definition = m_Brain.Definition;
            var kind = aggressive
                ? (definition != null ? definition.alarmCall : SoundKind.Screech)
                : (definition != null ? definition.voice : SoundKind.GrowlLow);
            AudioPool.PlayAt(kind, transform.position + Vector3.up, aggressive ? 1f : 0.7f, Pitch, 130f);
        }

        public void Bite()
        {
            AudioPool.PlayAt(SoundKind.MeleeHit, transform.position + Vector3.up, 1f, Pitch * 0.8f, 40f);
        }

        void Update()
        {
            // Breathing when it is close and creeping: the sound of being hunted.
            if (m_Brain.State != CreatureState.Stalk || Time.time < m_NextBreathAt)
            {
                return;
            }

            var local = PlayerVitals.Local;
            if (local == null)
            {
                return;
            }

            var distance = Vector3.Distance(local.transform.position, transform.position);
            if (distance < 14f)
            {
                m_NextBreathAt = Time.time + 1.9f;
                AudioPool.PlayAt(SoundKind.Breath, transform.position + Vector3.up, 0.9f, Pitch * 0.85f, 18f);
            }
            else
            {
                m_NextBreathAt = Time.time + 0.5f;
            }
        }
    }
}
