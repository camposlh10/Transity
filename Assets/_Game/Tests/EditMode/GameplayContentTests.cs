using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Transity.Combat;
using Transity.Creatures;
using Transity.Inventory;
using Transity.Missions;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Transity.Tests
{
    /// <summary>
    /// Checks the generated gameplay content rather than the code that generates it.
    ///
    /// Everything here fails silently in play: a creature prefab with no NavMeshAgent just
    /// stands still, a weapon with no behaviour is a dead click, a contract pointing at a
    /// deleted creature spawns an empty forest. None of it throws, so none of it shows up
    /// except as "the game feels broken".
    /// </summary>
    public sealed class GameplayContentTests
    {
        const string CreatureRegistryPath = "Assets/_Game/Data/Creatures/CreatureRegistry.asset";
        const string ContractRegistryPath = "Assets/_Game/Data/Contracts/ContractRegistry.asset";
        const string ItemRegistryPath = "Assets/_Game/Data/Items/ItemRegistry.asset";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"No {typeof(T).Name} at {path}. Run Tools > Transity > Build Vertical Slice Scaffold.");
            return asset;
        }

        static CreatureRegistry Creatures() => Load<CreatureRegistry>(CreatureRegistryPath);
        static ContractRegistry Contracts() => Load<ContractRegistry>(ContractRegistryPath);
        static ItemRegistry Items() => Load<ItemRegistry>(ItemRegistryPath);

        // -------------------------------------------------------------------- creatures

        [Test]
        public void Creatures_RegistryIsPopulated()
        {
            Assert.Greater(Creatures().Entries.Count, 0, "No creatures; the forest would be empty.");
        }

        [Test]
        public void Creatures_EveryEntryHasBothHalves()
        {
            foreach (var entry in Creatures().Entries)
            {
                Assert.IsNotNull(entry.definition, "A registry entry has no definition.");
                Assert.IsNotNull(entry.prefab, $"'{entry.definition.id}' has no prefab to spawn.");
            }
        }

        [Test]
        public void Creatures_IdsAreUniqueAndDoNotCollideWhenHashed()
        {
            var byHash = new Dictionary<int, string>();

            foreach (var entry in Creatures().Entries)
            {
                var id = entry.definition.StableId;
                Assert.IsFalse(byHash.ContainsKey(id),
                    $"Hash collision between '{entry.definition.id}' and '{byHash.GetValueOrDefault(id)}'.");
                byHash[id] = entry.definition.id;
            }
        }

        [Test]
        public void Creatures_ResolveThroughTheRegistryById()
        {
            var registry = Creatures();

            foreach (var entry in registry.Entries)
            {
                Assert.AreSame(entry.definition, registry.Find(entry.definition.StableId));
                Assert.AreSame(entry.prefab, registry.PrefabFor(entry.definition));
            }
        }

        [Test]
        public void Creatures_PrefabsCarryEverythingTheBrainNeeds()
        {
            foreach (var entry in Creatures().Entries)
            {
                var prefab = entry.prefab;
                var name = entry.definition.id;

                // A NavMeshAgent is the one that fails most quietly: without it the
                // creature spawns, is visible, and never moves.
                Assert.IsNotNull(prefab.GetComponent<NavMeshAgent>(), $"'{name}' cannot path.");
                Assert.IsNotNull(prefab.GetComponent<CreatureBrain>(), $"'{name}' has no brain.");
                Assert.IsNotNull(prefab.GetComponent<Health>(), $"'{name}' cannot be hurt.");
                Assert.IsNotNull(prefab.GetComponent<Sedation>(), $"'{name}' cannot be tranquilised.");
                Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), $"'{name}' cannot be networked.");
                Assert.IsNotNull(prefab.GetComponentInChildren<CreatureBody>(true), $"'{name}' has no body.");
            }
        }

        [Test]
        public void Creatures_HaveAWeakPointWorthAimingAt()
        {
            foreach (var entry in Creatures().Entries)
            {
                var hitboxes = entry.prefab.GetComponentsInChildren<Hitbox>(true);
                Assert.IsNotEmpty(hitboxes, $"'{entry.definition.id}' cannot be hit anywhere.");

                var weakPoints = hitboxes.Where(h => h.WeakPoint).ToList();
                Assert.IsNotEmpty(weakPoints,
                    $"'{entry.definition.id}' has no weak point, so aiming carefully earns nothing.");

                foreach (var weakPoint in weakPoints)
                {
                    Assert.Greater(weakPoint.DamageMultiplier, 1f,
                        $"The weak point on '{entry.definition.id}' takes normal damage.");
                }
            }
        }

        [Test]
        public void Creatures_AreOnTheCreatureLayer()
        {
            // Layer 9. Shots and the interaction ray both mask to it, so a creature on the
            // default layer is invulnerable and untaggable at the same time.
            foreach (var entry in Creatures().Entries)
            {
                foreach (var transform in entry.prefab.GetComponentsInChildren<Transform>(true))
                {
                    Assert.AreEqual(9, transform.gameObject.layer,
                        $"'{entry.definition.id}/{transform.name}' is on layer {transform.gameObject.layer}.");
                }
            }
        }

        [Test]
        public void Creatures_SpeedsAreOrdered()
        {
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Less(c.walkSpeed, c.runSpeed, $"'{c.id}' does not speed up to chase.");
                Assert.LessOrEqual(c.stalkSpeed, c.runSpeed, $"'{c.id}' stalks faster than it charges.");
                Assert.Greater(c.walkSpeed, 0f, $"'{c.id}' cannot move at all.");
            }
        }

        [Test]
        public void Creatures_CanBeEscapedByBreakingContact()
        {
            // Every creature is faster than a sprint, and sprinting costs stamina, so a
            // footrace is never the escape. Breaking line of sight is -- which only works
            // if attention actually decays and interest actually runs out. Without both of
            // these a chase is unlosable and the forest becomes a corridor.
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.awarenessDecayPerSecond, 0f,
                    $"'{c.id}' never forgets, so hiding does nothing.");
                Assert.Greater(c.loseInterestSeconds, 0f,
                    $"'{c.id}' pursues forever once it has seen you.");
                Assert.Less(c.loseInterestSeconds, 60f,
                    $"'{c.id}' hunts for {c.loseInterestSeconds}s, which outlasts the player's patience.");
            }
        }

        [Test]
        public void Creatures_AreFasterThanASprintSoStaminaMatters()
        {
            // The flip side of the rule above. If a creature were slower than a sprint it
            // would be harmless in the open, and the stamina system would have nothing to
            // push against.
            const float sprintSpeed = 6.4f;

            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.runSpeed, sprintSpeed,
                    $"'{c.id}' can be outrun indefinitely, so it is never a threat in the open.");
            }
        }

        [Test]
        public void Creatures_NoticeFastEnoughToBeDangerousAndSlowlyEnoughToBeFair()
        {
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.secondsToNotice, 0.5f,
                    $"'{c.id}' spots you instantly, which gives no chance to back away.");
                Assert.Less(c.secondsToNotice, 6f,
                    $"'{c.id}' takes so long to notice you that stealth is not a choice.");
            }
        }

        [Test]
        public void Creatures_CannotOneShotAHealthyHunter()
        {
            // 100 max health. A single bite that kills outright leaves no room to react,
            // which reads as unfair rather than frightening.
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Less(c.attackDamage, 100f, $"'{c.id}' kills a full-health hunter in one hit.");
            }
        }

        [Test]
        public void Creatures_GiveAWindupBeforeTheyCommit()
        {
            // The tell is what makes a lunge dodgeable instead of a dice roll.
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.attackWindup, 0.2f, $"'{c.id}' attacks with no readable tell.");
            }
        }

        [Test]
        public void Creatures_SedationIsReachableAndDecays()
        {
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.sedationThreshold, 0f, $"'{c.id}' can never be sedated.");
                Assert.Greater(c.sedationDecay, 0f,
                    $"'{c.id}' never sheds sedation, so a single dart eventually drops it.");
                Assert.Greater(c.collapseSeconds, 10f,
                    $"'{c.id}' wakes too fast to be worth containing.");
            }
        }

        [Test]
        public void Creatures_CaptureIsWorthMoreThanKilling()
        {
            // The whole reason to carry a tranquiliser and a containment case.
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.bountyCapture, c.bountyKill,
                    $"'{c.id}' pays no premium for a live capture.");
            }
        }

        [Test]
        public void Creatures_FleeBeforeTheyDieAndRecoverAbove()
        {
            foreach (var c in Creatures().Entries.Select(e => e.definition))
            {
                Assert.Greater(c.fleeHealthFraction, 0f, $"'{c.id}' fights to the death every time.");
                Assert.Greater(c.recoveredHealthFraction, c.fleeHealthFraction,
                    $"'{c.id}' returns to the fight at the health it fled at, so it ping-pongs.");
            }
        }

        [Test]
        public void Creatures_PackAnimalsHaveRoomToFlank()
        {
            foreach (var c in Creatures().Entries.Select(e => e.definition).Where(c => c.packSize > 1))
            {
                Assert.Greater(c.flankRadius, c.attackRange,
                    $"'{c.id}' flanks inside its own attack range, so the pack just stacks up.");
            }
        }

        // -------------------------------------------------------------------- contracts

        [Test]
        public void Contracts_RegistryIsPopulated()
        {
            Assert.Greater(Contracts().Count, 0, "No contracts; there is nothing to depart for.");
        }

        [Test]
        public void Contracts_ReferenceCreaturesThatExist()
        {
            var known = new HashSet<CreatureDefinition>(Creatures().Entries.Select(e => e.definition));

            foreach (var contract in Contracts().Contracts)
            {
                Assert.IsNotNull(contract.creature, $"'{contract.id}' has no creature.");
                Assert.Contains(contract.creature, known.ToList(),
                    $"'{contract.id}' names a creature that is not in the registry.");

                if (contract.secondaryCount > 0)
                {
                    Assert.IsNotNull(contract.secondaryCreature,
                        $"'{contract.id}' asks for {contract.secondaryCount} of nothing.");
                    Assert.Contains(contract.secondaryCreature, known.ToList(),
                        $"'{contract.id}' names a secondary creature that is not in the registry.");
                }
            }
        }

        [Test]
        public void Contracts_AskForAtLeastOneCreature()
        {
            foreach (var contract in Contracts().Contracts)
            {
                Assert.Greater(contract.count, 0, $"'{contract.id}' spawns nothing.");
            }
        }

        [Test]
        public void Contracts_IdsAreUnique()
        {
            var ids = Contracts().Contracts.Select(c => c.id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void Contracts_HarderTiersPayBetter()
        {
            var byTier = Contracts().Contracts
                .GroupBy(c => c.tier)
                .OrderBy(g => g.Key)
                .Select(g => (Tier: g.Key, Best: g.Max(c => c.rewardMultiplier)))
                .ToList();

            for (var i = 1; i < byTier.Count; i++)
            {
                Assert.GreaterOrEqual(byTier[i].Best, byTier[i - 1].Best,
                    $"Tier {byTier[i].Tier} pays worse than tier {byTier[i - 1].Tier}.");
            }
        }

        [Test]
        public void Contracts_TheEasiestOneIsSafeToStartOn()
        {
            // Whatever the crew's first departure lands on should not be a betrayal
            // contract with a pack of the deadliest creature in the game.
            var first = Contracts().Get(0);
            Assert.AreEqual(1, first.tier, "The first contract in the list is not a tier 1.");
            Assert.AreEqual(0f, first.betrayalChance,
                "The introductory contract can spawn the Collector, which is a poor first impression.");
        }

        [Test]
        public void Contracts_BetrayalOffersAreWorthTakingSeriously()
        {
            foreach (var contract in Contracts().Contracts.Where(c => c.betrayalChance > 0f))
            {
                Assert.Greater(contract.betrayalBonus, 0,
                    $"'{contract.id}' offers a betrayal for no money.");
            }
        }

        // ----------------------------------------------------------------- item tuning

        static IEnumerable<ItemDefinition> LiveItems() => Items().Items.Where(i => i != null);

        [Test]
        public void Items_WeaponsAllHaveABehaviour()
        {
            foreach (var item in LiveItems().Where(i => i.Category == ItemCategory.Weapon))
            {
                Assert.IsNotNull(item.Behaviour, $"'{item.ItemId}' is a weapon that does nothing when used.");
                Assert.IsInstanceOf<WeaponBehaviour>(item.Behaviour, $"'{item.ItemId}' is not wired as a weapon.");
            }
        }

        [Test]
        public void Items_WeaponsHaveSaneAmmoNumbers()
        {
            foreach (var item in LiveItems())
            {
                if (item.BehaviourAs<WeaponBehaviour>() is not { } weapon)
                {
                    continue;
                }

                // Melee has no magazine and never reloads, which is the whole reason to
                // carry it when the ammo runs out.
                if (weapon.fireMode == FireMode.Melee)
                {
                    Assert.AreEqual(0, weapon.magazineSize,
                        $"'{item.ItemId}' is melee but has a magazine, so it can run out.");
                    Assert.IsFalse(weapon.usesAmmoBox,
                        $"'{item.ItemId}' is melee but draws from the ammo box.");
                }
                else
                {
                    Assert.Greater(weapon.magazineSize, 0, $"'{item.ItemId}' has an empty magazine.");
                }

                Assert.Greater(weapon.pellets, 0, $"'{item.ItemId}' fires no projectiles.");
                Assert.Greater(weapon.range, 0f, $"'{item.ItemId}' has no reach.");
                Assert.Greater(weapon.roundsPerMinute, 0f, $"'{item.ItemId}' never fires.");

                // A tranquiliser trades damage for sedation, so it is allowed to be feeble,
                // but it has to actually sedate.
                if (weapon.IsSedative)
                {
                    Assert.Greater(weapon.sedation, 0f, $"'{item.ItemId}' sedates for nothing.");
                }
                else
                {
                    Assert.Greater(weapon.damage, 0f, $"'{item.ItemId}' does no damage.");
                }
            }
        }

        [Test]
        public void Items_LoudWeaponsHitHarderThanQuietOnes()
        {
            var weapons = LiveItems()
                .Select(i => (Item: i, Weapon: i.BehaviourAs<WeaponBehaviour>()))
                .Where(x => x.Weapon != null && !x.Weapon.IsSedative)
                .ToList();

            if (weapons.Count < 2)
            {
                Assert.Pass("Not enough weapons to compare.");
                return;
            }

            // The crossbow is the trade this checks: quiet has to cost you something.
            var quietest = weapons.OrderBy(x => x.Weapon.noiseRadius).First();
            var loudest = weapons.OrderByDescending(x => x.Weapon.noiseRadius).First();

            Assert.Less(quietest.Weapon.damage, loudest.Weapon.damage,
                $"'{quietest.Item.ItemId}' is both the quietest and among the hardest hitting.");
        }

        [Test]
        public void Items_DeployablesAllPointAtAPrefab()
        {
            foreach (var item in LiveItems())
            {
                if (item.BehaviourAs<DeployableBehaviour>() is not { } deployable)
                {
                    continue;
                }

                Assert.IsNotNull(deployable.prefab, $"'{item.ItemId}' deploys nothing.");
                Assert.IsNotNull(deployable.prefab.GetComponent<NetworkObject>(),
                    $"'{item.ItemId}' deploys a prefab the server cannot spawn.");
            }
        }

        [Test]
        public void Items_EveryUsableItemIsUsable()
        {
            // An item the player can select, aim and click with, that has no behaviour, is
            // indistinguishable from a bug.
            var categories = new[]
            {
                ItemCategory.Weapon, ItemCategory.Medical, ItemCategory.Trap,
                ItemCategory.Lighting, ItemCategory.Optics
            };

            foreach (var item in LiveItems().Where(i => categories.Contains(i.Category)))
            {
                Assert.IsNotNull(item.Behaviour,
                    $"'{item.ItemId}' ({item.Category}) has no behaviour and does nothing when used.");
            }
        }

        [Test]
        public void Items_HeldModelsAreStrippedOfColliders()
        {
            // The viewmodel sits centimetres from the camera. A collider on it would trip
            // the player's own shots and the interaction ray.
            foreach (var item in LiveItems().Where(i => i.HeldModel != null))
            {
                Assert.IsEmpty(item.HeldModel.GetComponentsInChildren<Collider>(true),
                    $"The held model for '{item.ItemId}' still has colliders.");
            }
        }
    }
}
