using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// A player's display name, replicated. Nothing gameplay-critical reads it, but the
    /// ledger, the debrief and the Collector's letter all need to say who is who.
    /// </summary>
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        const string PreferenceKey = "transity.name";

        readonly NetworkVariable<FixedString32Bytes> m_Name = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        static readonly Dictionary<ulong, PlayerIdentity> ByClient = new();

        public string DisplayName => m_Name.Value.Length > 0 ? m_Name.Value.ToString() : $"Hunter {OwnerClientId + 1}";

        public override void OnNetworkSpawn()
        {
            ByClient[OwnerClientId] = this;

            if (IsOwner)
            {
                var remembered = PlayerPrefs.GetString(PreferenceKey, string.Empty);
                m_Name.Value = new FixedString32Bytes(
                    string.IsNullOrWhiteSpace(remembered) ? $"Hunter {OwnerClientId + 1}" : remembered.Trim());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (ByClient.TryGetValue(OwnerClientId, out var existing) && existing == this)
            {
                ByClient.Remove(OwnerClientId);
            }
        }

        /// <summary>Name for a client id, on any peer. Falls back to a numbered label.</summary>
        public static string NameOf(ulong clientId)
        {
            return ByClient.TryGetValue(clientId, out var identity) && identity != null
                ? identity.DisplayName
                : $"Hunter {clientId + 1}";
        }

        public static PlayerIdentity Find(ulong clientId) =>
            ByClient.TryGetValue(clientId, out var identity) ? identity : null;

        public static IEnumerable<PlayerIdentity> All => ByClient.Values;
    }
}
