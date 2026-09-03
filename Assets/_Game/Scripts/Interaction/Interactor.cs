using System;
using Transity.Core;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Interaction
{
    /// <summary>
    /// Sits on the player. The owning client raycasts every frame to drive the HUD prompt
    /// and, on input, asks the server to run the interaction. The server independently
    /// re-resolves the target and re-checks range and line of sight -- the client's aim is
    /// a request, never a result.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class Interactor : NetworkBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] Transform rayOrigin;
        [SerializeField] float maxRange = 3f;
        [SerializeField] float sphereCastRadius = 0.12f;
        [SerializeField] LayerMask interactableMask = ~0;
        [SerializeField] LayerMask occlusionMask = ~0;

        [Tooltip("Server range allowance, multiplied onto the interactable's own range, to absorb latency.")]
        [SerializeField, Range(1f, 2f)] float serverRangeTolerance = 1.35f;

        IInteractable m_Current;

        /// <summary>Current target and its prompt, or (null, empty) when looking at nothing.</summary>
        public event Action<IInteractable, string> TargetChanged;

        public IInteractable CurrentTarget => m_Current;
        public Transform RayOrigin => rayOrigin != null ? rayOrigin : transform;

        void Update()
        {
            if (!IsOwner || input == null || input.Suppressed)
            {
                if (m_Current != null)
                {
                    SetTarget(null);
                }

                return;
            }

            ScanForTarget();

            if (!input.InteractPressed || m_Current is not NetworkInteractable networkInteractable)
            {
                return;
            }

            // An in-scene NetworkObject that never spawned (a stale scene built before the
            // GlobalObjectIdHash fix) would throw when referenced. Report it instead.
            if (!networkInteractable.IsSpawned)
            {
                GameLog.Error($"'{networkInteractable.name}' is not spawned, so it cannot be used. " +
                              "Rebuild the scaffold to regenerate its network id.");
                return;
            }

            RequestInteractRpc(new NetworkBehaviourReference(networkInteractable));
        }

        void ScanForTarget()
        {
            var origin = RayOrigin;
            var ray = new Ray(origin.position, origin.forward);

            IInteractable found = null;
            if (Physics.SphereCast(ray, sphereCastRadius, out var hit, maxRange, interactableMask,
                    QueryTriggerInteraction.Collide))
            {
                found = hit.collider.GetComponentInParent<IInteractable>();
                if (found != null && !found.CanInteract(this))
                {
                    found = null;
                }
            }

            if (!ReferenceEquals(found, m_Current))
            {
                SetTarget(found);
            }
        }

        void SetTarget(IInteractable target)
        {
            m_Current = target;
            TargetChanged?.Invoke(target, target != null ? target.GetPrompt(this) : string.Empty);
        }

        [Rpc(SendTo.Server)]
        void RequestInteractRpc(NetworkBehaviourReference reference)
        {
            if (!reference.TryGet(out NetworkInteractable target) || target == null)
            {
                GameLog.Net($"Client {OwnerClientId} referenced an interactable that no longer exists.");
                return;
            }

            if (!target.CanInteract(this))
            {
                return;
            }

            if (!IsWithinServerReach(target))
            {
                GameLog.Net($"Rejected interaction from client {OwnerClientId}: out of reach.");
                return;
            }

            target.OnServerInteract(this);
        }

        /// <summary>
        /// Sent by the server to the one client that used a station. Opening a screen is a
        /// view action, so it runs on the owner only -- but it still had to pass the same
        /// reach and line-of-sight checks as any other interaction to get here.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void OpenStationScreenRpc(NetworkBehaviourReference terminalRef)
        {
            if (!terminalRef.TryGet(out Transity.Train.StationTerminal terminal) || terminal == null)
            {
                return;
            }

            if (TryGetComponent<Transity.Player.StationFocusController>(out var focus))
            {
                focus.Open(terminal);
            }
        }

        /// <summary>
        /// Server-side validation. Distance is measured from the player root rather than
        /// the camera so a client cannot extend its reach by reporting a head position.
        /// </summary>
        bool IsWithinServerReach(IInteractable target)
        {
            var aimPoint = AimPointOf(target);
            var allowed = Mathf.Max(target.InteractionRange, maxRange) * serverRangeTolerance;
            var toTarget = aimPoint - transform.position;

            if (toTarget.sqrMagnitude > allowed * allowed)
            {
                return false;
            }

            var eye = transform.position + Vector3.up * 1.5f;
            var direction = aimPoint - eye;
            var distance = direction.magnitude;

            if (distance < 0.01f)
            {
                return true;
            }

            // Stop just short of the target. Ending the ray exactly on its surface lets
            // whatever it is standing on -- usually the floor slab -- count as an occluder.
            var checkDistance = Mathf.Max(distance - 0.2f, 0.01f);

            if (Physics.Raycast(eye, direction / distance, out var hit, checkDistance, occlusionMask,
                    QueryTriggerInteraction.Ignore))
            {
                var blockedBy = hit.collider.GetComponentInParent<IInteractable>();
                return ReferenceEquals(blockedBy, target);
            }

            return true;
        }

        /// <summary>
        /// The point on an interactable worth aiming at: the centre of its collider, not its
        /// transform origin. Kit pieces have bottom-centre origins, so an origin-based check
        /// measures to a point on the floor and tests line of sight through the floor.
        /// </summary>
        static Vector3 AimPointOf(IInteractable target)
        {
            var transform = target.Transform;

            if (transform.TryGetComponent<Collider>(out var own))
            {
                return own.bounds.center;
            }

            var child = transform.GetComponentInChildren<Collider>();
            return child != null ? child.bounds.center : transform.position;
        }
    }
}
