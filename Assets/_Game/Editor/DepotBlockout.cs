using System.Collections.Generic;
using System.Linq;
using Transity.Interaction;
using Transity.Inventory;
using Transity.Player;
using Transity.Train;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Assembles the depot lobby from the authored kit in Art/Models.
    ///
    /// The kit was built to the environment spec, so the pieces drop onto the layout grid
    /// without fudging: HD_WALL_4M is 4.00 x 7.00 x 0.25, HD_STEEL_COLUMN is 7.00 tall,
    /// HD_CENTRAL_TABLE is 4.20 x 1.80. Nothing here scales a mesh -- if a piece does not
    /// fit, the layout moves, not the art.
    ///
    /// Each kit piece stays its own prefab instance, so re-exporting from Blender updates
    /// the room with no code change.
    /// </summary>
    public static class DepotBlockout
    {
        // ---- room, in metres -----------------------------------------------------
        const float RoomWidth = 24f;
        const float RoomDepth = 18f;
        const float RoomHeight = 7f;
        const float WallThickness = 0.25f;

        const float HalfWidth = RoomWidth * 0.5f;   // +/- 12
        const float HalfDepth = RoomDepth * 0.5f;   // +/- 9

        // ---- kit measurements, taken from the exported FBXs ----------------------
        const float WallModule4 = 4f;
        const float WallModule2 = 2f;
        const float FloorTile = 2f;
        const float FloorTileHeight = 0.16f;
        const float ColumnWidth = 0.72f;
        const float BeamHeight = 0.62f;
        const float TrussSpan = 8.1f;
        const float TrussHeight = 2.25f;
        const float ArchWidth = 5.29f;
        const float PendantHeight = 1.76f;

        // Openings. The arch centre sits on the 1 m grid; wall runs either side are tiled
        // from the corners inward, and the final module is pushed flush against the opening
        // so any slack becomes hidden overlap rather than a visible gap.
        const float ArchCentre = 5f;
        const float DoorModuleMin = -8f;
        const float DoorModuleMax = -6f;

        static float ArchMin => ArchCentre - ArchWidth * 0.5f;
        static float ArchMax => ArchCentre + ArchWidth * 0.5f;

        const string KitFolder = "Assets/_Game/Art/Models";

        // ---- kit asset names -----------------------------------------------------
        const string Wall4 = "HD_WALL_4M";
        const string Wall2 = "HD_WALL_2M";
        const string Floor2 = "HD_FLOOR_2M";
        const string Column = "HD_STEEL_COLUMN";
        const string Beam4 = "HD_STEEL_BEAM_4M";
        const string Truss8 = "HD_ROOF_TRUSS_8M";
        const string Arch = "HD_TRAIN_ARCH_5M";
        const string TrainCar = "HD_TRAIN_CAR";
        const string Fireplace = "HD_FIREPLACE";
        const string Stairs = "HD_STAIRS_1_5M";
        const string Railing4 = "HD_RAILING_4M";
        const string WoodInsert = "HD_WOOD_INSERT_2M";
        const string Pendant = "HD_PENDANT_LIGHT";
        const string PlatformCrate = "HD_PLATFORM_CRATE";
        const string MissionStation = "HD_MISSION_STATION";
        const string LoadoutStation = "HD_LOADOUT_STATION";
        const string TrophyGallery = "HD_TROPHY_GALLERY";
        const string WardrobeStation = "HD_WARDROBE_STATION";
        const string TavernCounter = "HD_TAVERN_COUNTER";
        const string CentralTable = "HD_CENTRAL_TABLE";
        const string HunterChair = "HD_HUNTER_CHAIR";
        const string BarStool = "HD_BAR_STOOL";
        const string RugLarge = "HD_RUG_LARGE";

        // ---- batch two ------------------------------------------------------------
        const string WindowWide = "HD2_WINDOW_DEPOT_WIDE";
        const string WindowNarrow = "HD2_WINDOW_DEPOT_NARROW";
        const string WindowTrainArch = "HD2_WINDOW_TRAIN_ARCH";
        const string LightCageSconce = "HD2_LIGHT_CAGE_SCONCE";
        const string LightLodgeBox = "HD2_LIGHT_LODGE_BOX";
        const string LightPlatformCage = "HD2_LIGHT_PLATFORM_CAGE";
        const string LightReadingCone = "HD2_LIGHT_READING_CONE";
        const string ScreenMissionDual = "HD2_SCREEN_MISSION_DUAL";
        const string ScreenWallStatus = "HD2_SCREEN_WALL_STATUS";
        const string ScreenMissionDesk = "HD2_SCREEN_MISSION_DESK";
        const string ScreenFieldTablet = "HD2_SCREEN_FIELD_TABLET";
        const string MapForestRail = "HD2_MAP_FOREST_RAIL";
        const string MapMarsh = "HD2_MAP_MARSH";
        const string MapMountainQuarry = "HD2_MAP_MOUNTAIN_QUARRY";
        const string MapIndustrialDistrict = "HD2_MAP_INDUSTRIAL_DISTRICT";
        const string MapBountyRoutes = "HD2_MAP_BOUNTY_ROUTES";
        const string MapFoldedField = "HD2_MAP_FOLDED_FIELD";
        const string WeaponHeavyRifle = "HD2_WEAPON_HEAVY_RIFLE";
        const string WeaponScoutRifle = "HD2_WEAPON_SCOUT_RIFLE";
        const string WeaponFieldShotgun = "HD2_WEAPON_FIELD_SHOTGUN";
        const string WeaponDartLauncher = "HD2_WEAPON_TRACKING_DART_LAUNCHER";

        static readonly List<string> MissingAssets = new();

        /// <summary>
        /// The only objects a rebuild is allowed to destroy. Anything else in the scene is
        /// hand-authored and must survive. Keep this in step with the roots created below.
        /// </summary>
        public static readonly string[] GeneratedRoots =
        {
            "ENV_Depot",
            "Stations",
            "Gameplay",
            "Interior Lights",
            "Probes",
            "Directional Light",
            "ENV_ScaleReference_1m8"
        };

        /// <summary>Where hand-made additions live so a rebuild leaves them alone.</summary>
        public const string HandmadeRoot = "_Handmade";

        // Layer 6/7, added to TagManager. Putting interactables on their own layer means the
        // interaction cast tests a handful of colliders instead of the whole room, and can
        // no longer be blocked by the player's own capsule.
        const int InteractableLayer = 6;
        const int PlayerLayer = 7;

        /// <summary>Builds the depot into the currently open scene.</summary>
        public static void Build(ItemRegistry registry)
        {
            MissingAssets.Clear();

            var shell = new GameObject("ENV_Depot");
            BuildFloor(shell.transform);
            BuildRoof(shell.transform);
            BuildWalls(shell.transform);
            BuildWindows(shell.transform);
            BuildStructure(shell.transform);
            BuildPlatform(shell.transform);
            BuildFireplace(shell.transform);
            BuildStairs(shell.transform);
            GrayboxKit.MarkStatic(shell);

            BuildLighting();
            BuildProbes();
            BuildStations();
            DepotAtmosphere.Build(shell.transform,
                new Vector3(-1f, 0.55f, HalfDepth - 1.1f),
                new Vector3(ArchCentre - 2.5f, 0.2f, HalfDepth + 3.5f),
                RoomWidth, RoomDepth, RoomHeight);

            var gameplay = BuildGameplayFixtures();
            PlaceLooseItems(registry, gameplay.transform);
            BuildScaleReference();

            ApplyLightingSettings();

            if (MissingAssets.Count > 0)
            {
                Debug.LogWarning($"Depot kit pieces missing from {KitFolder}: " +
                                 string.Join(", ", MissingAssets.Distinct()));
            }
        }

        // ------------------------------------------------------------------- kit access

        static GameObject LoadKit(string assetName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitFolder}/{assetName}.fbx");
            if (prefab == null)
            {
                MissingAssets.Add(assetName);
            }

            return prefab;
        }

        /// <summary>
        /// Places one kit piece. Origins are bottom-centre, so <paramref name="position"/> is
        /// where the piece stands on the floor.
        /// </summary>
        static GameObject Place(string assetName, Transform parent, Vector3 position,
            float yaw = 0f, bool collide = false, string rename = null)
        {
            var prefab = LoadKit(assetName);
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            if (!string.IsNullOrEmpty(rename))
            {
                instance.name = rename;
            }

            if (collide)
            {
                AddBoxCollider(instance);
            }

            return instance;
        }

        /// <summary>
        /// Wraps a piece in one box collider sized to its renderers. The spec calls for
        /// simple boxes; a mesh collider per kit part would be wasted physics.
        /// </summary>
        /// <summary>
        /// Fits one box collider to a piece, measured in the piece's own local space.
        ///
        /// The previous version measured world-space renderer bounds and converted the size
        /// back, which is only correct when the object is axis-aligned. Anything placed at an
        /// angle -- the platform crates, the angled maps -- got a collider inflated to its
        /// world AABB, so players bumped into empty air beside them. Transforming each mesh's
        /// corners into local space first gives a tight box at any rotation.
        /// </summary>
        static void AddBoxCollider(GameObject instance)
        {
            var filters = instance.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0)
            {
                return;
            }

            var toLocal = instance.transform.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var found = false;

            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var bounds = mesh.bounds;
                var toInstance = toLocal * filter.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));

                    var local = toInstance.MultiplyPoint3x4(point);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    found = true;
                }
            }

            if (!found)
            {
                return;
            }

            var collider = instance.AddComponent<BoxCollider>();
            collider.center = (min + max) * 0.5f;
            collider.size = max - min;
        }

        /// <summary>Applies a layer to a whole hierarchy.</summary>
        static void SetLayer(GameObject go, int layer)
        {
            if (go == null)
            {
                return;
            }

            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }

        // ------------------------------------------------------------------ architecture

        static void BuildFloor(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Floor", parent, Vector3.zero);

            // Tiles are 0.16 thick with a bottom-centre origin, so they are dropped by that
            // amount and the walkable surface lands exactly on y = 0.
            for (var x = -HalfWidth; x < HalfWidth; x += FloorTile)
            {
                for (var z = -HalfDepth; z < HalfDepth; z += FloorTile)
                {
                    Place(Floor2, root.transform,
                        new Vector3(x + FloorTile * 0.5f, -FloorTileHeight, z + FloorTile * 0.5f),
                        rename: $"ENV_Floor_{x:0}_{z:0}");
                }
            }

            // One flat collider rather than 108 box colliders.
            var ground = new GameObject("ENV_Depot_FloorCollider");
            ground.transform.SetParent(root.transform, false);
            var box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -FloorTileHeight * 0.5f, 0f);
            box.size = new Vector3(RoomWidth, FloorTileHeight, RoomDepth);
        }

        /// <summary>
        /// Ceiling, tiled from the same 2 m floor pieces. It closes the room off from the
        /// directional light, which is exactly why the interior fixtures below have to be
        /// real lights rather than baked ones.
        /// </summary>
        static void BuildRoof(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Roof", parent, Vector3.zero);

            for (var x = -HalfWidth; x < HalfWidth; x += FloorTile)
            {
                for (var z = -HalfDepth; z < HalfDepth; z += FloorTile)
                {
                    Place(Floor2, root.transform,
                        new Vector3(x + FloorTile * 0.5f, RoomHeight, z + FloorTile * 0.5f),
                        rename: $"ENV_Roof_{x:0}_{z:0}");
                }
            }

            // One slab collider instead of 108, so nothing can be pushed through the top.
            var ceiling = new GameObject("ENV_Depot_RoofCollider");
            ceiling.transform.SetParent(root.transform, false);
            var box = ceiling.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, RoomHeight + FloorTileHeight * 0.5f, 0f);
            box.size = new Vector3(RoomWidth, FloorTileHeight, RoomDepth);
        }

        static void BuildWalls(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Walls", parent, Vector3.zero);

            // North wall, split around the arch.
            AddWallRun(root.transform, -HalfWidth, ArchMin, HalfDepth, true, "N");
            AddWallRun(root.transform, ArchMax, HalfWidth, HalfDepth, true, "N");

            // South wall, split around the 2 m door module.
            AddWallRun(root.transform, -HalfWidth, DoorModuleMin, -HalfDepth, true, "S");
            AddWallRun(root.transform, DoorModuleMax, HalfWidth, -HalfDepth, true, "S");

            // Side walls, unbroken.
            AddWallRun(root.transform, -HalfDepth, HalfDepth, -HalfWidth, false, "W");
            AddWallRun(root.transform, -HalfDepth, HalfDepth, HalfWidth, false, "E");

            BuildOpeningInfill(root.transform);

            var inserts = GrayboxKit.Empty("ENV_Depot_Inserts", root.transform, Vector3.zero);
            foreach (var x in new[] { -2f, 2f, 6f })
            {
                Place(WoodInsert, inserts.transform,
                    new Vector3(x, 0f, -HalfDepth + WallThickness), 0f,
                    rename: $"ENV_WoodInsert_{x:0}");
            }
        }

        /// <summary>
        /// Tiles a wall run with 4 m modules, then closes the remainder with a module butted
        /// against the far end. Overlap between the last two modules hides inside solid
        /// geometry, which beats a visible seam.
        /// </summary>
        static void AddWallRun(Transform parent, float from, float to, float fixedAxis,
            bool alongX, string label)
        {
            if (to - from <= 0.01f)
            {
                return;
            }

            var offset = fixedAxis + Mathf.Sign(fixedAxis) * WallThickness * 0.5f;
            var yaw = alongX ? 0f : 90f;
            var index = 0;
            var cursor = from;

            while (cursor < to - 0.01f)
            {
                var remaining = to - cursor;
                string asset;
                float width;

                if (remaining >= WallModule4 - 0.01f)
                {
                    asset = Wall4;
                    width = WallModule4;
                }
                else
                {
                    asset = remaining > WallModule2 + 0.01f ? Wall4 : Wall2;
                    width = asset == Wall4 ? WallModule4 : WallModule2;
                    cursor = to - width;
                }

                var centre = cursor + width * 0.5f;
                var position = alongX
                    ? new Vector3(centre, 0f, offset)
                    : new Vector3(offset, 0f, centre);

                Place(asset, parent, position, yaw, collide: true,
                    rename: $"ENV_Depot_Wall_{width:0}m_{label}_{index}");

                cursor += width;
                index++;
            }
        }

        /// <summary>
        /// Windows sit on the wall face rather than in cut openings -- the kit walls are
        /// solid, so these read as frames against them. Placed clear of the stations.
        /// </summary>
        static void BuildWindows(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Windows", parent, Vector3.zero);
            const float sill = 2.2f;
            var face = HalfWidth - WallThickness * 0.5f;

            // West wall, below the mission station's stretch.
            foreach (var z in new[] { -2f, -6.5f })
            {
                Place(WindowWide, root.transform, new Vector3(-face, sill, z), 90f,
                    rename: $"ENV_Window_W_{z:0}");
            }

            // South wall, above the tavern back bar.
            foreach (var x in new[] { 2f, 6f, 10f })
            {
                Place(WindowNarrow, root.transform,
                    new Vector3(x, sill, -HalfDepth + WallThickness * 0.5f), 0f,
                    rename: $"ENV_Window_S_{x:0}");
            }

            // North wall, west of the trophy gallery.
            Place(WindowTrainArch, root.transform,
                new Vector3(-10f, 2.4f, HalfDepth - WallThickness * 0.5f), 180f,
                rename: "ENV_Window_TrainArch");
        }

        /// <summary>
        /// Fills the wall above the two openings.
        ///
        /// The arch piece is 5.50 m tall and the doorway leaf 3.00 m, but the wall is 7 m --
        /// so both left an open slot straight through to the skybox. The kit has no lintel
        /// piece at these spans, so these are plain blockout fills using the architecture
        /// material; swap them for real geometry when the kit gains a header module.
        /// </summary>
        static void BuildOpeningInfill(Transform parent)
        {
            var material = KitMaterial("HD_TEX_ARCH_WALNUT") ??
                           GrayboxKit.SolidMaterial("GB_Infill", new Color(0.28f, 0.19f, 0.12f));

            var root = GrayboxKit.Empty("ENV_Depot_OpeningInfill", parent, Vector3.zero);

            // Above the train arch.
            const float archTop = 5.5f;
            var archFill = GrayboxKit.Box("ENV_Depot_ArchHeader", root.transform,
                new Vector3(ArchCentre, archTop + (RoomHeight - archTop) * 0.5f,
                    HalfDepth + WallThickness * 0.5f),
                new Vector3(ArchWidth, RoomHeight - archTop, WallThickness), material);
            archFill.isStatic = true;

            // Above the doorway, plus the jambs either side of the leaf.
            const float doorTop = 3f;
            var doorWidth = DoorModuleMax - DoorModuleMin;
            var doorFill = GrayboxKit.Box("ENV_Depot_DoorHeader", root.transform,
                new Vector3((DoorModuleMin + DoorModuleMax) * 0.5f,
                    doorTop + (RoomHeight - doorTop) * 0.5f,
                    -HalfDepth - WallThickness * 0.5f),
                new Vector3(doorWidth, RoomHeight - doorTop, WallThickness), material);
            doorFill.isStatic = true;
        }

        static Material KitMaterial(string materialName)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                $"Assets/_Game/Art/Materials/{materialName}.mat");
        }

        static void BuildStructure(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Structure", parent, Vector3.zero);
            var inset = ColumnWidth * 0.5f + WallThickness * 0.5f;

            foreach (var x in new[] { -12f, -8f, -6f, -4f, 0f, 8f, 12f })
            {
                foreach (var z in new[] { HalfDepth, -HalfDepth })
                {
                    Place(Column, root.transform,
                        new Vector3(x, 0f, z - Mathf.Sign(z) * inset), 0f, collide: true,
                        rename: $"ENV_Depot_Column_{x:0}_{z:0}");
                }
            }

            foreach (var z in new[] { -5f, -1f, 3f, 7f })
            {
                foreach (var x in new[] { HalfWidth, -HalfWidth })
                {
                    Place(Column, root.transform,
                        new Vector3(x - Mathf.Sign(x) * inset, 0f, z), 0f, collide: true,
                        rename: $"ENV_Depot_Column_{x:0}_{z:0}");
                }
            }

            var beams = GrayboxKit.Empty("ENV_Depot_Beams", root.transform, Vector3.zero);
            var beamY = RoomHeight - BeamHeight;

            for (var x = -HalfWidth; x < HalfWidth; x += WallModule4)
            {
                foreach (var z in new[] { HalfDepth - 0.3f, -HalfDepth + 0.3f })
                {
                    Place(Beam4, beams.transform,
                        new Vector3(x + WallModule4 * 0.5f, beamY, z), 0f,
                        rename: $"ENV_Depot_Beam_{x:0}_{z:0}");
                }
            }

            for (var z = -HalfDepth; z < HalfDepth; z += WallModule4)
            {
                foreach (var x in new[] { HalfWidth - 0.3f, -HalfWidth + 0.3f })
                {
                    Place(Beam4, beams.transform,
                        new Vector3(x, beamY, z + WallModule4 * 0.5f), 90f,
                        rename: $"ENV_Depot_Beam_{x:0}_{z:0}");
                }
            }

            // Roof trusses: three 8 m spans per line across the 24 m width.
            var trusses = GrayboxKit.Empty("ENV_Depot_Trusses", root.transform, Vector3.zero);
            foreach (var z in new[] { -6f, -2f, 2f, 6f })
            {
                for (var i = 0; i < 3; i++)
                {
                    Place(Truss8, trusses.transform,
                        new Vector3(-HalfWidth + TrussSpan * (i + 0.5f), RoomHeight - TrussHeight, z),
                        0f, rename: $"ENV_Depot_Truss_{z:0}_{i}");
                }
            }
        }

        static void BuildPlatform(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Platform", parent, Vector3.zero);

            Place(Arch, root.transform,
                new Vector3(ArchCentre, 0f, HalfDepth + WallThickness * 0.5f), 0f,
                rename: "ENV_Depot_TrainArch");

            // The car sits beyond the opening, framed by the arch.
            Place(TrainCar, root.transform, new Vector3(ArchCentre, 0f, HalfDepth + 4.5f), 0f,
                collide: true, rename: "ENV_TrainCar");

            var crates = GrayboxKit.Empty("Crates", root.transform, Vector3.zero);
            var spots = new[]
            {
                new Vector3(1.6f, 0f, 7.6f),
                new Vector3(2.4f, 0f, 7.6f),
                new Vector3(8.2f, 0f, 7.2f),
                new Vector3(1.6f, 0.6f, 7.6f)
            };

            for (var i = 0; i < spots.Length; i++)
            {
                Place(PlatformCrate, crates.transform, spots[i], i * 27f, collide: true,
                    rename: $"PRP_PlatformCrate_{i:00}");
            }
        }

        static void BuildFireplace(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Fireplace", parent, Vector3.zero);

            // Depth is 1.14, so the back sits against the north wall.
            Place(Fireplace, root.transform, new Vector3(-1f, 0f, HalfDepth - 0.57f), 180f,
                collide: true, rename: "ENV_Fireplace");

            var lightObject = GrayboxKit.Empty("Light_Fireplace", root.transform,
                new Vector3(-1f, 1f, HalfDepth - 1.4f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.28f);
            light.intensity = 11f;
            light.range = 15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            light.shadowBias = 0.08f;
            light.shadowNormalBias = 0.5f;

            // The one light in the room that moves, so the whole space feels alive.
            var flicker = lightObject.AddComponent<Transity.Core.LightFlicker>();
            GrayboxKit.Wire(flicker,
                ("target", light),
                ("baseIntensity", 11f),
                ("amplitude", 0.2f),
                ("speed", 1.5f),
                ("positionJitter", 0.05f));
        }

        static void BuildStairs(Transform parent)
        {
            var root = GrayboxKit.Empty("ENV_Depot_Stairs", parent, Vector3.zero);

            Place(Stairs, root.transform, new Vector3(-HalfWidth + 1.1f, 0f, -5f), 0f,
                collide: true, rename: "ENV_Stairs");
            Place(Railing4, root.transform, new Vector3(-HalfWidth + 2.1f, 0f, -5f), 90f,
                rename: "ENV_Railing");
        }

        // ---------------------------------------------------------------------- stations

        static void BuildStations()
        {
            var root = new GameObject("Stations");

            // Anchors put each station's back against its wall; the offsets are half the
            // measured depth of each kit piece.
            var missionAnchor = PlaceStation(root.transform, MissionStation, StationKind.Mission,
                new Vector3(-HalfWidth + 0.53f, 0f, 4f), 90f, new Vector2(3f, 5.5f),
                StationScreenKind.MissionTerminal, "Use the mission computer");

            PlaceStation(root.transform, TrophyGallery, StationKind.Trophy,
                new Vector3(-5.5f, 0f, HalfDepth - 0.38f), 180f, new Vector2(5f, 4f));

            var marketAnchor = PlaceStation(root.transform, LoadoutStation, StationKind.Loadout,
                new Vector3(HalfWidth - 0.48f, 0f, 3.5f), -90f, new Vector2(3f, 5f),
                StationScreenKind.Market, "Talk to the quartermaster");

            PlaceStation(root.transform, WardrobeStation, StationKind.Wardrobe,
                new Vector3(HalfWidth - 0.48f, 0f, -3f), -90f, new Vector2(3f, 4f),
                StationScreenKind.Loadout, "Open the wardrobe");

            BuildVendor(root.transform, marketAnchor);

            PlaceStation(root.transform, TavernCounter, StationKind.Tavern,
                new Vector3(6.5f, 0f, -7f), 0f, new Vector2(5f, 1.4f));

            var tableAnchor = BuildCommonTable(root.transform);
            BuildTavernStools(root.transform);
            BuildStationDressing(missionAnchor, marketAnchor, tableAnchor);
        }

        static GameObject PlaceStation(Transform parent, string assetName, StationKind kind,
            Vector3 position, float yaw, Vector2 footprint,
            StationScreenKind? screen = null, string prompt = null)
        {
            var anchorObject = GrayboxKit.Empty($"Anchor_{kind}", parent,
                GrayboxKit.Snap(position, 0.25f));
            anchorObject.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var anchor = anchorObject.AddComponent<StationAnchor>();
            GrayboxKit.Wire(anchor, ("kind", (int)kind), ("footprint", footprint));

            var instance = Place(assetName, anchorObject.transform,
                anchorObject.transform.position, yaw, collide: true, rename: assetName);

            if (instance != null)
            {
                GrayboxKit.MarkStatic(instance);
            }

            if (screen.HasValue)
            {
                AddTerminal(anchorObject, screen.Value, prompt);
                SetLayer(anchorObject, InteractableLayer);
            }

            return anchorObject;
        }

        /// <summary>
        /// Turns a station anchor into something you can use. The terminal lives on the
        /// anchor rather than the mesh, so re-importing art never disturbs the interaction.
        /// </summary>
        static void AddTerminal(GameObject anchorObject, StationScreenKind screen, string prompt)
        {
            anchorObject.AddComponent<NetworkObject>();
            var terminal = anchorObject.AddComponent<StationTerminal>();

            // Camera pose while the screen is open: standing a little back from the station,
            // at eye height, looking at it. Nudge these in the scene to reframe a shot.
            var focus = GrayboxKit.Empty("FocusPoint", anchorObject.transform,
                new Vector3(0f, 1.65f, 2.2f));
            var lookAt = GrayboxKit.Empty("LookTarget", anchorObject.transform,
                new Vector3(0f, 1.5f, 0.2f));

            GrayboxKit.Wire(terminal,
                ("screen", (int)screen),
                ("focusPoint", focus.transform),
                ("lookTarget", lookAt.transform),
                ("prompt", prompt ?? "Use"),
                ("interactionRange", 3f));
        }

        /// <summary>
        /// Stand-in for the quartermaster NPC, with the display panel on his left as the
        /// design calls for. Replace the capsule with a real character when one exists; the
        /// terminal only needs the transforms.
        /// </summary>
        static void BuildVendor(Transform parent, GameObject marketAnchor)
        {
            var root = GrayboxKit.Empty("NPC_Quartermaster", parent,
                new Vector3(10.2f, 0f, 4.6f));
            root.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "VendorPlaceholder";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
            GrayboxKit.Paint(body,
                GrayboxKit.SolidMaterial("GB_Vendor", new Color(0.42f, 0.36f, 0.28f)));

            if (marketAnchor == null ||
                !marketAnchor.TryGetComponent<StationTerminal>(out var terminal))
            {
                return;
            }

            // Aim slightly past the vendor so he sits on the right of the shot, leaving the
            // left half clear for the market board. Drag these two in the scene to reframe.
            var focus = GrayboxKit.Empty("MarketFocus", root.transform,
                new Vector3(2.6f, 1.65f, 0.55f));
            var lookAt = GrayboxKit.Empty("MarketLookTarget", root.transform,
                new Vector3(0f, 1.5f, -0.95f));

            GrayboxKit.Wire(terminal,
                ("focusPoint", focus.transform),
                ("lookTarget", lookAt.transform));
        }


        static GameObject BuildCommonTable(Transform parent)
        {
            var anchorObject = GrayboxKit.Empty($"Anchor_{StationKind.CommonTable}", parent,
                Vector3.zero);
            var anchor = anchorObject.AddComponent<StationAnchor>();
            GrayboxKit.Wire(anchor, ("kind", (int)StationKind.CommonTable),
                ("footprint", new Vector2(4.2f, 1.8f)));

            Place(RugLarge, anchorObject.transform, Vector3.zero, 0f, rename: "PRP_Rug");
            Place(CentralTable, anchorObject.transform, Vector3.zero, 0f, collide: true,
                rename: "PRP_CentralTable");

            // Six chairs, clear of the 1.8 m circulation ring around the table.
            var seats = new[]
            {
                new Vector3(-1.4f, 0f, 1.55f), new Vector3(0f, 0f, 1.55f), new Vector3(1.4f, 0f, 1.55f),
                new Vector3(-1.4f, 0f, -1.55f), new Vector3(0f, 0f, -1.55f), new Vector3(1.4f, 0f, -1.55f)
            };

            for (var i = 0; i < seats.Length; i++)
            {
                Place(HunterChair, anchorObject.transform, seats[i],
                    seats[i].z > 0f ? 180f : 0f, collide: true, rename: $"PRP_HunterChair_{i:00}");
            }

            return anchorObject;
        }

        /// <summary>
        /// Batch-two props, parented to the station anchors so they travel with the station
        /// if the layout moves. Local +Z on an anchor points into the room.
        /// </summary>
        static void BuildStationDressing(GameObject missionAnchor, GameObject loadoutAnchor,
            GameObject tableAnchor)
        {
            if (missionAnchor != null)
            {
                var t = missionAnchor.transform;
                PlaceLocal(ScreenMissionDual, t, new Vector3(-0.6f, 0.95f, 0.62f), 6f);
                PlaceLocal(ScreenMissionDesk, t, new Vector3(0.9f, 0.95f, 0.6f), -14f);
                PlaceLocal(ScreenFieldTablet, t, new Vector3(1.8f, 0.95f, 0.55f), 22f);
                PlaceLocal(ScreenWallStatus, t, new Vector3(-1.9f, 2.35f, 0.16f), 0f);

                // Contract maps pinned along the board.
                PlaceLocal(MapForestRail, t, new Vector3(0.35f, 1.75f, 0.17f), 0f, pitch: 90f);
                PlaceLocal(MapMarsh, t, new Vector3(1.85f, 1.75f, 0.17f), 0f, pitch: 90f);
                PlaceLocal(MapMountainQuarry, t, new Vector3(0.35f, 2.75f, 0.17f), 0f, pitch: 90f);
                PlaceLocal(MapIndustrialDistrict, t, new Vector3(1.85f, 2.75f, 0.17f), 0f, pitch: 90f);
            }

            if (loadoutAnchor != null)
            {
                var t = loadoutAnchor.transform;
                PlaceLocal(WeaponHeavyRifle, t, new Vector3(-1.6f, 2.05f, 0.28f), 0f);
                PlaceLocal(WeaponScoutRifle, t, new Vector3(-0.5f, 2.05f, 0.28f), 0f);
                PlaceLocal(WeaponFieldShotgun, t, new Vector3(0.6f, 2.05f, 0.28f), 0f);
                PlaceLocal(WeaponDartLauncher, t, new Vector3(1.7f, 2.05f, 0.28f), 0f);
            }

            if (tableAnchor != null)
            {
                var t = tableAnchor.transform;
                PlaceLocal(MapBountyRoutes, t, new Vector3(-0.85f, 0.93f, 0.05f), 8f);
                PlaceLocal(MapFoldedField, t, new Vector3(0.95f, 0.93f, -0.15f), -22f);
            }
        }

        /// <summary>Places a kit piece in a parent's local space.</summary>
        static GameObject PlaceLocal(string assetName, Transform parent, Vector3 localPosition,
            float localYaw, float pitch = 0f)
        {
            var prefab = LoadKit(assetName);
            if (prefab == null)
            {
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(pitch, localYaw, 0f);
            instance.name = assetName;
            GrayboxKit.Decorative(instance);
            GrayboxKit.MarkStatic(instance);
            return instance;
        }

        static void BuildTavernStools(Transform parent)
        {
            var root = GrayboxKit.Empty("TavernStools", parent, Vector3.zero);

            for (var i = 0; i < 4; i++)
            {
                Place(BarStool, root.transform, new Vector3(4.85f + i * 1.1f, 0f, -5.9f), 0f,
                    collide: true, rename: $"PRP_BarStool_{i:00}");
            }
        }

        // ------------------------------------------------------------- gameplay fixtures

        static GameObject BuildGameplayFixtures()
        {
            var root = GrayboxKit.Empty("Gameplay", null, Vector3.zero);

            var spawns = GrayboxKit.Empty("SpawnPoints", root.transform, Vector3.zero);
            for (var i = 0; i < 4; i++)
            {
                var point = GrayboxKit.Empty($"TrainSpawn_{i}", spawns.transform,
                    new Vector3(3f + i * 1.2f, 0f, 6.5f));
                point.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var spawn = point.AddComponent<PlayerSpawnPoint>();
                GrayboxKit.Wire(spawn, ("context", (int)SpawnContext.Train));
            }

            var lever = GrayboxKit.Box("DepartureLever", root.transform,
                new Vector3(8.6f, 1.2f, 8f), new Vector3(0.25f, 0.5f, 0.25f),
                GrayboxKit.SolidMaterial("GB_Lever", new Color(0.85f, 0.3f, 0.25f)));
            lever.AddComponent<NetworkObject>();
            var leverComponent = lever.AddComponent<DepartureLever>();
            GrayboxKit.Wire(leverComponent, ("prompt", "Depart on expedition"),
                ("interactionRange", 2.5f));
            SetLayer(lever, InteractableLayer);

            var platformAnchor = GrayboxKit.Empty("Anchor_TrainPlatform", root.transform,
                new Vector3(ArchCentre, 0f, HalfDepth - 1f));
            var anchor = platformAnchor.AddComponent<StationAnchor>();
            GrayboxKit.Wire(anchor, ("kind", (int)StationKind.TrainPlatform),
                ("footprint", new Vector2(5f, 5f)));

            // Door in the south opening, using a wood insert as the leaf.
            var doorRoot = GrayboxKit.Empty("Door", root.transform,
                new Vector3(DoorModuleMin, 0f, -HalfDepth));
            doorRoot.AddComponent<NetworkObject>();
            var doorComponent = doorRoot.AddComponent<SimpleDoor>();
            var hinge = GrayboxKit.Empty("Hinge", doorRoot.transform, Vector3.zero);

            var leaf = Place(WoodInsert, hinge.transform,
                new Vector3(DoorModuleMin + 1f, 0f, -HalfDepth), 0f, collide: true,
                rename: "DoorLeaf");

            if (leaf == null)
            {
                GrayboxKit.Box("DoorLeaf", hinge.transform, new Vector3(1f, 1.5f, 0f),
                    new Vector3(2f, 3f, 0.16f),
                    GrayboxKit.SolidMaterial("GB_Door", new Color(0.5f, 0.35f, 0.2f)));
            }

            GrayboxKit.Wire(doorComponent, ("hinge", hinge.transform), ("prompt", "door"),
                ("interactionRange", 2.5f));
            SetLayer(doorRoot, InteractableLayer);

            return root;
        }

        /// <summary>
        /// Loose pickups on the table. Parented under Gameplay on purpose: anything left at
        /// the scene root is outside every generated root, so a rebuild would not clean it
        /// up and each run would stack another copy on the table.
        /// </summary>
        static void PlaceLooseItems(ItemRegistry registry, Transform parent)
        {
            PlaceItem(registry, "Flashlight", new Vector3(-1.2f, 0.95f, 0.3f), parent);
            PlaceItem(registry, "Medkit", new Vector3(1.1f, 0.95f, -0.3f), parent);
        }

        static void PlaceItem(ItemRegistry registry, string displayName, Vector3 position,
            Transform parent)
        {
            var definition = registry.Items.FirstOrDefault(i => i != null && i.DisplayName == displayName);
            if (definition == null || definition.WorldPrefab == null)
            {
                Debug.LogWarning($"Could not place '{displayName}': no item by that display name, " +
                                 "or it has no world prefab.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.WorldPrefab, parent);
            instance.transform.position = GrayboxKit.Snap(position, 0.25f);
            SetLayer(instance, InteractableLayer);
        }

        static void BuildScaleReference()
        {
            var reference = GrayboxKit.Empty("ENV_ScaleReference_1m8", null, new Vector3(-3f, 0f, 3f));
            reference.tag = "EditorOnly";

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Mannequin";
            capsule.transform.SetParent(reference.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            GrayboxKit.Paint(capsule,
                GrayboxKit.SolidMaterial("GB_Reference", new Color(0.85f, 0.85f, 0.8f)));
            GrayboxKit.Decorative(reference);
        }

        // -------------------------------------------------------------------- lighting

        /// <summary>
        /// Interior lighting. Every light here is Realtime on purpose.
        ///
        /// The previous pass marked these Baked, which is why the room looked unlit: a baked
        /// light contributes nothing until a lightmap is actually generated, and nothing ever
        /// baked one. Now that the roof closes the room off from the sun, the fixtures are
        /// the only light there is, so they have to work without a bake. Only the fireplace
        /// casts shadows, which keeps the spec's 2-4 shadow-caster budget intact.
        ///
        /// Switch these to Mixed and bake once the room stops changing shape.
        /// </summary>
        static void BuildLighting()
        {
            // Kept for the platform beyond the arch, which is still open to the sky.
            var sunObject = new GameObject("Directional Light");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.62f, 0.72f, 0.90f);
            // Dim and cold. Its job is the open platform beyond the arch; the roof shadows it
            // out of the lobby, and the warm interior should win wherever they meet.
            sun.intensity = 0.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sunObject.transform.rotation = Quaternion.Euler(48f, 200f, 0f);

            var lamps = new GameObject("Interior Lights");
            var hangY = RoomHeight - PendantHeight;

            // Four pendants over the main floor.
            var pendants = new[]
            {
                new Vector3(-6f, 0f, 3f), new Vector3(6f, 0f, 3f),
                new Vector3(-6f, 0f, -4f), new Vector3(6f, 0f, -4f)
            };

            for (var i = 0; i < pendants.Length; i++)
            {
                Place(Pendant, lamps.transform,
                    new Vector3(pendants[i].x, hangY, pendants[i].z), 0f,
                    rename: $"PRP_PendantLight_{i:00}");

                // A downward spot gives the floor a readable pool of light, and a weak point
                // fill stops the walls going black between pools. Cheaper and far better
                // looking than one big point light per pendant.
                AddSpot(lamps.transform, $"Spot_Pendant_{i:00}",
                    new Vector3(pendants[i].x, hangY + 0.1f, pendants[i].z),
                    new Color(1f, 0.89f, 0.74f), 16f, 12f, 100f);

                AddLamp(lamps.transform, $"Lamp_Pendant_{i:00}",
                    new Vector3(pendants[i].x, hangY + 0.25f, pendants[i].z),
                    new Color(1f, 0.88f, 0.72f), 4.5f, 10f);
            }

            // Wall sconces. Fixture plus a short-range light so the walls are not black.
            var sconces = new[]
            {
                (new Vector3(-HalfWidth + 0.3f, 2.6f, -7f), 90f),
                (new Vector3(-HalfWidth + 0.3f, 2.6f, 7f), 90f),
                (new Vector3(HalfWidth - 0.3f, 2.6f, -7f), -90f),
                (new Vector3(HalfWidth - 0.3f, 2.6f, 7f), -90f)
            };

            foreach (var (position, yaw) in sconces)
            {
                Place(LightCageSconce, lamps.transform, position, yaw,
                    rename: $"PRP_Sconce_{position.x:0}_{position.z:0}");
                AddLamp(lamps.transform, $"Lamp_Sconce_{position.x:0}_{position.z:0}",
                    position + new Vector3(Mathf.Sign(position.x) * -0.35f, 0.1f, 0f),
                    new Color(1f, 0.82f, 0.6f), 6f, 8.5f);
            }

            // Lodge boxes over the lounge, and reading cones at the table.
            Place(LightLodgeBox, lamps.transform, new Vector3(-1f, 3.4f, HalfDepth - 0.6f), 180f,
                rename: "PRP_LodgeBox_Fireplace");
            Place(LightReadingCone, lamps.transform, new Vector3(-2.4f, 0.92f, 0.6f), 0f,
                rename: "PRP_ReadingCone_A");
            Place(LightReadingCone, lamps.transform, new Vector3(2.4f, 0.92f, -0.6f), 180f,
                rename: "PRP_ReadingCone_B");

            // Station accents, so the screens and racks are readable.
            AddLamp(lamps.transform, "Lamp_Mission", new Vector3(-9.5f, 2.6f, 4f),
                new Color(0.82f, 0.9f, 1f), 5.5f, 8f);
            AddLamp(lamps.transform, "Lamp_Loadout", new Vector3(9.5f, 2.6f, 3.5f),
                new Color(1f, 0.9f, 0.75f), 5.5f, 8f);

            // Platform cages beyond the arch.
            foreach (var x in new[] { ArchCentre - 3.2f, ArchCentre + 3.2f })
            {
                Place(LightPlatformCage, lamps.transform,
                    new Vector3(x, 3.2f, HalfDepth + 1.4f), 0f,
                    rename: $"PRP_PlatformCage_{x:0}");
                AddLamp(lamps.transform, $"Lamp_Platform_{x:0}",
                    new Vector3(x, 3.0f, HalfDepth + 1.4f),
                    new Color(0.85f, 0.9f, 1f), 4.5f, 9f);
            }
        }

        /// <summary>A downward realtime spot, for pooled light under a fixture.</summary>
        static void AddSpot(Transform parent, string lampName, Vector3 position,
            Color color, float intensity, float range, float angle)
        {
            var go = GrayboxKit.Empty(lampName, parent, position);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.45f;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
        }

        /// <summary>A realtime point light with no shadows -- cheap fill.</summary>
        static void AddLamp(Transform parent, string lampName, Vector3 position,
            Color color, float intensity, float range)
        {
            var go = GrayboxKit.Empty(lampName, parent, position);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
        }

        static void BuildProbes()
        {
            var root = GrayboxKit.Empty("Probes", null, Vector3.zero);

            var roomProbe = GrayboxKit.Empty("ReflectionProbe_Room", root.transform,
                new Vector3(0f, 2.5f, 0f));
            var room = roomProbe.AddComponent<ReflectionProbe>();
            room.size = new Vector3(RoomWidth, RoomHeight, RoomDepth);
            room.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;

            var platformProbe = GrayboxKit.Empty("ReflectionProbe_Platform", root.transform,
                new Vector3(ArchCentre, 2.5f, HalfDepth + 3f));
            var platform = platformProbe.AddComponent<ReflectionProbe>();
            platform.size = new Vector3(12f, 6f, 8f);
            platform.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;

            var probeObject = GrayboxKit.Empty("LightProbeGroup", root.transform, Vector3.zero);
            var group = probeObject.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();

            for (var x = -10f; x <= 10f; x += 5f)
            {
                for (var z = -7f; z <= 7f; z += 5f)
                {
                    positions.Add(new Vector3(x, 0.6f, z));
                    positions.Add(new Vector3(x, 3.2f, z));
                }
            }

            group.probePositions = positions.ToArray();
        }

        static void ApplyLightingSettings()
        {
            // Now that the room is enclosed, ambient is a floor rather than the main light
            // source -- kept dim and warm so the fixtures do the work.
            // Ambient is a black-clipping floor, not a light source. At the previous values
            // it lit every surface evenly from above, which is why the room read as lit by
            // the sky no matter what the fixtures did. Everything visible should now be
            // traceable to a bulb.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.045f, 0.05f, 0.065f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.035f, 0.03f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.018f, 0.015f);

            // Very light warm fog. At this density it is barely a haze, but it separates the
            // far wall from the near one and gives the dust motes something to sit in.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.13f, 0.11f, 0.10f);
            RenderSettings.fogDensity = 0.012f;

            RenderSettings.reflectionIntensity = 0.7f;
            RenderSettings.defaultReflectionMode =
                UnityEngine.Rendering.DefaultReflectionMode.Custom;
        }
    }
}
