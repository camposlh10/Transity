using Transity.Audio;
using Transity.Player;
using UnityEngine;

namespace Transity.UI
{
    /// <summary>
    /// The figure that delivers the letter. Built from primitives on the traitor's client
    /// only -- nobody else's game contains it -- so it can stand right beside another
    /// player without being seen by them. Drifts, faces the player, and fades once the
    /// letter is answered.
    /// </summary>
    public sealed class CollectorApparition : MonoBehaviour
    {
        Transform m_Follow;
        Vector3 m_Anchor;
        float m_BornAt;
        float m_FadeFrom = -1f;
        Renderer[] m_Renderers;
        Light m_Glow;

        public static CollectorApparition Spawn()
        {
            var local = PlayerCharacter.Local;
            if (local == null)
            {
                return null;
            }

            var root = new GameObject("~Collector");
            var apparition = root.AddComponent<CollectorApparition>();
            apparition.Build(local.transform);
            return apparition;
        }

        void Build(Transform player)
        {
            m_Follow = player;
            m_BornAt = Time.time;

            // Off to one side, just past arm's reach, where the eye goes when nothing is there.
            var side = Random.value < 0.5f ? -1f : 1f;
            m_Anchor = player.position + player.forward * 3.2f + player.right * side * 1.4f;
            transform.position = m_Anchor;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            material.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f));
            material.color = new Color(0.02f, 0.02f, 0.03f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            body.transform.localScale = new Vector3(0.55f, 1.35f, 0.4f);
            body.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(body.GetComponent<Collider>());

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0f, 2.55f, 0.05f);
            head.transform.localScale = new Vector3(0.36f, 0.42f, 0.36f);
            head.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(head.GetComponent<Collider>());

            var paper = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            paper.SetColor("_BaseColor", new Color(0.92f, 0.86f, 0.7f));
            paper.color = new Color(0.92f, 0.86f, 0.7f);

            var letter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            letter.name = "Letter";
            letter.transform.SetParent(transform, false);
            letter.transform.localPosition = new Vector3(0.35f, 1.5f, 0.45f);
            letter.transform.localScale = new Vector3(0.22f, 0.3f, 0.01f);
            letter.transform.localRotation = Quaternion.Euler(10f, -20f, 5f);
            letter.GetComponent<Renderer>().sharedMaterial = paper;
            Destroy(letter.GetComponent<Collider>());

            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = new Vector3(0f, 1.6f, 0.3f);
            m_Glow = glowObject.AddComponent<Light>();
            m_Glow.type = LightType.Point;
            m_Glow.color = new Color(0.55f, 0.6f, 0.9f);
            m_Glow.intensity = 0f;
            m_Glow.range = 5f;

            m_Renderers = GetComponentsInChildren<Renderer>();

            AudioPool.Play2D(SoundKind.Whisper, 0.9f, 0.8f);
        }

        public void Dismiss()
        {
            if (m_FadeFrom < 0f)
            {
                m_FadeFrom = Time.time;
            }
        }

        void Update()
        {
            if (m_Follow == null)
            {
                Destroy(gameObject);
                return;
            }

            var age = Time.time - m_BornAt;

            // Stays put but sways, and always faces the player.
            transform.position = m_Anchor + new Vector3(
                Mathf.Sin(age * 0.7f) * 0.05f,
                Mathf.Sin(age * 1.1f) * 0.06f + 0.02f,
                Mathf.Cos(age * 0.5f) * 0.05f);

            var to = m_Follow.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 2f * Time.deltaTime);
            }

            var fade = m_FadeFrom >= 0f ? Mathf.Clamp01(1f - (Time.time - m_FadeFrom) / 1.5f) : Mathf.Clamp01(age / 1.2f);

            if (m_Glow != null)
            {
                m_Glow.intensity = (1.5f + Mathf.Sin(age * 3f) * 0.4f) * fade;
            }

            if (m_Renderers != null)
            {
                foreach (var renderer in m_Renderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = fade > 0.02f;
                    }
                }
            }

            if (m_FadeFrom >= 0f && fade <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
