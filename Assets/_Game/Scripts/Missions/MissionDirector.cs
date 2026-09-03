using System;
using System.Collections.Generic;
using Transity.Combat;
using Transity.Core;
using Transity.Creatures;
using Transity.Inventory;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Transity.Missions
{
    /// <summary>
    /// The authoritative expedition state machine. It owns the phase, the chosen
    /// contract and the ledger, drives networked scene loads between the train and the
    /// forest, re-places players after each load, and settles the money at the end.
    ///
    /// Spawned once by the host when the session starts and kept alive across scene
    /// loads, so it is the one object that survives a train-to-forest transition.
    /// </summary>
    public sealed class MissionDirector : NetworkBehaviour
    {
        readonly NetworkVariable<MissionPhase> m_Phase = new(MissionPhase.Preparing);
        readonly NetworkVariable<int> m_ContractIndex = new(-1);
        readonly NetworkList<LedgerEntry> m_Ledger = new();

        readonly HashSet<ulong> m_Forfeited = new();
        int m_CrewTotal;

        public static MissionDirector Instance { get; private set; }

        public MissionPhase Phase => m_Phase.Value;
        public bool IsOnExpedition => Phase is MissionPhase.Expedition or MissionPhase.Extracting;
        public int ContractIndex => m_ContractIndex.Value;
        public ContractDefinition ActiveContract => GameContent.Contracts != null
            ? GameContent.Contracts.Get(m_ContractIndex.Value)
            : null;

        public int LedgerCount => m_Ledger.Count;
        public LedgerEntry GetLedgerEntry(int index) => m_Ledger[index];

        /// <summary>Raised on every peer when the phase changes.</summary>
        public event Action<MissionPhase> PhaseChanged;

        /// <summary>Raised on every peer when the ledger or the contract changes.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            m_Phase.OnValueChanged += HandlePhaseChanged;
            m_ContractIndex.OnValueChanged += HandleContractChanged;
            m_Ledger.OnListChanged += HandleLedgerChanged;

            if (IsServer)
            {
                NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
                PlayerVitals.ServerPlayerDied += HandlePlayerDied;
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Phase.OnValueChanged -= HandlePhaseChanged;
            m_ContractIndex.OnValueChanged -= HandleContractChanged;
            m_Ledger.OnListChanged -= HandleLedgerChanged;

            if (IsServer)
            {
                if (NetworkManager != null && NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                }

                PlayerVitals.ServerPlayerDied -= HandlePlayerDied;
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

        void HandleContractChanged(int previous, int current) => Changed?.Invoke();

        void HandleLedgerChanged(NetworkListEvent<LedgerEntry> _) => Changed?.Invoke();

        // ---------------------------------------------------------------- contracts

        /// <summary>Any crew member may pick; only while preparing.</summary>
        [Rpc(SendTo.Server)]
        public void SelectContractRpc(int index)
        {
            if (Phase != MissionPhase.Preparing || GameContent.Contracts == null)
            {
                return;
            }

            if (index < -1 || index >= GameContent.Contracts.Count)
            {
                return;
            }

            m_ContractIndex.Value = index;
            GameLog.Net($"Contract selected: {ActiveContract?.title ?? "none"}");
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

        /// <summary>
        /// Asked for by the mission computer. Any crew member may call it; the phase check
        /// on the server is what actually decides.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestDepartRpc()
        {
            BeginExpedition();
        }

        /// <summary>Server only. Departs for the expedition scene.</summary>
        public void BeginExpedition()
        {
            if (!IsServer || Phase != MissionPhase.Preparing)
            {
                return;
            }

            // Nobody picked: take the first job on the board rather than refuse. Solo
            // testing straight from the boot scene should not need a detour to the computer.
            if (ActiveContract == null && GameContent.Contracts != null && GameContent.Contracts.Count > 0)
            {
                m_ContractIndex.Value = 0;
                GameLog.Net("No contract selected; defaulting to the first.");
            }

            m_Ledger.Clear();
            m_Forfeited.Clear();
            m_CrewTotal = 0;

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

            SettleMoney(successful);

            // Gear that comes home goes back on the shelf; gear that does not is simply
            // gone, which is what makes carrying it out a decision.
            SettleCarriedEquipment(successful);

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

            // Back aboard: the dead get up. Restore before placing so the controller is on.
            if (context == SpawnContext.Train)
            {
                PlayerVitals.ServerRestoreAll();
            }

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

        // ---------------------------------------------------------------- the ledger

        /// <summary>Server only.</summary>
        public void ServerRecordKill(CreatureBrain creature, ulong byClientId)
        {
            if (!IsServer || creature == null || creature.Definition == null)
            {
                return;
            }

            var contract = ActiveContract;
            var value = Payout.Bounty(creature.Definition.bountyKill, contract != null ? contract.rewardMultiplier : 1f);
            m_CrewTotal += value;

            m_Ledger.Add(new LedgerEntry
            {
                Kind = LedgerKind.Kill,
                SubjectId = creature.Definition.StableId,
                ClientId = byClientId,
                ByClientId = DamageInfo.NoInstigator,
                Value = value
            });
        }

        /// <summary>Server only.</summary>
        public void ServerRecordCapture(CreatureBrain creature, ulong byClientId)
        {
            if (!IsServer || creature == null || creature.Definition == null)
            {
                return;
            }

            var contract = ActiveContract;
            var value = Payout.Bounty(creature.Definition.bountyCapture, contract != null ? contract.rewardMultiplier : 1f);
            m_CrewTotal += value;

            m_Ledger.Add(new LedgerEntry
            {
                Kind = LedgerKind.Capture,
                SubjectId = creature.Definition.StableId,
                ClientId = byClientId,
                ByClientId = DamageInfo.NoInstigator,
                Value = value
            });

            ForestDirector.Instance?.ServerNotifyCaptured(creature);
        }

        void HandlePlayerDied(PlayerVitals victim, DamageInfo info)
        {
            if (!IsOnExpedition)
            {
                return;
            }

            // The shooter is never written here. "Gunfire" is all the record says.
            m_Ledger.Add(new LedgerEntry
            {
                Kind = LedgerKind.PlayerDeath,
                SubjectId = info.SourceId,
                ClientId = victim.OwnerClientId,
                ByClientId = DamageInfo.NoInstigator,
                Cause = info.Kind
            });
        }

        /// <summary>Server only. Names a murderer for everyone to see.</summary>
        public void ServerRecordExposed(ulong killer, ulong victim)
        {
            if (!IsServer)
            {
                return;
            }

            m_Forfeited.Add(killer);
            m_Ledger.Add(new LedgerEntry
            {
                Kind = LedgerKind.Exposed,
                ClientId = victim,
                ByClientId = killer
            });
        }

        // ---------------------------------------------------------------- settlement

        void SettleMoney(bool successful)
        {
            if (!successful)
            {
                m_Ledger.Add(new LedgerEntry { Kind = LedgerKind.Lost });
                return;
            }

            var contract = ActiveContract;
            var remaining = ForestDirector.Instance != null ? ForestDirector.Instance.CreaturesRemaining : 0;
            if (contract != null && remaining == 0 && contract.completionBonus > 0)
            {
                m_CrewTotal += contract.completionBonus;
                m_Ledger.Add(new LedgerEntry { Kind = LedgerKind.Completion, Value = contract.completionBonus });
            }

            var survivors = new List<PlayerVitals>();
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals != null && !vitals.IsDead && !m_Forfeited.Contains(vitals.OwnerClientId))
                {
                    survivors.Add(vitals);
                }
            }

            foreach (var vitals in survivors)
            {
                var share = Payout.Share(m_CrewTotal, survivors.Count, vitals.BountyMultiplier());
                if (vitals.TryGetComponent<PlayerWallet>(out var wallet))
                {
                    wallet.ServerAdd(share);
                }

                m_Ledger.Add(new LedgerEntry
                {
                    Kind = LedgerKind.Extracted,
                    ClientId = vitals.OwnerClientId,
                    Value = share
                });
            }
        }

        /// <summary>
        /// Server only. Moves every player back into their stash on a clean extraction,
        /// or wipes what they carried on a failed one.
        /// </summary>
        void SettleCarriedEquipment(bool successful)
        {
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null ||
                    !client.PlayerObject.TryGetComponent<PlayerStash>(out var stash))
                {
                    continue;
                }

                // The dead already dropped their pack in the forest. Only the living carry
                // anything home.
                var alive = client.PlayerObject.TryGetComponent<PlayerVitals>(out var vitals) && !vitals.IsDead;

                if (successful && alive)
                {
                    stash.ServerReturnCarried();
                }
                else
                {
                    stash.ServerLoseCarried();
                }
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

        /// <summary>The host closes the debrief and the crew is back to preparing.</summary>
        [Rpc(SendTo.Server)]
        public void FinishDebriefRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            if (Phase == MissionPhase.Debrief)
            {
                SetPhase(MissionPhase.Preparing);
                m_ContractIndex.Value = -1;
            }
        }
    }
}
