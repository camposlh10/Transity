using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Gives the owner a body to look down at.
    ///
    /// The default first-person setup renders your own character shadows-only, so looking
    /// down shows the floor through where your chest should be. This turns the body back on
    /// for the owner and hides the head instead -- you cannot see your own face, and
    /// without this the camera sits inside the skull and renders the back of it.
    ///
    /// The head is hidden by collapsing its bone rather than by disabling a renderer,
    /// because the character is a single skinned mesh: there is no separate head object to
    /// switch off. Scaling the bone to nothing pulls those vertices into a point that never
    /// draws, and costs nothing per frame.
    ///
    /// The camera deliberately stays on the look pivot rather than following the animated
    /// head bone. Riding the head bone is more "correct" and is also how you give people
    /// motion sickness: every footstep would shake the view.
    /// </summary>
    public sealed class FirstPersonBody : NetworkBehaviour
    {
        [SerializeField] PlayerCharacter character;
        [SerializeField] CharacterSkin skin;

        [Tooltip("Nudge forward of the neck so the chest does not crowd the near clip plane. " +
                 "Kept small: a large offset swings the view when you look down.")]
        [SerializeField] float eyeForwardOffset = 0.06f;

        [Tooltip("Near clip while the body is visible. Tighter than default so shoulders " +
                 "and a held weapon do not slice open.")]
        [SerializeField] float nearClip = 0.03f;

        Transform m_HeadBone;
        Vector3 m_HeadBoneScale = Vector3.one;
        bool m_HeadHidden;

        public override void OnNetworkSpawn()
        {
            // Everyone else already sees this body normally; only the owner's view needs
            // rearranging.
            enabled = IsOwner;

            if (!IsOwner)
            {
                return;
            }

            if (character == null) character = GetComponent<PlayerCharacter>();
            if (skin == null) skin = GetComponent<CharacterSkin>();

            ApplyCamera();
            SetFirstPerson(true);

            if (skin != null)
            {
                // The body swaps when a player changes character, taking its skeleton with
                // it, so the head has to be found and hidden again on the new one.
                skin.SelectionChanged += _ =>
                {
                    m_HeadBone = null;
                    m_HeadHidden = false;
                    SetFirstPerson(true);
                };
            }
        }

        void ApplyCamera()
        {
            var camera = character != null ? character.PlayerCamera : null;
            if (camera == null)
            {
                return;
            }

            camera.nearClipPlane = nearClip;
            camera.transform.localPosition = new Vector3(0f, 0f, eyeForwardOffset);
        }

        /// <summary>
        /// True while the owner is in first person. The third-person view calls this with
        /// false so the character gets its head back when seen from behind.
        /// </summary>
        public void SetFirstPerson(bool firstPerson)
        {
            if (!IsOwner)
            {
                return;
            }

            skin?.SetVisibleToOwner(true);
            SetHeadHidden(firstPerson);
        }

        void SetHeadHidden(bool hidden)
        {
            if (m_HeadBone == null)
            {
                m_HeadBone = FindHeadBone();
                if (m_HeadBone == null)
                {
                    return;
                }

                m_HeadBoneScale = m_HeadBone.localScale;
            }

            if (m_HeadHidden == hidden)
            {
                return;
            }

            m_HeadHidden = hidden;

            // Not exactly zero: a zero scale makes the bone matrix non-invertible, which
            // some skinning paths take badly. Small enough to be sub-pixel either way.
            m_HeadBone.localScale = hidden ? Vector3.one * 0.0001f : m_HeadBoneScale;
        }

        Transform FindHeadBone()
        {
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                if (animator.gameObject.activeInHierarchy && animator.isHuman)
                {
                    return animator.GetBoneTransform(HumanBodyBones.Head);
                }
            }

            return null;
        }
    }
}
