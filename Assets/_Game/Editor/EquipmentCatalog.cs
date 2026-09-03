using System.Collections.Generic;
using Transity.Inventory;

namespace Transity.EditorTools
{
    /// <summary>
    /// The Hunter Depot equipment collection, as it lands in the game.
    ///
    /// Generated from the art drop's manifests, so the shop list, the item assets and
    /// the meshes on disk all come from one table. Prices, stash limits and shop aisles
    /// are balance rather than art, so those are set here and survive a re-import.
    ///
    /// Colliders are deliberately one primitive per item even where the art manifest
    /// asks for several: these are pickups, and a single fitted box or capsule is what
    /// the interaction ray needs. Revisit if equipment ever becomes physics debris.
    /// </summary>
    public static class EquipmentCatalog
    {
        public sealed class Entry
        {
            public string Model;
            public string ItemId;
            public string DisplayName;
            public EquipmentSlot Slot;
            public ItemCategory Category;
            public int Price;
            public int StashLimit;
            public bool AtRisk;
            public ColliderKind Collider;
            public string Atlas;
            public string[] EmissiveMaterials;
            public string Tradeoff;
        }

        public enum ColliderKind { Box, Capsule }

        public static readonly IReadOnlyList<Entry> Entries = new[]
        {
            // 4292 tris, 0.08 x 0.21 x 0.07 m -- shorter beam; reliable and inexpensive
            new Entry
            {
                Model = "HD_BasicFlashlight",
                ItemId = "item.hd.basicflashlight",
                DisplayName = "Basic Flashlight",
                Slot = EquipmentSlot.Flashlight,
                Category = ItemCategory.Lighting,
                Price = 0,
                StashLimit = 20,
                AtRisk = false,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_BasicFlashlight_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_BASIC_FLASHLIGHT_GLASS_INDICATOR" },
                Tradeoff = "shorter beam; reliable and inexpensive"
            },
            // 4100 tris, 0.10 x 0.29 x 0.14 m -- strong beam; heavier and faster battery drain
            new Entry
            {
                Model = "HD_HeavyFlashlight",
                ItemId = "item.hd.heavyflashlight",
                DisplayName = "Heavy Flashlight",
                Slot = EquipmentSlot.Flashlight,
                Category = ItemCategory.Lighting,
                Price = 180,
                StashLimit = 20,
                AtRisk = true,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_HeavyFlashlight_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_HEAVY_FLASHLIGHT_GLASS_INDICATOR" },
                Tradeoff = "strong beam; heavier and faster battery drain"
            },
            // 852 tris, 0.03 x 0.03 x 0.16 m -- silent area marker; visible to hunters and creatures
            new Entry
            {
                Model = "HD_GlowStick",
                ItemId = "item.hd.glowstick",
                DisplayName = "Glow Stick",
                Slot = EquipmentSlot.Utility,
                Category = ItemCategory.Lighting,
                Price = 25,
                StashLimit = 99,
                AtRisk = true,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_GlowStick_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_GLOW_STICK_GLASS_INDICATOR" },
                Tradeoff = "silent area marker; visible to hunters and creatures"
            },
            // 1424 tris, 0.22 x 0.16 x 0.10 m -- darkness visibility; blooms under bright light
            new Entry
            {
                Model = "HD_NightVisionGoggles",
                ItemId = "item.hd.nightvisiongoggles",
                DisplayName = "Night-Vision Goggles",
                Slot = EquipmentSlot.Vision,
                Category = ItemCategory.Optics,
                Price = 520,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_NightVisionGoggles_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_NIGHT_VISION_GOGGLES_GLASS_INDICATOR" },
                Tradeoff = "darkness visibility; blooms under bright light"
            },
            // 1092 tris, 0.12 x 0.19 x 0.09 m -- detects heat; occupies one hand and has narrow field of view
            new Entry
            {
                Model = "HD_ThermalMonocular",
                ItemId = "item.hd.thermalmonocular",
                DisplayName = "Thermal Monocular",
                Slot = EquipmentSlot.Vision,
                Category = ItemCategory.Optics,
                Price = 640,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_ThermalMonocular_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_THERMAL_MONOCULAR_GLASS_INDICATOR" },
                Tradeoff = "detects heat; occupies one hand and has narrow field of view"
            },
            // 540 tris, 0.07 x 0.07 x 0.19 m -- reveals biological traces; poor general illumination
            new Entry
            {
                Model = "HD_UVTrackingLight",
                ItemId = "item.hd.uvtrackinglight",
                DisplayName = "UV Tracking Light",
                Slot = EquipmentSlot.Flashlight,
                Category = ItemCategory.Lighting,
                Price = 210,
                StashLimit = 20,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_UVTrackingLight_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_UV_TRACKING_LIGHT_GLASS_INDICATOR" },
                Tradeoff = "reveals biological traces; poor general illumination"
            },
            // 1968 tris, 0.15 x 0.15 x 0.26 m -- draws the target; can also attract unwanted wildlife
            new Entry
            {
                Model = "HD_CreatureBaitCanister",
                ItemId = "item.hd.creaturebaitcanister",
                DisplayName = "Creature Bait Canister",
                Slot = EquipmentSlot.Utility,
                Category = ItemCategory.Gadget,
                Price = 90,
                StashLimit = 30,
                AtRisk = true,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_CreatureBaitCanister_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "draws the target; can also attract unwanted wildlife"
            },
            // 1208 tris, 0.15 x 0.09 x 0.20 m -- warns of movement; false triggers from players and wildlife
            new Entry
            {
                Model = "HD_MotionSensorAlarm",
                ItemId = "item.hd.motionsensoralarm",
                DisplayName = "Motion Sensor Alarm",
                Slot = EquipmentSlot.Utility,
                Category = ItemCategory.Trap,
                Price = 150,
                StashLimit = 30,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_MotionSensorAlarm_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_MOTION_SENSOR_ALARM_GLASS_INDICATOR" },
                Tradeoff = "warns of movement; false triggers from players and wildlife"
            },
            // 628 tris, 0.06 x 0.06 x 0.19 m -- reduces scent trail temporarily; rain shortens its duration
            new Entry
            {
                Model = "HD_ScentNeutralizerSpray",
                ItemId = "item.hd.scentneutralizerspray",
                DisplayName = "Scent Neutralizer Spray",
                Slot = EquipmentSlot.Utility,
                Category = ItemCategory.Gadget,
                Price = 110,
                StashLimit = 30,
                AtRisk = true,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_ScentNeutralizerSpray_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "reduces scent trail temporarily; rain shortens its duration"
            },
            // 1476 tris, 0.32 x 0.14 x 0.29 m -- restores health and stops bleeding; slow use animation
            new Entry
            {
                Model = "HD_MedicalKit",
                ItemId = "item.hd.medicalkit",
                DisplayName = "Medical Kit",
                Slot = EquipmentSlot.Medical,
                Category = ItemCategory.Medical,
                Price = 120,
                StashLimit = 20,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_MedicalKit_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "restores health and stops bleeding; slow use animation"
            },
            // 1168 tris, 0.05 x 0.05 x 0.16 m -- temporary speed and stamina; does not restore health
            new Entry
            {
                Model = "HD_AdrenalineInjector",
                ItemId = "item.hd.adrenalineinjector",
                DisplayName = "Adrenaline Injector",
                Slot = EquipmentSlot.Medical,
                Category = ItemCategory.Medical,
                Price = 95,
                StashLimit = 20,
                AtRisk = true,
                Collider = ColliderKind.Capsule,
                Atlas = "HD_AdrenalineInjector_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "temporary speed and stamina; does not restore health"
            },
            // 1096 tris, 0.27 x 0.18 x 0.17 m -- protects from spores and gas; narrows breathing audio and vision
            new Entry
            {
                Model = "HD_FieldRespirator",
                ItemId = "item.hd.fieldrespirator",
                DisplayName = "Field Respirator",
                Slot = EquipmentSlot.Medical,
                Category = ItemCategory.Medical,
                Price = 260,
                StashLimit = 10,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_FieldRespirator_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "protects from spores and gas; narrows breathing audio and vision"
            },
            // 1692 tris, 0.60 x 0.20 x 0.36 m -- revives or stabilizes critical injuries; bulky and single-use
            new Entry
            {
                Model = "HD_TraumaKit",
                ItemId = "item.hd.traumakit",
                DisplayName = "Trauma Kit",
                Slot = EquipmentSlot.Medical,
                Category = ItemCategory.Medical,
                Price = 300,
                StashLimit = 10,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_TraumaKit_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "revives or stabilizes critical injuries; bulky and single-use"
            },
            // 972 tris, 0.68 x 0.31 x 0.59 m -- moderate protection with minimal movement penalty
            new Entry
            {
                Model = "HD_LightHunterVest",
                ItemId = "item.hd.lighthuntervest",
                DisplayName = "Light Hunter Vest",
                Slot = EquipmentSlot.Armor,
                Category = ItemCategory.Armor,
                Price = 340,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_LightHunterVest_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "moderate protection with minimal movement penalty"
            },
            // 1512 tris, 0.68 x 0.32 x 0.67 m -- strong protection; slower movement and increased noise
            new Entry
            {
                Model = "HD_HeavyHunterVest",
                ItemId = "item.hd.heavyhuntervest",
                DisplayName = "Heavy Hunter Vest",
                Slot = EquipmentSlot.Armor,
                Category = ItemCategory.Armor,
                Price = 580,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_HeavyHunterVest_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "strong protection; slower movement and increased noise"
            },
            // 2028 tris, 0.06 x 0.24 x 0.25 m -- builds sedation over time; low immediate stopping power
            new Entry
            {
                Model = "HD_TranquilizerPistol",
                ItemId = "item.hd.tranquilizerpistol",
                DisplayName = "Tranquilizer Pistol",
                Slot = EquipmentSlot.Sidearm,
                Category = ItemCategory.Weapon,
                Price = 430,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_TranquilizerPistol_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_TRANQUILIZER_PISTOL_GLASS_INDICATOR" },
                Tradeoff = "builds sedation over time; low immediate stopping power"
            },
            // 1584 tris, 0.38 x 0.19 x 0.33 m -- repairs electronics and generators; heavy with limited charges
            new Entry
            {
                Model = "HD_FieldRepairKit",
                ItemId = "item.hd.fieldrepairkit",
                DisplayName = "Field Repair Kit",
                Slot = EquipmentSlot.Utility,
                Category = ItemCategory.Gadget,
                Price = 200,
                StashLimit = 10,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_FieldRepairKit_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "repairs electronics and generators; heavy with limited charges"
            },
            // 1172 tris, 0.05 x 0.06 x 0.10 m -- adds bounty evidence only when the camera is extracted
            new Entry
            {
                Model = "HD_HunterBodyCamera",
                ItemId = "item.hd.hunterbodycamera",
                DisplayName = "Hunter Body Camera",
                Slot = EquipmentSlot.Accessory,
                Category = ItemCategory.Gear,
                Price = 275,
                StashLimit = 10,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_HunterBodyCamera_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_HUNTER_BODY_CAMERA_GLASS_INDICATOR" },
                Tradeoff = "adds bounty evidence only when the camera is extracted"
            },
            // 1476 tris, 0.56 x 0.34 x 0.56 m -- standard capacity with balanced weight and noise
            new Entry
            {
                Model = "HD_StandardHunterBackpack",
                ItemId = "item.hd.standardhunterbackpack",
                DisplayName = "Standard Hunter Backpack",
                Slot = EquipmentSlot.Backpack,
                Category = ItemCategory.Gear,
                Price = 0,
                StashLimit = 3,
                AtRisk = false,
                Collider = ColliderKind.Box,
                Atlas = "HD_StandardHunterBackpack_Atlas_Albedo.png",
                EmissiveMaterials = System.Array.Empty<string>(),
                Tradeoff = "standard capacity with balanced weight and noise"
            },
            // 1640 tris, 0.06 x 0.21 x 0.24 m -- reliable and inexpensive; low creature stopping power
            new Entry
            {
                Model = "HD_FieldPistol",
                ItemId = "item.hd.fieldpistol",
                DisplayName = "9mm Field Pistol",
                Slot = EquipmentSlot.Sidearm,
                Category = ItemCategory.Weapon,
                Price = 250,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_FieldPistol_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_FIELD_PISTOL_GLASS_INDICATOR" },
                Tradeoff = "reliable and inexpensive; low creature stopping power"
            },
            // 1936 tris, 0.06 x 0.26 x 0.25 m -- strong damage; severe recoil and expensive ammunition
            new Entry
            {
                Model = "HD_HeavyHuntingHandgun",
                ItemId = "item.hd.heavyhuntinghandgun",
                DisplayName = "Heavy Hunting Handgun",
                Slot = EquipmentSlot.Sidearm,
                Category = ItemCategory.Weapon,
                Price = 610,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_HeavyHuntingHandgun_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_HEAVY_HUNTING_HANDGUN_GLASS_INDICATOR" },
                Tradeoff = "strong damage; severe recoil and expensive ammunition"
            },
            // 2272 tris, 0.24 x 0.98 x 0.30 m -- close-range burst damage; loud and slow to reload
            new Entry
            {
                Model = "HD_PumpShotgun",
                ItemId = "item.hd.pumpshotgun",
                DisplayName = "Pump Shotgun",
                Slot = EquipmentSlot.Primary,
                Category = ItemCategory.Weapon,
                Price = 480,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_PumpShotgun_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_PUMP_SHOTGUN_GLASS_INDICATOR" },
                Tradeoff = "close-range burst damage; loud and slow to reload"
            },
            // 2380 tris, 0.27 x 0.96 x 0.30 m -- fast follow-up shots; expensive and ammunition-hungry
            new Entry
            {
                Model = "HD_SemiAutomaticShotgun",
                ItemId = "item.hd.semiautomaticshotgun",
                DisplayName = "Semi-Automatic Shotgun",
                Slot = EquipmentSlot.Primary,
                Category = ItemCategory.Weapon,
                Price = 720,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_SemiAutomaticShotgun_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_SEMI_AUTOMATIC_SHOTGUN_GLASS_INDICATOR" },
                Tradeoff = "fast follow-up shots; expensive and ammunition-hungry"
            },
            // 2464 tris, 0.24 x 0.78 x 0.42 m -- balanced general-purpose weapon; no major specialization
            new Entry
            {
                Model = "HD_CompactCarbine",
                ItemId = "item.hd.compactcarbine",
                DisplayName = "Compact Carbine",
                Slot = EquipmentSlot.Primary,
                Category = ItemCategory.Weapon,
                Price = 540,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_CompactCarbine_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_COMPACT_CARBINE_GLASS_INDICATOR" },
                Tradeoff = "balanced general-purpose weapon; no major specialization"
            },
            // 2684 tris, 0.24 x 1.10 x 0.41 m -- extreme weak-point damage; very heavy and extremely loud
            new Entry
            {
                Model = "HD_BigGameRifle",
                ItemId = "item.hd.biggamerifle",
                DisplayName = "Big-Game Rifle",
                Slot = EquipmentSlot.Primary,
                Category = ItemCategory.Weapon,
                Price = 880,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_BigGameRifle_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_BIG_GAME_RIFLE_GLASS_INDICATOR" },
                Tradeoff = "extreme weak-point damage; very heavy and extremely loud"
            },
            // 2028 tris, 0.71 x 0.81 x 0.30 m -- quiet with recoverable bolts; slow reload and precise aim required
            new Entry
            {
                Model = "HD_HunterCrossbow",
                ItemId = "item.hd.huntercrossbow",
                DisplayName = "Hunter Crossbow",
                Slot = EquipmentSlot.Primary,
                Category = ItemCategory.Weapon,
                Price = 495,
                StashLimit = 5,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_HunterCrossbow_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_HUNTER_CROSSBOW_GLASS_INDICATOR" },
                Tradeoff = "quiet with recoverable bolts; slow reload and precise aim required"
            },
            // 1120 tris, 0.21 x 0.20 x 0.54 m -- requires no ammunition; extremely dangerous against creatures
            new Entry
            {
                Model = "HD_RescueHatchet",
                ItemId = "item.hd.rescuehatchet",
                DisplayName = "Rescue Hatchet",
                Slot = EquipmentSlot.Tool,
                Category = ItemCategory.Tool,
                Price = 70,
                StashLimit = 10,
                AtRisk = true,
                Collider = ColliderKind.Box,
                Atlas = "HD_RescueHatchet_Atlas_Albedo.png",
                EmissiveMaterials = new[] { "HD_MAT_RESCUE_HATCHET_GLASS_INDICATOR" },
                Tradeoff = "requires no ammunition; extremely dangerous against creatures"
            },
        };

        /// <summary>Looks an entry up by model name, e.g. "HD_BasicFlashlight".</summary>
        public static Entry Find(string model)
        {
            foreach (var entry in Entries)
            {
                if (entry.Model == model)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>Null for the graybox items, which are not part of this drop.</summary>
        public static Entry FindByItemId(string itemId)
        {
            foreach (var entry in Entries)
            {
                if (entry.ItemId == itemId)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
