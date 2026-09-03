using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Placed in the world in front of the player. The prefab must carry a NetworkObject
    /// and be registered in the network prefab list; the server spawns it. Slot state is
    /// how many are left in the pack.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Deployable", fileName = "Deployable_New")]
    public sealed class DeployableBehaviour : ItemBehaviour
    {
        public GameObject prefab;

        [Tooltip("Metres ahead of the player it lands.")]
        public float placeDistance = 1.6f;

        [Tooltip("Snap the placement down to the ground below the aim point.")]
        public bool placeOnGround = true;

        [Tooltip("Thrown instead of placed: spawns at the head and is given this speed.")]
        public float throwSpeed;

        public int charges = 1;

        public override ItemUseKind UseKind => ItemUseKind.Deployable;

        public override int InitialState(ItemDefinition definition) => Mathf.Max(1, charges);

        public override string DescribeState(int state) => charges > 1 ? $"x{state}" : string.Empty;

        public override bool ConsumedWhenEmpty => true;
    }
}
