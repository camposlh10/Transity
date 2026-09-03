using System;
using System.Collections;
using System.Collections.Generic;
using Transity.Combat;
using Transity.Core;
using Transity.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Missions
{
    /// <summary>
    /// The Collector. Some way into an expedition, one hunter -- and only that hunter --
    /// is visited by something that offers money for a teammate's death, on one
    /// condition: nobody sees it done.
    ///
    /// Everything about it is server-decided and privately delivered. The offer goes to
    /// a single client; the acceptance comes back on an owner-gated RPC; the kill is
    /// judged by a witness check at the moment of the killing blow. If seen, the killer
    /// is named on the ledger and forfeits their share. If not, the ledger records a death
    /// by gunfire and nothing else, and the payment arrives with a private note at the
    /// debrief. The crew is left with a body, a cause, and each other.
    /// </summary>
    public sealed class CollectorContract : NetworkBehaviour
    {
        [SerializeField] float earliestOfferSeconds = 45f;
        [SerializeField] float latestOfferSeconds = 150f;
        [SerializeField] float witnessFov = 110f;
        [SerializeField] float witnessRange = 45f;

        // ---- server ----
        ulong m_Traitor = DamageInfo.NoInstigator;
        ulong m_Target = DamageInfo.NoInstigator;
        bool m_Offered;
        bool m_Accepted;
        bool m_Fulfilled;
        bool m_Exposed;
        int m_Bonus;
        Coroutine m_Routine;

        public static CollectorContract Instance { get; private set; }

        // ---- local (the traitor's client) ----
        public static bool HasPendingOffer { get; private set; }
        public static string OfferTargetName { get; private set; }
        public static int OfferBonus { get; private set; }
        public static bool LocalAccepted { get; private set; }

        public static event Action OfferReceived;
        public static event Action OfferClosed;
        public static event Action<string> PrivateNote;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer && MissionDirector.Instance != null)
            {
                MissionDirector.Instance.PhaseChanged += HandlePhaseChanged;
                PlayerVitals.ServerPlayerDied += HandlePlayerDied;
                PlayerVitals.FullFriendlyFire = IsSanctioned;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (IsServer)
            {
                if (MissionDirector.Instance != null)
                {
                    MissionDirector.Instance.PhaseChanged -= HandlePhaseChanged;
                }

                PlayerVitals.ServerPlayerDied -= HandlePlayerDied;
                PlayerVitals.FullFriendlyFire = null;
            }

            HasPendingOffer = false;
            LocalAccepted = false;
        }

        bool IsSanctioned(ulong shooter, ulong victim) =>
            m_Accepted && !m_Fulfilled && !m_Exposed && shooter == m_Traitor && victim == m_Target;

        // ------------------------------------------------------------------ server

        void HandlePhaseChanged(MissionPhase phase)
        {
            switch (phase)
            {
                case MissionPhase.Expedition:
                    Reset();
                    m_Routine = StartCoroutine(OfferRoutine());
                    break;

                case MissionPhase.Extracting:
                    if (m_Routine != null)
                    {
                        StopCoroutine(m_Routine);
                        m_Routine = null;
                    }

                    SettlePrivately();
                    break;

                case MissionPhase.Preparing:
                    Reset();
                    ClearOfferRpc();
                    break;
            }
        }

        void Reset()
        {
            m_Traitor = DamageInfo.NoInstigator;
            m_Target = DamageInfo.NoInstigator;
            m_Offered = false;
            m_Accepted = false;
            m_Fulfilled = false;
            m_Exposed = false;
            m_Bonus = 0;
        }

        IEnumerator OfferRoutine()
        {
            var contract = MissionDirector.Instance != null ? MissionDirector.Instance.ActiveContract : null;
            if (contract == null || UnityEngine.Random.value > contract.betrayalChance)
            {
                yield break;
            }

            yield return new WaitForSeconds(UnityEngine.Random.Range(earliestOfferSeconds, latestOfferSeconds));

            if (MissionDirector.Instance == null || MissionDirector.Instance.Phase != MissionPhase.Expedition)
            {
                yield break;
            }

            var alive = new List<PlayerVitals>();
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals != null && !vitals.IsDead)
                {
                    alive.Add(vitals);
                }
            }

            // No one to betray, or no one to betray them to.
            if (alive.Count < 2)
            {
                yield break;
            }

            var traitor = alive[UnityEngine.Random.Range(0, alive.Count)];
            alive.Remove(traitor);
            var target = alive[UnityEngine.Random.Range(0, alive.Count)];

            m_Traitor = traitor.OwnerClientId;
            m_Target = target.OwnerClientId;
            m_Bonus = contract.betrayalBonus;
            m_Offered = true;

            GameLog.Net($"The Collector visits client {m_Traitor} about client {m_Target}.");
            OfferRpc(new FixedString32Bytes(PlayerIdentity.NameOf(m_Target)), m_Bonus,
                RpcTarget.Single(m_Traitor, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AnswerRpc(bool accepted, RpcParams rpcParams = default)
        {
            if (!m_Offered || rpcParams.Receive.SenderClientId != m_Traitor)
            {
                return;
            }

            m_Accepted = accepted;
            GameLog.Net($"Collector offer {(accepted ? "accepted" : "declined")} by client {m_Traitor}.");
        }

        void HandlePlayerDied(PlayerVitals victim, DamageInfo info)
        {
            if (!m_Accepted || m_Fulfilled || m_Exposed)
            {
                return;
            }

            if (victim.OwnerClientId != m_Target || info.InstigatorClientId != m_Traitor)
            {
                return;
            }

            var killer = PlayerVitals.Find(m_Traitor);
            if (killer == null)
            {
                return;
            }

            var observers = new List<WitnessCheck.Observer>();
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals == null || vitals.IsDead || vitals == killer || vitals == victim)
                {
                    continue;
                }

                observers.Add(new WitnessCheck.Observer
                {
                    Position = vitals.transform.position + Vector3.up * 1.5f,
                    Forward = vitals.transform.forward,
                    FovDegrees = witnessFov,
                    MaxDistance = witnessRange
                });
            }

            var killerHead = killer.transform.position + Vector3.up * 1.5f;
            var witnessed = WitnessCheck.IsWitnessed(killerHead, observers, HasLineOfSight);

            if (witnessed)
            {
                m_Exposed = true;
                MissionDirector.Instance?.ServerRecordExposed(m_Traitor, m_Target);
                GameLog.Net("The Collector's work was seen.");
            }
            else
            {
                m_Fulfilled = true;
                GameLog.Net("The Collector's work went unseen.");
            }
        }

        static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            var distance = direction.magnitude;
            return distance < 0.01f ||
                   !Physics.Raycast(from, direction / distance, distance - 0.5f, 1, QueryTriggerInteraction.Ignore);
        }

        void SettlePrivately()
        {
            if (!m_Fulfilled)
            {
                return;
            }

            var traitor = PlayerVitals.Find(m_Traitor);
            if (traitor == null || traitor.IsDead)
            {
                // Dead men are not paid.
                return;
            }

            if (traitor.TryGetComponent<PlayerWallet>(out var wallet))
            {
                wallet.ServerAdd(m_Bonus);
            }

            PrivateNoteRpc(new FixedString128Bytes($"The Collector is satisfied. {m_Bonus} cr, and no one the wiser."),
                RpcTarget.Single(m_Traitor, RpcTargetUse.Temp));
        }

        // ------------------------------------------------------------------ client

        [Rpc(SendTo.SpecifiedInParams)]
        void OfferRpc(FixedString32Bytes targetName, int bonus, RpcParams rpcParams = default)
        {
            HasPendingOffer = true;
            LocalAccepted = false;
            OfferTargetName = targetName.ToString();
            OfferBonus = bonus;
            OfferReceived?.Invoke();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void PrivateNoteRpc(FixedString128Bytes note, RpcParams rpcParams = default)
        {
            PrivateNote?.Invoke(note.ToString());
        }

        [Rpc(SendTo.Everyone)]
        void ClearOfferRpc()
        {
            HasPendingOffer = false;
            LocalAccepted = false;
            OfferClosed?.Invoke();
        }

        /// <summary>Local. The player's answer to the letter.</summary>
        public void Answer(bool accept)
        {
            if (!HasPendingOffer)
            {
                return;
            }

            HasPendingOffer = false;
            LocalAccepted = accept;
            AnswerRpc(accept);
            OfferClosed?.Invoke();
        }
    }
}
