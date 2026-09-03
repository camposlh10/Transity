using System;
using Transity.Audio;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Shrieks when a creature crosses it. Every player hears the shriek and gets a ping
    /// on the compass -- and so does every creature within earshot, which is the trade the
    /// item description promises.
    /// </summary>
    public sealed class MotionAlarm : DeployableBase
    {
        [SerializeField] float rearmSeconds = 7f;
        [SerializeField] float alarmNoiseRadius = 45f;
        [SerializeField] Renderer indicator;

        readonly NetworkVariable<int> m_TriggerCount = new();
        readonly NetworkVariable<bool> m_Armed = new(true);

        float m_RearmAt;

        /// <summary>Raised on every peer with the world position, for the compass ping.</summary>
        public static event Action<Vector3> Triggered;

        public override void OnNetworkSpawn()
        {
            m_TriggerCount.OnValueChanged += HandleTriggered;
            m_Armed.OnValueChanged += HandleArmedChanged;
            ApplyIndicator();
        }

        public override void OnNetworkDespawn()
        {
            m_TriggerCount.OnValueChanged -= HandleTriggered;
            m_Armed.OnValueChanged -= HandleArmedChanged;
        }

        public override string GetPrompt(Interaction.Interactor interactor) => "Pick up motion alarm";

        void Update()
        {
            if (IsServer && !m_Armed.Value && Time.time >= m_RearmAt)
            {
                m_Armed.Value = true;
            }

            if (indicator != null && m_Armed.Value)
            {
                // A slow blink says "armed" from across a clearing.
                var on = Mathf.Repeat(Time.time, 1.6f) < 0.12f;
                indicator.material.SetColor("_EmissionColor", on ? new Color(1f, 0.25f, 0.1f) * 4f : Color.black);
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (!IsServer || !m_Armed.Value || !IsCreature(other))
            {
                return;
            }

            if (other.GetComponentInParent<Health>() is not { IsAlive: true })
            {
                return;
            }

            m_Armed.Value = false;
            m_RearmAt = Time.time + rearmSeconds;
            m_TriggerCount.Value++;

            NoiseBus.Emit(transform.position, alarmNoiseRadius, NoiseKind.Alarm, DamageInfo.NoInstigator);
        }

        void HandleTriggered(int previous, int current)
        {
            AudioPool.PlayAt(SoundKind.Alarm, transform.position, 1f, 1f, 120f);
            Triggered?.Invoke(transform.position);
        }

        void HandleArmedChanged(bool previous, bool current) => ApplyIndicator();

        void ApplyIndicator()
        {
            if (indicator != null && !m_Armed.Value)
            {
                indicator.material.SetColor("_EmissionColor", new Color(1f, 0.25f, 0.1f) * 6f);
            }
        }
    }
}
