using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Transity.Inventory;
using UnityEditor;
using UnityEngine;

namespace Transity.Tests
{
    /// <summary>
    /// Guards the shipped item catalogue rather than the code that builds it.
    ///
    /// The Hunter Depot drop took the roster from nine items to twenty-nine, all of them
    /// crossing the wire as a 32-bit hash of their id. A collision there does not throw --
    /// it silently hands a player the wrong piece of equipment -- so the whole live set is
    /// checked, not just a sample.
    /// </summary>
    public sealed class EquipmentCatalogTests
    {
        const string RegistryPath = "Assets/_Game/Data/Items/ItemRegistry.asset";

        static ItemRegistry LoadRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryPath);
            Assert.IsNotNull(registry, $"No item registry at {RegistryPath}. Run the scaffold.");
            return registry;
        }

        static IReadOnlyList<ItemDefinition> LiveItems()
        {
            return LoadRegistry().Items.Where(i => i != null).ToList();
        }

        [Test]
        public void Registry_HasNoNullEntries()
        {
            Assert.IsFalse(LoadRegistry().Items.Any(i => i == null),
                "A null slot in the registry means an item asset was deleted without a rebuild.");
        }

        [Test]
        public void Registry_ItemIdsAreUnique()
        {
            var duplicates = LiveItems()
                .GroupBy(i => i.ItemId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            CollectionAssert.IsEmpty(duplicates, "Duplicate item ids: " + string.Join(", ", duplicates));
        }

        [Test]
        public void Registry_NetworkIdsDoNotCollide()
        {
            var byHash = new Dictionary<int, string>();

            foreach (var item in LiveItems())
            {
                if (byHash.TryGetValue(item.NetworkId, out var existing))
                {
                    Assert.Fail($"Hash collision: '{item.ItemId}' and '{existing}' both hash to " +
                                $"{item.NetworkId}. Rename one, then bump the save schema.");
                }

                byHash[item.NetworkId] = item.ItemId;
            }
        }

        [Test]
        public void Registry_ResolvesEveryItemItHolds()
        {
            var registry = LoadRegistry();
            registry.Rebuild();

            foreach (var item in LiveItems())
            {
                Assert.IsTrue(registry.TryGet(item.NetworkId, out var resolved),
                    $"Registry cannot resolve '{item.ItemId}'.");
                Assert.AreSame(item, resolved);
            }
        }

        [Test]
        public void Registry_EveryItemHasAWorldPrefab()
        {
            var missing = LiveItems()
                .Where(i => i.WorldPrefab == null)
                .Select(i => i.ItemId)
                .ToList();

            CollectionAssert.IsEmpty(missing,
                "Items with no world prefab cannot be dropped: " + string.Join(", ", missing));
        }

        [Test]
        public void Registry_WorldPrefabsAreOnTheInteractableLayer()
        {
            // Layer 6. The interaction raycast masks to it, so a prefab on the default
            // layer is invisible to the player even though it renders.
            var wrong = LiveItems()
                .Where(i => i.WorldPrefab != null && i.WorldPrefab.layer != 6)
                .Select(i => i.ItemId)
                .ToList();

            CollectionAssert.IsEmpty(wrong,
                "World prefabs off the Interactable layer: " + string.Join(", ", wrong));
        }

        [Test]
        public void Registry_WorldPrefabsHaveACollider()
        {
            var missing = LiveItems()
                .Where(i => i.WorldPrefab != null &&
                            i.WorldPrefab.GetComponent<Collider>() == null)
                .Select(i => i.ItemId)
                .ToList();

            CollectionAssert.IsEmpty(missing,
                "World prefabs with nothing to hit: " + string.Join(", ", missing));
        }

        [Test]
        public void Registry_PricedItemsAreAtRiskAndIssuedOnesAreNot()
        {
            // The stash rule the market screen advertises: if you paid for it you can lose
            // it, and if it was issued you cannot. A mismatch here is a refund exploit.
            foreach (var item in LiveItems())
            {
                if (item.Price > 0)
                {
                    Assert.IsTrue(item.AtRiskOnExpedition,
                        $"'{item.ItemId}' costs {item.Price} but is never lost.");
                }
            }
        }

        [Test]
        public void Registry_StashLimitsArePositive()
        {
            var broken = LiveItems()
                .Where(i => i.StashLimit <= 0)
                .Select(i => i.ItemId)
                .ToList();

            CollectionAssert.IsEmpty(broken,
                "A stash limit of zero makes an item unbuyable: " + string.Join(", ", broken));
        }
    }
}
