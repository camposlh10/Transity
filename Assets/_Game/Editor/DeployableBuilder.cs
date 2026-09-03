using System.Collections.Generic;
using Transity.Combat;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Graybox prefabs for the things players put down in the forest. Each is keyed by
    /// the item id that deploys it, so the tuning table can find its prefab.
    ///
    /// Layers matter here. The interaction ray only tests layer 6, so anything that can
    /// be picked back up has its small solid collider there; the large detection
    /// triggers live on a child on layer 10 so a trap does not prompt "pick up" from
    /// seven metres away.
    /// </summary>
    public static class DeployableBuilder
    {
        const string Folder = "Assets/_Game/Prefabs/Deployables";
        const int InteractableLayer = 6;
        const int DeployableLayer = 10;

        public static Dictionary<string, GameObject> BuildAll()
        {
            GrayboxKit.EnsureFolder(Folder);

            return new Dictionary<string, GameObject>
            {
                ["item.beartrap"] = BuildBearTrap(),
                ["item.hd.motionsensoralarm"] = BuildMotionAlarm(),
                ["item.hd.creaturebaitcanister"] = BuildBait(),
                ["item.hd.glowstick"] = BuildGlowStick()
            };
        }

        static GameObject BuildBearTrap()
        {
            var steel = GrayboxKit.SolidMaterial("GB_TrapSteel", new Color(0.32f, 0.33f, 0.35f), 0.5f);

            var root = new GameObject("DEP_BearTrap");
            SetLayer(root, InteractableLayer);
            root.AddComponent<NetworkObject>();
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;

            var plate = GrayboxKit.Cylinder("Plate", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.7f, 0.02f, 0.7f), steel);
            GrayboxKit.Decorative(plate);
            SetLayer(plate, InteractableLayer);

            var pickup = root.AddComponent<BoxCollider>();
            pickup.center = new Vector3(0f, 0.08f, 0f);
            pickup.size = new Vector3(0.7f, 0.16f, 0.7f);

            var hingeLeft = GrayboxKit.Empty("HingeLeft", root.transform, new Vector3(-0.3f, 0.04f, 0f));
            var jawLeft = GrayboxKit.Box("JawLeft", hingeLeft.transform, new Vector3(0.15f, 0f, 0f), new Vector3(0.3f, 0.03f, 0.55f), steel);
            GrayboxKit.Decorative(jawLeft);

            var hingeRight = GrayboxKit.Empty("HingeRight", root.transform, new Vector3(0.3f, 0.04f, 0f));
            var jawRight = GrayboxKit.Box("JawRight", hingeRight.transform, new Vector3(-0.15f, 0f, 0f), new Vector3(0.3f, 0.03f, 0.55f), steel);
            GrayboxKit.Decorative(jawRight);

            // Teeth so it reads as a trap from across a clearing.
            for (var i = 0; i < 4; i++)
            {
                var z = -0.2f + i * 0.13f;
                GrayboxKit.Decorative(GrayboxKit.Box($"ToothL{i}", jawLeft.transform, new Vector3(0.45f, 1.6f, z / 0.55f), new Vector3(0.12f, 2.4f, 0.08f), steel));
                GrayboxKit.Decorative(GrayboxKit.Box($"ToothR{i}", jawRight.transform, new Vector3(-0.45f, 1.6f, z / 0.55f), new Vector3(0.12f, 2.4f, 0.08f), steel));
            }

            var trigger = GrayboxKit.Empty("Trigger", root.transform, new Vector3(0f, 0.15f, 0f));
            SetLayer(trigger, DeployableLayer);
            var sphere = trigger.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            sphere.isTrigger = true;

            var trap = root.AddComponent<BearTrap>();
            GrayboxKit.Wire(trap,
                ("prompt", "Pick up bear trap"),
                ("interactionRange", 2.5f),
                ("jawLeft", hingeLeft.transform),
                ("jawRight", hingeRight.transform));

            return Save(root, "DEP_BearTrap");
        }

        static GameObject BuildMotionAlarm()
        {
            var shell = GrayboxKit.SolidMaterial("GB_AlarmShell", new Color(0.25f, 0.3f, 0.22f), 0.3f);
            var led = EmissiveMaterial("GB_AlarmLed", new Color(1f, 0.25f, 0.1f));

            var root = new GameObject("DEP_MotionAlarm");
            SetLayer(root, InteractableLayer);
            root.AddComponent<NetworkObject>();
            root.AddComponent<Rigidbody>().isKinematic = true;

            var housing = GrayboxKit.Box("Housing", root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.22f, 0.36f, 0.16f), shell);
            GrayboxKit.Decorative(housing);
            SetLayer(housing, InteractableLayer);

            var pickup = root.AddComponent<BoxCollider>();
            pickup.center = new Vector3(0f, 0.18f, 0f);
            pickup.size = new Vector3(0.24f, 0.36f, 0.18f);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "Led";
            indicator.transform.SetParent(root.transform, false);
            indicator.transform.localPosition = new Vector3(0f, 0.34f, 0.09f);
            indicator.transform.localScale = Vector3.one * 0.05f;
            Object.DestroyImmediate(indicator.GetComponent<Collider>());
            GrayboxKit.Paint(indicator, led);
            SetLayer(indicator, InteractableLayer);

            var trigger = GrayboxKit.Empty("Trigger", root.transform, new Vector3(0f, 0.5f, 0f));
            SetLayer(trigger, DeployableLayer);
            var sphere = trigger.AddComponent<SphereCollider>();
            sphere.radius = 7f;
            sphere.isTrigger = true;

            var alarm = root.AddComponent<MotionAlarm>();
            GrayboxKit.Wire(alarm,
                ("prompt", "Pick up motion alarm"),
                ("interactionRange", 2.5f),
                ("indicator", indicator.GetComponent<Renderer>()));

            return Save(root, "DEP_MotionAlarm");
        }

        static GameObject BuildBait()
        {
            var can = GrayboxKit.SolidMaterial("GB_BaitCan", new Color(0.45f, 0.4f, 0.25f), 0.4f);

            var root = new GameObject("DEP_BaitCanister");
            SetLayer(root, DeployableLayer);
            root.AddComponent<NetworkObject>();
            root.AddComponent<Rigidbody>().isKinematic = true;

            var body = GrayboxKit.Cylinder("Can", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.24f, 0.15f, 0.24f), can);
            SetLayer(body, DeployableLayer);

            var glowObject = GrayboxKit.Empty("Glow", root.transform, new Vector3(0f, 0.35f, 0f));
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.9f, 0.6f, 0.2f);
            glow.range = 3f;
            glow.intensity = 1.5f;

            var bait = root.AddComponent<BaitCanister>();
            GrayboxKit.Wire(bait, ("prompt", "Bait"), ("interactable", false), ("glow", glow));

            return Save(root, "DEP_BaitCanister");
        }

        static GameObject BuildGlowStick()
        {
            var tube = EmissiveMaterial("GB_GlowStick", new Color(0.3f, 1f, 0.35f));

            var root = new GameObject("DEP_GlowStick");
            SetLayer(root, DeployableLayer);
            root.AddComponent<NetworkObject>();

            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.Interpolate = true;

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            body.linearDamping = 0.4f;
            body.angularDamping = 1.5f;

            root.AddComponent<NetworkRigidbody>();

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.02f;
            capsule.height = 0.18f;
            capsule.direction = 2;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.name = "Tube";
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mesh.transform.localScale = new Vector3(0.04f, 0.09f, 0.04f);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            GrayboxKit.Paint(mesh, tube);
            SetLayer(mesh, DeployableLayer);

            var glowObject = GrayboxKit.Empty("Glow", root.transform, Vector3.zero);
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.4f, 1f, 0.45f);
            glow.range = 8f;
            glow.intensity = 5f;
            glow.shadows = LightShadows.None;

            var stick = root.AddComponent<GlowStickLight>();
            GrayboxKit.Wire(stick, ("prompt", "Glow stick"), ("interactable", false), ("glow", glow));

            return Save(root, "DEP_GlowStick");
        }

        // ------------------------------------------------------------------ helpers

        static Material EmissiveMaterial(string materialName, Color color)
        {
            var material = GrayboxKit.SolidMaterial(materialName, color * 0.4f, 0.6f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * 3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
        }

        static GameObject Save(GameObject root, string prefabName)
        {
            var path = $"{Folder}/{prefabName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
