using System;
using System.Collections;
using Transity.Audio;
using Transity.Combat;
using Transity.Core;
using Transity.Creatures;
using Transity.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace Transity.Player
{
    /// <summary>
    /// What the player does with the item in their hands. Owner input becomes requests;
    /// the server decides whether a shot landed, whether a reload had ammunition to draw
    /// on, whether a medkit had a charge left. Effects the owner can feel -- the kick, the
    /// flash, the sound -- play immediately on the owner and are echoed to everyone else
    /// once the server confirms.
    ///
    /// Hitscan is resolved on the server from the client's reported origin and direction.
    /// There is no lag compensation: with four players on a Relay the miss it introduces
    /// at creature speeds is a metre at worst, and the creatures are big. The origin is
    /// sanity-checked against the player's actual head so a client cannot shoot from
    /// somewhere it is not.
    /// </summary>
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] InventoryComponent inventory;
        [SerializeField] FirstPersonController movement;
        [SerializeField] PlayerLook look;
        [SerializeField] PlayerCharacter character;
        [SerializeField] Health health;
        [SerializeField] Transform remoteHandSocket;
        [SerializeField] Light muzzleLight;

        [Header("Aim")]
        [SerializeField] float defaultFieldOfView = 70f;
        [SerializeField] float aimBlendSpeed = 12f;
        [SerializeField, Range(0.2f, 1f)] float aimSensitivityScale = 0.6f;

        [Header("Hit layers")]
        [Tooltip("What a bullet can hit: world, creatures, players, deployables.")]
        [SerializeField] LayerMask shotMask = (1 << 0) | (1 << 7) | (1 << 9) | (1 << 10);

        // ---- replicated -------------------------------------------------------
        readonly NetworkVariable<bool> m_Reloading = new();
        readonly NetworkVariable<bool> m_Aiming = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // ---- owner ------------------------------------------------------------
        int m_ShownItemId = -1;
        float m_NextShotTime;
        float m_UseHeldSince = -1f;
        float m_Spread;
        bool m_Suspended;
        Animator m_RemoteAnimator;
        GameObject m_RemoteHeld;
        int m_RemoteShownItemId = -1;

        // ---- server -----------------------------------------------------------
        float m_ServerNextShotTime;
        Coroutine m_ReloadRoutine;

        public ItemDefinition Held { get; private set; }
        public WeaponBehaviour HeldWeapon => Held != null ? Held.BehaviourAs<WeaponBehaviour>() : null;
        public bool IsReloading => m_Reloading.Value;
        public bool IsAiming => m_Aiming.Value;
        public float UseProgress { get; private set; }

        /// <summary>Current spread half-angle in degrees, for the crosshair.</summary>
        public float CurrentSpread => m_Spread;

        public event Action HeldChanged;
        public event Action Fired;
        public event Action<bool, bool> HitConfirmed;

        void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (inventory == null) inventory = GetComponent<InventoryComponent>();
            if (movement == null) movement = GetComponent<FirstPersonController>();
            if (look == null) look = GetComponent<PlayerLook>();
            if (character == null) character = GetComponent<PlayerCharacter>();
            if (health == null) health = GetComponent<Health>();
        }

        public override void OnNetworkSpawn()
        {
            inventory.Changed += HandleInventoryChanged;
            m_Reloading.OnValueChanged += HandleReloadingChanged;

            // Left null on purpose: ShowRemote resolves the active body's animator itself,
            // which is the only way to get the right one once a player can change character.
            HandleInventoryChanged();
        }

        public override void OnNetworkDespawn()
        {
            inventory.Changed -= HandleInventoryChanged;
            m_Reloading.OnValueChanged -= HandleReloadingChanged;
        }

        /// <summary>Lowers the hands and ignores input; used while dead or in a screen.</summary>
        public void SetSuspended(bool suspended)
        {
            m_Suspended = suspended;

            if (suspended)
            {
                m_UseHeldSince = -1f;
                UseProgress = 0f;
            }
        }

        // ------------------------------------------------------------------ held item

        void HandleInventoryChanged()
        {
            var id = inventory.SelectedItem;
            ItemDefinition definition = null;
            if (id != InventoryComponent.EmptySlot)
            {
                GameContent.ItemRegistry?.TryGet(id, out definition);
            }

            var changed = Held != definition;
            Held = definition;

            // Owner and everyone else alike: the item goes in the character's right hand.
            // The owner can see their own arms now, and a viewmodel drawn on the camera
            // would sit beside those arms as a second, disembodied copy of the weapon.
            if (id != m_RemoteShownItemId)
            {
                m_RemoteShownItemId = id;
                ShowRemote(definition);
            }

            if (IsOwner && id != m_ShownItemId)
            {
                m_ShownItemId = id;
                m_UseHeldSince = -1f;
                UseProgress = 0f;
            }

            if (changed)
            {
                HeldChanged?.Invoke();
            }
        }

        /// <summary>
        /// Where shots and thrown items leave from: the head, not the camera.
        ///
        /// These are the same thing in first person, where the camera sits on the head
        /// pivot. They stop being the same the moment the camera pulls back for the
        /// third-person view, and firing from three metres behind your own body is both
        /// wrong and a way to shoot the tree you are standing next to.
        /// </summary>
        Vector3 AimOrigin
        {
            get
            {
                var head = character != null && character.PlayerCamera != null
                    ? character.PlayerCamera.transform.parent
                    : null;

                return head != null ? head.position : transform.position + Vector3.up * 1.6f;
            }
        }

        Vector3 AimForward
        {
            get
            {
                var head = character != null && character.PlayerCamera != null
                    ? character.PlayerCamera.transform.parent
                    : null;

                return head != null ? head.forward : transform.forward;
            }
        }

        /// <summary>
        /// Puts the item in the character's right hand -- for everyone, the owner included,
        /// since they can see their own arms. The humanoid rig gives us the bone; the
        /// fallback socket is for the capsule placeholder.
        /// </summary>
        void ShowRemote(ItemDefinition definition)
        {
            if (m_RemoteHeld != null)
            {
                Destroy(m_RemoteHeld);
                m_RemoteHeld = null;
            }

            if (definition == null || definition.HeldModel == null)
            {
                return;
            }

            // Must be the *active* body's animator. All three characters live on the prefab
            // and CharacterSkin enables one, so an inactive-inclusive search can hand back
            // a skeleton nobody is wearing -- and the weapon would then be parented into a
            // disabled hand and never drawn.
            Transform socket = null;
            if (m_RemoteAnimator == null || !m_RemoteAnimator.gameObject.activeInHierarchy)
            {
                m_RemoteAnimator = null;

                foreach (var animator in GetComponentsInChildren<Animator>(true))
                {
                    if (animator.gameObject.activeInHierarchy)
                    {
                        m_RemoteAnimator = animator;
                        break;
                    }
                }
            }

            if (m_RemoteAnimator != null && m_RemoteAnimator.isHuman)
            {
                socket = m_RemoteAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            }

            if (socket == null)
            {
                socket = remoteHandSocket != null ? remoteHandSocket : transform;
            }

            m_RemoteHeld = Instantiate(definition.HeldModel, socket);
            m_RemoteHeld.name = "HeldRemote";
            m_RemoteHeld.transform.localPosition = new Vector3(0.02f, -0.03f, 0.08f);
            m_RemoteHeld.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);

            foreach (var collider in m_RemoteHeld.GetComponentsInChildren<Collider>(true))
            {
                Destroy(collider);
            }
        }

        // ------------------------------------------------------------------- owner

        void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            UpdateAim();

            if (m_Suspended || input == null || input.Suppressed || (health != null && health.IsDead))
            {
                return;
            }

            switch (Held?.UseKind ?? ItemUseKind.Passive)
            {
                case ItemUseKind.Weapon:
                    UpdateWeapon();
                    break;
                case ItemUseKind.Consumable:
                    UpdateConsumable();
                    break;
                case ItemUseKind.Deployable:
                    if (input.AttackPressed)
                    {
                        TryDeploy();
                    }

                    break;
            }
        }

        void UpdateAim()
        {
            var camera = character != null ? character.PlayerCamera : null;
            var weapon = HeldWeapon;
            var wantsAim = !m_Suspended && input != null && !input.Suppressed && weapon != null &&
                           weapon.fireMode == FireMode.Hitscan && input.AimHeld && !m_Reloading.Value;

            if (m_Aiming.Value != wantsAim)
            {
                m_Aiming.Value = wantsAim;
            }

            if (look != null)
            {
                look.SensitivityScale = wantsAim ? aimSensitivityScale : 1f;
            }

            if (camera != null)
            {
                var targetFov = wantsAim && weapon.aimFieldOfView > 0f ? weapon.aimFieldOfView : defaultFieldOfView;
                camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, targetFov, aimBlendSpeed * Time.deltaTime);
            }

            // Spread blooms with movement and each shot, and tightens while aiming.
            var moving = movement != null ? Mathf.Clamp01(movement.CurrentSpeed / 6f) : 0f;
            var baseSpread = weapon != null ? (wantsAim ? weapon.aimSpreadDegrees : weapon.spreadDegrees) : 0f;
            var target = baseSpread * (1f + moving * 1.5f);
            m_Spread = Mathf.MoveTowards(m_Spread, target, Time.deltaTime * 12f);
        }

        // ------------------------------------------------------------------ weapons

        void UpdateWeapon()
        {
            var weapon = HeldWeapon;
            if (weapon == null)
            {
                return;
            }

            var slot = inventory.SelectedSlot;

            if (input.ReloadPressed)
            {
                TryReload(weapon, slot);
                return;
            }

            var wantsFire = weapon.automatic ? input.AttackHeld : input.AttackPressed;
            if (!wantsFire || Time.time < m_NextShotTime || m_Reloading.Value)
            {
                return;
            }

            if (weapon.magazineSize > 0 && inventory.GetState(slot) <= 0)
            {
                AudioPool.Play2D(SoundKind.DryFire, 0.6f);
                m_NextShotTime = Time.time + 0.25f;

                // Auto-reload on an empty click if there is anything to reload with.
                TryReload(weapon, slot);
                return;
            }

            m_NextShotTime = Time.time + weapon.SecondsBetweenShots;

            var origin = AimOrigin;
            var forward = AimForward;
            var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            PlayFireEffects(weapon, origin, forward, true);
            FireRpc(slot, origin, forward, seed);
        }

        void TryReload(WeaponBehaviour weapon, int slot)
        {
            if (m_Reloading.Value || weapon.magazineSize <= 0)
            {
                return;
            }

            if (inventory.GetState(slot) >= weapon.magazineSize)
            {
                return;
            }

            if (weapon.usesAmmoBox && FindAmmoSlot() < 0)
            {
                AudioPool.Play2D(SoundKind.DryFire, 0.5f);
                return;
            }

            ReloadRpc(slot);
        }

        int FindAmmoSlot() => inventory.FindSlot((definition, state) =>
            definition.ItemId == "item.ammo" && state > 0);

        /// <summary>The owner's half of a shot: what they feel before the server answers.</summary>
        void PlayFireEffects(WeaponBehaviour weapon, Vector3 origin, Vector3 direction, bool local)
        {
            // Off the model in the character's hand, so the flash is where the barrel is
            // rather than where the eye is.
            var muzzle = m_RemoteHeld != null
                ? m_RemoteHeld.transform.TransformPoint(weapon.muzzleOffset)
                : origin;

            if (local)
            {
                AudioPool.Play2D(weapon.fireSound, 0.9f, AudioPool.Vary(0.05f));
                look?.Kick(weapon.recoilKick, UnityEngine.Random.Range(-0.3f, 0.3f) * weapon.recoilKick);
                m_Spread += weapon.recoilKick * 0.8f;
                Fired?.Invoke();
            }
            else
            {
                AudioPool.PlayAt(weapon.fireSound, muzzle, 1f, AudioPool.Vary(0.05f), 90f);
            }

            if (weapon.fireMode == FireMode.Hitscan)
            {
                StartCoroutine(FlashMuzzle(muzzle));
            }
        }

        IEnumerator FlashMuzzle(Vector3 position)
        {
            if (muzzleLight == null)
            {
                yield break;
            }

            muzzleLight.transform.position = position;
            muzzleLight.enabled = true;
            yield return new WaitForSeconds(0.045f);
            muzzleLight.enabled = false;
        }

        // ------------------------------------------------------------- consumables

        void UpdateConsumable()
        {
            var consumable = Held.BehaviourAs<ConsumableBehaviour>();
            if (consumable == null)
            {
                return;
            }

            var slot = inventory.SelectedSlot;

            if (input.AttackPressed && m_UseHeldSince < 0f)
            {
                if (!consumable.canUseAtFullHealth && health != null && health.Fraction >= 0.999f &&
                    !health.IsBleeding && consumable.adrenalineSeconds <= 0f && !consumable.restoresStamina)
                {
                    GetComponent<PlayerFeedback>()?.NotifyLocal("You are unhurt.");
                    return;
                }

                m_UseHeldSince = Time.time;
            }

            if (m_UseHeldSince < 0f)
            {
                UseProgress = 0f;
                return;
            }

            // Sprinting or letting go interrupts. Taking a hit does too, through Health.
            var interrupted = !input.AttackHeld || (movement != null && movement.IsSprinting);
            if (interrupted)
            {
                m_UseHeldSince = -1f;
                UseProgress = 0f;
                return;
            }

            UseProgress = Mathf.Clamp01((Time.time - m_UseHeldSince) / Mathf.Max(0.05f, consumable.useSeconds));

            if (UseProgress >= 1f)
            {
                m_UseHeldSince = -1f;
                UseProgress = 0f;
                AudioPool.Play2D(SoundKind.Chime, 0.5f);
                ConsumeRpc(slot);
            }
        }

        // ------------------------------------------------------------- deployables

        void TryDeploy()
        {
            var deployable = Held.BehaviourAs<DeployableBehaviour>();
            if (deployable == null || deployable.prefab == null)
            {
                return;
            }

            var origin = AimOrigin;
            var forward = AimForward;

            Vector3 position;
            if (deployable.throwSpeed > 0f)
            {
                position = origin + forward * 0.5f;
            }
            else
            {
                position = origin + forward * deployable.placeDistance;

                if (deployable.placeOnGround &&
                    Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, 1,
                        QueryTriggerInteraction.Ignore))
                {
                    position = hit.point;
                }
            }

            DeployRpc(inventory.SelectedSlot, position, forward);
        }

        // ------------------------------------------------------------------ server

        [Rpc(SendTo.Server)]
        void FireRpc(int slot, Vector3 origin, Vector3 direction, int seed)
        {
            if (health != null && health.IsDead)
            {
                return;
            }

            if (!inventory.TryGetDefinition(slot, out var definition) ||
                definition.BehaviourAs<WeaponBehaviour>() is not { } weapon)
            {
                return;
            }

            // Lenient on cadence: the client's clock ran first. Hard on the origin: the
            // shot must come from roughly where this player's head is.
            if (Time.time < m_ServerNextShotTime - weapon.SecondsBetweenShots * 0.35f || m_Reloading.Value)
            {
                return;
            }

            var head = transform.position + Vector3.up * 1.7f;
            if ((origin - head).sqrMagnitude > 2.5f * 2.5f)
            {
                GameLog.Net($"Rejected shot from client {OwnerClientId}: origin too far from head.");
                return;
            }

            if (weapon.magazineSize > 0)
            {
                var rounds = inventory.GetState(slot);
                if (rounds <= 0)
                {
                    return;
                }

                inventory.ServerSetState(slot, rounds - 1);
            }

            m_ServerNextShotTime = Time.time + weapon.SecondsBetweenShots;
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;

            var random = new System.Random(seed);
            var anyHit = false;
            var anyWeak = false;
            var anyKill = false;
            var farthest = origin + direction * weapon.range;

            if (weapon.fireMode == FireMode.Melee)
            {
                ResolveMelee(weapon, origin, direction, ref anyHit, ref anyWeak, ref anyKill);
            }
            else
            {
                var spread = m_Aiming.Value ? weapon.aimSpreadDegrees : weapon.spreadDegrees;
                for (var i = 0; i < Mathf.Max(1, weapon.pellets); i++)
                {
                    var pellet = AmmoMath.Scatter(direction, spread, random);
                    if (ResolveHitscan(weapon, origin, pellet, out var point, ref anyWeak, ref anyKill))
                    {
                        anyHit = true;
                        farthest = point;
                    }
                }
            }

            NoiseBus.Emit(origin, weapon.noiseRadius, weapon.fireMode == FireMode.Melee ? NoiseKind.Impact : NoiseKind.Gunshot, OwnerClientId);
            CreatureBrain.ServerBroadcastStartle(origin, weapon.noiseRadius * 0.15f, OwnerClientId);

            FireEffectsRpc(slot, origin, direction, farthest, anyHit);

            if (anyHit)
            {
                HitConfirmRpc(anyWeak, anyKill);
            }
        }

        bool ResolveHitscan(WeaponBehaviour weapon, Vector3 origin, Vector3 direction, out Vector3 point,
            ref bool weak, ref bool kill)
        {
            point = origin + direction * weapon.range;

            // Skip the shooter's own colliders: everything on this object is on the Player
            // layer, and a shot from inside the capsule would hit it first.
            var hits = Physics.RaycastAll(origin, direction, weapon.range, shotMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                point = hit.point;

                if (!Hitbox.TryResolve(hit.collider, out var target, out var multiplier, out var weakPoint))
                {
                    return false;
                }

                if (!target.IsAlive)
                {
                    return false;
                }

                ApplyHit(weapon, target, hit.point, direction, multiplier, weakPoint, ref weak, ref kill);
                return true;
            }

            return false;
        }

        void ResolveMelee(WeaponBehaviour weapon, Vector3 origin, Vector3 direction, ref bool hit,
            ref bool weak, ref bool kill)
        {
            var reach = Mathf.Max(1.5f, weapon.range);
            var hits = Physics.SphereCastAll(origin, 0.45f, direction, reach, shotMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var candidate in hits)
            {
                if (candidate.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (!Hitbox.TryResolve(candidate.collider, out var target, out var multiplier, out var weakPoint) ||
                    !target.IsAlive)
                {
                    return;
                }

                hit = true;
                ApplyHit(weapon, target, candidate.point, direction, multiplier, weakPoint, ref weak, ref kill);
                MeleeImpactRpc(candidate.point);
                return;
            }
        }

        void ApplyHit(WeaponBehaviour weapon, Health target, Vector3 point, Vector3 direction,
            float multiplier, bool weakPoint, ref bool weak, ref bool kill)
        {
            var amount = weapon.damage * multiplier * (weakPoint ? weapon.weakPointMultiplier : 1f);
            var info = new DamageInfo
            {
                Amount = amount,
                Kind = weapon.fireMode == FireMode.Melee ? DamageKind.Melee
                    : weapon.IsSedative ? DamageKind.Sedative : DamageKind.Ballistic,
                Point = point,
                Direction = direction,
                InstigatorClientId = OwnerClientId,
                WeakPoint = weakPoint,
                Sedation = weapon.sedation,
                CausesBleeding = !weapon.IsSedative && UnityEngine.Random.value < weapon.bleedChance
            };

            var wasAlive = target.IsAlive;

            if (target.TryGetComponent<PlayerVitals>(out var vitals))
            {
                vitals.ServerApplyPlayerDamage(info);
            }
            else
            {
                target.ServerApplyDamage(info);

                if (weapon.sedation > 0f && target.TryGetComponent<Sedation>(out var sedation))
                {
                    sedation.ServerDose(weapon.sedation * multiplier);
                }

                if (weapon.knockback > 0f && target.TryGetComponent<CreatureBrain>(out var brain))
                {
                    brain.ServerShove(direction * weapon.knockback);
                }
            }

            weak |= weakPoint;
            kill |= wasAlive && target.IsDead;
        }

        [Rpc(SendTo.Server)]
        void ReloadRpc(int slot)
        {
            if (m_Reloading.Value || !inventory.TryGetDefinition(slot, out var definition) ||
                definition.BehaviourAs<WeaponBehaviour>() is not { } weapon || weapon.magazineSize <= 0)
            {
                return;
            }

            if (inventory.GetState(slot) >= weapon.magazineSize)
            {
                return;
            }

            var ammoSlot = weapon.usesAmmoBox ? FindAmmoSlot() : -1;
            if (weapon.usesAmmoBox && ammoSlot < 0)
            {
                return;
            }

            if (m_ReloadRoutine != null)
            {
                StopCoroutine(m_ReloadRoutine);
            }

            m_ReloadRoutine = StartCoroutine(ReloadRoutine(slot, ammoSlot, weapon));
        }

        IEnumerator ReloadRoutine(int slot, int ammoSlot, WeaponBehaviour weapon)
        {
            m_Reloading.Value = true;
            yield return new WaitForSeconds(weapon.reloadSeconds);

            // Re-validate: the weapon may have been dropped or swapped mid-reload.
            if (inventory.TryGetDefinition(slot, out var definition) &&
                definition.BehaviourAs<WeaponBehaviour>() == weapon)
            {
                var magazine = inventory.GetState(slot);
                var reserve = weapon.usesAmmoBox ? inventory.GetState(ammoSlot) : weapon.magazineSize;

                if (!weapon.usesAmmoBox || (inventory.TryGetDefinition(ammoSlot, out var box) && box.ItemId == "item.ammo"))
                {
                    AmmoMath.Reload(ref magazine, weapon.magazineSize, ref reserve);
                    inventory.ServerSetState(slot, magazine);

                    if (weapon.usesAmmoBox)
                    {
                        inventory.ServerSetState(ammoSlot, reserve);
                        if (reserve <= 0)
                        {
                            inventory.TakeFrom(ammoSlot);
                        }
                    }
                }
            }

            m_Reloading.Value = false;
            m_ReloadRoutine = null;
        }

        [Rpc(SendTo.Server)]
        void ConsumeRpc(int slot)
        {
            if (health == null || health.IsDead)
            {
                return;
            }

            if (!inventory.TryGetDefinition(slot, out var definition) ||
                definition.BehaviourAs<ConsumableBehaviour>() is not { } consumable)
            {
                return;
            }

            if (inventory.GetState(slot) <= 0)
            {
                return;
            }

            if (consumable.healAmount > 0f || consumable.stopsBleeding)
            {
                health.ServerHeal(consumable.healAmount, consumable.stopsBleeding);
            }

            if (consumable.adrenalineSeconds > 0f || consumable.restoresStamina)
            {
                BoostRpc(consumable.adrenalineSeconds, consumable.restoresStamina);
            }

            if (consumable.maskSeconds > 0f && TryGetComponent<PlayerVitals>(out var vitals))
            {
                vitals.ServerMask(consumable.maskSeconds);
            }

            inventory.ServerSpend(slot);
            GameLog.Net($"Client {OwnerClientId} used {definition.DisplayName}.");
        }

        [Rpc(SendTo.Server)]
        void DeployRpc(int slot, Vector3 position, Vector3 forward)
        {
            if (health == null || health.IsDead)
            {
                return;
            }

            if (!inventory.TryGetDefinition(slot, out var definition) ||
                definition.BehaviourAs<DeployableBehaviour>() is not { } deployable || deployable.prefab == null)
            {
                return;
            }

            if ((position - transform.position).sqrMagnitude > 5f * 5f)
            {
                GameLog.Net($"Rejected deploy from client {OwnerClientId}: too far.");
                return;
            }

            if (inventory.GetState(slot) <= 0)
            {
                return;
            }

            var yaw = Quaternion.Euler(0f, Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
            var instance = Instantiate(deployable.prefab, position, yaw);

            if (!instance.TryGetComponent<NetworkObject>(out var networkObject))
            {
                GameLog.Error($"Deployable prefab for '{definition.ItemId}' has no NetworkObject.");
                Destroy(instance);
                return;
            }

            networkObject.Spawn(true);

            if (instance.TryGetComponent<DeployableBase>(out var deployed))
            {
                deployed.ServerInit(OwnerClientId, definition.NetworkId);
            }

            if (deployable.throwSpeed > 0f && instance.TryGetComponent<Rigidbody>(out var body))
            {
                body.linearVelocity = forward.normalized * deployable.throwSpeed + Vector3.up * 2f;
            }

            inventory.ServerSpend(slot);
        }

        // --------------------------------------------------------------- echoes

        [Rpc(SendTo.NotOwner)]
        void FireEffectsRpc(int slot, Vector3 origin, Vector3 direction, Vector3 endPoint, bool hit)
        {
            if (inventory.TryGetDefinition(slot, out var definition) &&
                definition.BehaviourAs<WeaponBehaviour>() is { } weapon)
            {
                PlayFireEffects(weapon, origin, direction, false);
            }

            if (hit)
            {
                AudioPool.PlayAt(SoundKind.MeleeHit, endPoint, 0.35f, AudioPool.Vary(), 25f);
            }
        }

        [Rpc(SendTo.Everyone)]
        void MeleeImpactRpc(Vector3 point)
        {
            AudioPool.PlayAt(SoundKind.MeleeHit, point, 0.8f, AudioPool.Vary(), 25f);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(bool weakPoint, bool killed)
        {
            AudioPool.Play2D(weakPoint ? SoundKind.WeakPointMarker : SoundKind.HitMarker, 0.7f);
            HitConfirmed?.Invoke(weakPoint, killed);
        }

        [Rpc(SendTo.Owner)]
        void BoostRpc(float adrenalineSeconds, bool restoreStamina)
        {
            if (movement == null)
            {
                return;
            }

            if (restoreStamina)
            {
                movement.RestoreStamina();
            }

            if (adrenalineSeconds > 0f)
            {
                movement.GrantAdrenaline(adrenalineSeconds);
            }
        }

        void HandleReloadingChanged(bool previous, bool current)
        {
            if (current && !IsOwner)
            {
                AudioPool.PlayAt(SoundKind.Reload, transform.position + Vector3.up * 1.2f, 0.6f, 1f, 18f);
            }
            else if (current)
            {
                AudioPool.Play2D(SoundKind.Reload, 0.7f);
            }
        }
    }
}
