using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>How the player interacts with an item once it is in their hands.</summary>
    public enum ItemUseKind : byte
    {
        /// <summary>Does something just by being carried: vests, the body camera, the backpack.</summary>
        Passive = 0,

        /// <summary>Fires or swings on Attack.</summary>
        Weapon = 1,

        /// <summary>Held Attack to apply to yourself: medkit, injector.</summary>
        Consumable = 2,

        /// <summary>Placed in the world on Attack: traps, bait, glow sticks.</summary>
        Deployable = 3,

        /// <summary>Worn on/off with the Flashlight key without being selected: lights and optics.</summary>
        Toggle = 4
    }

    /// <summary>
    /// What an item does. An <see cref="ItemDefinition"/> is identity and shop data; this
    /// is the verb. Kept as a separate asset so the same behaviour can be shared (every
    /// medkit-like item is a ConsumableBehaviour with different numbers) and so the
    /// definition table stays free of gameplay tuning.
    ///
    /// Slot state is one int per carried slot, replicated beside the item id. What it means
    /// is the behaviour's business: rounds in a magazine, charges left, battery seconds.
    /// </summary>
    public abstract class ItemBehaviour : ScriptableObject
    {
        public abstract ItemUseKind UseKind { get; }

        /// <summary>The slot state a freshly issued copy starts with.</summary>
        public virtual int InitialState(ItemDefinition definition) => 0;

        /// <summary>Short HUD string for the slot state, or empty when it has none.</summary>
        public virtual string DescribeState(int state) => string.Empty;

        /// <summary>Whether the state is "spent": the item should vanish when it reaches zero.</summary>
        public virtual bool ConsumedWhenEmpty => false;
    }
}
