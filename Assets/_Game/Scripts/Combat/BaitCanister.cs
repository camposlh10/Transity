using Transity.Creatures;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Smells like dinner. Creatures within range come to investigate it in preference to
    /// the crew, which is the setup for a trap line or a clean shot on the weak point.
    /// Runs out; and the description's warning is real -- everything nearby comes.
    /// </summary>
    public sealed class BaitCanister : DeployableBase
    {
        [SerializeField] float attractRadius = 55f;
        [SerializeField] float lifetimeSeconds = 75f;
        [SerializeField] float pulseInterval = 6f;
        [SerializeField] Light glow;

        float m_NextPulse;
        float m_DespawnAt;

        protected override bool CanPickUp => false;

        protected override void OnServerPlaced()
        {
            m_DespawnAt = Time.time + lifetimeSeconds;
            m_NextPulse = Time.time + 0.5f;
        }

        public override bool CanInteract(Interaction.Interactor interactor) => false;

        void Update()
        {
            if (glow != null)
            {
                glow.intensity = 1.4f + Mathf.Sin(Time.time * 3f) * 0.6f;
            }

            if (!IsServer)
            {
                return;
            }

            if (Time.time >= m_NextPulse)
            {
                m_NextPulse = Time.time + pulseInterval;
                NoiseBus.Emit(transform.position, attractRadius, NoiseKind.Bait, DamageInfo.NoInstigator);

                foreach (var brain in CreatureBrain.All)
                {
                    if (brain != null && (brain.transform.position - transform.position).sqrMagnitude < attractRadius * attractRadius)
                    {
                        brain.ServerAttract(transform.position, pulseInterval + 2f);
                    }
                }
            }

            if (Time.time >= m_DespawnAt && IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
