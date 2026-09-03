using System;
using Transity.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Worn lights and optics. The Flashlight key turns on the first toggle item in the
    /// pack without selecting it, so a torch and a rifle coexist -- the rifle in the
    /// hands, the torch clipped to the chest.
    ///
    /// On/off is server state: creatures see a lit player from further away, and that has
    /// to be true on the machine that runs the creatures. Battery drains there too. The
    /// visible beam is a spot light every peer draws from the replicated state; NVG and
    /// thermal are owner-side overlays the HUD reads off <see cref="ActiveKind"/>.
    /// </summary>
    public sealed class PlayerLight : NetworkBehaviour
    {
        [SerializeField] InventoryComponent inventory;
        [SerializeField] PlayerInputReader input;
        [SerializeField] Light beam;

        readonly NetworkVariable<bool> m_On = new();
        readonly NetworkVariable<int> m_Slot = new(-1);
        readonly NetworkVariable<ToggleKind> m_Kind = new();

        float m_NextDrain;

        public static PlayerLight Local { get; private set; }

        public bool IsOn => m_On.Value;
        public ToggleKind Kind => m_Kind.Value;
        public ToggleKind? ActiveKind => m_On.Value ? m_Kind.Value : null;

        /// <summary>How much easier creatures see this player because of the light. 1 when off.</summary>
        public float VisibilityMultiplier
        {
            get
            {
                if (!m_On.Value || !TryGetBehaviour(m_Slot.Value, out var behaviour))
                {
                    return 1f;
                }

                return behaviour.visibilityMultiplier;
            }
        }

        /// <summary>The active toggle behaviour, on any peer.</summary>
        public ToggleBehaviour Active => m_On.Value && TryGetBehaviour(m_Slot.Value, out var b) ? b : null;

        public event Action<bool, ToggleKind> Changed;

        void Awake()
        {
            if (inventory == null) inventory = GetComponent<InventoryComponent>();
            if (input == null) input = GetComponent<PlayerInputReader>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                Local = this;
            }

            m_On.OnValueChanged += HandleChanged;
            m_Kind.OnValueChanged += HandleKindChanged;

            if (inventory != null)
            {
                inventory.Changed += HandleInventoryChanged;
            }

            ApplyBeam();
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
            {
                Local = null;
            }

            m_On.OnValueChanged -= HandleChanged;
            m_Kind.OnValueChanged -= HandleKindChanged;

            if (inventory != null)
            {
                inventory.Changed -= HandleInventoryChanged;
            }
        }

        void HandleChanged(bool previous, bool current)
        {
            ApplyBeam();
            Changed?.Invoke(current, m_Kind.Value);
        }

        void HandleKindChanged(ToggleKind previous, ToggleKind current) => ApplyBeam();

        void Update()
        {
            if (IsOwner && input != null && !input.Suppressed && input.FlashlightPressed)
            {
                RequestToggleRpc();
            }

            if (IsServer)
            {
                DrainBattery();
            }
        }

        // --------------------------------------------------------------------- server

        [Rpc(SendTo.Server)]
        void RequestToggleRpc()
        {
            if (m_On.Value)
            {
                m_On.Value = false;
                return;
            }

            // Prefer the slot that was last on, else the first light in the pack.
            var slot = TryGetBehaviour(m_Slot.Value, out _) ? m_Slot.Value : -1;
            if (slot < 0)
            {
                slot = inventory.FindSlot((definition, state) =>
                    definition.BehaviourAs<ToggleBehaviour>() != null && state != 0);
            }

            if (slot < 0)
            {
                if (TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Nothing to switch on.");
                }

                return;
            }

            if (!TryGetBehaviour(slot, out var behaviour))
            {
                return;
            }

            if (behaviour.batterySeconds > 0f && inventory.GetState(slot) <= 0)
            {
                if (TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Battery dead.");
                }

                return;
            }

            m_Slot.Value = slot;
            m_Kind.Value = behaviour.kind;
            m_On.Value = true;
            m_NextDrain = Time.time + 1f;
        }

        void DrainBattery()
        {
            if (!m_On.Value || Time.time < m_NextDrain)
            {
                return;
            }

            m_NextDrain = Time.time + 1f;

            if (!TryGetBehaviour(m_Slot.Value, out var behaviour))
            {
                m_On.Value = false;
                return;
            }

            if (behaviour.batterySeconds <= 0f)
            {
                return;
            }

            var remaining = inventory.GetState(m_Slot.Value) - 1;
            inventory.ServerSetState(m_Slot.Value, Mathf.Max(0, remaining));

            if (remaining <= 0)
            {
                m_On.Value = false;

                if (TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Battery dead.");
                }
            }
        }

        /// <summary>If the lit item leaves the pack, the light goes with it.</summary>
        void HandleInventoryChanged()
        {
            if (IsServer && m_On.Value && !TryGetBehaviour(m_Slot.Value, out _))
            {
                m_On.Value = false;
            }
        }

        bool TryGetBehaviour(int slot, out ToggleBehaviour behaviour)
        {
            behaviour = null;
            if (inventory == null || slot < 0 || !inventory.TryGetDefinition(slot, out var definition))
            {
                return false;
            }

            behaviour = definition.BehaviourAs<ToggleBehaviour>();
            return behaviour != null;
        }

        // ---------------------------------------------------------------------- beam

        void ApplyBeam()
        {
            if (beam == null)
            {
                return;
            }

            var behaviour = Active;
            var castsBeam = behaviour != null &&
                            behaviour.kind is ToggleKind.Flashlight or ToggleKind.UltraViolet;

            beam.enabled = castsBeam;

            if (!castsBeam)
            {
                return;
            }

            beam.type = LightType.Spot;
            beam.range = behaviour.beamRange;
            beam.spotAngle = behaviour.beamAngle;
            beam.intensity = behaviour.intensity;
            beam.color = behaviour.kind == ToggleKind.UltraViolet
                ? new Color(0.45f, 0.2f, 1f)
                : behaviour.color;
            beam.shadows = LightShadows.Soft;
        }
    }
}
