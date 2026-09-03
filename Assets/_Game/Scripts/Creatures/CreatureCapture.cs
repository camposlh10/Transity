using Transity.Interaction;
using Transity.Inventory;
using Transity.Missions;
using Transity.Player;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// The bounty end of a creature. A dead one can be tagged for the kill fee; a sedated
    /// one can be put in a containment case for the live fee, which consumes the case.
    /// Everything else about it refuses interaction, so nobody walks up and presses E on
    /// something that is awake.
    /// </summary>
    [RequireComponent(typeof(CreatureBrain))]
    public sealed class CreatureCapture : NetworkInteractable
    {
        [SerializeField] CreatureBrain brain;
        [SerializeField] string containmentItemId = "item.containmentcase";

        void Awake()
        {
            if (brain == null)
            {
                brain = GetComponent<CreatureBrain>();
            }
        }

        public override bool CanInteract(Interactor interactor)
        {
            if (!base.CanInteract(interactor) || brain == null)
            {
                return false;
            }

            return brain.State switch
            {
                CreatureState.Dead => !brain.IsTagged,
                CreatureState.Sedated => true,
                _ => false
            };
        }

        public override string GetPrompt(Interactor interactor)
        {
            var creatureName = brain.Definition != null ? brain.Definition.displayName : "creature";

            if (brain.State == CreatureState.Dead)
            {
                return $"Tag the {creatureName}";
            }

            var hasCase = interactor.TryGetComponent<InventoryComponent>(out var inventory) &&
                          inventory.FindSlot(d => d.ItemId == containmentItemId) >= 0;

            return hasCase ? $"Contain the {creatureName}" : "Sedated - needs a containment case";
        }

        public override void OnServerInteract(Interactor interactor)
        {
            var director = MissionDirector.Instance;
            if (director == null || brain == null)
            {
                return;
            }

            if (brain.State == CreatureState.Dead)
            {
                if (brain.IsTagged)
                {
                    return;
                }

                brain.ServerTag();
                director.ServerRecordKill(brain, interactor.OwnerClientId);
                return;
            }

            if (brain.State != CreatureState.Sedated)
            {
                return;
            }

            if (!interactor.TryGetComponent<InventoryComponent>(out var inventory))
            {
                return;
            }

            var slot = inventory.FindSlot(d => d.ItemId == containmentItemId);
            if (slot < 0)
            {
                if (interactor.TryGetComponent<PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("You need a containment case.");
                }

                return;
            }

            inventory.TakeFrom(slot);
            director.ServerRecordCapture(brain, interactor.OwnerClientId);
            NetworkObject.Despawn(true);
        }
    }
}
