using Transity.Audio;
using Transity.Creatures;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Snaps shut on the first thing that steps in it. A creature is wounded and held for
    /// a few seconds -- long enough to line up the weak point -- and a player who forgets
    /// where they put it learns the same lesson at a lower price.
    /// </summary>
    public sealed class BearTrap : DeployableBase
    {
        [SerializeField] float creatureDamage = 38f;
        [SerializeField] float holdSeconds = 4.5f;
        [SerializeField] float playerDamage = 14f;
        [SerializeField] float despawnAfterSprung = 4f;
        [SerializeField] Transform jawLeft;
        [SerializeField] Transform jawRight;

        readonly NetworkVariable<bool> m_Sprung = new();

        protected override bool CanPickUp => !m_Sprung.Value;

        public override void OnNetworkSpawn()
        {
            m_Sprung.OnValueChanged += HandleSprungChanged;
            ApplyJaws(m_Sprung.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_Sprung.OnValueChanged -= HandleSprungChanged;
        }

        public override string GetPrompt(Interaction.Interactor interactor) => "Pick up bear trap";

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || m_Sprung.Value)
            {
                return;
            }

            if (IsCreature(other) && other.GetComponentInParent<Health>() is { } creatureHealth && creatureHealth.IsAlive)
            {
                creatureHealth.ServerApplyDamage(new DamageInfo
                {
                    Amount = creatureDamage,
                    Kind = DamageKind.Trap,
                    Point = transform.position,
                    Direction = Vector3.up,
                    InstigatorClientId = OwnerClient,
                    CausesBleeding = true
                });

                if (creatureHealth.TryGetComponent<CreatureBrain>(out var brain))
                {
                    brain.ServerRoot(holdSeconds);
                }

                Spring();
            }
            else if (IsPlayer(other) && other.GetComponentInParent<PlayerVitals>() is { } vitals && !vitals.IsDead)
            {
                vitals.Health.ServerApplyDamage(new DamageInfo
                {
                    Amount = playerDamage,
                    Kind = DamageKind.Trap,
                    Point = transform.position,
                    Direction = Vector3.up,
                    InstigatorClientId = DamageInfo.NoInstigator,
                    CausesBleeding = true
                });

                if (vitals.TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("You stepped in a bear trap.");
                }

                Spring();
            }
        }

        void Spring()
        {
            m_Sprung.Value = true;
            NoiseBus.Emit(transform.position, 25f, NoiseKind.Impact, OwnerClient);
            Invoke(nameof(ServerDespawn), despawnAfterSprung);
        }

        void ServerDespawn()
        {
            if (IsServer && IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }

        void HandleSprungChanged(bool previous, bool current)
        {
            ApplyJaws(current);
            if (current)
            {
                AudioPool.PlayAt(SoundKind.TrapSnap, transform.position, 1f, 1f, 45f);
            }
        }

        void ApplyJaws(bool closed)
        {
            var angle = closed ? 0f : 80f;
            if (jawLeft != null)
            {
                jawLeft.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (jawRight != null)
            {
                jawRight.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }
    }
}
