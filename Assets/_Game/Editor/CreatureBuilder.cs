using System.Collections.Generic;
using Transity.Audio;
using Transity.Combat;
using Transity.Creatures;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Transity.EditorTools
{
    /// <summary>
    /// The three creatures, as data and as graybox bodies.
    ///
    /// A body is a rig of empties -- torso, head, four legs, tail -- each carrying a
    /// primitive and a hitbox. CreatureBody animates the empties; the collider on each
    /// primitive follows. The rig is the contract with the art pass: a real model drops
    /// in under the same empties and inherits the whole behaviour.
    /// </summary>
    public static class CreatureBuilder
    {
        const string DataFolder = "Assets/_Game/Data/Creatures";
        const string PrefabFolder = "Assets/_Game/Prefabs/Creatures";
        const int CreatureLayer = 9;

        public struct Result
        {
            public CreatureRegistry Registry;
            public List<GameObject> Prefabs;
            public Dictionary<string, CreatureDefinition> Definitions;
        }

        public static Result BuildAll()
        {
            GrayboxKit.EnsureFolder(DataFolder);
            GrayboxKit.EnsureFolder(PrefabFolder);

            var definitions = new Dictionary<string, CreatureDefinition>
            {
                ["mossback"] = Mossback(),
                ["stalker"] = StiltStalker(),
                ["hound"] = BrambleHound()
            };

            var prefabs = new List<GameObject>();
            var entries = new List<(CreatureDefinition definition, GameObject prefab)>();

            foreach (var pair in definitions)
            {
                var prefab = BuildPrefab(pair.Value);
                prefabs.Add(prefab);
                entries.Add((pair.Value, prefab));
            }

            var registry = BuildRegistry(entries);
            AssetDatabase.SaveAssets();

            return new Result { Registry = registry, Prefabs = prefabs, Definitions = definitions };
        }

        // ------------------------------------------------------------------ definitions

        static CreatureDefinition Mossback() => Define("Creature_Mossback", d =>
        {
            d.id = "creature.mossback";
            d.displayName = "Mossback";
            d.fieldNotes = "Bear-sized, moss-plated, territorial. Slow to notice you and impossible to shake off once it has. Leaves its patch reluctantly. The plate on its back is the soft spot.";
            d.temperament = Temperament.Territorial;
            d.shape = BodyShape.Quadruped;
            d.bodyLength = 2.6f; d.bodyHeight = 1.3f; d.bodyWidth = 1.15f; d.legLength = 0.95f;
            d.color = new Color(0.24f, 0.3f, 0.18f); d.eyeColor = new Color(1f, 0.6f, 0.15f);
            d.weakPointLabel = "moss plate"; d.weakPointMultiplier = 3f;
            d.maxHealth = 520f; d.regenWhileRecovering = 7f; d.fleeHealthFraction = 0.28f; d.recoveredHealthFraction = 0.7f;
            d.sedationThreshold = 130f; d.sedationDecay = 3.5f; d.collapseSeconds = 45f;
            d.walkSpeed = 1.4f; d.stalkSpeed = 2.2f; d.runSpeed = 6.8f; d.acceleration = 12f; d.turnSpeedDegrees = 160f;
            d.agentRadius = 0.8f; d.agentHeight = 2f; d.roamRadius = 26f;
            d.sightRange = 28f; d.sightAngle = 120f; d.hearing = 1.1f; d.secondsToNotice = 2.6f;
            d.awarenessDecayPerSecond = 0.06f; d.loseInterestSeconds = 18f; d.territoryRadius = 38f;
            d.attackRange = 2.7f; d.attackDamage = 38f; d.attackWindup = 0.65f; d.lungeSpeed = 12f; d.lungeSeconds = 0.34f;
            d.attackRecovery = 0.8f; d.attackCooldown = 1.6f; d.bleedChance = 0.6f;
            d.stalkDistance = 14f; d.boldness = 0.5f; d.skittishness = 0.05f;
            d.packSize = 1;
            d.bountyKill = 500; d.bountyCapture = 1100;
            d.voice = SoundKind.GrowlLow; d.alarmCall = SoundKind.Screech; d.voicePitch = 0.75f; d.footstepVolume = 1f;
        });

        static CreatureDefinition StiltStalker() => Define("Creature_StiltStalker", d =>
        {
            d.id = "creature.stiltstalker";
            d.displayName = "Stilt Stalker";
            d.fieldNotes = "Tall, pale, quiet. Follows at the edge of the torchlight and only closes when you look away. Fast, fragile, and it screams when it commits. Aim for the throat.";
            d.temperament = Temperament.Hunter;
            d.shape = BodyShape.Stilt;
            d.bodyLength = 1.7f; d.bodyHeight = 0.7f; d.bodyWidth = 0.55f; d.legLength = 1.8f;
            d.color = new Color(0.62f, 0.6f, 0.55f); d.eyeColor = new Color(0.7f, 0.95f, 1f);
            d.weakPointLabel = "throat sac"; d.weakPointMultiplier = 3f;
            d.maxHealth = 260f; d.regenWhileRecovering = 9f; d.fleeHealthFraction = 0.35f; d.recoveredHealthFraction = 0.75f;
            d.sedationThreshold = 90f; d.sedationDecay = 5f; d.collapseSeconds = 40f;
            d.walkSpeed = 1.8f; d.stalkSpeed = 2.8f; d.runSpeed = 8f; d.acceleration = 18f; d.turnSpeedDegrees = 300f;
            d.agentRadius = 0.5f; d.agentHeight = 2.6f; d.roamRadius = 40f;
            d.sightRange = 40f; d.sightAngle = 150f; d.hearing = 1.4f; d.secondsToNotice = 1.6f;
            d.awarenessDecayPerSecond = 0.05f; d.loseInterestSeconds = 22f; d.territoryRadius = 0f;
            d.attackRange = 2.9f; d.attackDamage = 26f; d.attackWindup = 0.45f; d.lungeSpeed = 14f; d.lungeSeconds = 0.3f;
            d.attackRecovery = 0.6f; d.attackCooldown = 1.1f; d.bleedChance = 0.45f;
            d.stalkDistance = 20f; d.boldness = 0.25f; d.skittishness = 0.3f;
            d.packSize = 1;
            d.bountyKill = 450; d.bountyCapture = 1000;
            d.voice = SoundKind.Breath; d.alarmCall = SoundKind.Screech; d.voicePitch = 1.45f; d.footstepVolume = 0.5f;
        });

        static CreatureDefinition BrambleHound() => Define("Creature_BrambleHound", d =>
        {
            d.id = "creature.bramblehound";
            d.displayName = "Bramble Hound";
            d.fieldNotes = "Dog-sized and never alone. A pack spreads out and comes from the sides; one on its own barks and keeps its distance. Loud noises scatter them, briefly.";
            d.temperament = Temperament.Pack;
            d.shape = BodyShape.Hound;
            d.bodyLength = 1.3f; d.bodyHeight = 0.62f; d.bodyWidth = 0.48f; d.legLength = 0.5f;
            d.color = new Color(0.28f, 0.2f, 0.14f); d.eyeColor = new Color(1f, 0.9f, 0.3f);
            d.weakPointLabel = "flank"; d.weakPointMultiplier = 2f;
            d.maxHealth = 120f; d.regenWhileRecovering = 8f; d.fleeHealthFraction = 0.4f; d.recoveredHealthFraction = 0.8f;
            d.sedationThreshold = 50f; d.sedationDecay = 6f; d.collapseSeconds = 30f;
            d.walkSpeed = 2f; d.stalkSpeed = 3f; d.runSpeed = 9f; d.acceleration = 22f; d.turnSpeedDegrees = 360f;
            d.agentRadius = 0.4f; d.agentHeight = 1f; d.roamRadius = 30f;
            d.sightRange = 26f; d.sightAngle = 140f; d.hearing = 1.3f; d.secondsToNotice = 1.4f;
            d.awarenessDecayPerSecond = 0.1f; d.loseInterestSeconds = 12f; d.territoryRadius = 0f;
            d.attackRange = 1.9f; d.attackDamage = 16f; d.attackWindup = 0.35f; d.lungeSpeed = 10f; d.lungeSeconds = 0.28f;
            d.attackRecovery = 0.5f; d.attackCooldown = 0.9f; d.bleedChance = 0.35f;
            d.stalkDistance = 10f; d.boldness = 0.4f; d.skittishness = 0.5f;
            d.packSize = 3; d.flankRadius = 6f; d.packCohesionRadius = 14f;
            d.bountyKill = 150; d.bountyCapture = 300;
            d.voice = SoundKind.Bark; d.alarmCall = SoundKind.Bark; d.voicePitch = 1.1f; d.footstepVolume = 0.55f;
        });

        static CreatureDefinition Define(string assetName, System.Action<CreatureDefinition> configure)
        {
            var path = $"{DataFolder}/{assetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CreatureDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ------------------------------------------------------------------ prefabs

        static GameObject BuildPrefab(CreatureDefinition definition)
        {
            var safe = definition.displayName.Replace(" ", string.Empty);
            var hide = GrayboxKit.SolidMaterial($"GB_Creature_{safe}", definition.color, 0.15f);
            var eyeMaterial = EyeMaterial(definition);

            var root = new GameObject($"CR_{safe}");
            root.layer = CreatureLayer;

            root.AddComponent<NetworkObject>();
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.Interpolate = true;
            networkTransform.SyncScaleX = false;
            networkTransform.SyncScaleY = false;
            networkTransform.SyncScaleZ = false;

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = definition.agentRadius;
            agent.height = definition.agentHeight;
            agent.speed = definition.walkSpeed;
            agent.acceleration = definition.acceleration;
            agent.angularSpeed = 0f;
            agent.stoppingDistance = definition.attackRange * 0.6f;
            agent.baseOffset = 0f;

            var health = root.AddComponent<Health>();
            GrayboxKit.Wire(health,
                ("maxHealth", definition.maxHealth),
                ("regenPerSecond", 0f),
                ("bleedPerSecond", 2f),
                ("bleedDuration", 25f),
                ("damageTakenMultiplier", 1f));

            var sedation = root.AddComponent<Sedation>();
            GrayboxKit.Wire(sedation,
                ("threshold", definition.sedationThreshold),
                ("decayPerSecond", definition.sedationDecay),
                ("collapseSeconds", definition.collapseSeconds),
                ("decayDelay", 3f));

            // ---- rig ----
            var hipHeight = definition.legLength;
            var torso = GrayboxKit.Empty("Torso", root.transform, new Vector3(0f, hipHeight + definition.bodyHeight * 0.5f, 0f));

            var torsoMesh = Part("TorsoMesh", torso.transform, Vector3.zero,
                new Vector3(definition.bodyWidth, definition.bodyHeight, definition.bodyLength), hide, 1f, false);

            Part("WeakPlate", torso.transform,
                new Vector3(0f, definition.bodyHeight * 0.5f + 0.05f, -definition.bodyLength * 0.18f),
                new Vector3(definition.bodyWidth * 0.62f, 0.12f, definition.bodyLength * 0.42f),
                GrayboxKit.SolidMaterial($"GB_CreatureWeak_{safe}", Color.Lerp(definition.color, new Color(0.6f, 0.2f, 0.2f), 0.45f), 0.3f),
                definition.weakPointMultiplier, true);

            var head = GrayboxKit.Empty("Head", torso.transform,
                new Vector3(0f, definition.bodyHeight * 0.22f, definition.bodyLength * 0.5f + definition.bodyLength * 0.12f));
            var headSize = new Vector3(definition.bodyWidth * 0.55f, definition.bodyHeight * 0.5f, definition.bodyLength * 0.3f);
            Part("HeadMesh", head.transform, new Vector3(0f, 0f, headSize.z * 0.35f), headSize, hide, 1.4f, false);

            var eye = GrayboxKit.Empty("Eye", head.transform, new Vector3(0f, headSize.y * 0.15f, headSize.z * 0.85f));

            var eyes = new List<Renderer>();
            foreach (var side in new[] { -1f, 1f })
            {
                var eyeball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eyeball.name = side < 0 ? "EyeL" : "EyeR";
                eyeball.transform.SetParent(head.transform, false);
                eyeball.transform.localPosition = new Vector3(side * headSize.x * 0.32f, headSize.y * 0.2f, headSize.z * 0.8f);
                eyeball.transform.localScale = Vector3.one * Mathf.Max(0.07f, headSize.x * 0.16f);
                eyeball.layer = CreatureLayer;
                Object.DestroyImmediate(eyeball.GetComponent<Collider>());
                GrayboxKit.Paint(eyeball, eyeMaterial);
                eyes.Add(eyeball.GetComponent<Renderer>());
            }

            var legs = new List<Transform>();
            var legX = definition.bodyWidth * 0.38f;
            var legZ = definition.bodyLength * 0.36f;
            var legThickness = definition.shape == BodyShape.Stilt ? 0.09f : Mathf.Max(0.12f, definition.bodyWidth * 0.2f);

            foreach (var (name, x, z) in new[]
                     {
                         ("Leg_FL", -legX, legZ), ("Leg_FR", legX, legZ),
                         ("Leg_BL", -legX, -legZ), ("Leg_BR", legX, -legZ)
                     })
            {
                var pivot = GrayboxKit.Empty(name, root.transform, new Vector3(x, hipHeight, z));
                Part("Mesh", pivot.transform, new Vector3(0f, -hipHeight * 0.5f, 0f),
                    new Vector3(legThickness, hipHeight, legThickness), hide, 0.7f, false);
                legs.Add(pivot.transform);
            }

            var tail = GrayboxKit.Empty("Tail", torso.transform, new Vector3(0f, definition.bodyHeight * 0.1f, -definition.bodyLength * 0.5f));
            var tailMesh = GrayboxKit.Box("Mesh", tail.transform, new Vector3(0f, 0f, -definition.bodyLength * 0.2f),
                new Vector3(legThickness * 0.8f, legThickness * 0.8f, definition.bodyLength * 0.4f), hide);
            GrayboxKit.Decorative(tailMesh);
            tailMesh.layer = CreatureLayer;

            // ---- behaviour ----
            var brain = root.AddComponent<CreatureBrain>();
            GrayboxKit.Wire(brain,
                ("definition", definition),
                ("eye", eye.transform),
                ("occlusionMask", 1));

            var creatureBody = root.AddComponent<CreatureBody>();
            GrayboxKit.Wire(creatureBody,
                ("brain", brain),
                ("torso", torso.transform),
                ("head", head.transform),
                ("tail", tail.transform),
                ("strideLength", Mathf.Max(0.6f, definition.legLength * 1.5f)));

            var bodySo = new SerializedObject(creatureBody);
            var legArray = bodySo.FindProperty("legs");
            legArray.arraySize = legs.Count;
            for (var i = 0; i < legs.Count; i++)
            {
                legArray.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            }

            var eyeArray = bodySo.FindProperty("eyes");
            eyeArray.arraySize = eyes.Count;
            for (var i = 0; i < eyes.Count; i++)
            {
                eyeArray.GetArrayElementAtIndex(i).objectReferenceValue = eyes[i];
            }

            bodySo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<CreatureAudio>();

            var capture = root.AddComponent<CreatureCapture>();
            GrayboxKit.Wire(capture,
                ("brain", brain),
                ("prompt", "Creature"),
                ("interactionRange", 3.2f));

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = CreatureLayer;
            }

            var path = $"{PrefabFolder}/CR_{safe}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>A primitive with a collider and a hitbox.</summary>
        static GameObject Part(string partName, Transform parent, Vector3 position, Vector3 size, Material material,
            float multiplier, bool weakPoint)
        {
            var part = GrayboxKit.Box(partName, parent, position, size, material);
            part.layer = CreatureLayer;

            var hitbox = part.AddComponent<Hitbox>();
            GrayboxKit.Wire(hitbox, ("damageMultiplier", multiplier), ("weakPoint", weakPoint));
            return part;
        }

        static Material EyeMaterial(CreatureDefinition definition)
        {
            var safe = definition.displayName.Replace(" ", string.Empty);
            var material = GrayboxKit.SolidMaterial($"GB_CreatureEye_{safe}", definition.eyeColor * 0.3f, 0.8f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", definition.eyeColor * 1.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static CreatureRegistry BuildRegistry(List<(CreatureDefinition definition, GameObject prefab)> entries)
        {
            var path = $"{DataFolder}/CreatureRegistry.asset";
            var registry = AssetDatabase.LoadAssetAtPath<CreatureRegistry>(path);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<CreatureRegistry>();
                AssetDatabase.CreateAsset(registry, path);
            }

            var so = new SerializedObject(registry);
            var list = so.FindProperty("entries");
            list.arraySize = entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("definition").objectReferenceValue = entries[i].definition;
                element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            return registry;
        }
    }
}
