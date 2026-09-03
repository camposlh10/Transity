using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Owner-side first person look. Yaw turns the body (so movement and the network
    /// transform follow it), pitch only tilts the head pivot.
    /// </summary>
    public sealed class PlayerLook : MonoBehaviour
    {
        [SerializeField] Transform body;
        [SerializeField] Transform headPivot;
        [SerializeField] PlayerInputReader input;

        [Header("Sensitivity")]
        [SerializeField, Range(0.01f, 1f)] float sensitivity = 0.12f;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        float m_Pitch;
        float m_RecoilPitch;
        float m_RecoilYaw;

        public Transform HeadPivot => headPivot;

        /// <summary>Set while aiming: the same mouse movement turns the view less.</summary>
        public float SensitivityScale { get; set; } = 1f;

        /// <summary>Last frame's look delta, for viewmodel sway.</summary>
        public Vector2 LastDelta { get; private set; }

        void LateUpdate()
        {
            if (input == null || input.Suppressed)
            {
                LastDelta = Vector2.zero;
                return;
            }

            var look = input.Look * sensitivity * SensitivityScale;
            LastDelta = look;

            // Recoil is an offset that eases back, not a permanent kick: the aim returns to
            // roughly where it was, which is what lets a burst stay on target.
            m_RecoilPitch = Mathf.MoveTowards(m_RecoilPitch, 0f, Time.deltaTime * 18f);
            m_RecoilYaw = Mathf.MoveTowards(m_RecoilYaw, 0f, Time.deltaTime * 18f);

            if (body != null)
            {
                body.Rotate(Vector3.up, look.x + m_RecoilYaw * Time.deltaTime * 60f, Space.Self);
            }

            m_Pitch = Mathf.Clamp(m_Pitch - look.y, minPitch, maxPitch);
            if (headPivot != null)
            {
                headPivot.localRotation = Quaternion.Euler(m_Pitch - m_RecoilPitch, 0f, 0f);
            }
        }

        /// <summary>Kicks the view up and slightly sideways. Degrees.</summary>
        public void Kick(float pitchDegrees, float yawDegrees)
        {
            m_RecoilPitch += pitchDegrees;
            m_RecoilYaw += yawDegrees;
        }

        public void ResetPitch()
        {
            m_Pitch = 0f;
            if (headPivot != null)
            {
                headPivot.localRotation = Quaternion.identity;
            }
        }
    }
}
