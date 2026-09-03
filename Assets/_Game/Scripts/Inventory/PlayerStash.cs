using System;
using Transity.Core;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// One entry in a player's stash. Unmanaged so it can live in a NetworkList.
    /// </summary>
    public struct StashEntry : IEquatable<StashEntry>
    {
        public int ItemId;
        public int Count;

        public bool Equals(StashEntry other) => ItemId == other.ItemId && Count == other.Count;

        public override bool Equals(object obj) => obj is StashEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ItemId, Count);
    }

    /// <summary>
    /// A player's own lobby stash: everything they have bought but are not currently
    /// carrying. Each player buys and keeps their own gear -- nothing here is shared.
    ///
    /// The stash is the persistent side of a player's kit and their
    /// <see cref="InventoryComponent"/> is the volatile side. Withdrawing moves a unit out
    /// of the stash immediately, so buying ten flashlights and taking two on an expedition
    /// leaves eight behind. Survive and the two come home; lose them and you are down to
    /// eight for good.
    ///
    /// Lives on the player prefab. Player NetworkObjects survive the networked train/forest
    /// scene loads, so the stash persists for the whole session.
    /// </summary>
    public sealed class PlayerStash : NetworkBehaviour
    {
        [Tooltip("Money is not wired up yet. Turn this on once wallets exist and purchases start costing.")]
        [SerializeField] bool chargeForPurchases;

        [Tooltip("What a player starts a fresh session holding.")]
        [SerializeField] StartingStock[] startingStock =
        {
            new() { itemId = "item.flashlight", count = 2 },
            new() { itemId = "item.medkit", count = 1 }
        };

        [Serializable]
        public struct StartingStock
        {
            public string itemId;
            public int count;
        }

        readonly NetworkList<StashEntry> m_Entries = new();

        /// <summary>The local player's stash, or null before one spawns.</summary>
        public static PlayerStash Local { get; private set; }

        /// <summary>Raised whenever these contents change.</summary>
        public event Action Changed;

        public bool ChargeForPurchases => chargeForPurchases;
        public int EntryCount => m_Entries.Count;

        public override void OnNetworkSpawn()
        {
            m_Entries.OnListChanged += HandleChanged;

            if (IsOwner)
            {
                Local = this;
            }

            if (IsServer)
            {
                foreach (var stock in startingStock)
                {
                    if (!string.IsNullOrWhiteSpace(stock.itemId) && stock.count > 0)
                    {
                        ServerAdd(ItemDefinition.StableHash(stock.itemId), stock.count);
                    }
                }
            }

            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_Entries.OnListChanged -= HandleChanged;

            if (Local == this)
            {
                Local = null;
            }
        }

        void HandleChanged(NetworkListEvent<StashEntry> _) => Changed?.Invoke();

        // ------------------------------------------------------------------ read access

        public int GetCount(int itemNetworkId)
        {
            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ItemId == itemNetworkId)
                {
                    return m_Entries[i].Count;
                }
            }

            return 0;
        }

        public StashEntry GetEntry(int index) => m_Entries[index];

        // ---------------------------------------------------------------- server writes

        /// <summary>Server only. Adds to the stash, clamped to the item's stash limit.</summary>
        public void ServerAdd(int itemNetworkId, int count)
        {
            if (!IsServer || count <= 0 || itemNetworkId == InventoryComponent.EmptySlot)
            {
                return;
            }

            var limit = int.MaxValue;
            var registry = GameContent.ItemRegistry;
            if (registry != null && registry.TryGet(itemNetworkId, out var definition))
            {
                limit = definition.StashLimit;
            }

            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ItemId != itemNetworkId)
                {
                    continue;
                }

                var entry = m_Entries[i];
                entry.Count = Mathf.Min(entry.Count + count, limit);
                m_Entries[i] = entry;
                return;
            }

            m_Entries.Add(new StashEntry
            {
                ItemId = itemNetworkId,
                Count = Mathf.Min(count, limit)
            });
        }

        /// <summary>Server only. Removes from the stash; false when there is not enough.</summary>
        public bool ServerRemove(int itemNetworkId, int count)
        {
            if (!IsServer || count <= 0)
            {
                return false;
            }

            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ItemId != itemNetworkId)
                {
                    continue;
                }

                var entry = m_Entries[i];
                if (entry.Count < count)
                {
                    return false;
                }

                entry.Count -= count;

                if (entry.Count <= 0)
                {
                    m_Entries.RemoveAt(i);
                }
                else
                {
                    m_Entries[i] = entry;
                }

                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ owner asks
        // InvokePermission is Owner throughout: a client may only spend and move gear in
        // its own stash, never in someone else's.

        /// <summary>
        /// Buy into this player's stash. Free while <see cref="chargeForPurchases"/> is off
        /// -- the price data already exists on every item, so switching it on is the only
        /// step needed once wallets land.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestPurchaseRpc(int itemNetworkId, int count)
        {
            if (count <= 0 || count > 99)
            {
                return;
            }

            var registry = GameContent.ItemRegistry;
            if (registry == null || !registry.TryGet(itemNetworkId, out var definition))
            {
                GameLog.Net($"Purchase rejected: unknown item {itemNetworkId}.");
                return;
            }

            if (chargeForPurchases)
            {
                GameLog.Net("Purchase rejected: charging is enabled but no wallet system exists.");
                return;
            }

            ServerAdd(definition.NetworkId, count);
            GameLog.Net($"Purchased {count}x {definition.DisplayName}.");
        }

        /// <summary>Move one unit from the stash into this player's carried slots.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestWithdrawRpc(int itemNetworkId)
        {
            if (GetCount(itemNetworkId) <= 0)
            {
                return;
            }

            if (!TryGetComponent<InventoryComponent>(out var inventory))
            {
                return;
            }

            if (!inventory.TryAdd(itemNetworkId))
            {
                if (TryGetComponent<Player.PlayerFeedback>(out var feedback))
                {
                    feedback.Notify("Your pack is full.");
                }

                return;
            }

            ServerRemove(itemNetworkId, 1);
        }

        /// <summary>Put one of this player's carried slots back into the stash.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestDepositRpc(int slotIndex)
        {
            if (!TryGetComponent<InventoryComponent>(out var inventory))
            {
                return;
            }

            var itemId = inventory.GetSlot(slotIndex);
            if (itemId == InventoryComponent.EmptySlot)
            {
                return;
            }

            inventory.TakeFrom(slotIndex);
            ServerAdd(itemId, 1);
        }

        // ---------------------------------------------------------------- expedition end

        /// <summary>
        /// Server only. Returns everything this player carried to their stash. Called on a
        /// successful extraction -- gear that comes home goes back on the shelf.
        /// </summary>
        public void ServerReturnCarried()
        {
            if (!IsServer || !TryGetComponent<InventoryComponent>(out var inventory))
            {
                return;
            }

            for (var i = 0; i < inventory.SlotCount; i++)
            {
                var itemId = inventory.TakeFrom(i);
                if (itemId != InventoryComponent.EmptySlot)
                {
                    ServerAdd(itemId, 1);
                }
            }
        }

        /// <summary>
        /// Server only. Wipes what this player carried without returning it. The "we lost
        /// them" path: the stash keeps only what never left the lobby.
        /// </summary>
        public void ServerLoseCarried()
        {
            if (!IsServer || !TryGetComponent<InventoryComponent>(out var inventory))
            {
                return;
            }

            for (var i = 0; i < inventory.SlotCount; i++)
            {
                inventory.TakeFrom(i);
            }
        }
    }
}
