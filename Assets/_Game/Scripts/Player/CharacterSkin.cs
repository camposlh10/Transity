using System;
using Transity.Core;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// Which character a player is wearing.
    ///
    /// All the bodies live on the player prefab and only the selected one is active. That
    /// keeps the choice a plain replicated int with no dynamic prefab spawning, so it
    /// survives late joins and scene loads for free -- a client that connects mid-session
    /// receives the value with the object and enables the right model on its own.
    ///
    /// The value is owner-writable because it is cosmetic. Nothing about it affects reach,
    /// damage or loot, so there is no reason to spend a server round trip on it.
    /// </summary>
    public sealed class CharacterSkin : NetworkBehaviour
    {
        [Tooltip("One body per roster entry, in roster order.")]
        [SerializeField] GameObject[] bodies = Array.Empty<GameObject>();

        [SerializeField] CharacterRoster roster;

        const string PreferenceKey = "transity.character";

        readonly NetworkVariable<int> m_Selected = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public static CharacterSkin Local { get; private set; }

        public CharacterRoster Roster => roster;
        public int Selected => m_Selected.Value;
        public int Count => bodies.Length;

        /// <summary>Raised on every peer when this player changes character.</summary>
        public event Action<int> SelectionChanged;

        public override void OnNetworkSpawn()
        {
            m_Selected.OnValueChanged += HandleSelectionChanged;

            if (IsOwner)
            {
                Local = this;

                // Restore the local player's last choice. Purely a convenience; other
                // players learn it through the NetworkVariable like any other state.
                var remembered = PlayerPrefs.GetString(PreferenceKey, string.Empty);
                if (!string.IsNullOrEmpty(remembered) && roster != null)
                {
                    var index = roster.IndexOf(remembered);
                    if (index != m_Selected.Value)
                    {
                        m_Selected.Value = index;
                    }
                }
            }

            Apply();
        }

        public override void OnNetworkDespawn()
        {
            m_Selected.OnValueChanged -= HandleSelectionChanged;

            if (Local == this)
            {
                Local = null;
            }
        }

        void HandleSelectionChanged(int previous, int current)
        {
            Apply();
            SelectionChanged?.Invoke(current);
        }

        /// <summary>Owner only. Picks a character by roster index.</summary>
        public void Select(int index)
        {
            if (!IsOwner || bodies.Length == 0)
            {
                return;
            }

            var clamped = Mathf.Clamp(index, 0, bodies.Length - 1);
            if (clamped == m_Selected.Value)
            {
                return;
            }

            m_Selected.Value = clamped;

            if (roster != null)
            {
                PlayerPrefs.SetString(PreferenceKey, roster.Get(clamped).id);
                PlayerPrefs.Save();
            }

            GameLog.Net($"Character set to index {clamped}.");
        }

        bool m_VisibleToOwner;

        /// <summary>
        /// Shows the owner their own body, which is normally shadows-only so they are not
        /// looking at the inside of their own head. Used by the third-person view.
        /// </summary>
        public void SetVisibleToOwner(bool visible)
        {
            if (m_VisibleToOwner == visible)
            {
                return;
            }

            m_VisibleToOwner = visible;
            Apply();
        }

        void Apply()
        {
            if (bodies.Length == 0)
            {
                return;
            }

            var selected = Mathf.Clamp(m_Selected.Value, 0, bodies.Length - 1);

            for (var i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null)
                {
                    continue;
                }

                var active = i == selected;
                bodies[i].SetActive(active);

                if (!active)
                {
                    continue;
                }

                // Owners keep their own body out of frame but still cast its shadow, so
                // they can see themselves on the floor and in firelight.
                var hideFromOwner = IsOwner && !m_VisibleToOwner;

                foreach (var renderer in bodies[i].GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = hideFromOwner
                        ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
        }
    }
}
