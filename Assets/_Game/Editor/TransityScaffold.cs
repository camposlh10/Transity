using System.Collections.Generic;
using System.Linq;
using Transity.Core;
using Transity.Interaction;
using Transity.Inventory;
using Transity.Missions;
using Transity.Networking;
using Transity.Player;
using Transity.Train;
using Transity.UI;
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

        [MenuItem("Tools/Transity/Build Vertical Slice Scaffold", priority = 0)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            GrayboxKit.EnsureFolder(ScenesFolder);
            GrayboxKit.EnsureFolder(ItemDataFolder);
            GrayboxKit.EnsureFolder(NetworkDataFolder);
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Player");
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Interactables");
            GrayboxKit.EnsureFolder($"{PrefabsFolder}/Network");

            var registry = BuildItems();
            var playerPrefab = BuildPlayerPrefab();
            var sessionScopePrefab = BuildSessionScopePrefab();
            var itemPrefabs = BuildItemPrefabs(registry);
            var prefabList = BuildNetworkPrefabsList(sessionScopePrefab, itemPrefabs);

            BuildBootScene(registry, playerPrefab, sessionScopePrefab, prefabList);
            BuildMainMenuScene();
            BuildTrainHubScene(registry);
            BuildForestScene();
            ApplyBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene($"{ScenesFolder}/{SceneCatalog.Boot}.unity");
            Debug.Log("<b>Transity</b>: scaffold built. Open the Boot scene and press Play, " +
                      "or use Multiplayer Play Mode for a second client.");
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

        static ItemRegistry BuildItems()
        {
            // The items the first build ships with. Flashlight and radio are free starting
            // kit; everything else is purchased and therefore at risk on an expedition.
            var specs = new (string id, string label, int price, bool atRisk)[]
            {
                ("item.flashlight", "Flashlight", 0, false),
                ("item.radio", "Radio", 0, false),
                ("item.rifle", "Rifle", 450, true),
                ("item.ammunition", "Ammunition", 60, true),
                ("item.medkit", "Medical Kit", 120, true),
                ("item.beartrap", "Bear Trap", 200, true),
                ("item.bait", "Bait", 75, true),
                ("item.container", "Containment Case", 300, true)
            };

            var definitions = new List<ItemDefinition>();

            foreach (var spec in specs)
            {
                var path = $"{ItemDataFolder}/{spec.label.Replace(" ", string.Empty)}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ItemDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                Wire(definition,
                    ("itemId", spec.id),
                    ("displayName", spec.label),
                    ("price", spec.price),
                    ("atRiskOnExpedition", spec.atRisk));

                definitions.Add(definition);
            }

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

                var safeName = definition.DisplayName.Replace(" ", string.Empty);
                var path = $"{PrefabsFolder}/Interactables/WorldItem_{safeName}.prefab";

                var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                root.name = $"WorldItem_{safeName}";
                root.transform.localScale = new Vector3(0.3f, 0.2f, 0.45f);
                GrayboxKit.Paint(root, crateMaterial);

                root.AddComponent<NetworkObject>();
                var worldItem = root.AddComponent<WorldItem>();

                Wire(worldItem,
                    ("definition", definition),
                    ("prompt", $"Pick up {definition.DisplayName}"),
                    ("interactionRange", 2.5f));

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);

                Wire(definition, ("worldPrefab", prefab));
                prefabs.Add(prefab);
            }

            return prefabs;
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

            root.AddComponent<NetworkObject>();

            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.InLocalSpace = false;
            networkTransform.Interpolate = true;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            // Visible body, hidden from its owner by PlayerCharacter.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            GrayboxKit.Paint(body, bodyMaterial);

            var head = GrayboxKit.Empty("Head", root.transform, new Vector3(0f, 1.65f, 0f));

            var cameraObject = GrayboxKit.Empty("PlayerCamera", head.transform, Vector3.zero);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 70f;
            var listener = cameraObject.AddComponent<AudioListener>();

            var dropOrigin = GrayboxKit.Empty("DropOrigin", head.transform, new Vector3(0f, -0.2f, 0.4f));

            var input = root.AddComponent<PlayerInputReader>();
            var look = root.AddComponent<PlayerLook>();
            var movement = root.AddComponent<FirstPersonController>();
            var interactor = root.AddComponent<Interactor>();
            var inventory = root.AddComponent<InventoryComponent>();
            var hotbar = root.AddComponent<PlayerHotbarInput>();
            root.AddComponent<PlayerFeedback>();
            var character = root.AddComponent<PlayerCharacter>();

            Wire(input, ("actions", actions), ("actionMapName", "Player"));
            Wire(look, ("body", root.transform), ("headPivot", head.transform), ("input", input));
            Wire(movement, ("input", input), ("headPivot", head.transform));
            Wire(interactor, ("input", input), ("rayOrigin", cameraObject.transform));
            Wire(inventory, ("dropOrigin", dropOrigin.transform));
            Wire(hotbar, ("input", input), ("inventory", inventory));

            Wire(character,
                ("playerCamera", camera),
                ("audioListener", listener),
                ("input", input),
                ("look", look),
                ("movement", movement),
                ("networkTransform", networkTransform));

            var characterSo = new SerializedObject(character);
            var renderers = characterSo.FindProperty("bodyRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = body.GetComponent<Renderer>();
            characterSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/Player/PlayerCharacter.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildSessionScopePrefab()
        {
            var root = new GameObject("SessionScope");
            root.AddComponent<NetworkObject>();
            root.AddComponent<MissionDirector>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/Network/SessionScope.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static NetworkPrefabsList BuildNetworkPrefabsList(GameObject sessionScope, List<GameObject> items)
        {
            var path = $"{NetworkDataFolder}/TransityNetworkPrefabs.asset";
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);

            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, path);
            }

            // The player prefab is registered separately via NetworkConfig.PlayerPrefab,
            // so it deliberately does not appear here.
            var all = new List<GameObject> { sessionScope };
            all.AddRange(items);

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

        static void BuildBootScene(ItemRegistry registry, GameObject playerPrefab,
            GameObject sessionScopePrefab, NetworkPrefabsList prefabList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("Transity Systems");
            systems.AddComponent<GameBootstrap>();
            var content = systems.AddComponent<GameContent>();
            systems.AddComponent<SessionManager>();
            Wire(content, ("itemRegistry", registry));

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

            Wire(serverBootstrap, ("sessionScopePrefab", sessionScopePrefab));

            BuildPersistentHud();
            BuildBootSplash();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.32f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{SceneCatalog.Boot}.unity");
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

            var prompt = CreateLabel(canvas.transform, "Prompt", string.Empty, 22,
                new Vector2(0f, -90f), new Vector2(700f, 40f), TextAnchor.MiddleCenter);

            var message = CreateLabel(canvas.transform, "Message", string.Empty, 20,
                new Vector2(0f, -140f), new Vector2(700f, 40f), TextAnchor.MiddleCenter);
            message.color = new Color(1f, 0.6f, 0.5f);

            var session = CreateLabel(canvas.transform, "SessionCode", string.Empty, 18,
                Vector2.zero, new Vector2(320f, 30f), TextAnchor.UpperLeft);
            Anchor(session.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f));

            var phase = CreateLabel(canvas.transform, "Phase", string.Empty, 18,
                Vector2.zero, new Vector2(320f, 30f), TextAnchor.UpperRight);
            Anchor(phase.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -20f));

            var crosshairObject = new GameObject("Crosshair", typeof(Image));
            crosshairObject.transform.SetParent(canvas.transform, false);
            var crosshair = crosshairObject.GetComponent<Image>();
            crosshair.color = new Color(1f, 1f, 1f, 0.35f);
            var crosshairRect = crosshairObject.GetComponent<RectTransform>();
            crosshairRect.sizeDelta = new Vector2(6f, 6f);
            crosshairRect.anchoredPosition = Vector2.zero;

            Wire(hud,
                ("promptLabel", prompt),
                ("sessionLabel", session),
                ("phaseLabel", phase),
                ("messageLabel", message),
                ("crosshair", crosshair));
        }

        static void BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(null);

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

            Wire(menu,
                ("hostButton", hostButton),
                ("joinButton", joinButton),
                ("quitButton", quitButton),
                ("codeField", codeField),
                ("statusLabel", status));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.32f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{SceneCatalog.MainMenu}.unity");
        }

        static void BuildTrainHubScene(ItemRegistry registry)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var floorMaterial = GrayboxKit.SolidMaterial("GB_TrainFloor", new Color(0.30f, 0.24f, 0.20f));
            var wallMaterial = GrayboxKit.SolidMaterial("GB_TrainWall", new Color(0.42f, 0.33f, 0.26f));
            var trimMaterial = GrayboxKit.SolidMaterial("GB_TrainTrim", new Color(0.62f, 0.48f, 0.30f), 0.25f);

            var root = new GameObject("TrainCar");

            // A single car: 3.4 m wide, 12 m long, 2.8 m tall.
            GrayboxKit.Box("Floor", root.transform, new Vector3(0f, -0.1f, 0f), new Vector3(3.2f, 0.2f, 12f), floorMaterial);
            GrayboxKit.Box("Ceiling", root.transform, new Vector3(0f, 2.7f, 0f), new Vector3(3.2f, 0.2f, 12f), wallMaterial);
            GrayboxKit.Box("Wall_West", root.transform, new Vector3(-1.7f, 1.3f, 0f), new Vector3(0.2f, 2.8f, 12f), wallMaterial);
            GrayboxKit.Box("Wall_East", root.transform, new Vector3(1.7f, 1.3f, 0f), new Vector3(0.2f, 2.8f, 12f), wallMaterial);
            GrayboxKit.Box("Wall_North", root.transform, new Vector3(0f, 1.3f, 6.1f), new Vector3(3.6f, 2.8f, 0.2f), wallMaterial);
            GrayboxKit.Box("Wall_South", root.transform, new Vector3(0f, 1.3f, -6.1f), new Vector3(3.6f, 2.8f, 0.2f), wallMaterial);

            GrayboxKit.Box("Table", root.transform, new Vector3(0f, 0.75f, 2.5f), new Vector3(1.2f, 0.1f, 2f), trimMaterial);
            GrayboxKit.Box("Bench_West", root.transform, new Vector3(-1.2f, 0.45f, -2f), new Vector3(0.6f, 0.1f, 3f), trimMaterial);
            GrayboxKit.Box("Bench_East", root.transform, new Vector3(1.2f, 0.45f, -2f), new Vector3(0.6f, 0.1f, 3f), trimMaterial);
            GrayboxKit.Box("Lockers", root.transform, new Vector3(-1.3f, 1f, 4.8f), new Vector3(0.6f, 2f, 1.6f), trimMaterial);

            // Warm interior light, deliberately in contrast with the cold forest.
            var lightObject = GrayboxKit.Empty("Interior Light", root.transform, new Vector3(0f, 2.4f, 0f));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.82f, 0.6f);
            light.intensity = 3.2f;
            light.range = 16f;
            light.shadows = LightShadows.Soft;

            var spawnRoot = GrayboxKit.Empty("SpawnPoints", null, Vector3.zero);
            var spawnPositions = new[]
            {
                new Vector3(-0.7f, 0f, -3.5f),
                new Vector3(0.7f, 0f, -3.5f),
                new Vector3(-0.7f, 0f, -4.6f),
                new Vector3(0.7f, 0f, -4.6f)
            };

            for (var i = 0; i < spawnPositions.Length; i++)
            {
                var point = GrayboxKit.Empty($"TrainSpawn_{i}", spawnRoot.transform, spawnPositions[i]);
                var spawn = point.AddComponent<PlayerSpawnPoint>();
                Wire(spawn, ("context", (int)SpawnContext.Train));
            }

            var leverMaterial = GrayboxKit.SolidMaterial("GB_Lever", new Color(0.85f, 0.3f, 0.25f));
            var lever = GrayboxKit.Box("DepartureLever", null, new Vector3(0f, 1.2f, 5.6f),
                new Vector3(0.25f, 0.5f, 0.25f), leverMaterial);
            lever.AddComponent<NetworkObject>();
            var leverComponent = lever.AddComponent<DepartureLever>();
            Wire(leverComponent, ("prompt", "Depart on expedition"), ("interactionRange", 2.5f));

            // One door, to prove interaction replicates to every client.
            var doorRoot = GrayboxKit.Empty("Door", null, new Vector3(-1.4f, 0f, 0f));
            doorRoot.AddComponent<NetworkObject>();
            var doorComponent = doorRoot.AddComponent<SimpleDoor>();
            var hinge = GrayboxKit.Empty("Hinge", doorRoot.transform, Vector3.zero);
            GrayboxKit.Box("Panel", hinge.transform, new Vector3(0f, 1f, 0.45f),
                new Vector3(0.1f, 2f, 0.9f), trimMaterial);
            Wire(doorComponent, ("hinge", hinge.transform), ("prompt", "door"), ("interactionRange", 2.5f));

            // Loose equipment, to test pickup.
            PlaceWorldItem(registry, "Flashlight", new Vector3(0f, 0.9f, 2.5f));
            PlaceWorldItem(registry, "Medical Kit", new Vector3(0.4f, 0.9f, 2.9f));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.19f, 0.16f);
            RenderSettings.fog = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{SceneCatalog.TrainHub}.unity");
        }

        static void BuildForestScene()
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
                GrayboxKit.Cylinder("Trunk", tree.transform, new Vector3(0f, height * 0.5f, 0f),
                    new Vector3(0.5f, height * 0.5f, 0.5f), trunkMaterial);
                GrayboxKit.Box("Canopy", tree.transform, new Vector3(0f, height + 1.2f, 0f),
                    new Vector3(3.4f, 3.2f, 3.4f), canopyMaterial);
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
                Wire(spawn, ("context", (int)SpawnContext.Expedition));
            }

            // Stand-in for the waiting train.
            var extractionMaterial = GrayboxKit.SolidMaterial("GB_Extraction", new Color(0.9f, 0.75f, 0.35f));
            var extraction = GrayboxKit.Box("ExtractionPoint", null, new Vector3(0f, 1f, 8f),
                new Vector3(2f, 2f, 2f), extractionMaterial);
            extraction.AddComponent<NetworkObject>();
            var extractionComponent = extraction.AddComponent<ExtractionPoint>();
            Wire(extractionComponent, ("prompt", "Board the train"), ("interactionRange", 3.5f));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.19f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.34f, 0.40f, 0.44f);
            RenderSettings.fogDensity = 0.022f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesFolder}/{SceneCatalog.Forest}.unity");
        }

        static void PlaceWorldItem(ItemRegistry registry, string displayName, Vector3 position)
        {
            var definition = registry.Items.FirstOrDefault(i => i != null && i.DisplayName == displayName);
            if (definition == null || definition.WorldPrefab == null)
            {
                Debug.LogWarning($"Could not place '{displayName}': no definition or world prefab.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.WorldPrefab);
            instance.transform.position = position;
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
                Wire(root, ("uniqueKey", canvasName));
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

        /// <summary>
        /// Assigns private [SerializeField] fields by name, so components can stay properly
        /// encapsulated instead of exposing setters just for this generator.
        /// </summary>
        static void Wire(Object target, params (string field, object value)[] assignments)
        {
            var so = new SerializedObject(target);

            foreach (var (field, value) in assignments)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'.");
                    continue;
                }

                switch (value)
                {
                    case null:
                        property.objectReferenceValue = null;
                        break;
                    case string s:
                        property.stringValue = s;
                        break;
                    case int i when property.propertyType == SerializedPropertyType.Enum:
                        property.enumValueIndex = i;
                        break;
                    case int i:
                        property.intValue = i;
                        break;
                    case float f:
                        property.floatValue = f;
                        break;
                    case bool b:
                        property.boolValue = b;
                        break;
                    case Object o:
                        property.objectReferenceValue = o;
                        break;
                    default:
                        Debug.LogError($"Unsupported value type for '{field}': {value.GetType().Name}");
                        break;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
