using Transity.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Turns owner input into inventory requests. Selection is owner-authoritative (it is
    /// only a view concern); dropping goes to the server, which owns the slots.
    /// </summary>
    [RequireComponent(typeof(InventoryComponent))]
    public sealed class PlayerHotbarInput : NetworkBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] InventoryComponent inventory;

        void Awake()
        {
            if (inventory == null)
            {
                inventory = GetComponent<InventoryComponent>();
            }
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
        }

        void Update()
        {
            if (!IsOwner || input == null || input.Suppressed)
            {
                return;
            }

            var direct = input.SlotPressed;
            if (direct >= 0)
            {
                inventory.SelectSlot(direct);
            }

            if (input.NextSlotPressed)
            {
                inventory.CycleSlot(1);
            }

            if (input.PreviousSlotPressed)
            {
                inventory.CycleSlot(-1);
            }

            if (input.DropPressed)
            {
                inventory.RequestDropSelectedRpc();
            }
        }
    }
}
