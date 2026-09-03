using UnityEngine;

namespace Transity.Core
{
    /// <summary>
    /// Drives a light's intensity with layered noise so firelight breathes instead of
    /// sitting flat. Uses Perlin rather than Random so the motion is smooth and does not
    /// strobe, which matters when the light also casts shadows.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class LightFlicker : MonoBehaviour
    {
        [SerializeField] Light target;
        [SerializeField] float baseIntensity = 8f;
        [SerializeField, Range(0f, 1f)] float amplitude = 0.22f;
        [SerializeField] float speed = 1.6f;
        [SerializeField] float positionJitter = 0.04f;

        Vector3 m_Origin;
        float m_Seed;

        void Awake()
        {
            if (target == null)
            {
                target = GetComponent<Light>();
            }

            m_Origin = transform.localPosition;
            m_Seed = Random.value * 100f;

            if (target != null && baseIntensity <= 0f)
            {
                baseIntensity = target.intensity;
            }
        }

        void Update()
        {
            if (target == null)
            {
                return;
            }

            var t = Time.time * speed + m_Seed;

            // Two octaves: a slow swell plus a faster tremble.
            var swell = Mathf.PerlinNoise(t, 0f) - 0.5f;
            var tremble = (Mathf.PerlinNoise(t * 3.7f, 11f) - 0.5f) * 0.45f;

            target.intensity = baseIntensity * (1f + (swell + tremble) * 2f * amplitude);

            if (positionJitter > 0f)
            {
                transform.localPosition = m_Origin + new Vector3(
                    (Mathf.PerlinNoise(t * 2.1f, 3f) - 0.5f) * positionJitter,
                    (Mathf.PerlinNoise(t * 1.7f, 7f) - 0.5f) * positionJitter,
                    (Mathf.PerlinNoise(t * 2.3f, 13f) - 0.5f) * positionJitter);
            }
        }
    }
}
