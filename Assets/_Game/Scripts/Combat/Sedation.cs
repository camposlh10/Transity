using System;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// The capture meter. Tranquiliser darts add to it, it drains on its own, and when it
    /// tops out the creature collapses for a while. A collapsed creature can be contained;
    /// one that is merely tranquilised keeps running.
    ///
    /// Separate from <see cref="Health"/> because the two pull in opposite directions:
    /// a dead creature is a smaller bounty than a live one, so a crew going for capture
    /// has to keep shooting darts *without* also shooting bullets.
    /// </summary>
    public sealed class Sedation : NetworkBehaviour
    {
        [SerializeField] float threshold = 100f;
        [SerializeField] float decayPerSecond = 4f;

        [Tooltip("How long the creature stays down once sedated.")]
        [SerializeField] float collapseSeconds = 45f;

        [Tooltip("Sedation does not drain for this long after a dart, so a volley stacks.")]
        [SerializeField] float decayDelay = 3f;

        readonly NetworkVariable<float> m_Level = new();
        readonly NetworkVariable<bool> m_Collapsed = new();

        float m_LastDoseTime = -100f;
        float m_CollapsedAt;

        public float Level => m_Level.Value;
        public float Fraction => threshold > 0f ? Mathf.Clamp01(m_Level.Value / threshold) : 0f;
        public bool IsCollapsed => m_Collapsed.Value;
        public float Threshold => threshold;

        /// <summary>Seconds of collapse left, server only.</summary>
        public float CollapseRemaining => IsServer && m_Collapsed.Value
            ? Mathf.Max(0f, m_CollapsedAt + collapseSeconds - Time.time)
            : 0f;

        /// <summary>Server only.</summary>
        public event Action ServerCollapsed;

        /// <summary>Server only.</summary>
        public event Action ServerRecovered;

        /// <summary>Every peer.</summary>
        public event Action<bool> CollapsedChanged;

        public override void OnNetworkSpawn()
        {
            m_Collapsed.OnValueChanged += HandleCollapsedChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Collapsed.OnValueChanged -= HandleCollapsedChanged;
        }

        void HandleCollapsedChanged(bool previous, bool current) => CollapsedChanged?.Invoke(current);

        void Update()
        {
            if (!IsServer)
            {
                return;
            }

            if (m_Collapsed.Value)
            {
                if (Time.time >= m_CollapsedAt + collapseSeconds)
                {
                    m_Collapsed.Value = false;
                    m_Level.Value = threshold * 0.35f;
                    ServerRecovered?.Invoke();
                }

                return;
            }

            if (m_Level.Value > 0f && Time.time >= m_LastDoseTime + decayDelay)
            {
                m_Level.Value = Mathf.Max(0f, m_Level.Value - decayPerSecond * Time.deltaTime);
            }
        }

        /// <summary>Server only. Adds a dose; collapses the creature when the meter fills.</summary>
        public void ServerDose(float amount)
        {
            if (!IsServer || amount <= 0f || m_Collapsed.Value)
            {
                return;
            }

            m_LastDoseTime = Time.time;
            m_Level.Value = Mathf.Min(threshold, m_Level.Value + amount);

            if (m_Level.Value >= threshold)
            {
                m_Collapsed.Value = true;
                m_CollapsedAt = Time.time;
                ServerCollapsed?.Invoke();
            }
        }

        /// <summary>Server only. Wakes the creature early, e.g. when it is shot while down.</summary>
        public void ServerWake()
        {
            if (!IsServer || !m_Collapsed.Value)
            {
                return;
            }

            m_Collapsed.Value = false;
            m_Level.Value = threshold * 0.5f;
            m_LastDoseTime = Time.time;
            ServerRecovered?.Invoke();
        }
    }
}
