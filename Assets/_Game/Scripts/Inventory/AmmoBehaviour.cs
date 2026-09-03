using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// A box of rounds. Occupies a slot -- bringing ammunition is a loadout decision, not
    /// a free counter -- and empties into magazines on reload. Slot state is rounds left.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Ammo", fileName = "Ammo_New")]
    public sealed class AmmoBehaviour : ItemBehaviour
    {
        public int roundsPerBox = 30;

        public override ItemUseKind UseKind => ItemUseKind.Passive;

        public override int InitialState(ItemDefinition definition) => Mathf.Max(1, roundsPerBox);

        public override string DescribeState(int state) => $"{state} rds";

        public override bool ConsumedWhenEmpty => true;
    }
}
