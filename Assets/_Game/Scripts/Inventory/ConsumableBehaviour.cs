using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Something you use on yourself by holding Attack. Slot state is charges remaining.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Consumable", fileName = "Consumable_New")]
    public sealed class ConsumableBehaviour : ItemBehaviour
    {
        [Header("Use")]
        [Tooltip("How long Attack has to be held. Interrupted by moving fast or taking a hit.")]
        public float useSeconds = 2.5f;
        public int charges = 1;
        public bool canUseAtFullHealth;

        [Header("Effect")]
        public float healAmount = 50f;
        public bool stopsBleeding = true;
        [Tooltip("Restores stamina to full immediately.")]
        public bool restoresStamina;
        [Tooltip("Seconds of adrenaline: faster movement, no stamina drain. Zero for none.")]
        public float adrenalineSeconds;
        [Tooltip("Revives a downed teammate rather than healing yourself. Reserved for the trauma kit.")]
        public bool revivesOthers;

        [Tooltip("Seconds of scent masking: creatures see and hear you at reduced range. Zero for none.")]
        public float maskSeconds;

        public override ItemUseKind UseKind => ItemUseKind.Consumable;

        public override int InitialState(ItemDefinition definition) => Mathf.Max(1, charges);

        public override string DescribeState(int state) => charges > 1 ? $"x{state}" : string.Empty;

        public override bool ConsumedWhenEmpty => true;
    }
}
