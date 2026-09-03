using System;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>One selectable character.</summary>
    [Serializable]
    public struct CharacterEntry
    {
        [Tooltip("Stable id. Saved selections reference this, not the display name.")]
        public string id;

        public string displayName;

        [Tooltip("Model prefab. Placed on the player and shown in the wardrobe preview.")]
        public GameObject prefab;

        [Tooltip("Off for models with no skeleton; they cannot play animation clips.")]
        public bool rigged;
    }

    /// <summary>
    /// The characters a player may pick between.
    ///
    /// One list drives three things: the bodies built onto the player prefab, the wardrobe
    /// picker, and the preview. Adding a character is an edit here plus a rebuild, with no
    /// code change anywhere.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Character Roster", fileName = "CharacterRoster")]
    public sealed class CharacterRoster : ScriptableObject
    {
        [SerializeField] CharacterEntry[] characters = Array.Empty<CharacterEntry>();

        public int Count => characters.Length;

        public CharacterEntry Get(int index) =>
            index >= 0 && index < characters.Length ? characters[index] : default;

        public int IndexOf(string id)
        {
            for (var i = 0; i < characters.Length; i++)
            {
                if (characters[i].id == id)
                {
                    return i;
                }
            }

            return 0;
        }

        public int Clamp(int index) => characters.Length == 0 ? 0 : Mathf.Clamp(index, 0, characters.Length - 1);
    }
}
