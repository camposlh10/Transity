using UnityEngine;

namespace Transity.UI
{
    /// <summary>
    /// Renders the player's character into a texture so UI can show them.
    ///
    /// Built as an off-world render rig rather than a camera pointed at the real player:
    /// the actual player is standing in a lit room at an arbitrary angle, is hidden from
    /// their own camera, and is usually facing away. A dedicated rig gives the same framing
    /// and lighting every time, and it costs nothing while the screen is shut because the
    /// camera is only enabled when something asks for the texture.
    ///
    /// Swapping skins later means pointing <see cref="SetCharacter"/> at a different prefab;
    /// nothing else has to change.
    /// </summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        [SerializeField] Transity.Player.CharacterRoster roster;
        [SerializeField] GameObject characterPrefab;
        [SerializeField] int textureWidth = 512;
        [SerializeField] int textureHeight = 760;

        [Header("Framing")]
        [Tooltip("Height of the character the camera should frame, in metres.")]
        [SerializeField] float subjectHeight = 1.8f;
        [SerializeField] float cameraDistance = 3.1f;
        [SerializeField] float cameraPitch = 6f;
        [SerializeField] float startYaw = 205f;

        [Header("Motion")]
        [SerializeField] bool autoRotate = true;
        [SerializeField] float autoRotateSpeed = 12f;

        // Layer 8, added to TagManager. The preview camera renders only this, and nothing
        // else renders it, so the rig can sit anywhere without leaking into the game view.
        const int PreviewLayer = 8;

        // Parked well below the world so stray point lights and reflection probes cannot
        // reach it. The directional light still does, which is fine and free.
        static readonly Vector3 RigOrigin = new(0f, -2000f, 0f);

        public static CharacterPreview Instance { get; private set; }

        Camera m_Camera;
        RenderTexture m_Texture;
        Transform m_Pivot;
        GameObject m_Instance;
        float m_Yaw;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            m_Yaw = startYaw;
        }

        void OnDestroy()
        {
            Release();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void LateUpdate()
        {
            if (m_Camera == null || !m_Camera.enabled || m_Pivot == null)
            {
                return;
            }

            if (autoRotate)
            {
                m_Yaw += autoRotateSpeed * Time.unscaledDeltaTime;
            }

            m_Pivot.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
        }

        /// <summary>
        /// Returns the preview texture, building the rig on first use and switching the
        /// camera on. Call <see cref="Hide"/> when the screen closes.
        /// </summary>
        public RenderTexture Show()
        {
            EnsureRig();

            if (m_Camera != null)
            {
                m_Camera.enabled = true;
            }

            return m_Texture;
        }

        public void Hide()
        {
            if (m_Camera != null)
            {
                m_Camera.enabled = false;
            }
        }

        /// <summary>Points the preview at a different character. This is the skin hook.</summary>
        public void SetCharacter(GameObject prefab)
        {
            if (prefab == characterPrefab && m_Instance != null)
            {
                return;
            }

            characterPrefab = prefab;

            if (m_Instance != null)
            {
                Destroy(m_Instance);
                m_Instance = null;
            }

            if (m_Pivot != null)
            {
                SpawnCharacter();
            }
        }

        public Transity.Player.CharacterRoster Roster => roster;

        /// <summary>Shows the roster entry at this index.</summary>
        public void ShowIndex(int index)
        {
            if (roster == null || roster.Count == 0)
            {
                return;
            }

            SetCharacter(roster.Get(roster.Clamp(index)).prefab);
        }

        public void Nudge(float degrees)
        {
            m_Yaw += degrees;
        }

        void EnsureRig()
        {
            if (m_Camera != null)
            {
                return;
            }

            var rig = new GameObject("CharacterPreviewRig");
            rig.transform.SetParent(transform, false);
            rig.transform.position = RigOrigin;

            var pivotObject = new GameObject("Pivot");
            pivotObject.transform.SetParent(rig.transform, false);
            m_Pivot = pivotObject.transform;

            SpawnCharacter();

            m_Texture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "CharacterPreviewTexture",
                antiAliasing = 2
            };
            m_Texture.Create();

            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(rig.transform, false);

            var focus = subjectHeight * 0.55f;
            cameraObject.transform.localPosition = new Vector3(0f, focus, -cameraDistance);
            cameraObject.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);

            m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.cullingMask = 1 << PreviewLayer;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.07f, 0.075f, 0.09f, 1f);
            m_Camera.fieldOfView = 34f;
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 20f;
            m_Camera.targetTexture = m_Texture;
            m_Camera.enabled = false;

            // Lights live on the rig so the preview is lit the same wherever the player is
            // standing and whatever the room is doing.
            AddLight(rig.transform, "Key", new Vector3(1.4f, subjectHeight + 0.9f, -1.9f),
                new Color(1f, 0.94f, 0.86f), 5.5f);
            AddLight(rig.transform, "Fill", new Vector3(-1.7f, subjectHeight * 0.6f, -1.5f),
                new Color(0.7f, 0.78f, 0.95f), 2.6f);
            AddLight(rig.transform, "Rim", new Vector3(0f, subjectHeight + 0.4f, 2.1f),
                new Color(0.85f, 0.9f, 1f), 4f);
        }

        static void AddLight(Transform parent, string lightName, Vector3 localPosition,
            Color color, float intensity)
        {
            var go = new GameObject("PreviewLight_" + lightName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.layer = PreviewLayer;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 8f;
            light.shadows = LightShadows.None;
        }

        void SpawnCharacter()
        {
            if (characterPrefab == null)
            {
                return;
            }

            m_Instance = Instantiate(characterPrefab, m_Pivot);
            m_Instance.transform.localPosition = Vector3.zero;
            m_Instance.transform.localRotation = Quaternion.identity;
            m_Instance.name = "PreviewCharacter";

            SetLayerRecursively(m_Instance, PreviewLayer);

            // A preview should never run gameplay logic, and an Animator with no controller
            // would just hold the bind pose anyway.
            foreach (var behaviour in m_Instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (var collider in m_Instance.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            // Renderers on the real player are switched to shadows-only for their owner;
            // the preview copy must be visible.
            foreach (var renderer in m_Instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.enabled = true;
            }
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }

        void Release()
        {
            if (m_Camera != null)
            {
                m_Camera.targetTexture = null;
            }

            if (m_Texture != null)
            {
                m_Texture.Release();
                Destroy(m_Texture);
                m_Texture = null;
            }
        }
    }
}
