using Transity.Creatures;
using Transity.Missions;
using Transity.Player;
using UnityEngine;

namespace Transity.Audio
{
    /// <summary>
    /// The score, such as it is: two detuned drones whose volume follows the forest
    /// director's tension, and a heartbeat that comes in when the local player is close
    /// to death or close to something. Nothing plays aboard the train.
    /// </summary>
    public sealed class TensionMusic : MonoBehaviour
    {
        [SerializeField] float droneVolume = 0.35f;
        [SerializeField] float heartbeatVolume = 0.55f;

        AudioSource m_Low;
        AudioSource m_High;
        AudioSource m_Heart;
        float m_Level;

        void Start()
        {
            m_Low = MakeSource(ProceduralAudio.DroneLoop(55f, 6f, 1));
            m_High = MakeSource(ProceduralAudio.DroneLoop(82.4f, 6f, 2));
            m_Heart = MakeSource(ProceduralAudio.Get(SoundKind.Heartbeat));
            m_Heart.loop = true;
        }

        AudioSource MakeSource(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.playOnAwake = false;
            source.Play();
            return source;
        }

        void Update()
        {
            var expedition = MissionDirector.Instance != null && MissionDirector.Instance.Phase == MissionPhase.Expedition;
            var tension = expedition && ForestDirector.Instance != null ? ForestDirector.Instance.Tension : 0f;

            // Local proximity sharpens the global number: what is near *you* matters most.
            var local = PlayerVitals.Local;
            var proximity = 0f;
            if (expedition && local != null && !local.IsDead)
            {
                foreach (var brain in CreatureBrain.All)
                {
                    if (brain == null || brain.IsDown)
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(brain.transform.position, local.transform.position);
                    var weight = brain.State is CreatureState.Chase or CreatureState.Attack ? 1f
                        : brain.State == CreatureState.Stalk ? 0.6f : 0.2f;
                    proximity = Mathf.Max(proximity, weight * (1f - Mathf.Clamp01((distance - 4f) / 40f)));
                }
            }

            var target = Mathf.Clamp01(Mathf.Max(tension, proximity));
            m_Level = Mathf.MoveTowards(m_Level, target, Time.deltaTime * (target > m_Level ? 0.9f : 0.25f));

            if (m_Low != null)
            {
                m_Low.volume = droneVolume * Mathf.SmoothStep(0f, 1f, m_Level);
                m_Low.pitch = 1f + m_Level * 0.08f;
            }

            if (m_High != null)
            {
                m_High.volume = droneVolume * 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, m_Level - 0.35f) / 0.65f);
            }

            if (m_Heart != null)
            {
                var lowHealth = local != null && !local.IsDead && local.Health != null
                    ? 1f - Mathf.Clamp01((local.Health.Fraction - 0.1f) / 0.3f)
                    : 0f;
                var heart = Mathf.Max(lowHealth, Mathf.Clamp01((m_Level - 0.65f) / 0.35f));
                m_Heart.volume = expedition ? heartbeatVolume * heart : 0f;
                m_Heart.pitch = 0.9f + heart * 0.5f;
            }
        }
    }
}
