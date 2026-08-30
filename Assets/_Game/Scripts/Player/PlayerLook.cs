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

        public Transform HeadPivot => headPivot;

        void LateUpdate()
        {
            if (input == null || input.Suppressed)
            {
                return;
            }

            var look = input.Look * sensitivity;

            if (body != null)
            {
                body.Rotate(Vector3.up, look.x, Space.Self);
            }

            m_Pitch = Mathf.Clamp(m_Pitch - look.y, minPitch, maxPitch);
            if (headPivot != null)
            {
                headPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            }
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
