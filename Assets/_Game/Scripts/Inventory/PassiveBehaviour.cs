using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Worn just by being in the pack. Vests soak damage and slow you; the body camera
    /// adds to the bounty if you bring it home. No slot state.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Passive", fileName = "Passive_New")]
    public sealed class PassiveBehaviour : ItemBehaviour
    {
        [Tooltip("Incoming damage multiplier while carried. 0.6 = takes 60%.")]
        public float damageTakenMultiplier = 1f;

        [Tooltip("Movement speed multiplier while carried.")]
        public float speedMultiplier = 1f;

        [Tooltip("Noise radius multiplier while carried. Heavy armour rattles.")]
        public float noiseMultiplier = 1f;

        [Tooltip("Bounty multiplier on extraction while carried. The body camera.")]
        public float bountyMultiplier = 1f;

        [Tooltip("Stops the respirator-only hazards. No hazards exist yet; kept so the item has a hook.")]
        public bool filtersAir;

        public override ItemUseKind UseKind => ItemUseKind.Passive;
    }
}
