using Transity.Interaction;
using Transity.Player;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// A piece of equipment lying in the world. Picking it up is a server decision: the
    /// item only disappears once it is actually in someone's pack.
    /// </summary>
    public sealed class WorldItem : NetworkInteractable
    {
        [SerializeField] ItemDefinition definition;

        public ItemDefinition Definition => definition;

        public override bool CanInteract(Interactor interactor) =>
            base.CanInteract(interactor) && definition != null;

        public override string GetPrompt(Interactor interactor) =>
            definition != null ? $"Pick up {definition.DisplayName}" : "Pick up";

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

            if (!inventory.TryAdd(definition.NetworkId))
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
