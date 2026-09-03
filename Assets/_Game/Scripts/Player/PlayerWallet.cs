using System;
using Unity.Netcode;

namespace Transity.Player
{
    /// <summary>
    /// A player's credits. Server-written only: bounties are paid by the mission director
    /// and nothing on a client can add to this. Spending is not wired to the shop yet --
    /// PlayerStash.chargeForPurchases is the switch for that, and this is what it will
    /// draw from.
    /// </summary>
    public sealed class PlayerWallet : NetworkBehaviour
    {
        readonly NetworkVariable<int> m_Credits = new();

        public int Credits => m_Credits.Value;

        public event Action<int> Changed;

        public override void OnNetworkSpawn()
        {
            m_Credits.OnValueChanged += HandleChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Credits.OnValueChanged -= HandleChanged;
        }

        void HandleChanged(int previous, int current) => Changed?.Invoke(current);

        /// <summary>Server only.</summary>
        public void ServerAdd(int amount)
        {
            if (IsServer && amount != 0)
            {
                m_Credits.Value = Math.Max(0, m_Credits.Value + amount);
            }
        }

        /// <summary>Server only. False when the player cannot afford it.</summary>
        public bool ServerTrySpend(int amount)
        {
            if (!IsServer || amount < 0 || m_Credits.Value < amount)
            {
                return false;
            }

            m_Credits.Value -= amount;
            return true;
        }
    }
}
