using NUnit.Framework;
using Transity.Inventory;
using UnityEditor;
using UnityEngine;

namespace Transity.Tests
{
    /// <summary>
    /// Item ids cross the wire as ints. If host and client ever disagree on the hash, a
    /// pickup silently becomes the wrong item, so the hash is pinned by test.
    /// </summary>
    public sealed class ItemIdentityTests
    {
        [Test]
        public void StableHash_IsDeterministic()
        {
            Assert.AreEqual(
                ItemDefinition.StableHash("item.flashlight"),
                ItemDefinition.StableHash("item.flashlight"));
        }

        [Test]
        public void StableHash_MatchesKnownValue()
        {
            // Pinned so a refactor of the hash cannot silently invalidate saved loadouts
            // or desync a host from a client running an older build.
            Assert.AreEqual(2064492614, ItemDefinition.StableHash("item.flashlight"),
                "Hash changed. If this is intentional, bump the save schema version too.");
        }

        [Test]
        public void StableHash_DiffersBetweenItems()
        {
            Assert.AreNotEqual(
                ItemDefinition.StableHash("item.rifle"),
                ItemDefinition.StableHash("item.bait"));
        }

        [Test]
        public void StableHash_NeverCollidesWithEmptySlot()
        {
            Assert.AreNotEqual(InventoryComponent.EmptySlot, ItemDefinition.StableHash("item.rifle"));
            Assert.AreEqual(InventoryComponent.EmptySlot, ItemDefinition.StableHash(null));
        }

        [Test]
        public void Registry_ResolvesDefinitionById()
        {
            var definition = MakeDefinition("item.test.lantern");
            var registry = ScriptableObject.CreateInstance<ItemRegistry>();

            var so = new SerializedObject(registry);
            var list = so.FindProperty("items");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            so.ApplyModifiedPropertiesWithoutUndo();

            registry.Rebuild();

            Assert.IsTrue(registry.TryGet(definition.NetworkId, out var resolved));
            Assert.AreSame(definition, resolved);

            Object.DestroyImmediate(registry);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Registry_ReturnsFalseForUnknownId()
        {
            var registry = ScriptableObject.CreateInstance<ItemRegistry>();
            registry.Rebuild();

            Assert.IsFalse(registry.TryGet(ItemDefinition.StableHash("item.does.not.exist"), out _));

            Object.DestroyImmediate(registry);
        }

        static ItemDefinition MakeDefinition(string id)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>();
            var so = new SerializedObject(definition);
            so.FindProperty("itemId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }
    }
}
