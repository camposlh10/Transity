using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Particle dressing for the depot: hearth fire, rising embers, drifting dust in the
    /// light shafts, and steam venting on the platform.
    ///
    /// Everything here is deliberately cheap -- small emission rates, no collision, no
    /// lights attached to particles, and all of it non-looping-free so it costs nothing when
    /// off screen. The room reads as occupied without spending the frame budget the creature
    /// work will need later.
    /// </summary>
    public static class DepotAtmosphere
    {
        const string MaterialFolder = "Assets/_Game/Art/Materials";

        /// <summary>
        /// Rebuilds just the particle materials and their sprite.
        ///
        /// The particle systems in a scene reference these assets, so refreshing them fixes
        /// existing effects in place -- no scene rebuild, and nothing hand-placed is lost.
        /// </summary>
        [MenuItem("Tools/Transity/Rebuild FX Materials", priority = 45)]
        public static void RebuildMaterials()
        {
            ParticleMaterial("FX_Fire_Particle", new Color(1f, 0.45f, 0.12f, 1f), true);
            ParticleMaterial("FX_Ember_Particle", new Color(1f, 0.6f, 0.2f, 1f), true);
            ParticleMaterial("FX_Dust_Particle", new Color(1f, 0.95f, 0.85f, 0.5f), false);
            ParticleMaterial("FX_Steam_Particle", new Color(0.85f, 0.9f, 0.95f, 0.35f), false);

            AssetDatabase.SaveAssets();
            Debug.Log("<b>Transity</b>: FX materials rebuilt with the soft particle sprite.");
        }

        public static void Build(Transform parent, Vector3 fireplacePosition,
            Vector3 platformPosition, float roomWidth, float roomDepth, float roomHeight)
        {
            var root = GrayboxKit.Empty("FX_Atmosphere", parent, Vector3.zero);

            BuildHearthFire(root.transform, fireplacePosition);
            BuildEmbers(root.transform, fireplacePosition);
            BuildDust(root.transform, roomWidth, roomDepth, roomHeight);
            BuildPlatformSteam(root.transform, platformPosition);
        }

        // ------------------------------------------------------------------ materials

        const string FxFolder = "Assets/_Game/Art/FX";
        const string SoftCirclePath = FxFolder + "/FX_SoftCircle.png";

        /// <summary>
        /// A soft radial falloff sprite. Without one, every particle is an untextured
        /// billboard -- a hard opaque square -- which is what makes procedural smoke and
        /// fire read as flickering boxes rather than anything atmospheric.
        /// </summary>
        static Texture2D EnsureSoftCircle()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftCirclePath);
            if (existing != null)
            {
                return existing;
            }

            GrayboxKit.EnsureFolder(FxFolder);

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var centre = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Smooth to zero at the edge, with a soft shoulder so it does not band.
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha = alpha * alpha * (3f - 2f * alpha);

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(SoftCirclePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(SoftCirclePath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(SoftCirclePath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 128;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(SoftCirclePath);
        }

        static Material ParticleMaterial(string materialName, Color color, bool additive)
        {
            var path = $"{MaterialFolder}/{materialName}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                return null;
            }

            // Reconfigure rather than early-out: an existing material from a previous run
            // may predate the sprite and would stay a hard square forever.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var material = existing != null ? existing : new Material(shader) { name = materialName };
            material.shader = shader;

            var sprite = EnsureSoftCircle();
            if (sprite != null)
            {
                material.SetTexture("_BaseMap", sprite);
            }

            material.SetColor("_BaseColor", color);

            // Surface Type = Transparent, Blend = Additive or Alpha.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 1f : 0f);
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (additive)
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.DisableKeyword("_ALPHAMODULATE_ON");
            }
            else
            {
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        static ParticleSystem CreateSystem(Transform parent, string systemName, Vector3 position,
            Material material, int sortingOrder = 0)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var system = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.alignment = ParticleSystemRenderSpace.View;

            return system;
        }

        // ---------------------------------------------------------------------- hearth

        static void BuildHearthFire(Transform parent, Vector3 fireplacePosition)
        {
            var material = ParticleMaterial("FX_Fire_Particle", new Color(1f, 0.45f, 0.12f, 1f), true);
            var system = CreateSystem(parent, "FX_HearthFire",
                fireplacePosition + new Vector3(0f, 0.45f, -0.35f), material, 10);

            var main = system.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.15f, 0.85f), new Color(1f, 0.28f, 0.05f, 0.85f));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var emission = system.emission;
            emission.rateOverTime = 26f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.75f, 0.08f, 0.28f);

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(new Color(1f, 0.6f, 0.2f), new Color(0.7f, 0.12f, 0.02f));

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        }

        static void BuildEmbers(Transform parent, Vector3 fireplacePosition)
        {
            var material = ParticleMaterial("FX_Ember_Particle", new Color(1f, 0.6f, 0.2f, 1f), true);
            var system = CreateSystem(parent, "FX_Embers",
                fireplacePosition + new Vector3(0f, 0.6f, -0.3f), material, 11);

            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.65f, 0.25f, 1f));
            main.gravityModifier = -0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = system.emission;
            emission.rateOverTime = 7f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.7f, 0.1f, 0.25f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.3f;

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = Fade(new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.25f, 0.05f));
        }

        // ------------------------------------------------------------------------ dust

        static void BuildDust(Transform parent, float roomWidth, float roomDepth, float roomHeight)
        {
            var material = ParticleMaterial("FX_Dust_Particle", new Color(1f, 0.95f, 0.85f, 0.5f), false);
            var system = CreateSystem(parent, "FX_DustMotes",
                new Vector3(0f, roomHeight * 0.45f, 0f), material, 5);

            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.96f, 0.88f, 0.35f));
            main.gravityModifier = 0.004f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            // Prewarm so the room is never briefly empty when you walk in.
            main.prewarm = true;

            var emission = system.emission;
            emission.rateOverTime = 16f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(roomWidth * 0.85f, roomHeight * 0.7f, roomDepth * 0.85f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.05f;

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = FadeInOut(new Color(1f, 0.96f, 0.88f));
        }

        // ------------------------------------------------------------------- platform

        static void BuildPlatformSteam(Transform parent, Vector3 platformPosition)
        {
            var material = ParticleMaterial("FX_Steam_Particle", new Color(0.85f, 0.9f, 0.95f, 0.35f), false);
            var system = CreateSystem(parent, "FX_PlatformSteam", platformPosition, material, 4);

            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 2.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.88f, 0.92f, 0.96f, 0.22f));
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 45;

            var emission = system.emission;
            emission.rateOverTime = 5f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3.5f, 0.2f, 1.2f);

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = FadeInOut(new Color(0.9f, 0.94f, 0.98f));

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.2f;
        }

        // ------------------------------------------------------------------- gradients

        static ParticleSystem.MinMaxGradient Fade(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.35f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        static ParticleSystem.MinMaxGradient FadeInOut(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }
    }
}
