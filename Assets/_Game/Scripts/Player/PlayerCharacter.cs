using Transity.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// The player root. Owns local-only rig activation (camera, listener, input) and the
    /// server-driven teleport used for spawning and scene transitions.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerCharacter : NetworkBehaviour
    {
        [Header("Local rig")]
        [SerializeField] Camera playerCamera;
        [SerializeField] AudioListener audioListener;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerLook look;
        [SerializeField] FirstPersonController movement;
        [SerializeField] NetworkTransform networkTransform;

        [Header("Third person visuals")]
        [Tooltip("Renderers hidden for the owning player so they do not see their own body from the inside.")]
        [SerializeField] Renderer[] bodyRenderers;

        CharacterController m_Controller;

        public static PlayerCharacter Local { get; private set; }
        public Camera PlayerCamera => playerCamera;
        public PlayerInputReader Input => input;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            ConfigureLocalRig(IsOwner);

            if (IsOwner)
            {
                Local = this;
            }

            if (IsServer)
            {
                // OwnerClientId is dense enough for a 4-player lobby; a dedicated slot
                // allocator only becomes worthwhile with late joins and reconnects.
                PlaceAtSpawn(SpawnContext.Train, (int)OwnerClientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
            {
                Local = null;
            }
        }

        void ConfigureLocalRig(bool owned)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = owned;
            }

            if (audioListener != null)
            {
                audioListener.enabled = owned;
            }

            if (input != null)
            {
                input.enabled = owned;
            }

            if (look != null)
            {
                look.enabled = owned;
            }

            foreach (var renderer in bodyRenderers)
            {
                if (renderer != null)
                {
                    // Still casts shadows, so the owner sees their own silhouette.
                    renderer.shadowCastingMode = owned
                        ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            if (owned)
            {
                SetCursorLocked(true);
            }
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Server-side: move this player onto a spawn marker of the given context.</summary>
        public void PlaceAtSpawn(SpawnContext context, int slot)
        {
            if (!IsServer)
            {
                return;
            }

            if (!PlayerSpawnPoint.TryGetPose(context, slot, out var position, out var yaw))
            {
                // Expected for the host at start-up: its player object spawns before the
                // train scene finishes loading. MissionDirector re-places everyone once
                // the load completes, so leaving the player put is the right move here.
                GameLog.Net($"No {context} spawn point available yet; deferring placement.");
                return;
            }

            TeleportRpc(position, yaw);
        }

        /// <summary>
        /// Position is owner-authoritative, so the server asks the owner to move rather
        /// than writing the transform itself (which the owner would immediately overwrite).
        /// </summary>
        [Rpc(SendTo.Owner)]
        void TeleportRpc(Vector3 position, float yaw)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);

            if (m_Controller != null)
            {
                m_Controller.enabled = false;
                transform.SetPositionAndRotation(position, rotation);
                m_Controller.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            if (networkTransform != null)
            {
                networkTransform.Teleport(position, rotation, transform.localScale);
            }

            if (movement != null)
            {
                movement.ResetMomentum();
            }

            if (look != null)
            {
                look.ResetPitch();
            }

            GameLog.Net($"Teleported to {position}");
        }
    }
}
