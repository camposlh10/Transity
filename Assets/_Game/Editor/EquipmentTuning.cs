using System;
using System.Collections.Generic;
using Transity.Audio;
using Transity.Inventory;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// What every item does, as numbers. Builds one behaviour asset per item and wires it
    /// -- plus the held model -- onto the item definition.
    ///
    /// This is the balance table for equipment. Art lives in EquipmentCatalog, identity
    /// on the definition, and everything a designer would tune is here.
    /// </summary>
    public static class EquipmentTuning
    {
        const string BehaviourFolder = "Assets/_Game/Data/Behaviours";
        const string HeldFolder = "Assets/_Game/Prefabs/Held";

        /// <summary>Builds and wires behaviours for every item in the registry.</summary>
        public static void ApplyBehaviours(ItemRegistry registry, IReadOnlyDictionary<string, GameObject> deployablePrefabs)
        {
            GrayboxKit.EnsureFolder(BehaviourFolder);
            GrayboxKit.EnsureFolder(HeldFolder);

            var crate = EnsureCrateHeldModel();

            foreach (var definition in registry.Items)
            {
                if (definition == null)
                {
                    continue;
                }

                var behaviour = BuildFor(definition.ItemId, deployablePrefabs);
                var entry = EquipmentCatalog.FindByItemId(definition.ItemId);
                var held = entry != null ? EquipmentImportSettings.LoadModel(entry.Model) : crate;

                GrayboxKit.Wire(definition, ("behaviour", behaviour), ("heldModel", held));

                if (behaviour == null)
                {
                    Debug.LogWarning($"No tuning for '{definition.ItemId}'; it will be inert.");
                }
            }

            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ table

        static ItemBehaviour BuildFor(string itemId, IReadOnlyDictionary<string, GameObject> deployables)
        {
            switch (itemId)
            {
                // ---- sidearms ----
                case "item.hd.fieldpistol":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 22f; w.roundsPerMinute = 300f; w.magazineSize = 12; w.reloadSeconds = 1.6f;
                        w.spreadDegrees = 1.8f; w.aimSpreadDegrees = 0.5f; w.range = 80f; w.recoilKick = 1.6f;
                        w.noiseRadius = 55f; w.fireSound = SoundKind.GunshotLight; w.aimFieldOfView = 55f;
                    });
                case "item.hd.heavyhuntinghandgun":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 55f; w.roundsPerMinute = 150f; w.magazineSize = 6; w.reloadSeconds = 2.4f;
                        w.spreadDegrees = 2.2f; w.aimSpreadDegrees = 0.6f; w.range = 90f; w.recoilKick = 4f;
                        w.noiseRadius = 75f; w.fireSound = SoundKind.GunshotHeavy; w.weakPointMultiplier = 2f; w.aimFieldOfView = 52f;
                    });
                case "item.hd.tranquilizerpistol":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 3f; w.sedation = 28f; w.roundsPerMinute = 120f; w.magazineSize = 5; w.reloadSeconds = 2.2f;
                        w.spreadDegrees = 1.5f; w.aimSpreadDegrees = 0.4f; w.range = 45f; w.recoilKick = 0.8f;
                        w.noiseRadius = 12f; w.fireSound = SoundKind.TranqPuff; w.bleedChance = 0f; w.aimFieldOfView = 55f;
                    });

                // ---- primaries ----
                case "item.hd.pumpshotgun":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 14f; w.pellets = 8; w.roundsPerMinute = 60f; w.magazineSize = 5; w.reloadSeconds = 3f;
                        w.spreadDegrees = 6f; w.aimSpreadDegrees = 4f; w.range = 28f; w.recoilKick = 5f;
                        w.noiseRadius = 85f; w.fireSound = SoundKind.GunshotShotgun; w.knockback = 6f; w.bleedChance = 0.6f; w.aimFieldOfView = 60f;
                    });
                case "item.hd.semiautomaticshotgun":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 12f; w.pellets = 8; w.roundsPerMinute = 150f; w.magazineSize = 6; w.reloadSeconds = 2.6f;
                        w.spreadDegrees = 6.5f; w.aimSpreadDegrees = 4.5f; w.range = 26f; w.recoilKick = 4f;
                        w.noiseRadius = 85f; w.fireSound = SoundKind.GunshotShotgun; w.knockback = 4f; w.bleedChance = 0.5f; w.aimFieldOfView = 60f;
                    });
                case "item.hd.compactcarbine":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 24f; w.automatic = true; w.roundsPerMinute = 520f; w.magazineSize = 30; w.reloadSeconds = 2.2f;
                        w.spreadDegrees = 2.5f; w.aimSpreadDegrees = 0.8f; w.range = 120f; w.recoilKick = 1.3f;
                        w.noiseRadius = 70f; w.fireSound = SoundKind.GunshotLight; w.aimFieldOfView = 50f;
                    });
                case "item.hd.biggamerifle":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 150f; w.roundsPerMinute = 40f; w.magazineSize = 4; w.reloadSeconds = 3.4f;
                        w.spreadDegrees = 1.2f; w.aimSpreadDegrees = 0.15f; w.range = 250f; w.recoilKick = 7f;
                        w.noiseRadius = 110f; w.fireSound = SoundKind.GunshotHeavy; w.weakPointMultiplier = 3.5f;
                        w.aimFieldOfView = 32f; w.bleedChance = 0.9f;
                    });
                case "item.hd.huntercrossbow":
                    return Weapon(itemId, w =>
                    {
                        w.damage = 70f; w.roundsPerMinute = 45f; w.magazineSize = 1; w.reloadSeconds = 2.4f; w.usesAmmoBox = false;
                        w.spreadDegrees = 0.8f; w.aimSpreadDegrees = 0.2f; w.range = 70f; w.recoilKick = 1f;
                        w.noiseRadius = 8f; w.fireSound = SoundKind.CrossbowTwang; w.weakPointMultiplier = 3f; w.bleedChance = 0.8f; w.aimFieldOfView = 45f;
                    });
                case "item.hd.rescuehatchet":
                    return Weapon(itemId, w =>
                    {
                        w.fireMode = FireMode.Melee; w.damage = 45f; w.roundsPerMinute = 80f; w.magazineSize = 0; w.usesAmmoBox = false;
                        w.range = 2.2f; w.noiseRadius = 14f; w.fireSound = SoundKind.MeleeSwing; w.bleedChance = 0.7f; w.knockback = 2f;
                        w.recoilKick = 0.5f; w.aimFieldOfView = 0f;
                    });

                // ---- medical ----
                case "item.hd.medicalkit":
                    return Consumable(itemId, c => { c.healAmount = 50f; c.stopsBleeding = true; c.useSeconds = 3f; c.charges = 2; });
                case "item.hd.traumakit":
                    return Consumable(itemId, c => { c.healAmount = 100f; c.stopsBleeding = true; c.useSeconds = 5f; c.charges = 1; c.revivesOthers = true; });
                case "item.hd.adrenalineinjector":
                    return Consumable(itemId, c =>
                    {
                        c.healAmount = 0f; c.stopsBleeding = false; c.adrenalineSeconds = 20f; c.restoresStamina = true;
                        c.useSeconds = 1.2f; c.charges = 1; c.canUseAtFullHealth = true;
                    });
                case "item.hd.scentneutralizerspray":
                    return Consumable(itemId, c =>
                    {
                        c.healAmount = 0f; c.stopsBleeding = false; c.maskSeconds = 60f; c.useSeconds = 1.5f; c.charges = 3; c.canUseAtFullHealth = true;
                    });

                // ---- deployables ----
                case "item.hd.motionsensoralarm":
                    return Deployable(itemId, deployables, "item.hd.motionsensoralarm", d => { d.placeDistance = 1.8f; });
                case "item.hd.creaturebaitcanister":
                    return Deployable(itemId, deployables, "item.hd.creaturebaitcanister", d => { d.placeDistance = 2f; });
                case "item.hd.glowstick":
                    return Deployable(itemId, deployables, "item.hd.glowstick", d => { d.throwSpeed = 9f; d.placeOnGround = false; });
                case "item.beartrap":
                    return Deployable(itemId, deployables, "item.beartrap", d => { d.placeDistance = 1.6f; });

                // ---- lights and optics ----
                case "item.hd.basicflashlight":
                    return Toggle(itemId, t => { t.kind = ToggleKind.Flashlight; t.batterySeconds = 480f; t.beamRange = 18f; t.beamAngle = 45f; t.intensity = 700f; t.visibilityMultiplier = 1.5f; });
                case "item.hd.heavyflashlight":
                    return Toggle(itemId, t => { t.kind = ToggleKind.Flashlight; t.batterySeconds = 300f; t.beamRange = 30f; t.beamAngle = 52f; t.intensity = 1400f; t.visibilityMultiplier = 1.9f; });
                case "item.hd.uvtrackinglight":
                    return Toggle(itemId, t => { t.kind = ToggleKind.UltraViolet; t.batterySeconds = 360f; t.beamRange = 12f; t.beamAngle = 40f; t.intensity = 400f; t.visibilityMultiplier = 1.2f; });
                case "item.hd.nightvisiongoggles":
                    return Toggle(itemId, t => { t.kind = ToggleKind.NightVision; t.batterySeconds = 420f; t.visibilityMultiplier = 1f; t.nightVisionGain = 2.2f; });
                case "item.hd.thermalmonocular":
                    return Toggle(itemId, t => { t.kind = ToggleKind.Thermal; t.batterySeconds = 300f; t.visibilityMultiplier = 1f; t.thermalRange = 45f; });

                // ---- worn ----
                case "item.hd.lighthuntervest":
                    return Passive(itemId, p => { p.damageTakenMultiplier = 0.8f; p.speedMultiplier = 0.96f; p.noiseMultiplier = 1.1f; });
                case "item.hd.heavyhuntervest":
                    return Passive(itemId, p => { p.damageTakenMultiplier = 0.55f; p.speedMultiplier = 0.85f; p.noiseMultiplier = 1.5f; });
                case "item.hd.hunterbodycamera":
                    return Passive(itemId, p => { p.bountyMultiplier = 1.25f; });
                case "item.hd.fieldrespirator":
                    return Passive(itemId, p => { p.filtersAir = true; });
                case "item.hd.standardhunterbackpack":
                case "item.hd.fieldrepairkit":
                case "item.containmentcase":
                    return Passive(itemId, _ => { });

                // ---- ammunition ----
                case "item.ammo":
                    return Build<AmmoBehaviour>(itemId, a => { a.roundsPerBox = 30; });

                default:
                    return null;
            }
        }

        // ------------------------------------------------------------------ helpers

        static T Build<T>(string itemId, Action<T> configure) where T : ItemBehaviour
        {
            var safe = itemId.Replace("item.", string.Empty).Replace('.', '_');
            var path = $"{BehaviourFolder}/{typeof(T).Name.Replace("Behaviour", string.Empty)}_{safe}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static WeaponBehaviour Weapon(string itemId, Action<WeaponBehaviour> configure) =>
            Build<WeaponBehaviour>(itemId, w =>
            {
                // Defaults every weapon starts from; the table above overrides.
                w.fireMode = FireMode.Hitscan; w.automatic = false; w.pellets = 1; w.usesAmmoBox = true;
                w.weakPointMultiplier = 1.5f; w.sedation = 0f; w.bleedChance = 0.25f; w.knockback = 0f;
                w.recoilRecoverySpeed = 8f;
                configure(w);
            });

        static ConsumableBehaviour Consumable(string itemId, Action<ConsumableBehaviour> configure) =>
            Build<ConsumableBehaviour>(itemId, c =>
            {
                c.adrenalineSeconds = 0f; c.restoresStamina = false; c.canUseAtFullHealth = false; c.maskSeconds = 0f; c.revivesOthers = false;
                configure(c);
            });

        static DeployableBehaviour Deployable(string itemId, IReadOnlyDictionary<string, GameObject> prefabs, string prefabKey,
            Action<DeployableBehaviour> configure) =>
            Build<DeployableBehaviour>(itemId, d =>
            {
                d.prefab = prefabs != null && prefabs.TryGetValue(prefabKey, out var prefab) ? prefab : null;
                d.placeOnGround = true; d.throwSpeed = 0f; d.charges = 1;
                configure(d);

                if (d.prefab == null)
                {
                    Debug.LogWarning($"No deployable prefab for '{itemId}'.");
                }
            });

        static ToggleBehaviour Toggle(string itemId, Action<ToggleBehaviour> configure) =>
            Build<ToggleBehaviour>(itemId, t =>
            {
                t.color = new Color(1f, 0.93f, 0.8f);
                configure(t);
            });

        static PassiveBehaviour Passive(string itemId, Action<PassiveBehaviour> configure) =>
            Build<PassiveBehaviour>(itemId, p =>
            {
                p.damageTakenMultiplier = 1f; p.speedMultiplier = 1f; p.noiseMultiplier = 1f; p.bountyMultiplier = 1f; p.filtersAir = false;
                configure(p);
            });

        /// <summary>The orange crate as a plain visual, for items with no real model.</summary>
        static GameObject EnsureCrateHeldModel()
        {
            var path = $"{HeldFolder}/Held_Crate.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Held_Crate";
            crate.transform.localScale = new Vector3(0.2f, 0.13f, 0.3f);
            UnityEngine.Object.DestroyImmediate(crate.GetComponent<Collider>());
            GrayboxKit.Paint(crate, GrayboxKit.SolidMaterial("GB_Item", new Color(0.85f, 0.65f, 0.25f)));

            var prefab = PrefabUtility.SaveAsPrefabAsset(crate, path);
            UnityEngine.Object.DestroyImmediate(crate);
            return prefab;
        }
    }
}
