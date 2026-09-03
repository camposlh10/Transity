using UnityEngine;

namespace Transity.Combat
{
    /// <summary>
    /// A collider that belongs to something with <see cref="Health"/>, with a damage
    /// multiplier. A creature's moss plate is a 3x hitbox; its armoured flank is 0.5x. The
    /// weapon that hits it never needs to know which creature it was.
    /// </summary>
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] float damageMultiplier = 1f;

        [Tooltip("Marks this as the weak point, for the hit marker and the bounty tally.")]
        [SerializeField] bool weakPoint;

        [Tooltip("Optional. Found on the parent when left empty.")]
        [SerializeField] Health health;

        public float DamageMultiplier => damageMultiplier;
        public bool WeakPoint => weakPoint;
        public Health Health => health;

        void Awake()
        {
            if (health == null)
            {
                health = GetComponentInParent<Health>();
            }
        }

        /// <summary>
        /// Resolves the damageable behind a collider, applying any hitbox multiplier.
        /// Returns false when the collider belongs to nothing that can be hurt.
        /// </summary>
        public static bool TryResolve(Collider collider, out Health health, out float multiplier, out bool weakPoint)
        {
            health = null;
            multiplier = 1f;
            weakPoint = false;

            if (collider == null)
            {
                return false;
            }

            if (collider.TryGetComponent<Hitbox>(out var hitbox))
            {
                health = hitbox.health != null ? hitbox.health : collider.GetComponentInParent<Health>();
                multiplier = hitbox.damageMultiplier;
                weakPoint = hitbox.weakPoint;
                return health != null;
            }

            health = collider.GetComponentInParent<Health>();
            return health != null;
        }
    }
}
