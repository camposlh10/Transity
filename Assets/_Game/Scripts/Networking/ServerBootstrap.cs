using Transity.Core;
using Transity.Missions;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Networking
{
    /// <summary>
    /// Sits on the NetworkManager object. When this peer becomes the server it spawns the
    /// session-scoped objects (the mission director) and loads the train hub.
    ///
    /// Deliberately takes the NetworkManager from the same GameObject rather than the
    /// singleton, because singleton assignment order during Awake is not guaranteed.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class ServerBootstrap : MonoBehaviour
    {
        [Tooltip("Prefab holding MissionDirector. Must be registered in the NetworkManager prefab list.")]
        [SerializeField] GameObject sessionScopePrefab;

        NetworkManager m_NetworkManager;

        void Awake()
        {
            m_NetworkManager = GetComponent<NetworkManager>();
            m_NetworkManager.OnServerStarted += HandleServerStarted;
        }

        void OnDestroy()
        {
            if (m_NetworkManager != null)
            {
                m_NetworkManager.OnServerStarted -= HandleServerStarted;
            }
        }

        void HandleServerStarted()
        {
            if (sessionScopePrefab == null)
            {
                GameLog.Error($"{nameof(ServerBootstrap)} has no session scope prefab; the mission director will not exist.");
                return;
            }

            if (MissionDirector.Instance == null)
            {
                var instance = Instantiate(sessionScopePrefab);
                if (!instance.TryGetComponent<NetworkObject>(out var networkObject))
                {
                    GameLog.Error("Session scope prefab is missing a NetworkObject.");
                    Destroy(instance);
                    return;
                }

                // destroyWithScene: false keeps the director alive across the train/forest
                // transition, which is the whole reason it is spawned rather than placed.
                networkObject.Spawn(destroyWithScene: false);
            }

            GameLog.Net("Server started; loading the train hub.");
            MissionDirector.Instance.LoadTrainHub();
        }
    }
}
