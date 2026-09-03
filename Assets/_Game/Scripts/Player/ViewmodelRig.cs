using Transity.Inventory;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// The owner's hands. Holds the current item's model under the camera and gives it
    /// the small motions that make a first-person weapon feel held rather than glued:
    /// sway against the look, bob with the stride, a kick on firing, a dip on reload, and
    /// a slide to centre when aiming.
    ///
    /// Every pose is a local offset from the camera so nothing here fights the look
    /// controller. Purely visual and owner-only; remote players see the item in the
    /// character's hand instead.
    /// </summary>
    public sealed class ViewmodelRig : MonoBehaviour
    {
        [Header("Poses (camera space)")]
        [SerializeField] Vector3 weaponPosition = new(0.26f, -0.24f, 0.48f);
        [SerializeField] Vector3 weaponEuler = new(0f, -4f, 0f);
        [SerializeField] Vector3 aimPosition = new(0f, -0.13f, 0.34f);
        [SerializeField] Vector3 toolPosition = new(0.22f, -0.3f, 0.42f);
        [SerializeField] Vector3 toolEuler = new(10f, -30f, 0f);
        [SerializeField] Vector3 loweredOffset = new(0f, -0.45f, 0f);

        [Header("Motion")]
        [SerializeField] float swayAmount = 0.012f;
        [SerializeField] float swayRotation = 2.5f;
        [SerializeField] float swaySmoothing = 9f;
        [SerializeField] float bobAmount = 0.011f;
        [SerializeField] float bobFrequency = 2.1f;
        [SerializeField] float poseSmoothing = 10f;

        FirstPersonController m_Movement;
        PlayerLook m_Look;
        GameObject m_Model;
        ItemUseKind m_Kind;
        Vector3 m_KickOffset;
        Vector3 m_KickEuler;
        Vector3 m_SwayOffset;
        Vector3 m_SwayEuler;
        float m_BobTime;
        float m_ReloadUntil;
        float m_ReloadDuration;
        bool m_Aiming;
        bool m_Lowered;

        public GameObject Model => m_Model;

        /// <summary>World position of the muzzle offset for the current model.</summary>
        public Vector3 MuzzleWorld(Vector3 muzzleOffset) =>
            m_Model != null ? m_Model.transform.TransformPoint(muzzleOffset) : transform.position;

        public void Bind(FirstPersonController movement, PlayerLook look)
        {
            m_Movement = movement;
            m_Look = look;
        }

        /// <summary>Swaps the held model. Null clears the hands.</summary>
        public void Show(GameObject prefab, ItemUseKind kind)
        {
            if (m_Model != null)
            {
                Destroy(m_Model);
                m_Model = null;
            }

            m_Kind = kind;

            if (prefab == null)
            {
                return;
            }

            m_Model = Instantiate(prefab, transform);
            m_Model.name = "Held";

            // Nothing in the hands should be hit by the player's own rays or block anything.
            foreach (var collider in m_Model.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }

            foreach (var renderer in m_Model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Snap to the rest pose so a swap does not glide in from the last item's place.
            m_Model.transform.localPosition = RestPosition();
            m_Model.transform.localRotation = Quaternion.Euler(RestEuler());
        }

        public void SetAiming(bool aiming) => m_Aiming = aiming;

        public void SetLowered(bool lowered) => m_Lowered = lowered;

        public void Kick(float strength)
        {
            m_KickOffset += new Vector3(0f, 0.01f, -0.06f) * strength;
            m_KickEuler += new Vector3(-6f, Random.Range(-1.5f, 1.5f), Random.Range(-2f, 2f)) * strength;
        }

        public void PlayReload(float seconds)
        {
            m_ReloadDuration = Mathf.Max(0.1f, seconds);
            m_ReloadUntil = Time.time + m_ReloadDuration;
        }

        Vector3 RestPosition() => m_Kind == ItemUseKind.Weapon ? weaponPosition : toolPosition;

        Vector3 RestEuler() => m_Kind == ItemUseKind.Weapon ? weaponEuler : toolEuler;

        void LateUpdate()
        {
            if (m_Model == null)
            {
                return;
            }

            var dt = Time.deltaTime;

            // ---- sway: the hands lag the look a little ----
            var look = m_Look != null ? m_Look.LastDelta : Vector2.zero;
            var swayTarget = new Vector3(-look.x, -look.y, 0f) * swayAmount;
            var swayEulerTarget = new Vector3(look.y, -look.x, -look.x * 0.6f) * swayRotation;
            m_SwayOffset = Vector3.Lerp(m_SwayOffset, swayTarget, swaySmoothing * dt);
            m_SwayEuler = Vector3.Lerp(m_SwayEuler, swayEulerTarget, swaySmoothing * dt);

            // ---- bob: figure-eight with the stride ----
            var speed = m_Movement != null ? m_Movement.CurrentSpeed : 0f;
            var grounded = m_Movement == null || m_Movement.IsGrounded;
            var stride = grounded ? Mathf.Clamp01(speed / 6f) : 0f;
            m_BobTime += dt * bobFrequency * (1f + stride);
            var bob = new Vector3(
                Mathf.Sin(m_BobTime * Mathf.PI) * bobAmount,
                -Mathf.Abs(Mathf.Cos(m_BobTime * Mathf.PI)) * bobAmount * 0.7f,
                0f) * stride * (m_Aiming ? 0.25f : 1f);

            // ---- kick recovers ----
            m_KickOffset = Vector3.Lerp(m_KickOffset, Vector3.zero, 12f * dt);
            m_KickEuler = Vector3.Lerp(m_KickEuler, Vector3.zero, 12f * dt);

            // ---- reload: dip down and tilt, then come back ----
            var reload = Vector3.zero;
            var reloadEuler = Vector3.zero;
            if (Time.time < m_ReloadUntil)
            {
                var t = 1f - (m_ReloadUntil - Time.time) / m_ReloadDuration;
                var curve = Mathf.Sin(t * Mathf.PI);
                reload = new Vector3(0f, -0.09f, -0.03f) * curve;
                reloadEuler = new Vector3(18f, 0f, -22f) * curve;
            }

            // ---- pose ----
            var basePosition = m_Aiming && m_Kind == ItemUseKind.Weapon ? aimPosition : RestPosition();
            var baseEuler = m_Aiming && m_Kind == ItemUseKind.Weapon ? Vector3.zero : RestEuler();
            if (m_Lowered)
            {
                basePosition += loweredOffset;
                baseEuler += new Vector3(35f, 0f, 0f);
            }

            var targetPosition = basePosition + m_SwayOffset + bob + m_KickOffset + reload;
            var targetRotation = Quaternion.Euler(baseEuler + m_SwayEuler + m_KickEuler + reloadEuler);

            m_Model.transform.localPosition = Vector3.Lerp(
                m_Model.transform.localPosition, targetPosition, poseSmoothing * dt);
            m_Model.transform.localRotation = Quaternion.Slerp(
                m_Model.transform.localRotation, targetRotation, poseSmoothing * dt);
        }
    }
}
