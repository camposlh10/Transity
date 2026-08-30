using System;
using System.Collections.Generic;
using Transity.Core;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transity.Missions
{
    /// <summary>
    /// The authoritative expedition state machine. It owns the phase, drives networked
    /// scene loads between the train and the forest, and re-places players after each load.
    ///
    /// Spawned once by the host when the session starts and kept alive across scene loads,
    /// so it is the one object that survives a train-to-forest transition.
    /// </summary>
    public sealed class MissionDirector : NetworkBehaviour
    {
        readonly NetworkVariable<MissionPhase> m_Phase = new(MissionPhase.Preparing);

        public static MissionDirector Instance { get; private set; }

        public MissionPhase Phase => m_Phase.Value;
        public bool IsOnExpedition => Phase is MissionPhase.Expedition or MissionPhase.Extracting;

        /// <summary>Raised on every peer when the phase changes.</summary>
        public event Action<MissionPhase> PhaseChanged;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            m_Phase.OnValueChanged += HandlePhaseChanged;

            if (IsServer)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Phase.OnValueChanged -= HandlePhaseChanged;

            if (IsServer && NetworkManager != null && NetworkManager.SceneManager != null)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void HandlePhaseChanged(MissionPhase previous, MissionPhase current)
        {
            GameLog.Net($"Mission phase {previous} -> {current}");
            PhaseChanged?.Invoke(current);
        }

        // ---------------------------------------------------------------- server flow

        /// <summary>Server only. Loads the train hub; the starting point of every session.</summary>
        public void LoadTrainHub()
        {
            if (!IsServer)
            {
                return;
            }

            SetPhase(MissionPhase.Preparing);
            LoadNetworked(SceneCatalog.TrainHub);
        }

        /// <summary>Server only. Departs for the expedition scene.</summary>
        public void BeginExpedition()
        {
            if (!IsServer || Phase != MissionPhase.Preparing)
            {
                return;
            }

            SetPhase(MissionPhase.Deploying);
            LoadNetworked(SceneCatalog.Forest);
        }

        /// <summary>Server only. Ends the run and returns everyone to the train.</summary>
        public void Extract(bool successful)
        {
            if (!IsServer || !IsOnExpedition)
            {
                return;
            }

            GameLog.Net($"Extraction requested. successful={successful}");
            SetPhase(MissionPhase.Extracting);
            LoadNetworked(SceneCatalog.TrainHub);
        }

        void LoadNetworked(string sceneName)
        {
            var status = NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                GameLog.Error($"Networked load of '{sceneName}' failed: {status}");
            }
        }

        void SetPhase(MissionPhase phase)
        {
            if (IsServer)
            {
                m_Phase.Value = phase;
            }
        }

        void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (clientsTimedOut.Count > 0)
            {
                GameLog.Warn($"{clientsTimedOut.Count} client(s) timed out loading '{sceneName}'.");
            }

            var context = sceneName == SceneCatalog.Forest ? SpawnContext.Expedition : SpawnContext.Train;
            PlaceAllPlayers(context);

            // Settle into the resting phase for whichever scene we just arrived in.
            switch (sceneName)
            {
                case SceneCatalog.Forest:
                    SetPhase(MissionPhase.Expedition);
                    break;
                case SceneCatalog.TrainHub when Phase == MissionPhase.Extracting:
                    SetPhase(MissionPhase.Debrief);
                    break;
                case SceneCatalog.TrainHub:
                    SetPhase(MissionPhase.Preparing);
                    break;
            }
        }

        void PlaceAllPlayers(SpawnContext context)
        {
            var slot = 0;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject != null &&
                    client.PlayerObject.TryGetComponent<PlayerCharacter>(out var character))
                {
                    character.PlaceAtSpawn(context, slot);
                }

                slot++;
            }
        }

        /// <summary>Server only. Closes the debrief and returns to the ready state.</summary>
        public void FinishDebrief()
        {
            if (IsServer && Phase == MissionPhase.Debrief)
            {
                SetPhase(MissionPhase.Preparing);
            }
        }
    }
}
