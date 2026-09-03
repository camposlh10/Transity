using Transity.Interaction;
using UnityEngine;

namespace Transity.Train
{
    /// <summary>
    /// A station you can walk up to and use. Pressing interact asks the server, which
    /// validates reach exactly as it does for any other interactable, then tells that one
    /// client to open the screen.
    ///
    /// Opening a screen changes nothing in the world, so it is a client-side view action.
    /// Everything the screen can then *do* -- buying, withdrawing, departing -- goes back
    /// through the server on its own RPC.
    /// </summary>
    public sealed class StationTerminal : NetworkInteractable
    {
        [Header("Terminal")]
        [SerializeField] StationScreenKind screen = StationScreenKind.Market;

        [Tooltip("Where the camera sits while this screen is open. Falls back to this object.")]
        [SerializeField] Transform focusPoint;

        [Tooltip("Optional: what the focus camera should look at, e.g. the vendor.")]
        [SerializeField] Transform lookTarget;

        public StationScreenKind Screen => screen;
        public Transform FocusPoint => focusPoint != null ? focusPoint : transform;
        public Transform LookTarget => lookTarget;

        public override void OnServerInteract(Interactor interactor)
        {
            interactor.OpenStationScreenRpc(new Unity.Netcode.NetworkBehaviourReference(this));
        }
    }
}
