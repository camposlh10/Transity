using UnityEngine;

namespace Transity.Core
{
    /// <summary>
    /// Keeps a plain scene object (the HUD canvas, the event system) alive across scene
    /// loads, and destroys duplicates if the Boot scene is ever loaded twice.
    /// </summary>
    public sealed class PersistentRoot : MonoBehaviour
    {
        [SerializeField] string uniqueKey = "";

        static readonly System.Collections.Generic.HashSet<string> Claimed = new();

        bool m_OwnsKey;

        void Awake()
        {
            var key = string.IsNullOrEmpty(uniqueKey) ? name : uniqueKey;

            if (!Claimed.Add(key))
            {
                // A duplicate: leave the original owner registered.
                Destroy(gameObject);
                return;
            }

            m_OwnsKey = true;

            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (!m_OwnsKey)
            {
                return;
            }

            Claimed.Remove(string.IsNullOrEmpty(uniqueKey) ? name : uniqueKey);
        }
    }
}
