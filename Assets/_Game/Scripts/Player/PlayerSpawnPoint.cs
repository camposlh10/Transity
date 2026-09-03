using System.Collections.Generic;
using UnityEngine;

namespace Transity.Player
{
    public enum SpawnContext
    {
        Train,
        Expedition
    }

    /// <summary>
    /// Marker placed in the Train and Forest scenes. Registers itself so the server can
    /// hand out distinct positions without holding scene references across a load.
    /// </summary>
    /// <remarks>
    /// ExecuteAlways so markers register in the editor as well as at runtime. Level layout
    /// tooling and the blockout generator both place these outside play mode, and a marker
    /// that only exists once you press Play cannot be validated.
    /// </remarks>
    [ExecuteAlways]
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [SerializeField] SpawnContext context = SpawnContext.Train;

        static readonly List<PlayerSpawnPoint> Registered = new();

        public SpawnContext Context => context;

        void OnEnable()
        {
            if (!Registered.Contains(this))
            {
                Registered.Add(this);
            }
        }

        void OnDisable() => Registered.Remove(this);

        /// <summary>
        /// Returns a spawn pose for the given slot, cycling if there are fewer points
        /// than players. Falls back to the world origin so a missing marker never
        /// silently drops players through the floor.
        /// </summary>
        public static bool TryGetPose(SpawnContext context, int slot, out Vector3 position, out float yaw)
        {
            var matching = new List<PlayerSpawnPoint>();
            foreach (var point in Registered)
            {
                if (point != null && point.context == context)
                {
                    matching.Add(point);
                }
            }

            if (matching.Count == 0)
            {
                position = Vector3.up;
                yaw = 0f;
                return false;
            }

            matching.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var chosen = matching[slot % matching.Count];
            position = chosen.transform.position;
            yaw = chosen.transform.eulerAngles.y;
            return true;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = context == SpawnContext.Train
                ? new Color(1f, 0.75f, 0.3f, 0.9f)
                : new Color(0.4f, 0.9f, 0.6f, 0.9f);

            var origin = transform.position + Vector3.up * 0.9f;
            Gizmos.DrawWireSphere(origin, 0.35f);
            Gizmos.DrawLine(origin, origin + transform.forward * 1.2f);
        }
    }
}
