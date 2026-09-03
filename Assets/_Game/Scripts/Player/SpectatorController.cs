using System.Collections.Generic;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Where a dead player's eyes go. Follows a living teammate from behind the shoulder,
    /// cycling with the slot keys; with nobody left it becomes a free camera so the last
    /// moments are at least watchable.
    ///
    /// Owner-only and purely a camera: the dead player's network object stays exactly
    /// where it fell, so the body and the pack remain for the others to find.
    /// </summary>
    public sealed class SpectatorController : MonoBehaviour
    {
        [SerializeField] PlayerCharacter character;
        [SerializeField] PlayerInputReader input;
        [SerializeField] float followDistance = 2.6f;
        [SerializeField] float followHeight = 0.6f;
        [SerializeField] float freeFlySpeed = 8f;
        [SerializeField, Range(0.01f, 1f)] float sensitivity = 0.12f;

        Camera m_Camera;
        AudioListener m_Listener;
        PlayerVitals m_Following;
        float m_Yaw;
        float m_Pitch;
        bool m_Active;

        public bool IsActive => m_Active;
        public PlayerVitals Following => m_Following;

        void Awake()
        {
            if (character == null) character = GetComponent<PlayerCharacter>();
            if (input == null) input = GetComponent<PlayerInputReader>();
        }

        public void Begin()
        {
            if (m_Active)
            {
                return;
            }

            m_Active = true;
            EnsureCamera();

            var playerCamera = character != null ? character.PlayerCamera : null;
            if (playerCamera != null)
            {
                m_Camera.transform.SetPositionAndRotation(
                    playerCamera.transform.position, playerCamera.transform.rotation);
                m_Yaw = playerCamera.transform.eulerAngles.y;
                m_Pitch = 10f;
                playerCamera.enabled = false;

                if (playerCamera.TryGetComponent<AudioListener>(out var listener))
                {
                    listener.enabled = false;
                }
            }

            m_Camera.enabled = true;
            m_Listener.enabled = true;
            PickNext(0);
        }

        public void End()
        {
            if (!m_Active)
            {
                return;
            }

            m_Active = false;

            if (m_Camera != null)
            {
                m_Camera.enabled = false;
                m_Listener.enabled = false;
            }

            var playerCamera = character != null ? character.PlayerCamera : null;
            if (playerCamera != null)
            {
                playerCamera.enabled = true;

                if (playerCamera.TryGetComponent<AudioListener>(out var listener))
                {
                    listener.enabled = true;
                }
            }

            m_Following = null;
        }

        void EnsureCamera()
        {
            if (m_Camera != null)
            {
                return;
            }

            var go = new GameObject("SpectatorCamera");
            m_Camera = go.AddComponent<Camera>();
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 400f;
            m_Camera.fieldOfView = 65f;
            m_Camera.enabled = false;
            m_Listener = go.AddComponent<AudioListener>();
            m_Listener.enabled = false;
        }

        void LateUpdate()
        {
            if (!m_Active || m_Camera == null)
            {
                return;
            }

            if (input != null && !input.Suppressed)
            {
                if (input.NextSlotPressed || input.SlotPressed == 1)
                {
                    PickNext(1);
                }
                else if (input.PreviousSlotPressed || input.SlotPressed == 0)
                {
                    PickNext(-1);
                }

                var look = input.Look * sensitivity;
                m_Yaw += look.x;
                m_Pitch = Mathf.Clamp(m_Pitch - look.y, -60f, 75f);
            }

            if (m_Following == null || m_Following.IsDead)
            {
                PickNext(0);
            }

            var rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);

            if (m_Following != null)
            {
                // Over the shoulder, pulled in if a wall is in the way.
                var anchor = m_Following.transform.position + Vector3.up * 1.6f;
                var wanted = anchor - rotation * Vector3.forward * followDistance + Vector3.up * followHeight;

                if (Physics.Linecast(anchor, wanted, out var hit, 1, QueryTriggerInteraction.Ignore))
                {
                    wanted = hit.point + hit.normal * 0.15f;
                }

                m_Camera.transform.position = Vector3.Lerp(m_Camera.transform.position, wanted, 10f * Time.deltaTime);
                m_Camera.transform.rotation = rotation;
            }
            else
            {
                var move = input != null ? input.Move : Vector2.zero;
                var motion = rotation * new Vector3(move.x, 0f, move.y) * (freeFlySpeed * Time.deltaTime);
                m_Camera.transform.position += motion;
                m_Camera.transform.rotation = rotation;
            }
        }

        void PickNext(int direction)
        {
            var candidates = new List<PlayerVitals>();
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals != null && vitals != GetComponent<PlayerVitals>() && !vitals.IsDead)
                {
                    candidates.Add(vitals);
                }
            }

            if (candidates.Count == 0)
            {
                m_Following = null;
                return;
            }

            var index = Mathf.Max(0, candidates.IndexOf(m_Following));
            index = (index + direction + candidates.Count) % candidates.Count;
            m_Following = candidates[index];
        }
    }
}
