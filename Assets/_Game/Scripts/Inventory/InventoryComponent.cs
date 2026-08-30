using System;
using Transity.Core;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Server-authoritative carried equipment. Slots hold item network ids; 0 is empty.
    /// Clients read the list to draw the hotbar and never write to it -- every add, drop
    /// and swap goes through the server.
    /// </summary>
    public sealed class InventoryComponent : NetworkBehaviour
    {
        public const int EmptySlot = 0;

        [SerializeField, Range(1, 8)] int capacity = 4;
        [SerializeField] Transform dropOrigin;
        [SerializeField] float dropForward = 0.8f;

        readonly NetworkList<int> m_Slots = new();

        /// <summary>Selected hotbar index. Owner-writable so switching feels instant.</summary>
        readonly NetworkVariable<int> m_SelectedSlot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public int Capacity => capacity;
        public int SelectedSlot => m_SelectedSlot.Value;
        public int SlotCount => m_Slots.Count;

        /// <summary>Raised on every client when the contents change, for HUD refreshes.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            m_Slots.OnListChanged += HandleListChanged;
            m_SelectedSlot.OnValueChanged += HandleSelectionChanged;

            if (IsServer)
            {
                for (var i = m_Slots.Count; i < capacity; i++)
                {
                    m_Slots.Add(EmptySlot);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Slots.OnListChanged -= HandleListChanged;
            m_SelectedSlot.OnValueChanged -= HandleSelectionChanged;
        }

        void HandleListChanged(NetworkListEvent<int> _) => Changed?.Invoke();

        void HandleSelectionChanged(int _, int __) => Changed?.Invoke();

        public int GetSlot(int index) =>
            index >= 0 && index < m_Slots.Count ? m_Slots[index] : EmptySlot;

        public bool HasItem(int itemNetworkId)
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (m_Slots[i] == itemNetworkId)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- server API

        /// <summary>Server only. Returns false when the pack is full.</summary>
        public bool TryAdd(int itemNetworkId)
        {
            if (!IsServer || itemNetworkId == EmptySlot)
            {
                return false;
            }

            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (m_Slots[i] == EmptySlot)
                {
                    m_Slots[i] = itemNetworkId;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Server only. Clears a slot and returns what was in it.</summary>
        public int TakeFrom(int index)
        {
            if (!IsServer || index < 0 || index >= m_Slots.Count)
            {
                return EmptySlot;
            }

            var item = m_Slots[index];
            m_Slots[index] = EmptySlot;
            return item;
        }

        /// <summary>Server only. Spawns the item's world prefab in front of the player.</summary>
        public bool DropSlot(int index)
        {
            if (!IsServer)
            {
                return false;
            }

            var itemId = GetSlot(index);
            if (itemId == EmptySlot)
            {
                return false;
            }

            var registry = GameContent.ItemRegistry;
            if (registry == null || !registry.TryGet(itemId, out var definition))
            {
                GameLog.Error($"Cannot drop item {itemId}: not present in the item registry.");
                return false;
            }

            if (definition.WorldPrefab == null)
            {
                GameLog.Error($"Item '{definition.ItemId}' has no world prefab, so it cannot be dropped.");
                return false;
            }

            TakeFrom(index);

            var origin = dropOrigin != null ? dropOrigin : transform;
            var position = origin.position + origin.forward * dropForward;
            var instance = Instantiate(definition.WorldPrefab, position, Quaternion.identity);

            if (instance.TryGetComponent<NetworkObject>(out var networkObject))
            {
                networkObject.Spawn();
            }
            else
            {
                GameLog.Error($"World prefab for '{definition.ItemId}' is missing a NetworkObject.");
                Destroy(instance);
                return false;
            }

            return true;
        }

        /// <summary>Server only. Used when a player disconnects or dies mid-expedition.</summary>
        public void DropAll()
        {
            if (!IsServer)
            {
                return;
            }

            for (var i = 0; i < m_Slots.Count; i++)
            {
                DropSlot(i);
            }
        }

        // ---------------------------------------------------------------- owner API

        public void SelectSlot(int index)
        {
            if (IsOwner && index >= 0 && index < capacity)
            {
                m_SelectedSlot.Value = index;
            }
        }

        public void CycleSlot(int direction)
        {
            if (!IsOwner || capacity == 0)
            {
                return;
            }

            var next = (m_SelectedSlot.Value + direction) % capacity;
            if (next < 0)
            {
                next += capacity;
            }

            m_SelectedSlot.Value = next;
        }

        /// <summary>Asks the server to drop the selected slot.</summary>
        [Rpc(SendTo.Server)]
        public void RequestDropSelectedRpc()
        {
            DropSlot(m_SelectedSlot.Value);
        }
    }
}
