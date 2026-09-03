using System;
using System.Collections.Generic;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// Every creature type in the build and the prefab that carries it. The forest
    /// director spawns from here; clients resolve a replicated definition id through it.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Creature Registry", fileName = "CreatureRegistry")]
    public sealed class CreatureRegistry : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public CreatureDefinition definition;
            public GameObject prefab;
        }

        [SerializeField] List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        public CreatureDefinition Find(int stableId)
        {
            foreach (var entry in entries)
            {
                if (entry.definition != null && entry.definition.StableId == stableId)
                {
                    return entry.definition;
                }
            }

            return null;
        }

        public GameObject PrefabFor(CreatureDefinition definition)
        {
            foreach (var entry in entries)
            {
                if (entry.definition == definition)
                {
                    return entry.prefab;
                }
            }

            return null;
        }
    }
}
