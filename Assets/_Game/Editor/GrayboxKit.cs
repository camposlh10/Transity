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
