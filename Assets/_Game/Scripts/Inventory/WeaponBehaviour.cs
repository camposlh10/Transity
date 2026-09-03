using Transity.Audio;
using UnityEngine;

namespace Transity.Inventory
{
    public enum FireMode : byte
    {
        Hitscan = 0,
        Melee = 1
    }

    /// <summary>
    /// Numbers for anything that hurts a creature. Slot state is rounds in the magazine.
    ///
    /// Lethal and sedative weapons share this class: a tranquiliser is a weapon with
    /// <see cref="sedation"/> above zero and <see cref="damage"/> near it. Bringing both
    /// kinds and choosing which to raise is the capture-versus-kill decision.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Weapon", fileName = "Weapon_New")]
    public sealed class WeaponBehaviour : ItemBehaviour
    {
        [Header("Firing")]
        public FireMode fireMode = FireMode.Hitscan;
        public bool automatic;
        [Tooltip("Shots per minute. Also the melee swing rate.")]
        public float roundsPerMinute = 90f;
        [Tooltip("Pellets per shot; shotguns fire several with independent spread.")]
        [Range(1, 12)] public int pellets = 1;
        public float range = 120f;
        [Range(0f, 15f)] public float spreadDegrees = 1.5f;
        [Range(0f, 15f)] public float aimSpreadDegrees = 0.4f;

        [Header("Damage")]
        public float damage = 20f;
        [Tooltip("Applied on top of the hitbox multiplier when the hitbox is a weak point.")]
        public float weakPointMultiplier = 1.5f;
        [Tooltip("Sedation per hit. Zero for lethal weapons.")]
        public float sedation;
        [Range(0f, 1f)] public float bleedChance = 0.25f;
        [Tooltip("Metres per second of shove on the target's agent, for the shotgun.")]
        public float knockback;

        [Header("Ammunition")]
        [Tooltip("Zero means no magazine: melee, or a weapon that never runs dry.")]
        public int magazineSize = 10;
        public float reloadSeconds = 2f;
        [Tooltip("Reloading pulls rounds out of an Ammo box in the pack. Off for the crossbow.")]
        public bool usesAmmoBox = true;

        [Header("Feel")]
        [Tooltip("Degrees of vertical kick per shot.")]
        public float recoilKick = 1.5f;
        public float recoilRecoverySpeed = 8f;
        [Tooltip("Camera field of view while aiming. 0 keeps the default.")]
        public float aimFieldOfView = 50f;
        public float noiseRadius = 60f;
        public SoundKind fireSound = SoundKind.GunshotLight;
        [Tooltip("Where the muzzle flash sits, in viewmodel space.")]
        public Vector3 muzzleOffset = new(0f, 0.05f, 0.5f);

        public override ItemUseKind UseKind => ItemUseKind.Weapon;

        public float SecondsBetweenShots => roundsPerMinute > 0f ? 60f / roundsPerMinute : 1f;

        public bool IsSedative => sedation > 0f;

        public override int InitialState(ItemDefinition definition) => magazineSize;

        public override string DescribeState(int state) =>
            magazineSize > 0 ? state.ToString() : string.Empty;
    }
}
