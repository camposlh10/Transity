using System.Collections.Generic;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// A patch of forest creatures can start in. The director picks regions far from the
    /// crew so nothing spawns in view, and territorial creatures treat the region centre
    /// as home.
    /// </summary>
    public sealed class CreatureSpawnRegion : MonoBehaviour
    {
        [SerializeField] float radius = 18f;

        static readonly List<CreatureSpawnRegion> Registered = new();

        public float Radius => radius;
        public static IReadOnlyList<CreatureSpawnRegion> All => Registered;

        void OnEnable() => Registered.Add(this);

        void OnDisable() => Registered.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
