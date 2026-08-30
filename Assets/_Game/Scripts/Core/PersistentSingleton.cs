using UnityEngine;

namespace Transity.Core
{
    /// <summary>
    /// Base class for the handful of long-lived managers that survive scene loads
    /// (session, mission, save). Deliberately not a general-purpose service locator:
    /// if a system does not need global lifetime, it should not derive from this.
    /// </summary>
    public abstract class PersistentSingleton<T> : MonoBehaviour where T : PersistentSingleton<T>
    {
        public static T Instance { get; private set; }
        public static bool Exists => Instance != null;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = (T)this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
