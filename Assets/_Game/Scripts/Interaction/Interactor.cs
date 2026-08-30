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

            if (input.InteractPressed && m_Current is NetworkInteractable networkInteractable)
            {
                RequestInteractRpc(new NetworkBehaviourReference(networkInteractable));
            }
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
        /// Server-side validation. Distance is measured from the player root rather than
        /// the camera so a client cannot extend its reach by reporting a head position.
        /// </summary>
        bool IsWithinServerReach(IInteractable target)
        {
            var allowed = Mathf.Max(target.InteractionRange, maxRange) * serverRangeTolerance;
            var toTarget = target.Transform.position - transform.position;

            if (toTarget.sqrMagnitude > allowed * allowed)
            {
                return false;
            }

            // Cheap line of sight check: block interactions through solid geometry.
            var eye = transform.position + Vector3.up * 1.5f;
            var direction = target.Transform.position - eye;
            var distance = direction.magnitude;

            if (distance < 0.01f)
            {
                return true;
            }

            if (Physics.Raycast(eye, direction / distance, out var hit, distance, occlusionMask,
                    QueryTriggerInteraction.Ignore))
            {
                var blockedBy = hit.collider.GetComponentInParent<IInteractable>();
                return ReferenceEquals(blockedBy, target);
            }

            return true;
        }
    }
}
