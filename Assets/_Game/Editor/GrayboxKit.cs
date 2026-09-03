using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Small helpers for throwing together blockout geometry and the flat material palette
    /// that goes with it. Everything here is temporary by design -- it exists so the loop
    /// can be played before any real art exists.
    /// </summary>
    public static class GrayboxKit
    {
        public const string MaterialFolder = "Assets/_Game/Art/Materials";

        public static Material SolidMaterial(string materialName, Color color, float smoothness = 0.05f)
        {
            var path = $"{MaterialFolder}/{materialName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("URP Lit shader not found; falling back to the default material.");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = materialName };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);

            EnsureFolder(MaterialFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static GameObject Box(string boxName, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = boxName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Paint(go, material);
            return go;
        }

        public static GameObject Cylinder(string cylinderName, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = cylinderName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Paint(go, material);
            return go;
        }

        public static void Paint(GameObject go, Material material)
        {
            if (material != null && go.TryGetComponent<Renderer>(out var renderer))
            {
                renderer.sharedMaterial = material;
            }
        }

        public static GameObject Empty(string emptyName, Transform parent, Vector3 position)
        {
            var go = new GameObject(emptyName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        /// <summary>Snaps to the construction grid: 1 m for architecture, 0.25 m for props.</summary>
        public static Vector3 Snap(Vector3 position, float grid)
        {
            return new Vector3(
                Mathf.Round(position.x / grid) * grid,
                Mathf.Round(position.y / grid) * grid,
                Mathf.Round(position.z / grid) * grid);
        }

        /// <summary>
        /// Flags architecture for batching, occlusion and baked GI. The room targets mostly
        /// baked lighting and a few hundred draw calls, and neither happens without this.
        /// </summary>
        public static void MarkStatic(GameObject go)
        {
            const StaticEditorFlags flags = StaticEditorFlags.ContributeGI
                                            | StaticEditorFlags.BatchingStatic
                                            | StaticEditorFlags.OccluderStatic
                                            | StaticEditorFlags.OccludeeStatic
                                            | StaticEditorFlags.ReflectionProbeStatic;

            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, flags);
            }
        }

        /// <summary>Strips colliders. Small decorative objects should not have collision.</summary>
        public static void Decorative(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        /// <summary>
        /// Assigns private [SerializeField] fields by name, so components stay encapsulated
        /// instead of exposing setters purely for generated content.
        /// </summary>
        public static void Wire(Object target, params (string field, object value)[] assignments)
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
                    case Vector2 v:
                        property.vector2Value = v;
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

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            var parts = assetFolder.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
