using System;
using Transity.Core;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Server-authoritative carried equipment. Slots hold item network ids; 0 is empty.
    /// Beside each slot sits one int of state -- rounds in the magazine, charges left,
    /// battery seconds -- whose meaning belongs to the item's <see cref="ItemBehaviour"/>.
    ///
    /// Clients read both lists to draw the hotbar and never write to them; every add,
    /// drop, reload and use goes through the server.
    /// </summary>
    public sealed class InventoryComponent : NetworkBehaviour
    {
        public const int EmptySlot = 0;

        [SerializeField, Range(1, 8)] int capacity = 5;
        [SerializeField] Transform dropOrigin;
        [SerializeField] float dropForward = 0.8f;

        readonly NetworkList<int> m_Slots = new();
        readonly NetworkList<int> m_State = new();

        /// <summary>Selected hotbar index. Owner-writable so switching feels instant.</summary>
        readonly NetworkVariable<int> m_SelectedSlot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public int Capacity => capacity;
        public int SelectedSlot => m_SelectedSlot.Value;
        public int SlotCount => m_Slots.Count;

        /// <summary>Raised on every client when the contents or selection change.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            m_Slots.OnListChanged += HandleListChanged;
            m_State.OnListChanged += HandleStateChanged;
            m_SelectedSlot.OnValueChanged += HandleSelectionChanged;

            if (IsServer)
            {
                for (var i = m_Slots.Count; i < capacity; i++)
                {
                    m_Slots.Add(EmptySlot);
                }

                for (var i = m_State.Count; i < capacity; i++)
                {
                    m_State.Add(0);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Slots.OnListChanged -= HandleListChanged;
            m_State.OnListChanged -= HandleStateChanged;
            m_SelectedSlot.OnValueChanged -= HandleSelectionChanged;
        }

        void HandleListChanged(NetworkListEvent<int> _) => Changed?.Invoke();

        void HandleStateChanged(NetworkListEvent<int> _) => Changed?.Invoke();

        void HandleSelectionChanged(int _, int __) => Changed?.Invoke();

        // -------------------------------------------------------------------- reading

        public int GetSlot(int index) =>
            index >= 0 && index < m_Slots.Count ? m_Slots[index] : EmptySlot;

        public int GetState(int index) =>
            index >= 0 && index < m_State.Count ? m_State[index] : 0;

        public int SelectedItem => GetSlot(m_SelectedSlot.Value);

        public bool TryGetDefinition(int index, out ItemDefinition definition)
        {
            definition = null;
            var id = GetSlot(index);
            var registry = GameContent.ItemRegistry;
            return id != EmptySlot && registry != null && registry.TryGet(id, out definition);
        }

        public bool HasItem(int itemNetworkId) => FindSlot(itemNetworkId) >= 0;

        public int FindSlot(int itemNetworkId)
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (m_Slots[i] == itemNetworkId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>First slot whose definition passes the test, or -1.</summary>
        public int FindSlot(Predicate<ItemDefinition> test)
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (TryGetDefinition(i, out var definition) && test(definition))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>First slot whose definition and state pass the test, or -1.</summary>
        public int FindSlot(Func<ItemDefinition, int, bool> test)
        {
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (TryGetDefinition(i, out var definition) && test(definition, GetState(i)))
                {
                    return i;
                }
            }

            return -1;
        }

        public int CountEmpty()
        {
            var empty = 0;
            for (var i = 0; i < m_Slots.Count; i++)
            {
                if (m_Slots[i] == EmptySlot)
                {
                    empty++;
                }
            }

            return empty;
        }

        // ---------------------------------------------------------------- server API

        /// <summary>Server only. Adds with the item's default state. False when the pack is full.</summary>
        public bool TryAdd(int itemNetworkId)
        {
            var state = 0;
            var registry = GameContent.ItemRegistry;
            if (registry != null && registry.TryGet(itemNetworkId, out var definition) &&
                definition.Behaviour != null)
            {
                state = definition.Behaviour.InitialState(definition);
            }

            return TryAdd(itemNetworkId, state);
        }

        /// <summary>Server only. Adds with explicit state, e.g. a half-empty magazine picked back up.</summary>
        public bool TryAdd(int itemNetworkId, int state)
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
                    m_State[i] = state;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Server only. Clears a slot and returns what was in it.</summary>
        public int TakeFrom(int index) => TakeFrom(index, out _);

        /// <summary>Server only. Clears a slot and returns the item and its state.</summary>
        public int TakeFrom(int index, out int state)
        {
            state = 0;

            if (!IsServer || index < 0 || index >= m_Slots.Count)
            {
                return EmptySlot;
            }

            var item = m_Slots[index];
            state = m_State[index];
            m_Slots[index] = EmptySlot;
            m_State[index] = 0;
            return item;
        }

        /// <summary>Server only.</summary>
        public void ServerSetState(int index, int state)
        {
            if (IsServer && index >= 0 && index < m_State.Count && m_Slots[index] != EmptySlot)
            {
                m_State[index] = state;
            }
        }

        /// <summary>
        /// Server only. Spends one unit of state; removes the item if that empties a
        /// consumable. Returns false when there was nothing to spend.
        /// </summary>
        public bool ServerSpend(int index, int amount = 1)
        {
            if (!IsServer || !TryGetDefinition(index, out var definition))
            {
                return false;
            }

            var state = GetState(index);
            if (state < amount)
            {
                return false;
            }

            state -= amount;
            m_State[index] = state;

            if (state <= 0 && definition.Behaviour != null && definition.Behaviour.ConsumedWhenEmpty)
            {
                TakeFrom(index);
            }

            return true;
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

            TakeFrom(index, out var state);

            var origin = dropOrigin != null ? dropOrigin : transform;
            var position = origin.position + origin.forward * dropForward;
            var instance = Instantiate(definition.WorldPrefab, position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));

            if (instance.TryGetComponent<NetworkObject>(out var networkObject))
            {
                networkObject.Spawn();

                if (instance.TryGetComponent<WorldItem>(out var worldItem))
                {
                    worldItem.ServerSetState(state);
                }
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
