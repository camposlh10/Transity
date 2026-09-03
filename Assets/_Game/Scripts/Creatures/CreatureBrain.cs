using System;
using System.Collections.Generic;
using Transity.Combat;
using Transity.Core;
using Transity.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Transity.Creatures
{
    /// <summary>
    /// A creature's mind. Runs on the server only; clients receive the state, the target
    /// and an awareness figure and do the rest with those.
    ///
    /// The design goal is a creature that is fair and frightening in equal measure. Every
    /// attack is telegraphed -- a sound and a wind-up before the lunge -- so a player who
    /// is paying attention can sidestep it. Awareness accrues rather than flipping, so
    /// there is a window to duck away. Stalkers respect being looked at. And when hurt
    /// badly enough, creatures leave, heal, and come back: a fight you did not finish is
    /// a fight you will have again.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(Sedation))]
    public sealed class CreatureBrain : NetworkBehaviour
    {
        [SerializeField] CreatureDefinition definition;
        [SerializeField] Transform eye;

        [Tooltip("Layers that block sight.")]
        [SerializeField] LayerMask occlusionMask = 1;

        // ---- replicated ---------------------------------------------------
        readonly NetworkVariable<CreatureState> m_State = new(CreatureState.Idle);
        readonly NetworkVariable<ulong> m_Target = new(DamageInfo.NoInstigator);
        readonly NetworkVariable<float> m_Awareness = new();
        readonly NetworkVariable<bool> m_Tagged = new();
        readonly NetworkVariable<int> m_DefinitionId = new();

        // ---- server -------------------------------------------------------
        NavMeshAgent m_Agent;
        Health m_Health;
        Sedation m_Sedation;
        int m_PackId;
        Vector3 m_Home;
        float m_Awareness01;
        Vector3 m_LastKnown;
        float m_LastKnownTime = -100f;
        float m_StateEnteredAt;
        float m_NextDestinationAt;
        float m_NextRoamAt;
        float m_RootedUntil;
        float m_AttractUntil;
        Vector3 m_AttractPoint;
        float m_AttackPhaseEndsAt;
        int m_AttackPhase;
        float m_NextAttackAt;
        bool m_AttackLanded;
        Vector3 m_LungeDirection;
        float m_NextBoldnessRoll;
        bool m_PressingOn;
        float m_LostSightSince = -1f;
        float m_StalkSide = 1f;
        float m_NextVocalAt;
        Vector3 m_ShoveVelocity;

        static readonly List<CreatureBrain> Registered = new();
        static readonly Collider[] s_Overlap = new Collider[8];

        /// <summary>Director-controlled: scales awareness gain so the forest can breathe between chases.</summary>
        public static float GlobalAggression = 1f;

        public static IReadOnlyList<CreatureBrain> All => Registered;

        public CreatureDefinition Definition => definition;
        public CreatureState State => m_State.Value;
        public ulong TargetClientId => m_Target.Value;
        public float Awareness => m_Awareness.Value;
        public bool IsTagged => m_Tagged.Value;
        public Health Health => m_Health;
        public Sedation Sedation => m_Sedation;
        public int PackId => m_PackId;
        public Transform Eye => eye != null ? eye : transform;
        public bool IsDown => State is CreatureState.Dead or CreatureState.Sedated;

        /// <summary>Server only: seconds since the current state began.</summary>
        public float TimeInState => Time.time - m_StateEnteredAt;

        /// <summary>Every peer.</summary>
        public event Action<CreatureState, CreatureState> StateChanged;

        /// <summary>Server only. Raised when the creature lands a hit on a player.</summary>
        public event Action<PlayerVitals> ServerBit;

        void Awake()
        {
            m_Agent = GetComponent<NavMeshAgent>();
            m_Health = GetComponent<Health>();
            m_Sedation = GetComponent<Sedation>();
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Server only, before Spawn: which creature this is and where it lives.</summary>
        public void ServerConfigure(CreatureDefinition creature, int packId, Vector3 home)
        {
            definition = creature;
            m_PackId = packId;
            m_Home = home;
        }

        public override void OnNetworkSpawn()
        {
            Registered.Add(this);
            m_State.OnValueChanged += HandleStateChanged;

            if (definition == null && m_DefinitionId.Value != 0 && GameContent.Creatures != null)
            {
                definition = GameContent.Creatures.Find(m_DefinitionId.Value);
            }

            if (!IsServer)
            {
                // The agent only ever moves on the server; a client-side agent would fight
                // the replicated transform.
                m_Agent.enabled = false;
                return;
            }

            if (definition != null)
            {
                m_DefinitionId.Value = definition.StableId;
                ApplyDefinition();
            }

            CreaturePack.Join(m_PackId, this);
            NoiseBus.Emitted += HandleNoise;
            m_Health.ServerDied += HandleServerDied;
            m_Sedation.ServerCollapsed += HandleCollapsed;
            m_Sedation.ServerRecovered += HandleRecoveredFromSedation;
            m_Health.ServerDamaged += HandleServerDamaged;

            if (m_Home == Vector3.zero)
            {
                m_Home = transform.position;
            }

            EnterState(CreatureState.Roam);
            m_NextRoamAt = Time.time + UnityEngine.Random.Range(1f, 4f);
        }

        public override void OnNetworkDespawn()
        {
            Registered.Remove(this);
            m_State.OnValueChanged -= HandleStateChanged;

            if (IsServer)
            {
                CreaturePack.Leave(m_PackId, this);
                NoiseBus.Emitted -= HandleNoise;
                m_Health.ServerDied -= HandleServerDied;
                m_Sedation.ServerCollapsed -= HandleCollapsed;
                m_Sedation.ServerRecovered -= HandleRecoveredFromSedation;
                m_Health.ServerDamaged -= HandleServerDamaged;
            }
        }

        void ApplyDefinition()
        {
            m_Agent.speed = definition.walkSpeed;
            m_Agent.acceleration = definition.acceleration;
            m_Agent.angularSpeed = 0f;
            m_Agent.updateRotation = false;
            m_Agent.radius = definition.agentRadius;
            m_Agent.height = definition.agentHeight;
            m_Agent.stoppingDistance = definition.attackRange * 0.6f;
            m_Agent.autoBraking = true;
            m_Agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            if (NavMesh.SamplePosition(transform.position, out var hit, 6f, NavMesh.AllAreas))
            {
                m_Agent.Warp(hit.position);
            }
        }

        void HandleStateChanged(CreatureState previous, CreatureState current) =>
            StateChanged?.Invoke(previous, current);

        // ------------------------------------------------------------------ server tick

        void Update()
        {
            if (!IsServer || definition == null)
            {
                return;
            }

            if (State == CreatureState.Dead)
            {
                return;
            }

            if (State == CreatureState.Sedated)
            {
                return;
            }

            Perceive();
            Think();
            Steer();
            Vocalise();

            var awareness = Mathf.Clamp01(m_Awareness01);
            if (Mathf.Abs(awareness - m_Awareness.Value) > 0.02f)
            {
                m_Awareness.Value = awareness;
            }
        }

        // ------------------------------------------------------------------ perception

        void Perceive()
        {
            var best = 0f;
            PlayerVitals bestPlayer = null;
            var eyePosition = Eye.position;
            var inTerritory = false;

            foreach (var player in PlayerVitals.All)
            {
                if (player == null || player.IsDead)
                {
                    continue;
                }

                var head = player.transform.position + Vector3.up * 1.5f;
                var to = head - eyePosition;
                var distance = to.magnitude;

                if (definition.temperament == Temperament.Territorial &&
                    (player.transform.position - m_Home).sqrMagnitude < definition.territoryRadius * definition.territoryRadius)
                {
                    inTerritory = true;
                }

                if (distance > definition.sightRange * 2.5f)
                {
                    continue;
                }

                var angle = Vector3.Angle(transform.forward, to);
                var lineOfSight = !Physics.Raycast(eyePosition, to / Mathf.Max(distance, 0.01f), distance,
                    occlusionMask, QueryTriggerInteraction.Ignore);

                var score = Perception.SightScore(distance, definition.sightRange, angle, definition.sightAngle,
                    lineOfSight, VisibilityOf(player));

                if (score > best)
                {
                    best = score;
                    bestPlayer = player;
                }
            }

            var gain = Perception.AwarenessGain(best, definition.secondsToNotice) * GlobalAggression;
            if (inTerritory)
            {
                gain *= 2.2f;
            }

            if (best > 0f && bestPlayer != null)
            {
                m_Awareness01 = Mathf.Min(1.5f, m_Awareness01 + gain * Time.deltaTime);
                m_LastKnown = bestPlayer.transform.position;
                m_LastKnownTime = Time.time;
                m_LostSightSince = -1f;

                if (m_Target.Value == DamageInfo.NoInstigator || m_Awareness01 > 0.6f)
                {
                    m_Target.Value = bestPlayer.OwnerClientId;
                }
            }
            else
            {
                if (m_LostSightSince < 0f)
                {
                    m_LostSightSince = Time.time;
                }

                var decay = State is CreatureState.Chase or CreatureState.Attack
                    ? definition.awarenessDecayPerSecond * 0.35f
                    : definition.awarenessDecayPerSecond;
                m_Awareness01 = Mathf.Max(0f, m_Awareness01 - decay * Time.deltaTime);
            }
        }

        float VisibilityOf(PlayerVitals player)
        {
            var visibility = 1f;

            if (player.TryGetComponent<PlayerLight>(out var light))
            {
                visibility *= light.VisibilityMultiplier;
            }

            if (player.TryGetComponent<FirstPersonController>(out var movement))
            {
                visibility *= movement.State switch
                {
                    MoveState.Crouching => 0.55f,
                    MoveState.Sprinting => 1.45f,
                    MoveState.Walking => 1.1f,
                    _ => 0.9f
                };
            }

            if (GlowStickLight.IsLit(player.transform.position))
            {
                visibility *= 1.4f;
            }

            if (player.IsMasked)
            {
                visibility *= 0.55f;
            }

            return visibility;
        }

        void HandleNoise(NoiseEvent noise)
        {
            if (definition == null || State is CreatureState.Dead or CreatureState.Sedated)
            {
                return;
            }

            var distance = Vector3.Distance(transform.position, noise.Position);
            var loudness = Perception.HearingScore(distance, noise.Radius, definition.hearing);
            if (loudness <= 0f)
            {
                return;
            }

            var weight = noise.Kind switch
            {
                NoiseKind.Gunshot => 0.9f,
                NoiseKind.Alarm => 0.7f,
                NoiseKind.Sprint => 0.35f,
                NoiseKind.Footstep => 0.15f,
                NoiseKind.Impact => 0.4f,
                NoiseKind.Bait => 0.5f,
                _ => 0.2f
            };

            m_Awareness01 = Mathf.Min(1.5f, m_Awareness01 + loudness * weight * GlobalAggression);

            // A noise gives a place to look, not a target to chase.
            if (State is CreatureState.Roam or CreatureState.Idle or CreatureState.Investigate or CreatureState.Recover)
            {
                m_LastKnown = noise.Position;
                m_LastKnownTime = Time.time;

                if (noise.SourceClientId != DamageInfo.NoInstigator && m_Target.Value == DamageInfo.NoInstigator)
                {
                    m_Target.Value = noise.SourceClientId;
                }

                if (State != CreatureState.Recover && m_Awareness01 > 0.25f)
                {
                    EnterState(CreatureState.Investigate);
                }
            }
        }

        // ------------------------------------------------------------------ decisions

        void Think()
        {
            var target = TargetPlayer();
            var distanceToTarget = target != null ? Vector3.Distance(transform.position, target.transform.position) : float.MaxValue;
            var health = m_Health.Fraction;

            // Hurt enough to leave, unless it is a pack that still has the numbers.
            if (State is not (CreatureState.Flee or CreatureState.Recover or CreatureState.Rooted) &&
                health <= definition.fleeHealthFraction)
            {
                var together = CreaturePack.Together(m_PackId, this, definition.packCohesionRadius);
                if (definition.temperament != Temperament.Pack || together < 2 || health <= definition.fleeHealthFraction * 0.5f)
                {
                    EnterState(CreatureState.Flee);
                    return;
                }
            }

            switch (State)
            {
                case CreatureState.Idle:
                    if (m_Awareness01 >= 0.35f)
                    {
                        EnterState(CreatureState.Investigate);
                    }
                    else if (Time.time >= m_NextRoamAt)
                    {
                        EnterState(CreatureState.Roam);
                    }

                    break;

                case CreatureState.Roam:
                    if (Time.time >= m_AttractUntil && m_Awareness01 >= 0.35f)
                    {
                        EnterState(CreatureState.Investigate);
                    }
                    else if (Time.time < m_AttractUntil)
                    {
                        // Bait: walk to it and linger.
                        if (Vector3.Distance(transform.position, m_AttractPoint) < 3f)
                        {
                            EnterState(CreatureState.Idle);
                            m_NextRoamAt = Time.time + 6f;
                        }
                    }
                    else if (!m_Agent.pathPending && m_Agent.remainingDistance < 1.2f && Time.time >= m_NextRoamAt)
                    {
                        EnterState(CreatureState.Idle);
                        m_NextRoamAt = Time.time + UnityEngine.Random.Range(2f, 7f);
                    }

                    break;

                case CreatureState.Investigate:
                    if (m_Awareness01 >= 0.95f && target != null)
                    {
                        EnterState(definition.temperament == Temperament.Hunter ? CreatureState.Stalk : CreatureState.Chase);
                    }
                    else if (m_Awareness01 >= 0.6f && target != null && definition.temperament == Temperament.Hunter)
                    {
                        EnterState(CreatureState.Stalk);
                    }
                    else if (m_Awareness01 < 0.1f || (Time.time - m_LastKnownTime > definition.loseInterestSeconds))
                    {
                        m_Target.Value = DamageInfo.NoInstigator;
                        EnterState(CreatureState.Roam);
                    }
                    else if (!m_Agent.pathPending && m_Agent.remainingDistance < 1.5f && TimeInState > 4f)
                    {
                        // Got there, found nothing. Look around a bit longer, then wander.
                        if (TimeInState > 10f)
                        {
                            m_Awareness01 *= 0.5f;
                            EnterState(CreatureState.Roam);
                        }
                    }

                    break;

                case CreatureState.Stalk:
                    ThinkStalk(target, distanceToTarget);
                    break;

                case CreatureState.Chase:
                    ThinkChase(target, distanceToTarget);
                    break;

                case CreatureState.Attack:
                    ThinkAttack(target);
                    break;

                case CreatureState.Flee:
                    if (!m_Agent.pathPending && m_Agent.remainingDistance < 2f || TimeInState > 12f)
                    {
                        EnterState(CreatureState.Recover);
                    }

                    break;

                case CreatureState.Recover:
                    if (health >= definition.recoveredHealthFraction)
                    {
                        // It remembers. Awareness stays warm so it comes back looking.
                        m_Awareness01 = Mathf.Max(m_Awareness01, 0.5f);
                        EnterState(CreatureState.Investigate);
                    }
                    else if (target != null && distanceToTarget < 9f)
                    {
                        // Cornered while healing: fight anyway.
                        EnterState(CreatureState.Chase);
                    }

                    break;

                case CreatureState.Rooted:
                    if (Time.time >= m_RootedUntil)
                    {
                        EnterState(target != null ? CreatureState.Chase : CreatureState.Investigate);
                    }

                    break;
            }
        }

        void ThinkStalk(PlayerVitals target, float distance)
        {
            if (target == null || m_Awareness01 < 0.2f)
            {
                EnterState(CreatureState.Investigate);
                return;
            }

            if (Time.time - m_LastKnownTime > definition.loseInterestSeconds)
            {
                EnterState(CreatureState.Investigate);
                return;
            }

            // The whole trick of the stalker: it closes when you are not looking.
            var lookedAt = IsLookedAtBy(target);

            if (Time.time >= m_NextBoldnessRoll)
            {
                m_NextBoldnessRoll = Time.time + 1.2f;
                m_PressingOn = UnityEngine.Random.value < definition.boldness * GlobalAggression;
                if (UnityEngine.Random.value < 0.25f)
                {
                    m_StalkSide = -m_StalkSide;
                }
            }

            var committed = distance < definition.stalkDistance * 0.45f || m_Awareness01 >= 1.4f;
            if (committed || (!lookedAt && distance < definition.stalkDistance * 0.7f) || (m_PressingOn && distance < definition.stalkDistance))
            {
                EnterState(CreatureState.Chase);
            }
        }

        void ThinkChase(PlayerVitals target, float distance)
        {
            if (target == null)
            {
                EnterState(CreatureState.Investigate);
                return;
            }

            // Territorial creatures do not follow you out of their patch.
            if (definition.temperament == Temperament.Territorial &&
                (transform.position - m_Home).sqrMagnitude > Mathf.Pow(definition.territoryRadius * 1.6f, 2f))
            {
                m_Awareness01 = 0.4f;
                EnterState(CreatureState.Roam);
                return;
            }

            // Lost them for long enough: go to where they were last seen.
            if (m_LostSightSince > 0f && Time.time - m_LostSightSince > definition.loseInterestSeconds * 0.6f)
            {
                EnterState(CreatureState.Investigate);
                return;
            }

            // Pack members hang back unless they have company.
            if (definition.temperament == Temperament.Pack)
            {
                var together = CreaturePack.Together(m_PackId, this, definition.packCohesionRadius);
                if (together < 2 && distance < definition.attackRange * 3f && UnityEngine.Random.value < 0.6f * Time.deltaTime)
                {
                    m_StalkSide = -m_StalkSide;
                }
            }

            if (distance <= definition.attackRange && Time.time >= m_NextAttackAt && IsFacing(target.transform.position, 70f))
            {
                EnterState(CreatureState.Attack);
            }
        }

        /// <summary>
        /// Wind-up, lunge, recovery. The wind-up is the tell: the creature stops, the body
        /// coils, the sound plays. The lunge is committed to the direction it faced at the
        /// end of the wind-up, so a sidestep during the wind-up makes it miss.
        /// </summary>
        void ThinkAttack(PlayerVitals target)
        {
            if (Time.time < m_AttackPhaseEndsAt)
            {
                if (m_AttackPhase == 1 && !m_AttackLanded)
                {
                    TryLandBite(target);
                }

                return;
            }

            switch (m_AttackPhase)
            {
                case 0:
                    // Wind-up done: lunge along the last facing.
                    m_AttackPhase = 1;
                    m_LungeDirection = transform.forward;
                    m_AttackPhaseEndsAt = Time.time + definition.lungeSeconds;
                    break;

                case 1:
                    m_AttackPhase = 2;
                    m_AttackPhaseEndsAt = Time.time + definition.attackRecovery;
                    break;

                default:
                    m_NextAttackAt = Time.time + definition.attackCooldown;
                    EnterState(CreatureState.Chase);
                    break;
            }
        }

        void TryLandBite(PlayerVitals target)
        {
            var count = Physics.OverlapSphereNonAlloc(transform.position + transform.forward * definition.attackRange * 0.6f,
                definition.attackRange * 0.75f, s_Overlap, 1 << 7, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var vitals = s_Overlap[i].GetComponentInParent<PlayerVitals>();
                if (vitals == null || vitals.IsDead)
                {
                    continue;
                }

                m_AttackLanded = true;

                var direction = (vitals.transform.position - transform.position).normalized;
                vitals.Health.ServerApplyDamage(DamageInfo.FromCreature(
                    definition.attackDamage, DamageKind.Bite,
                    vitals.transform.position + Vector3.up, direction,
                    UnityEngine.Random.value < definition.bleedChance, definition.StableId));

                ServerBit?.Invoke(vitals);
                BiteLandedRpc();
                return;
            }
        }

        // ------------------------------------------------------------------ steering

        void Steer()
        {
            if (!m_Agent.enabled || !m_Agent.isOnNavMesh)
            {
                return;
            }

            var target = TargetPlayer();
            var wantedSpeed = definition.walkSpeed;
            var stopped = false;
            var faceTarget = false;

            switch (State)
            {
                case CreatureState.Idle:
                    stopped = true;
                    break;

                case CreatureState.Roam:
                    if (Time.time >= m_NextDestinationAt)
                    {
                        m_NextDestinationAt = Time.time + 0.5f;
                        if (Time.time < m_AttractUntil)
                        {
                            m_Agent.SetDestination(m_AttractPoint);
                        }
                        else if (!m_Agent.hasPath || m_Agent.remainingDistance < 1f)
                        {
                            m_Agent.SetDestination(RandomPointNear(m_Home, definition.roamRadius));
                        }
                    }

                    break;

                case CreatureState.Investigate:
                    wantedSpeed = definition.stalkSpeed;
                    if (Time.time >= m_NextDestinationAt)
                    {
                        m_NextDestinationAt = Time.time + 0.4f;
                        m_Agent.SetDestination(m_LastKnown);
                    }

                    break;

                case CreatureState.Stalk:
                    wantedSpeed = definition.stalkSpeed;
                    if (target != null && Time.time >= m_NextDestinationAt)
                    {
                        m_NextDestinationAt = Time.time + 0.3f;
                        var lookedAt = IsLookedAtBy(target);
                        var distance = Vector3.Distance(transform.position, target.transform.position);

                        if (lookedAt && !m_PressingOn)
                        {
                            // Hold or drift sideways; never straight at them while watched.
                            if (distance < definition.stalkDistance * 0.8f)
                            {
                                m_Agent.SetDestination(SideStep(target.transform.position, m_StalkSide, 6f, true));
                            }
                            else
                            {
                                stopped = true;
                                faceTarget = true;
                            }
                        }
                        else
                        {
                            m_Agent.SetDestination(SideStep(target.transform.position, m_StalkSide, 4f, false));
                        }
                    }

                    break;

                case CreatureState.Chase:
                    wantedSpeed = definition.runSpeed;
                    if (target != null && Time.time >= m_NextDestinationAt)
                    {
                        m_NextDestinationAt = Time.time + 0.2f;
                        var goal = definition.temperament == Temperament.Pack
                            ? CreaturePack.FlankPoint(m_PackId, this, target.transform.position, definition.flankRadius)
                            : target.transform.position;

                        // Close the last few metres straight on, whatever the flank slot says.
                        if (Vector3.Distance(transform.position, target.transform.position) < definition.attackRange * 2.5f)
                        {
                            goal = target.transform.position;
                        }

                        m_Agent.SetDestination(goal);
                    }

                    break;

                case CreatureState.Attack:
                    if (m_AttackPhase == 0)
                    {
                        stopped = true;
                        faceTarget = true;
                    }
                    else if (m_AttackPhase == 1)
                    {
                        m_Agent.isStopped = false;
                        m_Agent.velocity = m_LungeDirection * definition.lungeSpeed;
                        RotateTowards(m_LungeDirection, definition.turnSpeedDegrees * 0.5f);
                        return;
                    }
                    else
                    {
                        stopped = true;
                    }

                    break;

                case CreatureState.Flee:
                    wantedSpeed = definition.runSpeed;
                    if (Time.time >= m_NextDestinationAt)
                    {
                        m_NextDestinationAt = Time.time + 0.6f;
                        m_Agent.SetDestination(FleePoint());
                    }

                    break;

                case CreatureState.Recover:
                    stopped = true;
                    break;

                case CreatureState.Rooted:
                    stopped = true;
                    faceTarget = true;
                    break;
            }

            m_Agent.isStopped = stopped;
            m_Agent.speed = Mathf.MoveTowards(m_Agent.speed, wantedSpeed, definition.acceleration * Time.deltaTime);

            if (m_ShoveVelocity.sqrMagnitude > 0.01f)
            {
                m_Agent.velocity += m_ShoveVelocity;
                m_ShoveVelocity = Vector3.MoveTowards(m_ShoveVelocity, Vector3.zero, 30f * Time.deltaTime);
            }

            // Rotation is ours, not the agent's, so turns are smooth and the head leads
            // the body into corners instead of snapping.
            Vector3 facing;
            if (faceTarget && target != null)
            {
                facing = target.transform.position - transform.position;
            }
            else if (m_Agent.velocity.sqrMagnitude > 0.05f)
            {
                facing = m_Agent.velocity;
            }
            else if (m_Agent.desiredVelocity.sqrMagnitude > 0.05f)
            {
                facing = m_Agent.desiredVelocity;
            }
            else
            {
                return;
            }

            RotateTowards(facing, definition.turnSpeedDegrees * (State == CreatureState.Chase ? 1.4f : 1f));
        }

        void RotateTowards(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var wanted = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, degreesPerSecond * Time.deltaTime);
        }

        Vector3 SideStep(Vector3 around, float side, float lateral, bool retreat)
        {
            var to = transform.position - around;
            to.y = 0f;
            var distance = Mathf.Max(to.magnitude, 0.5f);
            var right = Vector3.Cross(Vector3.up, to / distance);
            var radial = retreat ? Mathf.Min(distance + 3f, definition.stalkDistance) : Mathf.Max(distance - 4f, 2f);
            var point = around + (to / distance) * radial + right * (side * lateral);
            return NavMesh.SamplePosition(point, out var hit, 4f, NavMesh.AllAreas) ? hit.position : point;
        }

        Vector3 FleePoint()
        {
            var away = Vector3.zero;
            foreach (var player in PlayerVitals.All)
            {
                if (player == null || player.IsDead)
                {
                    continue;
                }

                var to = transform.position - player.transform.position;
                to.y = 0f;
                away += to.normalized / Mathf.Max(1f, to.magnitude * 0.1f);
            }

            if (away.sqrMagnitude < 0.01f)
            {
                away = transform.forward;
            }

            var point = transform.position + away.normalized * 35f + UnityEngine.Random.insideUnitSphere * 8f;
            point.y = transform.position.y;
            return NavMesh.SamplePosition(point, out var hit, 12f, NavMesh.AllAreas) ? hit.position : m_Home;
        }

        static Vector3 RandomPointNear(Vector3 centre, float radius)
        {
            for (var i = 0; i < 6; i++)
            {
                var candidate = centre + UnityEngine.Random.insideUnitSphere * radius;
                candidate.y = centre.y;
                if (NavMesh.SamplePosition(candidate, out var hit, 4f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return centre;
        }

        bool IsFacing(Vector3 point, float coneDegrees) =>
            Perception.IsInsideCone(transform.position, transform.forward, point, coneDegrees);

        bool IsLookedAtBy(PlayerVitals player)
        {
            var head = player.transform.position + Vector3.up * 1.5f;
            var forward = player.transform.forward;
            if (player.TryGetComponent<PlayerCharacter>(out var character) && character.PlayerCamera != null)
            {
                forward = character.PlayerCamera.transform.forward;
            }

            if (!Perception.IsInsideCone(head, forward, Eye.position, 50f))
            {
                return false;
            }

            var to = Eye.position - head;
            return !Physics.Raycast(head, to.normalized, to.magnitude, occlusionMask, QueryTriggerInteraction.Ignore);
        }

        PlayerVitals TargetPlayer()
        {
            if (m_Target.Value == DamageInfo.NoInstigator)
            {
                return null;
            }

            var vitals = PlayerVitals.Find(m_Target.Value);
            if (vitals == null || vitals.IsDead)
            {
                m_Target.Value = DamageInfo.NoInstigator;
                return null;
            }

            return vitals;
        }

        // ------------------------------------------------------------------ transitions

        void EnterState(CreatureState next)
        {
            if (m_State.Value == next && next != CreatureState.Attack)
            {
                return;
            }

            var previous = m_State.Value;
            m_State.Value = next;
            m_StateEnteredAt = Time.time;
            m_NextDestinationAt = 0f;

            switch (next)
            {
                case CreatureState.Attack:
                    m_AttackPhase = 0;
                    m_AttackLanded = false;
                    m_AttackPhaseEndsAt = Time.time + definition.attackWindup;
                    if (m_Agent.enabled && m_Agent.isOnNavMesh)
                    {
                        m_Agent.isStopped = true;
                        m_Agent.velocity = Vector3.zero;
                    }

                    break;

                case CreatureState.Flee:
                    m_Health.RegenPerSecond = 0f;
                    break;

                case CreatureState.Recover:
                    m_Health.RegenPerSecond = definition.regenWhileRecovering;
                    break;

                case CreatureState.Chase:
                    m_LostSightSince = -1f;
                    break;
            }

            if (previous == CreatureState.Recover)
            {
                m_Health.RegenPerSecond = 0f;
            }
        }

        void HandleServerDamaged(DamageInfo info)
        {
            if (State is CreatureState.Dead)
            {
                return;
            }

            // Being shot is very noticeable.
            m_Awareness01 = Mathf.Min(1.5f, m_Awareness01 + 0.6f);

            if (info.HasInstigator)
            {
                m_Target.Value = info.InstigatorClientId;
                var shooter = PlayerVitals.Find(info.InstigatorClientId);
                if (shooter != null)
                {
                    m_LastKnown = shooter.transform.position;
                    m_LastKnownTime = Time.time;
                }
            }

            if (State == CreatureState.Sedated)
            {
                // Shot while down: lethal damage wakes it angry.
                if (info.Kind != DamageKind.Sedative)
                {
                    m_Sedation.ServerWake();
                }

                return;
            }

            if (State is CreatureState.Roam or CreatureState.Idle or CreatureState.Investigate or CreatureState.Stalk)
            {
                EnterState(CreatureState.Chase);
            }
        }

        void HandleServerDied(DamageInfo info)
        {
            EnterState(CreatureState.Dead);
            if (m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }

            m_Agent.enabled = false;
            CreaturePack.Leave(m_PackId, this);
        }

        void HandleCollapsed()
        {
            EnterState(CreatureState.Sedated);
            if (m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }
        }

        void HandleRecoveredFromSedation()
        {
            if (State == CreatureState.Sedated)
            {
                m_Awareness01 = 1f;
                EnterState(CreatureState.Chase);
            }
        }

        // ------------------------------------------------------------------ outside pokes

        /// <summary>Server only. Held in place; a trap.</summary>
        public void ServerRoot(float seconds)
        {
            if (!IsServer || IsDown)
            {
                return;
            }

            m_RootedUntil = Time.time + seconds;
            EnterState(CreatureState.Rooted);
            if (m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }
        }

        /// <summary>Server only. Bait: come here and linger, unless already in a fight.</summary>
        public void ServerAttract(Vector3 point, float seconds)
        {
            if (!IsServer || IsDown || State is CreatureState.Chase or CreatureState.Attack or CreatureState.Flee)
            {
                return;
            }

            m_AttractPoint = point;
            m_AttractUntil = Time.time + seconds;
            if (State != CreatureState.Roam)
            {
                EnterState(CreatureState.Roam);
            }

            m_NextDestinationAt = 0f;
        }

        /// <summary>Server only. A shove from a shotgun or a swing.</summary>
        public void ServerShove(Vector3 impulse)
        {
            if (IsServer && !IsDown)
            {
                m_ShoveVelocity += impulse;
            }
        }

        /// <summary>
        /// Server only. A loud noise close by; skittish creatures that are not already
        /// committed may bolt. Called by weapons through <see cref="ServerBroadcastStartle"/>.
        /// </summary>
        public void ServerStartle(Vector3 from, float strength)
        {
            if (!IsServer || IsDown || definition == null)
            {
                return;
            }

            if (State is CreatureState.Attack or CreatureState.Flee or CreatureState.Recover or CreatureState.Rooted)
            {
                return;
            }

            if (UnityEngine.Random.value < definition.skittishness * strength)
            {
                EnterState(CreatureState.Flee);
            }
        }

        public static void ServerBroadcastStartle(Vector3 from, float radius, ulong sourceClientId)
        {
            foreach (var brain in Registered)
            {
                if (brain == null)
                {
                    continue;
                }

                var distance = Vector3.Distance(brain.transform.position, from);
                if (distance < radius)
                {
                    brain.ServerStartle(from, 1f - distance / radius);
                }
            }
        }

        /// <summary>Server only. Marks a corpse as claimed.</summary>
        public void ServerTag()
        {
            if (IsServer)
            {
                m_Tagged.Value = true;
            }
        }

        void Vocalise()
        {
            if (Time.time < m_NextVocalAt)
            {
                return;
            }

            // Occasional calls while hunting so the crew has an audio bearing on it.
            if (State is CreatureState.Chase or CreatureState.Stalk)
            {
                m_NextVocalAt = Time.time + UnityEngine.Random.Range(4f, 9f);
                VocaliseRpc(State == CreatureState.Chase);
            }
            else
            {
                m_NextVocalAt = Time.time + 2f;
            }
        }

        [Rpc(SendTo.Everyone)]
        void VocaliseRpc(bool aggressive)
        {
            if (TryGetComponent<CreatureAudio>(out var audio))
            {
                audio.Call(aggressive);
            }
        }

        [Rpc(SendTo.Everyone)]
        void BiteLandedRpc()
        {
            if (TryGetComponent<CreatureAudio>(out var audio))
            {
                audio.Bite();
            }
        }

        /// <summary>Client-side estimate of how far through a wind-up the creature is.</summary>
        public float WindupProgress(float sinceStateChange) =>
            definition != null && State == CreatureState.Attack
                ? Mathf.Clamp01(sinceStateChange / Mathf.Max(0.05f, definition.attackWindup))
                : 0f;
    }
}
