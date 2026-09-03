using System.Collections.Generic;
using Transity.Inventory;
using Transity.Player;
using UnityEngine;

namespace Transity.Creatures
{
    /// <summary>
    /// The marks a creature leaves as it moves, visible only under ultraviolet. Purely
    /// client-side and deterministic enough: every peer sees the same replicated creature
    /// positions, so every peer drops the same marks in the same places.
    /// </summary>
    public sealed class ScentTrailSystem : MonoBehaviour
    {
        const int PoolSize = 240;
        const float DropSpacing = 2.2f;
        const float Lifetime = 150f;

        static ScentTrailSystem s_Instance;

        readonly List<Mark> m_Marks = new();
        readonly Dictionary<CreatureBrain, Vector3> m_LastDrop = new();
        Material m_Material;
        Mesh m_Mesh;
        int m_Next;

        sealed class Mark
        {
            public Transform Transform;
            public Renderer Renderer;
            public float BornAt;
        }

        public static ScentTrailSystem Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("~ScentTrail");
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<ScentTrailSystem>();
                }

                return s_Instance;
            }
        }

        void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            m_Material.SetColor("_BaseColor", new Color(0.55f, 0.25f, 1f, 0.85f));
            m_Material.color = new Color(0.55f, 0.25f, 1f, 0.85f);

            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            for (var i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Mark");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.transform.localScale = new Vector3(0.35f, 0.08f, 0.5f);
                go.SetActive(false);
                m_Marks.Add(new Mark { Transform = go.transform, Renderer = renderer, BornAt = -1000f });
            }

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => ClearAll();
        }

        void Update()
        {
            // Drop marks behind every creature that has moved far enough.
            foreach (var brain in CreatureBrain.All)
            {
                if (brain == null || brain.IsDown)
                {
                    continue;
                }

                var position = brain.transform.position;
                if (!m_LastDrop.TryGetValue(brain, out var last) || (position - last).sqrMagnitude > DropSpacing * DropSpacing)
                {
                    m_LastDrop[brain] = position;
                    Drop(position, brain.transform.forward, brain.Health != null && brain.Health.IsBleeding);
                }
            }

            var visible = PlayerLight.Local != null && PlayerLight.Local.ActiveKind == ToggleKind.UltraViolet;
            var now = Time.time;

            foreach (var mark in m_Marks)
            {
                var age = now - mark.BornAt;
                var alive = age < Lifetime;
                var show = alive && visible;

                if (mark.Transform.gameObject.activeSelf != show)
                {
                    mark.Transform.gameObject.SetActive(show);
                }
            }
        }

        void Drop(Vector3 position, Vector3 forward, bool bleeding)
        {
            if (!Physics.Raycast(position + Vector3.up, Vector3.down, out var hit, 3f, 1, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var mark = m_Marks[m_Next];
            m_Next = (m_Next + 1) % m_Marks.Count;

            mark.BornAt = Time.time;
            mark.Transform.SetPositionAndRotation(hit.point + Vector3.up * 0.03f,
                Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward : Vector3.forward, hit.normal));
            mark.Transform.localScale = bleeding ? new Vector3(0.5f, 0.08f, 0.6f) : new Vector3(0.35f, 0.08f, 0.5f);
        }

        void ClearAll()
        {
            m_LastDrop.Clear();
            foreach (var mark in m_Marks)
            {
                mark.BornAt = -1000f;
                mark.Transform.gameObject.SetActive(false);
            }
        }
    }
}
