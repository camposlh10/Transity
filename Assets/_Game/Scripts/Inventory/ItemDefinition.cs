using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// Authoring data for a piece of equipment. Systems never reference item prefabs
    /// directly -- they go through the definition, so adding the ninth item is an asset
    /// change rather than a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Item Definition", fileName = "Item_New")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable string id. Renaming this breaks existing saves; the display name is free to change.")]
        [SerializeField] string itemId = "item.new";
        [SerializeField] string displayName = "New Item";
        [SerializeField, TextArea] string description;
        [SerializeField] Sprite icon;
        [Tooltip("Shop aisle. Presentation only.")]
        [SerializeField] ItemCategory category = ItemCategory.Tool;

        [Tooltip("Where this sits on the hunter. Items sharing a slot compete for it.")]
        [SerializeField] EquipmentSlot slot = EquipmentSlot.None;

        [Header("World")]
        [Tooltip("Networked prefab spawned when this item is dropped. Must be in the NetworkManager prefab list.")]
        [SerializeField] GameObject worldPrefab;

        [Tooltip("Visual-only model shown in the hands. Usually the same mesh the world prefab uses.")]
        [SerializeField] GameObject heldModel;

        [Header("Use")]
        [Tooltip("What the item does when carried or used. Empty means it is inert.")]
        [SerializeField] ItemBehaviour behaviour;

        [Header("Economy")]
        [SerializeField] int price = 100;

        [Tooltip("How many of this the crew may hold in the lobby stash.")]
        [SerializeField] int stashLimit = 99;
        [Tooltip("Free starting gear (flashlight, radio) is not at risk. Purchased gear is lost on a failed extraction.")]
        [SerializeField] bool atRiskOnExpedition = true;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public ItemCategory Category => category;
        public EquipmentSlot Slot => slot;
        public GameObject WorldPrefab => worldPrefab;
        public GameObject HeldModel => heldModel;
        public ItemBehaviour Behaviour => behaviour;
        public ItemUseKind UseKind => behaviour != null ? behaviour.UseKind : ItemUseKind.Passive;

        /// <summary>The behaviour as a specific type, or null when it is something else.</summary>
        public T BehaviourAs<T>() where T : ItemBehaviour => behaviour as T;
        public int Price => price;
        public int StashLimit => stashLimit;
        public bool AtRiskOnExpedition => atRiskOnExpedition;

        /// <summary>
        /// Network-safe identity. Uses FNV-1a rather than string.GetHashCode, which is not
        /// guaranteed to match between processes -- host and client must agree.
        /// </summary>
        public int NetworkId => StableHash(itemId);

        public static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                var hash = offsetBasis;
                foreach (var c in value)
                {
                    hash ^= c;
                    hash *= prime;
                }

                // Reserve 0 for "empty slot".
                var result = (int)hash;
                return result == 0 ? 1 : result;
            }
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = name.ToLowerInvariant();
            }
        }
    }
}
