using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// A thrown chemical light. Marks a spot for the crew, and lights anyone standing in
    /// it for the creatures. Burns out on its own.
    /// </summary>
    public sealed class GlowStickLight : DeployableBase
    {
        [SerializeField] float lifetimeSeconds = 150f;
        [SerializeField] float litRadius = 7f;
        [SerializeField] Light glow;

        static readonly List<GlowStickLight> Registered = new();

        float m_DespawnAt;

        public static IReadOnlyList<GlowStickLight> All => Registered;
        public float LitRadius => litRadius;

        protected override bool CanPickUp => false;

        public override bool CanInteract(Interaction.Interactor interactor) => false;

        public override void OnNetworkSpawn()
        {
            Registered.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            Registered.Remove(this);
        }

        protected override void OnServerPlaced()
        {
            m_DespawnAt = Time.time + lifetimeSeconds;
        }

        void Update()
        {
            if (glow != null)
            {
                glow.intensity = 5f + Mathf.PerlinNoise(Time.time * 2f, 0.3f) * 1.5f;
            }

            if (IsServer && m_DespawnAt > 0f && Time.time >= m_DespawnAt && IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
        }

        /// <summary>Whether a point is inside any glow stick's light.</summary>
        public static bool IsLit(Vector3 position)
        {
            foreach (var stick in Registered)
            {
                if (stick != null && (stick.transform.position - position).sqrMagnitude < stick.litRadius * stick.litRadius)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
