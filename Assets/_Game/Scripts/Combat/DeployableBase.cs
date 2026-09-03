using Transity.Interaction;
using Transity.Inventory;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// Something a player placed in the world. Remembers who placed it and which item it
    /// was, so an unsprung trap can be picked back up and an alarm can tell its owner.
    /// </summary>
    public abstract class DeployableBase : NetworkInteractable
    {
        readonly NetworkVariable<ulong> m_Owner = new(DamageInfo.NoInstigator);
        readonly NetworkVariable<int> m_ItemId = new();

        public ulong OwnerClient => m_Owner.Value;
        public int ItemNetworkId => m_ItemId.Value;

        /// <summary>Whether it can still be recovered into a pack.</summary>
        protected virtual bool CanPickUp => true;

        /// <summary>Server only.</summary>
        public void ServerInit(ulong ownerClientId, int itemNetworkId)
        {
            if (IsServer)
            {
                m_Owner.Value = ownerClientId;
                m_ItemId.Value = itemNetworkId;
                OnServerPlaced();
            }
        }

        protected virtual void OnServerPlaced()
        {
        }

        public override bool CanInteract(Interactor interactor) => base.CanInteract(interactor) && CanPickUp;

        public override string GetPrompt(Interactor interactor) => "Pick up";

        public override void OnServerInteract(Interactor interactor)
        {
            if (!CanPickUp || m_ItemId.Value == InventoryComponent.EmptySlot)
            {
                return;
            }

            var inventory = interactor.GetComponent<InventoryComponent>();
            if (inventory == null)
            {
                return;
            }

            if (!inventory.TryAdd(m_ItemId.Value))
            {
                if (interactor.TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Your pack is full.");
                }

                return;
            }

            NetworkObject.Despawn(true);
        }

        protected static bool IsCreature(Collider other) => other.gameObject.layer == 9;

        protected static bool IsPlayer(Collider other) => other.gameObject.layer == 7;
    }
}
