using System;
using Transity.Core;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Server-authoritative hit points, shared by players and creatures.
    ///
    /// The value is replicated so every HUD can draw it, but only the server ever writes.
    /// A client that wants to hurt something sends a request through its weapon; the
    /// weapon's server half decides whether the shot landed and calls in here. That keeps
    /// damage, death and bounty out of reach of a modified client.
    ///
    /// Bleeding is modelled here rather than on the player because creatures bleed too --
    /// a wounded creature leaves a trail a UV light can follow.
    /// </summary>
    public class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField] float maxHealth = 100f;

        [Tooltip("Passive regeneration per second. Zero for players; creatures use it while recovering.")]
        [SerializeField] float regenPerSecond;

        [Tooltip("Health lost per second while bleeding.")]
        [SerializeField] float bleedPerSecond = 1.5f;

        [Tooltip("How long a wound bleeds before it clots on its own. Zero means until treated.")]
        [SerializeField] float bleedDuration;

        [Tooltip("Incoming damage is multiplied by this. Vests lower it.")]
        [SerializeField] float damageTakenMultiplier = 1f;

        readonly NetworkVariable<float> m_Current = new();
        readonly NetworkVariable<bool> m_Dead = new();
        readonly NetworkVariable<bool> m_Bleeding = new();

        float m_BleedClotAt;
        float m_LastDamageTime = -100f;

        public float MaxHealth => maxHealth;
        public float Current => m_Current.Value;
        public float Fraction => maxHealth > 0f ? Mathf.Clamp01(m_Current.Value / maxHealth) : 0f;
        public bool IsDead => m_Dead.Value;
        public bool IsAlive => !m_Dead.Value;
        public bool IsBleeding => m_Bleeding.Value;
        public Transform Transform => transform;

        /// <summary>Seconds since the last hit. Creatures use it to decide when a fight is over.</summary>
        public float TimeSinceDamaged => Time.time - m_LastDamageTime;

        /// <summary>Server-side multiplier hook, e.g. worn armour.</summary>
        public float DamageTakenMultiplier
        {
            get => damageTakenMultiplier;
            set => damageTakenMultiplier = Mathf.Max(0f, value);
        }

        /// <summary>Server-side. Creatures turn this on while they hide and lick their wounds.</summary>
        public float RegenPerSecond
        {
            get => regenPerSecond;
            set => regenPerSecond = Mathf.Max(0f, value);
        }

        /// <summary>Raised on every peer when a dead thing is brought back (end of expedition).</summary>
        public event Action Revived;

        /// <summary>Raised on the server only, with the hit that was applied.</summary>
        public event Action<DamageInfo> ServerDamaged;

        /// <summary>Raised on the server only, once, with the killing hit.</summary>
        public event Action<DamageInfo> ServerDied;

        /// <summary>Raised on every peer when the replicated value changes.</summary>
        public event Action<float, float> Changed;

        /// <summary>Raised on every peer when this thing dies.</summary>
        public event Action Died;

        /// <summary>Raised on every peer with the direction a hit came from, for the HUD.</summary>
        public event Action<Vector3, float> HitReceived;

        public override void OnNetworkSpawn()
        {
            m_Current.OnValueChanged += HandleChanged;
            m_Dead.OnValueChanged += HandleDeadChanged;

            if (IsServer)
            {
                m_Current.Value = maxHealth;
                m_Dead.Value = false;
                m_Bleeding.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Current.OnValueChanged -= HandleChanged;
            m_Dead.OnValueChanged -= HandleDeadChanged;
        }

        void HandleChanged(float previous, float current) => Changed?.Invoke(previous, current);

        void HandleDeadChanged(bool previous, bool current)
        {
            if (current && !previous)
            {
                Died?.Invoke();
            }
            else if (!current && previous)
            {
                Revived?.Invoke();
            }
        }

        void Update()
        {
            if (!IsServer || m_Dead.Value)
            {
                return;
            }

            if (m_Bleeding.Value)
            {
                if (bleedDuration > 0f && Time.time >= m_BleedClotAt)
                {
                    m_Bleeding.Value = false;
                }
                else if (bleedPerSecond > 0f)
                {
                    var tick = bleedPerSecond * Time.deltaTime;
                    ServerApplyDamage(new DamageInfo
                    {
                        Amount = tick,
                        Kind = DamageKind.Bleed,
                        Point = transform.position,
                        Direction = Vector3.zero,
                        InstigatorClientId = DamageInfo.NoInstigator
                    });
                }
            }

            if (regenPerSecond > 0f && m_Current.Value < maxHealth)
            {
                m_Current.Value = Mathf.Min(maxHealth, m_Current.Value + regenPerSecond * Time.deltaTime);
            }
        }

        // ------------------------------------------------------------------ server API

        public virtual void ServerApplyDamage(in DamageInfo info)
        {
            if (!IsServer || m_Dead.Value)
            {
                return;
            }

            var amount = info.Amount * damageTakenMultiplier;

            // Bleed ticks are not "hits": they do not reset the fight timer or flash the
            // HUD, or a wounded player would be told they were being attacked every frame.
            var isTick = info.Kind == DamageKind.Bleed;

            if (!isTick)
            {
                m_LastDamageTime = Time.time;

                if (info.CausesBleeding && bleedPerSecond > 0f)
                {
                    m_Bleeding.Value = true;
                    m_BleedClotAt = Time.time + bleedDuration;
                }
            }

            if (amount > 0f)
            {
                m_Current.Value = Mathf.Max(0f, m_Current.Value - amount);
            }

            if (!isTick)
            {
                ServerDamaged?.Invoke(info);
                HitReceivedRpc(info.Direction, amount);
            }

            if (m_Current.Value <= 0f)
            {
                m_Dead.Value = true;
                m_Bleeding.Value = false;
                GameLog.Net($"{name} died to {info.Kind} (instigator {info.InstigatorClientId}).");
                ServerDied?.Invoke(info);
            }
        }

        public void ServerHeal(float amount, bool stopBleeding)
        {
            if (!IsServer || m_Dead.Value)
            {
                return;
            }

            m_Current.Value = Mathf.Min(maxHealth, m_Current.Value + Mathf.Max(0f, amount));

            if (stopBleeding)
            {
                m_Bleeding.Value = false;
            }
        }

        public void ServerSetBleeding(bool bleeding)
        {
            if (IsServer && !m_Dead.Value)
            {
                m_Bleeding.Value = bleeding;
                m_BleedClotAt = Time.time + bleedDuration;
            }
        }

        /// <summary>Server only. Used by the scaffold's respawn path and by creature recovery.</summary>
        public void ServerRestore(float fraction = 1f)
        {
            if (!IsServer)
            {
                return;
            }

            m_Current.Value = maxHealth * Mathf.Clamp01(fraction);
            m_Dead.Value = false;
            m_Bleeding.Value = false;
        }

        [Rpc(SendTo.Everyone)]
        void HitReceivedRpc(Vector3 direction, float amount)
        {
            HitReceived?.Invoke(direction, amount);
        }
    }
}
