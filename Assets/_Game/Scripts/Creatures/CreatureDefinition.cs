using Transity.Audio;
using Transity.Inventory;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// Everything that makes one creature type different from another. The brain, body
    /// and voice all read from this; a new creature is a new asset, not new code.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Creature Definition", fileName = "Creature_New")]
    public sealed class CreatureDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "creature.new";
        public string displayName = "Creature";
        [TextArea] public string fieldNotes;
        public Temperament temperament = Temperament.Territorial;

        [Header("Body (graybox)")]
        public BodyShape shape = BodyShape.Quadruped;
        public float bodyLength = 2.2f;
        public float bodyHeight = 1.1f;
        public float bodyWidth = 0.9f;
        public float legLength = 0.9f;
        public Color color = new(0.25f, 0.28f, 0.2f);
        public Color eyeColor = new(1f, 0.6f, 0.2f);
        public string weakPointLabel = "back plate";
        public float weakPointMultiplier = 3f;

        [Header("Vitals")]
        public float maxHealth = 420f;
        public float regenWhileRecovering = 6f;
        [Range(0f, 1f)] public float fleeHealthFraction = 0.3f;
        [Range(0f, 1f)] public float recoveredHealthFraction = 0.7f;
        public float sedationThreshold = 100f;
        public float sedationDecay = 4f;
        public float collapseSeconds = 45f;

        [Header("Movement")]
        public float walkSpeed = 1.6f;
        public float stalkSpeed = 2.4f;
        public float runSpeed = 6.5f;
        public float acceleration = 14f;
        public float turnSpeedDegrees = 220f;
        public float agentRadius = 0.6f;
        public float agentHeight = 1.6f;
        public float roamRadius = 30f;

        [Header("Senses")]
        public float sightRange = 32f;
        public float sightAngle = 130f;
        [Tooltip("Multiplies the radius of every noise before it is compared with distance.")]
        public float hearing = 1f;
        [Tooltip("Seconds of full, close sight to go from oblivious to certain.")]
        public float secondsToNotice = 2.2f;
        public float awarenessDecayPerSecond = 0.08f;
        public float loseInterestSeconds = 14f;
        [Tooltip("Awareness gain multiplier inside the territory. Territorial only.")]
        public float territoryRadius = 38f;

        [Header("Combat")]
        public float attackRange = 2.4f;
        public float attackDamage = 34f;
        public float attackWindup = 0.55f;
        public float lungeSpeed = 11f;
        public float lungeSeconds = 0.32f;
        public float attackRecovery = 0.7f;
        public float attackCooldown = 1.4f;
        [Range(0f, 1f)] public float bleedChance = 0.5f;
        [Tooltip("Preferred hover distance while stalking.")]
        public float stalkDistance = 16f;
        [Tooltip("0 always backs off when looked at; 1 never does.")]
        [Range(0f, 1f)] public float boldness = 0.35f;
        [Tooltip("Chance to bolt from a loud noise nearby when not already committed.")]
        [Range(0f, 1f)] public float skittishness = 0.15f;

        [Header("Pack")]
        [Range(1, 6)] public int packSize = 1;
        public float flankRadius = 7f;
        [Tooltip("Members within this range of each other count as together.")]
        public float packCohesionRadius = 14f;

        [Header("Bounty")]
        public int bountyKill = 400;
        public int bountyCapture = 900;

        [Header("Voice")]
        public SoundKind voice = SoundKind.GrowlLow;
        public SoundKind alarmCall = SoundKind.Screech;
        public float voicePitch = 1f;
        public float footstepVolume = 0.8f;

        public int StableId => ItemDefinition.StableHash(id);
    }
}
