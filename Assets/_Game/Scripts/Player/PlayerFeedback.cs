using System;
using Unity.Collections;
using Unity.Netcode;

namespace Transity.Player
{
    /// <summary>
    /// One-way channel for the server to tell a single player why something did not work
    /// ("pack is full", "out of range"). Keeps refusal messages off the client's guesswork.
    /// </summary>
    public sealed class PlayerFeedback : NetworkBehaviour
    {
        /// <summary>Raised on the owning client only.</summary>
        public event Action<string> MessageReceived;

        /// <summary>Server only.</summary>
        public void Notify(string message)
        {
            if (IsServer)
            {
                ShowMessageRpc(new FixedString128Bytes(message));
            }
        }

        [Rpc(SendTo.Owner)]
        void ShowMessageRpc(FixedString128Bytes message)
        {
            MessageReceived?.Invoke(message.ToString());
        }
    }
}
