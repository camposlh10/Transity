using System;
using System.Collections.Generic;
using Transity.Combat;
using Transity.Core;
using Transity.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// The player's body as a thing that can be hurt: armour from worn gear, adrenaline,
    /// bleeding, and what happens when the health bar empties.
    ///
    /// Death is final for the expedition. The pack drops where the player fell, the body
    /// stays, and the owner is handed to the spectator camera until the crew extracts or
    /// wipes. The server restores everyone when the train scene loads again.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class PlayerVitals : NetworkBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] InventoryComponent inventory;
        [SerializeField] FirstPersonController movement;
        [SerializeField] PlayerCharacter character;
        [SerializeField] Transform bodyRoot;

        [Tooltip("Damage from other players is scaled by this unless a contract says otherwise.")]
        [SerializeField, Range(0f, 1f)] float friendlyFireMultiplier = 0.35f;

        static readonly List<PlayerVitals> Registered = new();

        // Scent neutraliser. Replicated as a flag; the server keeps the clock.
        readonly NetworkVariable<bool> m_Masked = new();
        float m_MaskedUntil;

        /// <summary>Creatures notice a masked player less. Any peer.</summary>
        public bool IsMasked => m_Masked.Value;

        /// <summary>Raised on the server when any player dies.</summary>
        public static event Action<PlayerVitals, DamageInfo> ServerPlayerDied;

        /// <summary>Server-side record of how this player last died.</summary>
        public DamageInfo LastDeath { get; private set; }

        public Health Health => health;
        public bool IsDead => health != null && health.IsDead;
        public static IReadOnlyList<PlayerVitals> All => Registered;
        public static PlayerVitals Local { get; private set; }

        /// <summary>Hook for the betrayal contract: full damage for a traitor on their mark.</summary>
        public static Func<ulong, ulong, bool> FullFriendlyFire;

        public static int AliveCount
        {
            get
            {
                var alive = 0;
                foreach (var vitals in Registered)
                {
                    if (vitals != null && !vitals.IsDead)
                    {
                        alive++;
                    }
                }

                return alive;
            }
        }

        void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            if (inventory == null) inventory = GetComponent<InventoryComponent>();
            if (movement == null) movement = GetComponent<FirstPersonController>();
            if (character == null) character = GetComponent<PlayerCharacter>();
        }

        public override void OnNetworkSpawn()
        {
            Registered.Add(this);

            if (IsOwner)
            {
                Local = this;
            }

            health.Died += HandleDied;
            health.Revived += HandleRevived;

            if (IsServer)
            {
                health.ServerDied += HandleServerDied;
            }

            if (inventory != null)
            {
                inventory.Changed += RefreshWornGear;
                RefreshWornGear();
            }
        }

        public override void OnNetworkDespawn()
        {
            Registered.Remove(this);

            if (Local == this)
            {
                Local = null;
            }

            health.Died -= HandleDied;
            health.Revived -= HandleRevived;

            if (IsServer)
            {
                health.ServerDied -= HandleServerDied;
            }

            if (inventory != null)
            {
                inventory.Changed -= RefreshWornGear;
            }
        }

        // ------------------------------------------------------------------ worn gear

        /// <summary>
        /// Vests and the like apply just by being carried. Recomputed whenever the pack
        /// changes, on every peer: the server needs the damage multiplier, the owner needs
        /// the speed one, and doing both everywhere is simpler than splitting them.
        /// </summary>
        void RefreshWornGear()
        {
            var damageMultiplier = 1f;
            var speedMultiplier = 1f;
            var noiseMultiplier = 1f;

            for (var i = 0; i < inventory.SlotCount; i++)
            {
                if (!inventory.TryGetDefinition(i, out var definition))
                {
                    continue;
                }

                var passive = definition.BehaviourAs<PassiveBehaviour>();
                if (passive == null)
                {
                    continue;
                }

                damageMultiplier *= passive.damageTakenMultiplier;
                speedMultiplier *= passive.speedMultiplier;
                noiseMultiplier *= passive.noiseMultiplier;
            }

            if (IsServer)
            {
                health.DamageTakenMultiplier = damageMultiplier;
            }

            if (movement != null)
            {
                movement.SpeedMultiplier = speedMultiplier;
                movement.NoiseMultiplier = noiseMultiplier;
            }
        }

        /// <summary>Bounty multiplier from carried gear (the body camera). Any peer.</summary>
        public float BountyMultiplier()
        {
            var multiplier = 1f;
            for (var i = 0; i < inventory.SlotCount; i++)
            {
                if (inventory.TryGetDefinition(i, out var definition) &&
                    definition.BehaviourAs<PassiveBehaviour>() is { } passive)
                {
                    multiplier *= passive.bountyMultiplier;
                }
            }

            return multiplier;
        }

        void Update()
        {
            if (IsServer && m_Masked.Value && Time.time >= m_MaskedUntil)
            {
                m_Masked.Value = false;
            }
        }

        /// <summary>Server only.</summary>
        public void ServerMask(float seconds)
        {
            if (IsServer && seconds > 0f)
            {
                m_MaskedUntil = Mathf.Max(m_MaskedUntil, Time.time + seconds);
                m_Masked.Value = true;
            }
        }

        // ------------------------------------------------------------------- damage

        /// <summary>
        /// Server only. Applies a hit from another player with the friendly-fire rule, so
        /// weapons do not have to know about contracts.
        /// </summary>
        public void ServerApplyPlayerDamage(DamageInfo info)
        {
            if (!IsServer)
            {
                return;
            }

            if (info.HasInstigator && info.InstigatorClientId != OwnerClientId)
            {
                var full = FullFriendlyFire != null && FullFriendlyFire(info.InstigatorClientId, OwnerClientId);
                if (!full)
                {
                    info.Amount *= friendlyFireMultiplier;
                }
            }

            health.ServerApplyDamage(info);
        }

        void HandleServerDied(DamageInfo info)
        {
            LastDeath = info;

            // The pack falls where they did. Whoever is left can pick it up -- and a
            // teammate who was standing a little too close is who the crew will look at.
            inventory?.DropAll();

            GameLog.Net($"Player {OwnerClientId} died: {info.Kind}.");
            ServerPlayerDied?.Invoke(this, info);
        }

        // ---------------------------------------------------------------- visuals

        void HandleDied()
        {
            SetCollapsed(true);

            if (IsOwner)
            {
                Transity.Audio.AudioPool.Play2D(Transity.Audio.SoundKind.Death, 0.9f);

                if (TryGetComponent<SpectatorController>(out var spectator))
                {
                    spectator.Begin();
                }
            }
        }

        void HandleRevived()
        {
            SetCollapsed(false);

            if (IsOwner && TryGetComponent<SpectatorController>(out var spectator))
            {
                spectator.End();
            }
        }

        /// <summary>
        /// A dead hunter lies down. The controller stays so the body still blocks the way
        /// and the pack can be found beside it.
        /// </summary>
        void SetCollapsed(bool collapsed)
        {
            if (bodyRoot != null)
            {
                bodyRoot.localRotation = collapsed ? Quaternion.Euler(-82f, 0f, 12f) : Quaternion.identity;
                bodyRoot.localPosition = collapsed ? new Vector3(0f, 0.25f, 0.3f) : Vector3.zero;
            }

            if (movement != null)
            {
                movement.enabled = !collapsed && (IsOwner || IsServer);
            }

            if (TryGetComponent<Interaction.Interactor>(out var interactor))
            {
                interactor.enabled = !collapsed;
            }

            if (TryGetComponent<PlayerEquipment>(out var equipment))
            {
                equipment.SetSuspended(collapsed);
            }

            if (IsOwner && TryGetComponent<CharacterController>(out var controller))
            {
                controller.enabled = !collapsed;
            }
        }

        // ----------------------------------------------------------------- helpers

        public static PlayerVitals Find(ulong clientId)
        {
            foreach (var vitals in Registered)
            {
                if (vitals != null && vitals.OwnerClientId == clientId)
                {
                    return vitals;
                }
            }

            return null;
        }

        /// <summary>Server only. Everyone back on their feet, used when the train scene loads.</summary>
        public static void ServerRestoreAll()
        {
            foreach (var vitals in Registered)
            {
                if (vitals != null && vitals.IsServer)
                {
                    vitals.health.ServerRestore();
                }
            }
        }
    }
}
