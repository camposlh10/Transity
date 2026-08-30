using UnityEngine;

namespace Transity.Interaction
{
    /// <summary>
    /// Everything a player can look at and use: doors, dropped equipment, the contract
    /// board, the shop terminal, the extraction lever.
    ///
    /// The split matters. <see cref="GetPrompt"/> and <see cref="CanInteract"/> run on the
    /// looking client purely to draw UI; <see cref="OnServerInteract"/> runs only on the
    /// host and is the single place where anything actually changes.
    /// </summary>
    public interface IInteractable
    {
        Transform Transform { get; }

        /// <summary>Metres. The server re-checks this, so a client cannot reach through walls.</summary>
        float InteractionRange { get; }

        /// <summary>Client-side prediction of availability, for greying out the prompt.</summary>
        bool CanInteract(Interactor interactor);

        string GetPrompt(Interactor interactor);

        /// <summary>Authoritative. Called on the host only, after range and sight checks.</summary>
        void OnServerInteract(Interactor interactor);
    }
}
