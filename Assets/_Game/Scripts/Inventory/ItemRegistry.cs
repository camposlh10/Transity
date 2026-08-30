using System.Collections.Generic;
using Transity.Core;
using UnityEngine;

namespace Transity.Inventory
{
    /// <summary>
    /// The set of items that exist in the build. Lets the server turn a replicated item id
    /// back into a definition when spawning dropped equipment.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : ScriptableObject
    {
        [SerializeField] List<ItemDefinition> items = new();

        readonly Dictionary<int, ItemDefinition> m_ById = new();
        bool m_Built;

        public IReadOnlyList<ItemDefinition> Items => items;

        void OnEnable() => Rebuild();

        public void Rebuild()
        {
            m_ById.Clear();
            m_Built = true;

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var id = item.NetworkId;
                if (m_ById.TryGetValue(id, out var existing))
                {
                    GameLog.Error($"Duplicate item id '{item.ItemId}' on '{item.name}' and '{existing.name}'.");
                    continue;
                }

                m_ById[id] = item;
            }
        }

        public bool TryGet(int networkId, out ItemDefinition definition)
        {
            if (!m_Built)
            {
                Rebuild();
            }

            return m_ById.TryGetValue(networkId, out definition);
        }
    }
}
