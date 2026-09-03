using Transity.Interaction;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// A piece of equipment lying in the world. Picking it up is a server decision: the
    /// item only disappears once it is actually in someone's pack.
    ///
    /// Carries the slot state it was dropped with, so a rifle dropped with three rounds is
    /// picked up with three rounds -- and a dead hunter's pack is worth exactly what they
    /// had left, not a fresh issue.
    /// </summary>
    public sealed class WorldItem : NetworkInteractable
    {
        [SerializeField] ItemDefinition definition;

        // -1 means "not set": use the item's default state on pickup.
        readonly NetworkVariable<int> m_State = new(-1);

        public ItemDefinition Definition => definition;
        public int State => m_State.Value;

        public override bool CanInteract(Interactor interactor) =>
            base.CanInteract(interactor) && definition != null;

        public override string GetPrompt(Interactor interactor)
        {
            if (definition == null)
            {
                return "Pick up";
            }

            var extra = definition.Behaviour != null && m_State.Value >= 0
                ? definition.Behaviour.DescribeState(m_State.Value)
                : string.Empty;

            return string.IsNullOrEmpty(extra)
                ? $"Pick up {definition.DisplayName}"
                : $"Pick up {definition.DisplayName}  ({extra})";
        }

        /// <summary>Server only.</summary>
        public void ServerSetState(int state)
        {
            if (IsServer)
            {
                m_State.Value = state;
            }
        }

        public override void OnServerInteract(Interactor interactor)
        {
            if (definition == null)
            {
                return;
            }

            var inventory = interactor.GetComponent<InventoryComponent>();
            if (inventory == null)
            {
                return;
            }

            var added = m_State.Value >= 0
                ? inventory.TryAdd(definition.NetworkId, m_State.Value)
                : inventory.TryAdd(definition.NetworkId);

            if (!added)
            {
                if (interactor.TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Your pack is full.");
                }

                return;
            }

            NetworkObject.Despawn(true);
        }
    }
}
