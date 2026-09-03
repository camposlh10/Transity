using System.Collections.Generic;
using System.Linq;
using Transity.Combat;
using Transity.Core;
using Transity.Creatures;
using Transity.Interaction;
using Transity.Inventory;
using Transity.Missions;
using Transity.Networking;
using Transity.Player;
using Transity.UI;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Transity.EditorTools
{
    /// <summary>
    /// Generates the runnable skeleton of the vertical slice: item data, the networked
    /// player and session prefabs, and the four scenes with blockout geometry.
    ///
    /// Re-runnable. Generated scenes are overwritten wholesale, so treat them as
    /// disposable until you start hand-editing one -- after that, edit it by hand.
    /// </summary>
    public static class TransityScaffold
    {
        const string ScenesFolder = "Assets/_Game/Scenes";
        const string PrefabsFolder = "Assets/_Game/Prefabs";
        const string ItemDataFolder = "Assets/_Game/Data/Items";
        const string NetworkDataFolder = "Assets/_Game/Data/Network";
        const string InputActionsPath = "Assets/_Game/Input/InputSystem_Actions.inputactions";

        /// <summary>Layer 6. The interaction ray only tests this layer.</summary>
        const int InteractableLayer = 6;

        [MenuItem("Tools/Transity/Build Vertical Slice Scaffold", priority = 0)]
        public static void BuildAll() => BuildAll(regenerateDepotLayout: true);

        /// <summary>
        /// Rebuilds everything except the depot layout, so a hand-edited TrainHub is left
        /// completely untouched. Use this once you have started arranging the room yourself.
        /// </summary>
        [MenuItem("Tools/Transity/Rebuild (Keep My Depot Edits)", priority = 1)]
        public static void BuildKeepingDepot() => BuildAll(regenerateDepotLayout: false);

        static void BuildAll(bool regenerateDepotLayout)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            // Materials must exist before the models import, or the kit comes in grey.
            KitImportSettings.ReimportKit();

            // Same ordering problem for the equipment: atlases first, then materials, then
            // the models that reference them.
            EquipmentImportSettings.ReimportEquipment();

            GrayboxKit.EnsureFolder(ScenesFolder);
            GrayboxKit.EnsureFolder(ItemDataFolder);
            GrayboxKit.EnsureFolder(NetworkDataFolder);
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Player");
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Interactables");
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Network");

            var registry = BuildItems();

            // Creatures and deployables first: the item tuning table needs the deployable
            // prefabs to point its trap and bait behaviours at.
            var creatures = CreatureBuilder.BuildAll();
            var contracts = ContractBuilder.Build(creatures.Definitions);
            var deployables = DeployableBuilder.BuildAll();

            EquipmentTuning.ApplyBehaviours(registry, deployables);

            var playerPrefab = BuildPlayerPrefab();
            var sessionScopePrefab = BuildSessionScopePrefab();
            var itemPrefabs = BuildItemPrefabs(registry);

            // Icons need the held models the tuning pass just wired on.
            ItemIconRenderer.RenderAll(registry);

            // The prefab assets exist on disk now, so their NetworkObjects can finally be
            // given real, distinct hashes. Without this pass they all share one junk value.
            NetworkIdFixup.RefreshPrefab(playerPrefab);
            NetworkIdFixup.RefreshPrefab(sessionScopePrefab);
            foreach (var itemPrefab in itemPrefabs)
            {
                NetworkIdFixup.RefreshPrefab(itemPrefab);
            }

            foreach (var creaturePrefab in creatures.Prefabs)
            {
                NetworkIdFixup.RefreshPrefab(creaturePrefab);
            }

            foreach (var deployable in deployables.Values)
            {
                NetworkIdFixup.RefreshPrefab(deployable);
            }

            AssetDatabase.SaveAssets();

            var networked = new List<GameObject> { sessionScopePrefab };
            networked.AddRange(itemPrefabs);
            networked.AddRange(creatures.Prefabs);
            networked.AddRange(deployables.Values);

            var prefabList = BuildNetworkPrefabsList(networked);

            BuildBootScene(registry, creatures.Registry, contracts, playerPrefab, sessionScopePrefab, prefabList);
            BuildMainMenuScene();

            if (regenerateDepotLayout)
            {
                BuildTrainHubScene(registry);
            }
            else
            {
                Debug.Log("<b>Transity</b>: depot layout left as-is.");
            }

            BuildForestScene(registry);
            ApplyBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene($"{ScenesFolder}/{SceneCatalog.Boot}.unity");
            Debug.Log("<b>Transity</b>: scaffold built. Open the Boot scene and press Play, " +
                      "or use Multiplayer Play Mode for a second client.");
        }

        /// <summary>
        /// Rebuilds creatures, contracts, deployables and item tuning without touching a
        /// single scene. This is the balance loop: change a number in CreatureBuilder or
        /// EquipmentTuning, run this, press Play.
        /// </summary>
        [MenuItem("Tools/Transity/Rebuild Gameplay Content", priority = 2)]
        public static void RebuildGameplayContent()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>($"{ItemDataFolder}/ItemRegistry.asset");
            if (registry == null)
            {
                Debug.LogError("No item registry; run the full scaffold first.");
                return;
            }

            var creatures = CreatureBuilder.BuildAll();
            ContractBuilder.Build(creatures.Definitions);
            var deployables = DeployableBuilder.BuildAll();
            EquipmentTuning.ApplyBehaviours(registry, deployables);

            foreach (var prefab in creatures.Prefabs)
            {
                NetworkIdFixup.RefreshPrefab(prefab);
            }

            foreach (var prefab in deployables.Values)
            {
                NetworkIdFixup.RefreshPrefab(prefab);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Transity</b>: rebuilt {creatures.Prefabs.Count} creatures, " +
                      $"{deployables.Count} deployables and the equipment tuning. " +
                      "Run the full scaffold if you added or removed a prefab.");
        }

        [MenuItem("Tools/Transity/Open Boot Scene", priority = 20)]
        public static void OpenBootScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene($"{ScenesFolder}/{SceneCatalog.Boot}.unity");
            }
        }

        // ------------------------------------------------------------------ item data

        /// <summary>
        /// Graybox items with no Hunter Depot counterpart. The rest of the original
        /// placeholder list is superseded by the art drop, so only these two still need a
        /// crate stand-in; they keep their original ids so existing stashes survive.
        /// </summary>
        static readonly (string id, string label, int price, bool atRisk, ItemCategory category,
            EquipmentSlot slot, int stash)[] LegacyItems =
        {
            ("item.ammo", "Ammo", 60, true, ItemCategory.Ammunition, EquipmentSlot.None, 99),
            ("item.beartrap", "Bear Trap", 200, true, ItemCategory.Trap, EquipmentSlot.Utility, 30)
        };

        static ItemRegistry BuildItems()
        {
            var definitions = new List<ItemDefinition>();

            // ---- Hunter Depot collection ----------------------------------------
            var missingModels = EquipmentImportSettings.MissingModels();
            if (missingModels.Count > 0)
            {
                Debug.LogWarning("Equipment models missing from Art/Equipment: " +
                                 string.Join(", ", missingModels) +
                                 ". Their items are still built, but with no mesh.");
            }

            foreach (var entry in EquipmentCatalog.Entries)
            {
                // Prefixed so a Hunter Depot asset cannot collide with a legacy one of the
                // same display name (MedicalKit exists in both).
                var path = $"{ItemDataFolder}/{entry.Model}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                GrayboxKit.Wire(definition,
                    ("itemId", entry.ItemId),
                    ("displayName", entry.DisplayName),
                    ("description", entry.Tradeoff),
                    ("price", entry.Price),
                    ("stashLimit", entry.StashLimit),
                    ("atRiskOnExpedition", entry.AtRisk),
                    ("category", (int)entry.Category),
                    ("slot", (int)entry.Slot));

                definitions.Add(definition);
            }

            // ---- retained graybox items ------------------------------------------
            foreach (var spec in LegacyItems)
            {
                var path = $"{ItemDataFolder}/{spec.label.Replace(" ", string.Empty)}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                GrayboxKit.Wire(definition,
                    ("itemId", spec.id),
                    ("displayName", spec.label),
                    ("price", spec.price),
                    ("stashLimit", spec.stash),
                    ("atRiskOnExpedition", spec.atRisk),
                    ("category", (int)spec.category),
                    ("slot", (int)spec.slot));

                definitions.Add(definition);
            }

            ReportOrphanedItems(definitions);

            var registryPath = $"{ItemDataFolder}/ItemRegistry.asset";
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(registryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<ItemRegistry>();
                AssetDatabase.CreateAsset(registry, registryPath);
            }

            var registrySo = new SerializedObject(registry);
            var list = registrySo.FindProperty("items");
            list.arraySize = definitions.Count;
            for (var i = 0; i < definitions.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            registrySo.ApplyModifiedPropertiesWithoutUndo();
            return registry;
        }

        /// <summary>
        /// Names item assets left in the data folder that nothing references any more --
        /// the placeholders the Hunter Depot collection replaced, mostly. Reported rather
        /// than deleted: they are cheap to leave lying about and expensive to get wrong.
        /// </summary>
        static void ReportOrphanedItems(List<ItemDefinition> live)
        {
            var orphans = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDataFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition != null && !live.Contains(definition))
                {
                    orphans.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                }
            }

            if (orphans.Count > 0)
            {
                Debug.Log($"<b>Transity</b>: {orphans.Count} item assets are no longer in the " +
                          "registry and will not appear in the shop: " + string.Join(", ", orphans) +
                          ". Delete them with Tools > Transity > Delete Unused Item Assets.");
            }
        }

        [MenuItem("Tools/Transity/Delete Unused Item Assets", priority = 46)]
        static void DeleteUnusedItems()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>($"{ItemDataFolder}/ItemRegistry.asset");
            if (registry == null)
            {
                Debug.LogError("No item registry; run the scaffold first.");
                return;
            }

            var live = new HashSet<ItemDefinition>(registry.Items);
            var doomed = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDataFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!live.Contains(AssetDatabase.LoadAssetAtPath<ItemDefinition>(path)))
                {
                    doomed.Add(path);
                }
            }

            // The world prefabs those items owned are dead weight too, and worse than the
            // definitions: they carry NetworkObjects, so a stale one can still be dragged
            // into a scene and spawn an item the registry cannot resolve.
            var liveNames = new HashSet<string>();
            foreach (var definition in registry.Items)
            {
                if (definition != null)
                {
                    liveNames.Add($"WorldItem_{definition.DisplayName.Replace(" ", string.Empty)}");
                }
            }

            var interactables = $"{PrefabsFolder}/Interactables";
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { interactables }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);

                if (name.StartsWith("WorldItem_") && !liveNames.Contains(name))
                {
                    doomed.Add(path);
                }
            }

            if (doomed.Count == 0)
            {
                Debug.Log("<b>Transity</b>: no unused item assets.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete unused item assets",
                    $"Permanently delete {doomed.Count} item assets?\n\n" +
                    string.Join("\n", doomed), "Delete", "Cancel"))
            {
                return;
            }

            foreach (var path in doomed)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Transity</b>: deleted {doomed.Count} unused item assets.");
        }

        static List<GameObject> BuildItemPrefabs(ItemRegistry registry)
        {
            var crateMaterial = GrayboxKit.SolidMaterial("GB_Item", new Color(0.85f, 0.65f, 0.25f));
            var prefabs = new List<GameObject>();

            foreach (var definition in registry.Items)
            {
                if (definition == null)
                {
                    continue;
                }

                var entry = EquipmentCatalog.FindByItemId(definition.ItemId);
                var safeName = definition.DisplayName.Replace(" ", string.Empty);
                var path = $"{PrefabsFolder}/Interactables/WorldItem_{safeName}.prefab";

                var root = new GameObject($"WorldItem_{safeName}");
                var built = entry != null && BuildEquipmentVisual(root, entry);

                if (!built)
                {
                    // No Hunter Depot mesh for this one (the retained graybox items, or a
                    // model that failed to import): fall back to the orange crate so the
                    // item is still pickable in the world.
                    var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    crate.name = "Mesh";
                    crate.transform.SetParent(root.transform, false);
                    crate.transform.localScale = new Vector3(0.3f, 0.2f, 0.45f);
                    Object.DestroyImmediate(crate.GetComponent<Collider>());
                    GrayboxKit.Paint(crate, crateMaterial);

                    var box = root.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.3f, 0.2f, 0.45f);
                }

                // Whole hierarchy: the collider sits on the root, but a stray child on the
                // default layer is the kind of thing that quietly breaks a later change.
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.layer = InteractableLayer;
                }

                root.AddComponent<NetworkObject>();
                var worldItem = root.AddComponent<WorldItem>();

                GrayboxKit.Wire(worldItem,
                    ("definition", definition),
                    ("prompt", $"Pick up {definition.DisplayName}"),
                    ("interactionRange", 2.5f));

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);

                GrayboxKit.Wire(definition, ("worldPrefab", prefab));
                prefabs.Add(prefab);
            }

            return prefabs;
        }

        /// <summary>
        /// Parents the imported equipment mesh under <paramref name="root"/> and fits a
        /// single collider to it.
        ///
        /// The collider is measured from the imported renderers rather than from the art
        /// manifest's dimensions, because the manifest quotes the design size while the
        /// generator's bevels and fittings push the real silhouette a little past it. What
        /// the player's interaction ray hits should match what they can see.
        /// </summary>
        static bool BuildEquipmentVisual(GameObject root, EquipmentCatalog.Entry entry)
        {
            var model = EquipmentImportSettings.LoadModel(entry.Model);
            if (model == null)
            {
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.name = "Mesh";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(instance);
                return false;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // Instantiated at the origin with no rotation, so world bounds are already the
            // prefab-local ones.
            var size = bounds.size;
            var center = bounds.center;

            if (entry.Collider == EquipmentCatalog.ColliderKind.Capsule)
            {
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = center;

                // Longest axis is the barrel of a torch or the body of a canister.
                var direction = size.x >= size.y && size.x >= size.z ? 0
                    : size.y >= size.z ? 1 : 2;
                capsule.direction = direction;
                capsule.height = size[direction];
                capsule.radius = 0.5f * Mathf.Max(
                    size[(direction + 1) % 3], size[(direction + 2) % 3]);
            }
            else
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = center;
                box.size = size;
            }

            return true;
        }

        // -------------------------------------------------------------------- prefabs

        static GameObject BuildPlayerPrefab()
        {
            var bodyMaterial = GrayboxKit.SolidMaterial("GB_Player", new Color(0.35f, 0.55f, 0.75f));
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

            if (actions == null)
            {
                Debug.LogError($"Input actions not found at {InputActionsPath}. " +
                               "Assign one to PlayerInputReader on the generated prefab.");
            }

            var root = new GameObject("PlayerCharacter");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.02f;
            // Slightly generous so the player does not catch on the 0.16 m floor tile lip or
            // the kit's bevelled trim.
            controller.minMoveDistance = 0f;

            root.AddComponent<NetworkObject>();

            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.InLocalSpace = false;
            networkTransform.Interpolate = true;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            // Visible body, hidden from its owner by PlayerCharacter. Uses the rigged
            // character when it has been imported, and falls back to the old capsule so the
            // scaffold still produces a working player on a fresh clone.
            CharacterImportSettings.EnsureAnimatorControllers();
            var roster = BuildCharacterRoster();
            var body = BuildPlayerBody(root.transform, bodyMaterial, roster, out var bodies);

            var head = GrayboxKit.Empty("Head", root.transform, new Vector3(0f, 1.65f, 0f));

            var cameraObject = GrayboxKit.Empty("PlayerCamera", head.transform, Vector3.zero);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 70f;
            var listener = cameraObject.AddComponent<AudioListener>();

            var dropOrigin = GrayboxKit.Empty("DropOrigin", head.transform, new Vector3(0f, -0.2f, 0.4f));

            var muzzleObject = GrayboxKit.Empty("MuzzleFlash", cameraObject.transform, new Vector3(0f, 0f, 0.6f));
            var muzzleLight = muzzleObject.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1f, 0.86f, 0.6f);
            muzzleLight.range = 14f;
            muzzleLight.intensity = 900f;
            muzzleLight.shadows = LightShadows.None;
            muzzleLight.enabled = false;

            // Worn light. Offset to the shoulder so it throws its shadows off-axis rather
            // than flatly ahead, but still parented to the head pivot: a torch that cannot
            // be aimed up at a canopy is realistic and miserable.
            var beamObject = GrayboxKit.Empty("WornLight", head.transform, new Vector3(0.12f, -0.25f, 0.16f));
            var beam = beamObject.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 22f;
            beam.spotAngle = 48f;
            beam.intensity = 900f;
            beam.shadows = LightShadows.Soft;
            beam.enabled = false;

            var input = root.AddComponent<PlayerInputReader>();
            var look = root.AddComponent<PlayerLook>();
            var movement = root.AddComponent<FirstPersonController>();
            var interactor = root.AddComponent<Interactor>();
            var inventory = root.AddComponent<InventoryComponent>();
            var hotbar = root.AddComponent<PlayerHotbarInput>();
            root.AddComponent<PlayerFeedback>();
            var character = root.AddComponent<PlayerCharacter>();
            var stationFocus = root.AddComponent<StationFocusController>();

            root.AddComponent<PlayerIdentity>();
            root.AddComponent<PlayerWallet>();

            // Players do not regenerate and their wounds do not clot on their own: a medkit
            // is the only way to stop bleeding, which is what makes carrying one a decision.
            var health = root.AddComponent<Health>();
            GrayboxKit.Wire(health,
                ("maxHealth", 100f),
                ("regenPerSecond", 0f),
                ("bleedPerSecond", 1.6f),
                ("bleedDuration", 0f),
                ("damageTakenMultiplier", 1f));

            var vitals = root.AddComponent<PlayerVitals>();
            var equipment = root.AddComponent<PlayerEquipment>();
            var playerLight = root.AddComponent<PlayerLight>();
            var spectator = root.AddComponent<SpectatorController>();

            // Which character this player is wearing, replicated so everyone sees the same.
            var skin = root.AddComponent<CharacterSkin>();
            GrayboxKit.Wire(skin, ("roster", roster));

            var skinSo = new SerializedObject(skin);
            var bodyList = skinSo.FindProperty("bodies");
            bodyList.arraySize = bodies.Count;
            for (var i = 0; i < bodies.Count; i++)
            {
                bodyList.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            }

            skinSo.ApplyModifiedPropertiesWithoutUndo();

            // Each player keeps their own stash. Player NetworkObjects survive the
            // networked scene loads, so it persists for the whole session.
            root.AddComponent<PlayerStash>();

            GrayboxKit.Wire(input, ("actions", actions), ("actionMapName", "Player"));
            GrayboxKit.Wire(look, ("body", root.transform), ("headPivot", head.transform), ("input", input));
            GrayboxKit.Wire(movement, ("input", input), ("headPivot", head.transform));
            // Layer 7 = Player, layer 6 = Interactable (see TagManager). Casting only
            // against interactables keeps the player capsule from blocking its own aim, and
            // occlusion tests Default so walls still break line of sight.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = 7;
            }

            // Layer 6 is props and dropped gear; layer 9 is creatures, which have to be
            // reachable so a corpse can be tagged and a sedated one contained. Alive ones
            // refuse the interaction themselves, so nothing prompts at a living creature.
            // Cast from the head pivot rather than the camera. They are the same transform
            // position in first person -- the camera is the pivot's child at zero -- but
            // the third-person view moves the camera back, and reach must not move with it.
            GrayboxKit.Wire(interactor,
                ("input", input),
                ("rayOrigin", head.transform),
                ("interactableMask", (1 << 6) | (1 << 9)),
                ("occlusionMask", 1),
                ("maxRange", 3.2f));
            GrayboxKit.Wire(inventory, ("dropOrigin", dropOrigin.transform), ("capacity", 5));
            GrayboxKit.Wire(hotbar, ("input", input), ("inventory", inventory));
            GrayboxKit.Wire(stationFocus, ("character", character), ("input", input));

            GrayboxKit.Wire(vitals,
                ("health", health),
                ("inventory", inventory),
                ("movement", movement),
                ("character", character),
                ("bodyRoot", body.transform),
                ("friendlyFireMultiplier", 0.35f));

            // Shots hit the world, other players, creatures and deployables -- layers
            // 0, 7, 9 and 10. Not layer 6: an interactable prop should not eat a bullet
            // meant for what is behind it.
            GrayboxKit.Wire(equipment,
                ("input", input),
                ("inventory", inventory),
                ("movement", movement),
                ("look", look),
                ("character", character),
                ("health", health),
                ("remoteHandSocket", head.transform),
                ("muzzleLight", muzzleLight),
                ("defaultFieldOfView", 70f),
                ("shotMask", (1 << 0) | (1 << 7) | (1 << 9) | (1 << 10)));

            GrayboxKit.Wire(playerLight,
                ("inventory", inventory),
                ("input", input),
                ("beam", beam));

            GrayboxKit.Wire(spectator, ("character", character), ("input", input));

            // Added after CharacterSkin so it can find the bodies it built.
            var playerAnimator = root.AddComponent<PlayerAnimator>();
            GrayboxKit.Wire(playerAnimator,
                ("skin", skin),
                ("movement", movement),
                ("input", input),
                ("vitals", vitals),
                ("equipment", equipment));

            // Owner sees their own body: legs and arms when they look down, no head.
            var firstPersonBody = root.AddComponent<FirstPersonBody>();
            GrayboxKit.Wire(firstPersonBody,
                ("character", character),
                ("skin", skin));

            // Alt+5. Collides against Default only -- the interactable and creature layers
            // should not shove the camera about.
            var thirdPerson = root.AddComponent<ThirdPersonView>();
            GrayboxKit.Wire(thirdPerson,
                ("character", character),
                ("skin", skin),
                ("firstPersonBody", firstPersonBody),
                ("stationFocus", stationFocus),
                ("collisionMask", 1));

            GrayboxKit.Wire(character,
                ("playerCamera", camera),
                ("audioListener", listener),
                ("input", input),
                ("look", look),
                ("movement", movement),
                ("networkTransform", networkTransform));

            // Left empty on purpose: CharacterSkin owns renderer visibility now, because it
            // is the only thing that knows which body is currently active.
            var characterSo = new SerializedObject(character);
            characterSo.FindProperty("bodyRenderers").arraySize = 0;
            characterSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/Player/PlayerCharacter.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        const string CharacterFolder = "Assets/_Game/Art/Characters";
        const string PlayerCharacterModel = CharacterFolder + "/CH_AdventurerGirl.fbx";
        const string RosterPath = "Assets/_Game/Data/Characters/CharacterRoster.asset";

        /// <summary>
        /// Builds the roster from the importer's character table, so the list of models and
        /// the list of playable characters cannot drift apart.
        /// </summary>
        static CharacterRoster BuildCharacterRoster()
        {
            GrayboxKit.EnsureFolder("Assets/_Game/Data/Characters");

            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
            {
                roster = ScriptableObject.CreateInstance<CharacterRoster>();
                AssetDatabase.CreateAsset(roster, RosterPath);
            }

            var entries = new List<(string Model, string Display, bool Rigged, GameObject Prefab)>();

            foreach (var entry in CharacterImportSettings.Characters)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterFolder}/{entry.Model}.fbx");
                if (prefab == null)
                {
                    Debug.LogWarning($"Character model missing: {entry.Model}.fbx");
                    continue;
                }

                entries.Add((entry.Model, Prettify(entry.Model), entry.Rigged, prefab));
            }

            var so = new SerializedObject(roster);
            var list = so.FindProperty("characters");
            list.arraySize = entries.Count;

            for (var i = 0; i < entries.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = entries[i].Model;
                element.FindPropertyRelative("displayName").stringValue = entries[i].Display;
                element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].Prefab;
                element.FindPropertyRelative("rigged").boolValue = entries[i].Rigged;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(roster);
            return roster;
        }

        /// <summary>CH_AdventurerGirl -> "Adventurer Girl".</summary>
        static string Prettify(string modelName)
        {
            var stripped = modelName.StartsWith("CH_") ? modelName[3..] : modelName;
            var builder = new System.Text.StringBuilder();

            for (var i = 0; i < stripped.Length; i++)
            {
                if (i > 0 && char.IsUpper(stripped[i]) && !char.IsUpper(stripped[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(stripped[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Builds the visible body. Prefers the rigged character; falls back to a capsule so
        /// the scaffold never produces a player with nothing to look at.
        /// </summary>
        static GameObject BuildPlayerBody(Transform parent, Material fallbackMaterial,
            CharacterRoster roster, out List<GameObject> bodies)
        {
            bodies = new List<GameObject>();

            var root = GrayboxKit.Empty("Body", parent, Vector3.zero);

            if (roster != null && roster.Count > 0)
            {
                CharacterImportSettings.EnsureAllCharacterMaterials();

                for (var i = 0; i < roster.Count; i++)
                {
                    var entry = roster.Get(i);
                    if (entry.prefab == null)
                    {
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, root.transform);
                    instance.name = entry.id;

                    // Exported at 1.8 m with the feet on the origin, so every character lines
                    // up with the CharacterController without a per-model fudge factor.
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;

                    if (entry.rigged)
                    {
                        if (!instance.TryGetComponent<Animator>(out var animator))
                        {
                            animator = instance.AddComponent<Animator>();
                        }

                        animator.applyRootMotion = false;

                        // Remote players still need their bones updated while off screen,
                        // because their body is what everyone else is looking at.
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                        var controller = CharacterImportSettings.ControllerFor(entry.id);
                        if (controller != null)
                        {
                            animator.runtimeAnimatorController = controller;
                        }
                        else
                        {
                            Debug.LogWarning($"No animator controller for {entry.id}.");
                        }
                    }

                    // Only the first is active; CharacterSkin turns the chosen one on.
                    instance.SetActive(i == 0);
                    bodies.Add(instance);
                }

                if (bodies.Count > 0)
                {
                    return root;
                }
            }

            Debug.LogWarning("No character models found; falling back to the capsule placeholder.");

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "BodyPlaceholder";
            capsule.transform.SetParent(root.transform, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(capsule.GetComponent<Collider>());
            GrayboxKit.Paint(capsule, fallbackMaterial);
            bodies.Add(capsule);
            return root;
        }

        static GameObject BuildSessionScopePrefab()
        {
            var root = new GameObject("SessionScope");
            root.AddComponent<NetworkObject>();
            root.AddComponent<MissionDirector>();

            // Lives beside the director so it hears every phase change and survives the
            // train-to-forest load with it.
            root.AddComponent<CollectorContract>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/Network/SessionScope.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Everything the server can spawn at runtime: the session scope, dropped items,
        /// creatures and deployables. The player prefab is registered separately via
        /// NetworkConfig.PlayerPrefab, so it deliberately does not appear here.
        /// </summary>
        static NetworkPrefabsList BuildNetworkPrefabsList(List<GameObject> all)
        {
            var path = $"{NetworkDataFolder}/TransityNetworkPrefabs.asset";
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);

            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }

            // The backing field is internal, so this goes through SerializedObject rather
            // than NetworkPrefabsList.Add -- which also lets a re-run clear stale entries.
            var so = new SerializedObject(list);
            var array = so.FindProperty("List");
            array.arraySize = all.Count;
            for (var i = 0; i < all.Count; i++)
            {
                array.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue = all[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return list;
        }

        // --------------------------------------------------------------------- scenes

        static void BuildBootScene(ItemRegistry registry, CreatureRegistry creatures,
            ContractRegistry contracts, GameObject playerPrefab,
            GameObject sessionScopePrefab, NetworkPrefabsList prefabList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("Transity Systems");
            var bootstrap = systems.AddComponent<GameBootstrap>();

            // Straight into the depot, no menu. Flip with Tools > Transity > Startup Mode.
            GrayboxKit.Wire(bootstrap, ("startupMode", (int)StartupMode.OfflineHost));
            var content = systems.AddComponent<GameContent>();
            systems.AddComponent<SessionManager>();
            GrayboxKit.Wire(content,
                ("itemRegistry", registry),
                ("creatureRegistry", creatures),
                ("contractRegistry", contracts));

            // The score. Persistent, because it has to survive the scene load into the
            // forest and back without restarting.
            var music = new GameObject("TensionMusic");
            music.AddComponent<Transity.Audio.TensionMusic>();
            var musicRoot = music.AddComponent<PersistentRoot>();
            GrayboxKit.Wire(musicRoot, ("uniqueKey", "TensionMusic"));

            // NetworkManager must stay a root object: it only calls DontDestroyOnLoad on
            // itself when it has no parent.
            var networkRoot = new GameObject("NetworkManager");
            var networkManager = networkRoot.AddComponent<NetworkManager>();
            var transport = networkRoot.AddComponent<UnityTransport>();
            var serverBootstrap = networkRoot.AddComponent<ServerBootstrap>();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabList };
            EditorUtility.SetDirty(networkManager);

            GrayboxKit.Wire(serverBootstrap, ("sessionScopePrefab", sessionScopePrefab));

            BuildPersistentHud();
            BuildStationScreenHost();
            BuildBootSplash();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.32f);

            SaveSceneWithNetworkIds(scene, SceneCatalog.Boot);
        }

        static void BuildStationScreenHost()
        {
            // The one EventSystem for the whole game, created here so it exists before any
            // scene that needs it and survives every load.
            var events = new GameObject("EventSystem",
                typeof(EventSystem), typeof(InputSystemUIInputModule));
            var persistent = events.AddComponent<PersistentRoot>();
            GrayboxKit.Wire(persistent, ("uniqueKey", "EventSystem"));

            // Builds its own canvas (and an EventSystem when none exists) at runtime, so it
            // needs nothing wired here.
            var host = new GameObject("StationScreens");
            host.AddComponent<StationScreenUI>();

            // Renders the player's character for the wardrobe. Pointing this at a different
            // prefab is all a skin swap will need.
            var preview = host.AddComponent<CharacterPreview>();
            var previewRoster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);

            if (previewRoster != null && previewRoster.Count > 0)
            {
                GrayboxKit.Wire(preview,
                    ("roster", previewRoster),
                    ("characterPrefab", previewRoster.Get(0).prefab));
            }
            else
            {
                Debug.LogWarning("No character roster for the wardrobe preview.");
            }
        }

        static void BuildBootSplash()
        {
            var cameraObject = new GameObject("Boot Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var canvas = CreateCanvas("Boot Canvas", persistent: false);
            CreateLabel(canvas.transform, "Loading", "Preparing expedition...", 28,
                Vector2.zero, new Vector2(600f, 60f), TextAnchor.MiddleCenter);
        }

        static void BuildPersistentHud()
        {
            var canvas = CreateCanvas("HUD", persistent: true);
            var hud = canvas.gameObject.AddComponent<HudUI>();

            // Prompt sits on a soft plate so it stays readable against a bright wall or a
            // dark corner, rather than white text hoping for the best.
            var promptPlate = CreatePlate(canvas.transform, "PromptPlate",
                new Vector2(0f, -96f), new Vector2(460f, 46f), new Color(0f, 0f, 0f, 0.55f));
            var prompt = CreateLabel(promptPlate, "Prompt", string.Empty, 21,
                Vector2.zero, new Vector2(440f, 46f), TextAnchor.MiddleCenter);
            Stretch(prompt.rectTransform, 8f);

            var messagePlate = CreatePlate(canvas.transform, "MessagePlate",
                new Vector2(0f, -150f), new Vector2(520f, 40f), new Color(0.25f, 0.05f, 0.05f, 0.6f));
            var message = CreateLabel(messagePlate, "Message", string.Empty, 19,
                Vector2.zero, new Vector2(500f, 40f), TextAnchor.MiddleCenter);
            Stretch(message.rectTransform, 8f);
            message.color = new Color(1f, 0.72f, 0.62f);

            var session = CreateLabel(canvas.transform, "SessionCode", string.Empty, 17,
                Vector2.zero, new Vector2(340f, 28f), TextAnchor.UpperLeft);
            session.color = new Color(0.78f, 0.82f, 0.86f, 0.85f);
            Anchor(session.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -22f));

            var phase = CreateLabel(canvas.transform, "Phase", string.Empty, 17,
                Vector2.zero, new Vector2(340f, 28f), TextAnchor.UpperRight);
            phase.color = new Color(0.95f, 0.78f, 0.45f, 0.9f);
            Anchor(phase.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -22f));

            // Four ticks around a centre dot: legible on any background, and the gap gives
            // the eye something to aim with.
            var crosshairRoot = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            crosshairRoot.transform.SetParent(canvas.transform, false);
            var crosshair = crosshairRoot.GetComponent<Image>();
            crosshair.color = new Color(1f, 1f, 1f, 0.5f);
            var crosshairRect = crosshairRoot.GetComponent<RectTransform>();
            crosshairRect.sizeDelta = new Vector2(3f, 3f);
            crosshairRect.anchoredPosition = Vector2.zero;

            var ticks = new (Vector2 offset, Vector2 size)[]
            {
                (new Vector2(0f, 9f), new Vector2(2f, 7f)),
                (new Vector2(0f, -9f), new Vector2(2f, 7f)),
                (new Vector2(-9f, 0f), new Vector2(7f, 2f)),
                (new Vector2(9f, 0f), new Vector2(7f, 2f))
            };

            foreach (var (offset, size) in ticks)
            {
                var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
                tick.transform.SetParent(crosshairRoot.transform, false);
                tick.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
                var tickRect = tick.GetComponent<RectTransform>();
                tickRect.sizeDelta = size;
                tickRect.anchoredPosition = offset;
            }

            GrayboxKit.Wire(hud,
                ("promptLabel", prompt),
                ("sessionLabel", session),
                ("phaseLabel", phase),
                ("messageLabel", message),
                ("crosshair", crosshair),
                ("promptPlate", promptPlate.GetComponent<Image>()),
                ("messagePlate", messagePlate.GetComponent<Image>()));
        }

        /// <summary>A rounded-ish backing plate for HUD text.</summary>
        static RectTransform CreatePlate(Transform parent, string plateName,
            Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(plateName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static void BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);

            // No EventSystem here on purpose. Boot creates a persistent one, and a second
            // in this scene is what produced the "there are 2 event systems" warning.

            var canvas = CreateCanvas("Menu Canvas", persistent: false);
            var menu = canvas.gameObject.AddComponent<MainMenuUI>();

            CreateLabel(canvas.transform, "Title", "TRANSITY", 52,
                new Vector2(0f, 190f), new Vector2(700f, 80f), TextAnchor.MiddleCenter);

            var hostButton = CreateButton(canvas.transform, "HostButton", "Host expedition",
                new Vector2(0f, 80f), new Vector2(320f, 50f));

            var codeField = CreateInputField(canvas.transform, "CodeField", "Join code",
                new Vector2(0f, 10f), new Vector2(320f, 44f));

            var joinButton = CreateButton(canvas.transform, "JoinButton", "Join with code",
                new Vector2(0f, -50f), new Vector2(320f, 50f));

            var quitButton = CreateButton(canvas.transform, "QuitButton", "Quit",
                new Vector2(0f, -120f), new Vector2(320f, 40f));

            var status = CreateLabel(canvas.transform, "Status", "Connecting to Unity services...", 18,
                new Vector2(0f, -210f), new Vector2(760f, 80f), TextAnchor.MiddleCenter);

            GrayboxKit.Wire(menu,
                ("hostButton", hostButton),
                ("joinButton", joinButton),
                ("quitButton", quitButton),
                ("codeField", codeField),
                ("statusLabel", status));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.32f);

            SaveSceneWithNetworkIds(scene, SceneCatalog.MainMenu);
        }

        /// <summary>
        /// Rebuilds the depot without discarding the scene.
        ///
        /// Rather than starting from an empty scene, this opens the existing TrainHub and
        /// deletes only the roots it generated last time. Everything else -- anything you
        /// added yourself, and in particular anything under "_Handmade" -- is left exactly
        /// where it was.
        ///
        /// The limit is worth being clear about: objects *inside* a generated root are still
        /// replaced, because that whole subtree is rebuilt from code. Nudging a generated
        /// wall will not survive. Put your own work at the top level or under "_Handmade".
        /// </summary>
        static void BuildTrainHubScene(ItemRegistry registry)
        {
            var path = $"{ScenesFolder}/{SceneCatalog.TrainHub}.unity";
            var scene = System.IO.File.Exists(path)
                ? EditorSceneManager.OpenScene(path, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var generated = new HashSet<string>(DepotBlockout.GeneratedRoots);
            var removed = 0;
            var kept = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (generated.Contains(root.name))
                {
                    Object.DestroyImmediate(root);
                    removed++;
                }
                else
                {
                    kept++;
                }
            }

            DepotBlockout.Build(registry);
            EnsureHandmadeRoot(scene);

            if (kept > 0)
            {
                Debug.Log($"<b>Transity</b>: depot rebuilt. Replaced {removed} generated root(s), " +
                          $"kept {kept} of your own.");
            }

            SaveSceneWithNetworkIds(scene, SceneCatalog.TrainHub);
        }

        /// <summary>
        /// Creates the parking spot for hand-made content if it does not exist yet, so there
        /// is an obvious safe place to put things.
        /// </summary>
        static void EnsureHandmadeRoot(UnityEngine.SceneManagement.Scene scene)
        {
            // Scene roots rather than GameObject.Find, which ignores inactive objects and
            // would happily create a second one.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == DepotBlockout.HandmadeRoot)
                {
                    return;
                }
            }

            var handmade = new GameObject(DepotBlockout.HandmadeRoot);
            handmade.transform.SetSiblingIndex(0);
        }

        static void BuildForestScene(ItemRegistry registry)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var groundMaterial = GrayboxKit.SolidMaterial("GB_Ground", new Color(0.20f, 0.26f, 0.20f));
            var trunkMaterial = GrayboxKit.SolidMaterial("GB_Trunk", new Color(0.24f, 0.19f, 0.15f));
            var canopyMaterial = GrayboxKit.SolidMaterial("GB_Canopy", new Color(0.16f, 0.30f, 0.20f));
            var rockMaterial = GrayboxKit.SolidMaterial("GB_Rock", new Color(0.35f, 0.36f, 0.38f));

            GrayboxKit.Box("Ground", null, new Vector3(0f, -0.5f, 0f), new Vector3(160f, 1f, 160f), groundMaterial);

            var sunObject = new GameObject("Directional Light");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.68f, 0.76f, 0.88f);
            sun.intensity = 0.85f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(38f, 155f, 0f);

            // Fixed seed so everyone blocks out against the same forest.
            var random = new System.Random(20260829);
            var trees = GrayboxKit.Empty("Trees", null, Vector3.zero);

            for (var i = 0; i < 220; i++)
            {
                var x = (float)(random.NextDouble() * 150.0 - 75.0);
                var z = (float)(random.NextDouble() * 150.0 - 75.0);

                // Keep the landing area clear.
                if (new Vector2(x, z).magnitude < 12f)
                {
                    continue;
                }

                var height = 5f + (float)random.NextDouble() * 6f;
                var tree = GrayboxKit.Empty($"Tree_{i}", trees.transform, new Vector3(x, 0f, z));

                // Trunks keep their colliders: they block movement, break line of sight and
                // are what the NavMesh bake carves around.
                GrayboxKit.Cylinder("Trunk", tree.transform, new Vector3(0f, height * 0.5f, 0f),
                    new Vector3(0.5f, height * 0.5f, 0.5f), trunkMaterial);

                // Canopies do not. Six metres up they block nothing a player can reach, and
                // leaving them solid gives the bake a flat roof to call walkable -- which is
                // how creatures end up pathing through the treetops.
                var canopy = GrayboxKit.Box("Canopy", tree.transform, new Vector3(0f, height + 1.2f, 0f),
                    new Vector3(3.4f, 3.2f, 3.4f), canopyMaterial);
                GrayboxKit.Decorative(canopy);
            }

            var rocks = GrayboxKit.Empty("Rocks", null, Vector3.zero);
            for (var i = 0; i < 30; i++)
            {
                var x = (float)(random.NextDouble() * 140.0 - 70.0);
                var z = (float)(random.NextDouble() * 140.0 - 70.0);
                var size = 1f + (float)random.NextDouble() * 2.5f;
                GrayboxKit.Box($"Rock_{i}", rocks.transform, new Vector3(x, size * 0.3f, z),
                    new Vector3(size, size * 0.6f, size), rockMaterial);
            }

            var spawnRoot = GrayboxKit.Empty("SpawnPoints", null, Vector3.zero);
            for (var i = 0; i < 4; i++)
            {
                var angle = i * 90f * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 2.5f, 0f, Mathf.Sin(angle) * 2.5f);
                var point = GrayboxKit.Empty($"ExpeditionSpawn_{i}", spawnRoot.transform, position);
                point.transform.rotation = Quaternion.LookRotation(-position.normalized, Vector3.up);
                var spawn = point.AddComponent<PlayerSpawnPoint>();
                GrayboxKit.Wire(spawn, ("context", (int)SpawnContext.Expedition));
            }

            // Stand-in for the waiting train.
            var extractionMaterial = GrayboxKit.SolidMaterial("GB_Extraction", new Color(0.9f, 0.75f, 0.35f));
            var extraction = GrayboxKit.Box("ExtractionPoint", null, new Vector3(0f, 1f, 8f),
                new Vector3(2f, 2f, 2f), extractionMaterial);
            extraction.layer = InteractableLayer;
            extraction.AddComponent<NetworkObject>();
            var extractionComponent = extraction.AddComponent<ExtractionPoint>();
            GrayboxKit.Wire(extractionComponent, ("prompt", "Board the train"), ("interactionRange", 3.5f));

            BuildSupplyCache(registry);
            BuildCreatureRegions(random);
            BuildForestDirector();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.19f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.34f, 0.40f, 0.44f);
            RenderSettings.fogDensity = 0.022f;

            // Save first: the NavMesh bake writes an asset beside the scene file, so the
            // scene has to exist on disk before it runs.
            SaveSceneWithNetworkIds(scene, SceneCatalog.Forest);
            BakeForestNavMesh(scene);
        }

        /// <summary>
        /// A crate of gear beside the landing zone.
        ///
        /// Deliberately generous and deliberately temporary. Arriving in the forest with an
        /// empty pack means the only thing to test is walking, so this exists so a hunt can
        /// be played the moment the scene loads. When the depot economy is the real way to
        /// equip a crew, delete the call -- everything here is bought at the quartermaster.
        /// </summary>
        static void BuildSupplyCache(ItemRegistry registry)
        {
            var root = GrayboxKit.Empty("SupplyCache", null, new Vector3(0f, 0f, 5f));

            var crateMaterial = GrayboxKit.SolidMaterial("GB_Cache", new Color(0.35f, 0.32f, 0.22f));
            var pallet = GrayboxKit.Box("Pallet", root.transform, new Vector3(0f, 0.1f, 0f),
                new Vector3(3.4f, 0.2f, 2.2f), crateMaterial);
            GrayboxKit.MarkStatic(pallet);

            // One of everything a hunt needs: something to shoot with, something to shoot,
            // something to heal with, something to place, and something to see by.
            var wanted = new[]
            {
                "Compact Carbine", "Pump Shotgun", "9mm Field Pistol", "Hunter Crossbow",
                "Tranquilizer Pistol", "Ammo", "Ammo",
                "Medical Kit", "Trauma Kit", "Adrenaline Injector",
                "Bear Trap", "Motion Sensor Alarm", "Creature Bait Canister",
                "Basic Flashlight", "Heavy Flashlight", "Glow Stick", "Glow Stick",
                "Night-Vision Goggles", "Thermal Monocular", "Hunter Body Camera"
            };

            var placed = 0;
            var missing = new List<string>();

            for (var i = 0; i < wanted.Length; i++)
            {
                var definition = registry.Items.FirstOrDefault(
                    item => item != null && item.DisplayName == wanted[i]);

                if (definition == null || definition.WorldPrefab == null)
                {
                    missing.Add(wanted[i]);
                    continue;
                }

                // Two rows along the pallet, laid out rather than piled, so every item is
                // separately visible and separately pickable.
                var column = placed / 2;
                var rowSide = placed % 2 == 0 ? -0.45f : 0.45f;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.WorldPrefab, root.transform);
                instance.transform.localPosition = new Vector3(-1.45f + column * 0.32f, 0.28f, rowSide);
                instance.transform.localRotation = Quaternion.Euler(0f, placed * 37f, 0f);

                foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.layer = InteractableLayer;
                }

                placed++;
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning("Supply cache could not place: " + string.Join(", ", missing) +
                                 ". Check the display names against the item registry.");
            }

            Debug.Log($"<b>Transity</b>: supply cache placed with {placed} item(s).");
        }

        /// <summary>
        /// Places the patches creatures start in, spread around the ring beyond the
        /// landing area so nothing spawns where the crew can see it arrive.
        /// </summary>
        static void BuildCreatureRegions(System.Random random)
        {
            var root = GrayboxKit.Empty("CreatureRegions", null, Vector3.zero);

            // Spread from close to far. The director refuses any region nearer than its
            // minimum, so on a real expedition the near ones are simply never picked --
            // they exist for the sandbox, where walking 45 m before the test begins is
            // just dead time.
            for (var i = 0; i < 8; i++)
            {
                var angle = (i / 8f) * Mathf.PI * 2f + (float)random.NextDouble() * 0.4f;
                var distance = 24f + (i / 8f) * 42f + (float)random.NextDouble() * 6f;
                var position = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                var region = GrayboxKit.Empty($"Region_{i}", root.transform, position);
                var component = region.AddComponent<CreatureSpawnRegion>();
                GrayboxKit.Wire(component, ("radius", 16f));
            }
        }

        static void BuildForestDirector()
        {
            var go = new GameObject("ForestDirector");
            go.AddComponent<NetworkObject>();

            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = 1;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            var director = go.AddComponent<ForestDirector>();
            GrayboxKit.Wire(director,
                ("navMesh", surface),
                ("minimumSpawnDistanceFromCrew", 45f),
                ("wipeGraceSeconds", 4f));
        }

        /// <summary>
        /// Bakes the forest NavMesh. Creatures are NavMeshAgents, so without this every
        /// one of them spawns and stands still -- the single most likely way for this
        /// whole feature to look broken.
        /// </summary>
        static void BakeForestNavMesh(UnityEngine.SceneManagement.Scene scene)
        {
            NavMeshSurface surface = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                surface = root.GetComponentInChildren<NavMeshSurface>(true);
                if (surface != null)
                {
                    break;
                }
            }

            if (surface == null)
            {
                Debug.LogWarning("No NavMeshSurface in the forest; creatures will bake one at runtime.");
                return;
            }

            // Agents are up to 0.8 m across and 2.6 m tall (the Stilt Stalker), so the bake
            // has to clear both or the tall one cannot path anywhere the others can.
            var settings = surface.GetBuildSettings();
            settings.agentRadius = 0.5f;
            settings.agentHeight = 2.8f;
            settings.agentSlope = 45f;
            settings.agentClimb = 0.5f;

            surface.agentTypeID = settings.agentTypeID;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.2f;

            surface.BuildNavMesh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{SceneCatalog.Forest}.unity");

            Debug.Log("<b>Transity</b>: forest NavMesh baked.");
        }

        /// <summary>
        /// Saves a generated scene twice on purpose. NetworkObject can only compute its
        /// GlobalObjectIdHash once the object exists in a saved scene, so the first save
        /// gives it an identity, the regeneration pass fills the hash in, and the second
        /// save persists it. Skip this and nothing in the scene ever spawns.
        /// </summary>
        static void SaveSceneWithNetworkIds(UnityEngine.SceneManagement.Scene scene, string sceneName)
        {
            var path = $"{ScenesFolder}/{sceneName}.unity";

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);

            var count = NetworkIdFixup.RefreshScene(scene);
            if (count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, path);
            }
        }

        static void ApplyBuildSettings()
        {
            EditorBuildSettings.scenes = SceneCatalog.All
                .Select(name => new EditorBuildSettingsScene($"{ScenesFolder}/{name}.unity", true))
                .ToArray();
        }

        // ----------------------------------------------------------------- ui helpers

        static Canvas CreateCanvas(string canvasName, bool persistent)
        {
            var go = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (persistent)
            {
                var root = go.AddComponent<PersistentRoot>();
                GrayboxKit.Wire(root, ("uniqueKey", canvasName));
            }

            return canvas;
        }

        static Text CreateLabel(Transform parent, string labelName, string content, int fontSize,
            Vector2 anchoredPosition, Vector2 size, TextAnchor alignment)
        {
            var go = new GameObject(labelName, typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return text;
        }

        static Button CreateButton(Transform parent, string buttonName, string label,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(buttonName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            go.GetComponent<Image>().color = new Color(0.18f, 0.20f, 0.24f, 0.95f);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var text = CreateLabel(go.transform, "Label", label, 22, Vector2.zero, size, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);

            return go.GetComponent<Button>();
        }

        static InputField CreateInputField(Transform parent, string fieldName, string placeholder,
            Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(fieldName, typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 0.95f);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            var text = CreateLabel(go.transform, "Text", string.Empty, 20, Vector2.zero, size, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 10f);
            text.supportRichText = false;

            var hint = CreateLabel(go.transform, "Placeholder", placeholder, 20, Vector2.zero, size, TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, 10f);
            hint.color = new Color(1f, 1f, 1f, 0.4f);
            hint.fontStyle = FontStyle.Italic;

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = 12;
            field.characterValidation = InputField.CharacterValidation.Alphanumeric;
            return field;
        }

        static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        static void Anchor(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPosition;
        }

    }
}
