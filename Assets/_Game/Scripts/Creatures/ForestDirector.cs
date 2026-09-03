using System.Collections;
using System.Collections.Generic;
using Transity.Core;
using Transity.Missions;
using Transity.Player;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Transity.Creatures
{
    /// <summary>
    /// Runs the forest for one expedition: populates it from the contract, paces the
    /// pressure, and calls the run when the crew is gone.
    ///
    /// Pacing is the part worth explaining. Horror that never lets up stops being horror
    /// after ten minutes, so after every chase ends the director lowers creature
    /// aggression and ramps it back over half a minute. The quiet is where the dread
    /// lives; the chase is the release. Tension is replicated so every client's music
    /// and heartbeat agree on how bad things are.
    /// </summary>
    public sealed class ForestDirector : NetworkBehaviour
    {
        [SerializeField] NavMeshSurface navMesh;
        [SerializeField] float minimumSpawnDistanceFromCrew = 45f;

        [Tooltip("Used instead of the above when the session started in the forest sandbox. " +
                 "Keeping creatures far away is right for a real expedition and tedious when " +
                 "the point is to test a fight.")]
        [SerializeField] float sandboxSpawnDistance = 22f;
        [SerializeField] float wipeGraceSeconds = 4f;

        readonly NetworkVariable<float> m_Tension = new();
        readonly NetworkVariable<int> m_CreaturesRemaining = new();

        readonly List<CreatureBrain> m_Spawned = new();
        float m_ReliefUntil;
        float m_LastChaseSeen;
        bool m_Wiping;
        int m_NextPackId = 1;

        public static ForestDirector Instance { get; private set; }

        /// <summary>0..1 how bad things are, replicated.</summary>
        public float Tension => m_Tension.Value;

        public int CreaturesRemaining => m_CreaturesRemaining.Value;

        /// <summary>How far off creatures start. Closer in the sandbox, so a test is a fight.</summary>
        float SpawnDistance => Core.GameBootstrap.Mode == Core.StartupMode.ForestSandbox
            ? sandboxSpawnDistance
            : minimumSpawnDistanceFromCrew;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer)
            {
                StartCoroutine(ServerRun());
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
                CreatureBrain.GlobalAggression = 1f;
                CreaturePack.Clear();
            }
        }

        IEnumerator ServerRun()
        {
            // Let the scene settle and the players be placed.
            yield return new WaitForSeconds(0.5f);

            while (MissionDirector.Instance == null || MissionDirector.Instance.Phase != MissionPhase.Expedition)
            {
                yield return null;
            }

            EnsureNavMesh();
            yield return null;

            var contract = MissionDirector.Instance.ActiveContract;
            if (contract == null)
            {
                GameLog.Warn("Forest director: no active contract; spawning nothing.");
                yield break;
            }

            SpawnContract(contract);

            // Ease in: the first minute is for finding your feet.
            CreatureBrain.GlobalAggression = 0.35f;
            m_ReliefUntil = Time.time + 50f;

            while (IsSpawned)
            {
                UpdatePacing();
                UpdateTension();
                CheckWipe();
                yield return new WaitForSeconds(0.25f);
            }
        }

        // ------------------------------------------------------------------ navmesh

        /// <summary>
        /// The scaffold bakes a NavMesh into the scene. If a hand-edited forest has none,
        /// bake one now rather than spawn creatures that cannot move.
        /// </summary>
        void EnsureNavMesh()
        {
            if (NavMesh.SamplePosition(Vector3.zero, out _, 5f, NavMesh.AllAreas))
            {
                return;
            }

            if (navMesh == null)
            {
                navMesh = FindFirstObjectByType<NavMeshSurface>();
            }

            if (navMesh == null)
            {
                var go = new GameObject("NavMesh (runtime)");
                navMesh = go.AddComponent<NavMeshSurface>();
                navMesh.collectObjects = CollectObjects.All;
                navMesh.layerMask = 1;
            }

            GameLog.Warn("No NavMesh in the forest; baking at runtime.");
            navMesh.BuildNavMesh();
        }

        // ------------------------------------------------------------------ spawning

        void SpawnContract(ContractDefinition contract)
        {
            var registry = GameContent.Creatures;
            if (registry == null)
            {
                GameLog.Error("No creature registry on GameContent.");
                return;
            }

            SpawnGroup(registry, contract.creature, contract.count, contract.spawnAsPack);

            if (contract.secondaryCreature != null && contract.secondaryCount > 0)
            {
                SpawnGroup(registry, contract.secondaryCreature, contract.secondaryCount, contract.secondaryAsPack);
            }

            m_CreaturesRemaining.Value = m_Spawned.Count;
            GameLog.Net($"Forest populated: {m_Spawned.Count} creatures for '{contract.title}'.");
        }

        void SpawnGroup(CreatureRegistry registry, CreatureDefinition definition, int count, bool asPack)
        {
            if (definition == null || count <= 0)
            {
                return;
            }

            var prefab = registry.PrefabFor(definition);
            if (prefab == null)
            {
                GameLog.Error($"No prefab registered for creature '{definition.id}'.");
                return;
            }

            if (asPack)
            {
                var packId = m_NextPackId++;
                var region = PickRegion();
                for (var i = 0; i < count; i++)
                {
                    SpawnOne(prefab, definition, packId, region, i);
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    SpawnOne(prefab, definition, 0, PickRegion(), i);
                }
            }
        }

        void SpawnOne(GameObject prefab, CreatureDefinition definition, int packId, CreatureSpawnRegion region, int index)
        {
            var centre = region != null ? region.transform.position : transform.position;
            var radius = region != null ? region.Radius : 15f;

            var position = centre;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var candidate = centre + Random.insideUnitSphere * (packId > 0 ? 5f : radius);
                candidate.y = centre.y;
                if (NavMesh.SamplePosition(candidate, out var hit, 6f, NavMesh.AllAreas))
                {
                    position = hit.position;
                    break;
                }
            }

            var instance = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var brain = instance.GetComponent<CreatureBrain>();
            brain.ServerConfigure(definition, packId, centre);

            instance.GetComponent<NetworkObject>().Spawn(true);
            m_Spawned.Add(brain);

            brain.Health.ServerDied += _ => HandleCreatureGone(brain);
        }

        CreatureSpawnRegion PickRegion()
        {
            CreatureSpawnRegion best = null;
            var bestScore = float.MinValue;

            foreach (var region in CreatureSpawnRegion.All)
            {
                if (region == null)
                {
                    continue;
                }

                var nearest = float.MaxValue;
                foreach (var player in PlayerVitals.All)
                {
                    if (player != null)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(region.transform.position, player.transform.position));
                    }
                }

                // Far from the crew, with some randomness so runs differ.
                // Just beyond the minimum, not as far away as possible. Out of sight is the
                // requirement; the far corner of the map only means a long walk before
                // anything happens.
                var minimum = SpawnDistance;
                var score = -Mathf.Abs(nearest - minimum) + Random.Range(0f, 12f);
                if (nearest < minimum)
                {
                    score -= 100f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = region;
                }
            }

            return best;
        }

        void HandleCreatureGone(CreatureBrain brain)
        {
            m_CreaturesRemaining.Value = Mathf.Max(0, m_CreaturesRemaining.Value - 1);
        }

        /// <summary>Server only. A capture removes the creature without it dying.</summary>
        public void ServerNotifyCaptured(CreatureBrain brain)
        {
            HandleCreatureGone(brain);
        }

        // ------------------------------------------------------------------ pacing

        void UpdatePacing()
        {
            var anyChasing = false;
            foreach (var brain in CreatureBrain.All)
            {
                if (brain != null && brain.State is CreatureState.Chase or CreatureState.Attack)
                {
                    anyChasing = true;
                    break;
                }
            }

            if (anyChasing)
            {
                m_LastChaseSeen = Time.time;
                CreatureBrain.GlobalAggression = 1f;
                return;
            }

            // A chase just ended: give the crew a breather, then bring the pressure back.
            if (Time.time - m_LastChaseSeen < 1f && m_LastChaseSeen > 0f)
            {
                m_ReliefUntil = Time.time + 35f;
            }

            if (Time.time < m_ReliefUntil)
            {
                CreatureBrain.GlobalAggression = 0.3f;
            }
            else
            {
                CreatureBrain.GlobalAggression = Mathf.MoveTowards(CreatureBrain.GlobalAggression, 1f, 0.25f * 0.03f);
            }
        }

        void UpdateTension()
        {
            var tension = 0f;

            foreach (var brain in CreatureBrain.All)
            {
                if (brain == null || brain.IsDown)
                {
                    continue;
                }

                var stateWeight = brain.State switch
                {
                    CreatureState.Attack => 1f,
                    CreatureState.Chase => 0.9f,
                    CreatureState.Stalk => 0.6f,
                    CreatureState.Investigate => 0.35f,
                    _ => 0.1f
                };

                // Nearest living player to this creature.
                var nearest = float.MaxValue;
                foreach (var player in PlayerVitals.All)
                {
                    if (player != null && !player.IsDead)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, brain.transform.position));
                    }
                }

                var proximity = 1f - Mathf.Clamp01((nearest - 5f) / 45f);
                tension = Mathf.Max(tension, Mathf.Clamp01(stateWeight * (0.4f + 0.6f * proximity) + brain.Awareness * 0.2f));
            }

            // Ease down slower than up: the heart takes a moment to settle.
            var current = m_Tension.Value;
            var next = tension > current
                ? Mathf.MoveTowards(current, tension, 0.9f * 0.25f)
                : Mathf.MoveTowards(current, tension, 0.18f * 0.25f);

            if (Mathf.Abs(next - current) > 0.01f)
            {
                m_Tension.Value = next;
            }
        }

        void CheckWipe()
        {
            if (m_Wiping || PlayerVitals.All.Count == 0)
            {
                return;
            }

            if (PlayerVitals.AliveCount > 0)
            {
                return;
            }

            m_Wiping = true;
            StartCoroutine(WipeRoutine());
        }

        IEnumerator WipeRoutine()
        {
            GameLog.Net("Crew wiped. Ending the expedition.");
            yield return new WaitForSeconds(wipeGraceSeconds);
            MissionDirector.Instance?.Extract(successful: false);
        }
    }
}
