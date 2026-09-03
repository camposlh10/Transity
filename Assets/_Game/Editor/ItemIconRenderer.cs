using System.IO;
using Transity.Inventory;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Renders each item's held model to a small sprite for the hotbar. Same approach as
    /// the equipment contact sheet, per item, with a transparent background.
    /// </summary>
    public static class ItemIconRenderer
    {
        const string Folder = "Assets/_Game/Art/Icons";
        const int Size = 128;

        public static void RenderAll(ItemRegistry registry)
        {
            GrayboxKit.EnsureFolder(Folder);

            var stage = new GameObject("~IconStage");
            var cameraObject = new GameObject("~IconCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;

            var keyObject = new GameObject("~IconKey");
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            keyObject.transform.rotation = Quaternion.Euler(40f, 150f, 0f);

            var fillObject = new GameObject("~IconFill");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.6f;
            fillObject.transform.rotation = Quaternion.Euler(10f, -50f, 0f);

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var rendered = 0;
            var paths = new System.Collections.Generic.List<(ItemDefinition definition, string path)>();

            try
            {
                foreach (var definition in registry.Items)
                {
                    if (definition == null || definition.HeldModel == null)
                    {
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.HeldModel, stage.transform);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.Euler(0f, 140f, 0f);

                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                    {
                        Object.DestroyImmediate(instance);
                        continue;
                    }

                    var bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }

                    var extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    camera.orthographicSize = extent * 1.15f;
                    camera.transform.position = bounds.center + new Vector3(0f, extent * 0.5f, -extent * 4f);
                    camera.transform.LookAt(bounds.center);
                    camera.nearClipPlane = 0.001f;
                    camera.farClipPlane = extent * 12f;
                    camera.Render();

                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;

                    var safe = definition.DisplayName.Replace(" ", string.Empty).Replace("-", string.Empty);
                    var path = $"{Folder}/ICON_{safe}.png";
                    File.WriteAllBytes(path, readback.EncodeToPNG());
                    paths.Add((definition, path));

                    Object.DestroyImmediate(instance);
                    rendered++;
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(keyObject);
                Object.DestroyImmediate(fillObject);
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (_, path) in paths)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach (var (definition, path) in paths)
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = Size;
                    importer.SaveAndReimport();
                }

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                GrayboxKit.Wire(definition, ("icon", sprite));
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<b>Transity</b>: rendered {rendered} item icons.");
        }
    }
}
