using System.IO;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Renders every piece of equipment to a single contact sheet.
    ///
    /// Exists because an atlas-per-asset pipeline fails quietly: a model with the wrong UVs
    /// or a missing texture still imports, still spawns and still has a collider. It only
    /// looks wrong, and nothing in the console says so. One image of all of it is the
    /// cheapest way to see that an art drop actually landed.
    /// </summary>
    public static class EquipmentContactSheet
    {
        const int Cell = 256;
        const int Columns = 7;
        const string OutputPath = "Assets/_Game/Art/Equipment/EquipmentContactSheet.png";

        [MenuItem("Tools/Transity/Render Equipment Contact Sheet", priority = 47)]
        public static void Render()
        {
            var entries = EquipmentCatalog.Entries;
            var rows = Mathf.CeilToInt(entries.Count / (float)Columns);

            var sheet = new Texture2D(Columns * Cell, rows * Cell, TextureFormat.RGBA32, false);
            var background = new Color(0.12f, 0.12f, 0.14f, 1f);

            var clear = new Color[sheet.width * sheet.height];
            for (var i = 0; i < clear.Length; i++)
            {
                clear[i] = background;
            }

            sheet.SetPixels(clear);

            var stage = new GameObject("~EquipmentStage");
            var cameraObject = new GameObject("~EquipmentCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.orthographic = true;

            // Two lights so the silhouette reads and the unlit side is not solid black.
            var keyObject = new GameObject("~Key");
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.5f;
            keyObject.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            var fillObject = new GameObject("~Fill");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.5f;
            fillObject.transform.rotation = Quaternion.Euler(10f, -40f, 0f);

            var target = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            camera.targetTexture = target;

            var readback = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false);
            var rendered = 0;

            try
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var model = EquipmentImportSettings.LoadModel(entry.Model);

                    if (model == null)
                    {
                        Debug.LogWarning($"Contact sheet: no model for {entry.Model}.");
                        continue;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, stage.transform);
                    instance.transform.localPosition = Vector3.zero;

                    // Three-quarter view: shows the front face and one side, which is what
                    // makes a wrong-cell UV obvious.
                    instance.transform.localRotation = Quaternion.Euler(0f, 145f, 0f);

                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                    {
                        Object.DestroyImmediate(instance);
                        continue;
                    }

                    var bounds = renderers[0].bounds;
                    for (var r = 1; r < renderers.Length; r++)
                    {
                        bounds.Encapsulate(renderers[r].bounds);
                    }

                    // Frame each item to its own size, so a glow stick and a rifle both fill
                    // the cell. Absolute scale is not what this sheet is checking.
                    var extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    camera.orthographicSize = extent * 1.25f;
                    camera.transform.position = bounds.center + new Vector3(0f, extent * 0.55f, -extent * 4f);
                    camera.transform.LookAt(bounds.center);
                    camera.nearClipPlane = 0.001f;
                    camera.farClipPlane = extent * 12f;

                    camera.Render();

                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;

                    var column = i % Columns;
                    var row = i / Columns;

                    // Texture2D is bottom-up, so the first row has to land at the top.
                    sheet.SetPixels(column * Cell, (rows - 1 - row) * Cell, Cell, Cell,
                        readback.GetPixels());

                    Object.DestroyImmediate(instance);
                    rendered++;
                }

                sheet.Apply();
                File.WriteAllBytes(OutputPath, sheet.EncodeToPNG());
                AssetDatabase.ImportAsset(OutputPath);

                Debug.Log($"<b>Transity</b>: contact sheet rendered {rendered}/{entries.Count} " +
                          $"items to {OutputPath}.");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(keyObject);
                Object.DestroyImmediate(fillObject);
            }
        }
    }
}
