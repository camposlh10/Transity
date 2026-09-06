using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Slot = Transity.EditorTools.LocomotionClipSet.Slot;

namespace Transity.EditorTools
{
    /// <summary>
    /// Builds a locomotion Animator Controller for one character.
    ///
    /// The shape is a 1D blend on speed (idle → walk → run → sprint) whose moving stops are
    /// each a 2D directional blend. That split is the trick: the runtime feeds
    /// <c>Speed</c> in metres per second and <c>MoveX/MoveY</c> as a *normalised*
    /// direction, so the 2D trees are only ever sampled out on the rim where the clips sit,
    /// and neither number has to know anything about the other.
    ///
    /// Four cardinal directions rather than eight, because Mixamo has no diagonal
    /// locomotion clips and a controller that changes shape depending on which library
    /// filled it would be worse than one that is merely adequate in both. The diagonals
    /// interpolate from their neighbours, which in first person nobody will ever study.
    ///
    /// Crouching is a separate branch rather than another speed stop: it is a pose the
    /// player chooses, not a speed they happen to be at, and blending it by velocity would
    /// stand them up every time they crouch-walked quickly.
    /// </summary>
    public static class LocomotionControllerBuilder
    {
        const string OutputFolder = "Assets/_Game/Animation";

        // Where each tier sits on the speed blend. Matched to FirstPersonController:
        // crouch 1.8, walk 3.6, sprint 6.4.
        const float WalkSpeed = 1.9f;
        const float RunSpeed = 4.2f;
        const float SprintSpeed = 6.4f;
        const float CrouchWalkSpeed = 1.8f;

        static readonly Vector2[] CardinalPositions =
        {
            new(0f, 1f),    // forward
            new(0f, -1f),   // backward
            new(-1f, 0f),   // left
            new(1f, 0f)     // right
        };

        static readonly Slot[] WalkSlots =
        {
            Slot.WalkForward, Slot.WalkBackward, Slot.WalkLeft, Slot.WalkRight
        };

        static readonly Slot[] RunSlots =
        {
            Slot.RunForward, Slot.RunBackward, Slot.RunLeft, Slot.RunRight
        };

        /// <summary>
        /// Picks the best available library. Mixamo wins once its set is complete, because
        /// that is what the project is moving to; until then the Kevin Iglesias package
        /// keeps the characters animating rather than T-posing.
        /// </summary>
        public static LocomotionClipSet SelectClipSet(string clipSet, bool logChoice)
        {
            var mixamo = new MixamoClipSet();
            if (mixamo.IsComplete)
            {
                return mixamo;
            }

            var fallback = KevinIglesiasClipSet.PackagePresent ? new KevinIglesiasClipSet(clipSet) : null;

            if (logChoice)
            {
                var missing = mixamo.DescribeMissing();
                Debug.Log($"<b>Transity</b>: Mixamo set incomplete, still needed: {missing}. " +
                          (fallback != null
                              ? "Using the Kevin Iglesias package until then."
                              : "No fallback library present; characters will not animate."));
            }

            return fallback;
        }

        public static AnimatorController Build(string characterId, string clipSet)
        {
            var clips = SelectClipSet(clipSet, logChoice: characterId.EndsWith("Girl"));
            if (clips == null)
            {
                return null;
            }

            if (!clips.IsComplete)
            {
                var missing = string.Join(", ", clips.Missing());
                Debug.LogWarning($"{characterId}: {clips.Name} is missing {missing}. Building anyway.");
            }

            GrayboxKit.EnsureFolder(OutputFolder);

            var path = $"{OutputFolder}/{characterId}_Locomotion.controller";

            // Rebuilt from scratch: patching a graph in place is how you end up with
            // orphaned states nobody can see in the window.
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Crouching", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;
            var idle = clips.Resolve(Slot.Idle);

            // ---- standing locomotion ---------------------------------------
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            if (idle != null)
            {
                tree.AddChild(idle, 0f);
            }

            AddTier(tree, clips, idle, WalkSpeed, "Walk", WalkSlots);
            AddTier(tree, clips, idle, RunSpeed, "Run", RunSlots);
            AddSprintTier(tree, clips, idle);

            machine.defaultState = locomotion;

            // ---- crouching -------------------------------------------------
            var crouch = BuildCrouch(controller, clips, idle);

            if (crouch != null)
            {
                var down = locomotion.AddTransition(crouch);
                down.AddCondition(AnimatorConditionMode.If, 0f, "Crouching");
                down.hasExitTime = false;
                down.duration = 0.2f;

                var up = crouch.AddTransition(locomotion);
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouching");
                up.hasExitTime = false;
                up.duration = 0.2f;
            }

            // ---- jump and fall ---------------------------------------------
            var jumpBegin = AddState(machine, "JumpBegin", clips.Resolve(Slot.JumpBegin));
            var airborne = AddState(machine, "Airborne", clips.Resolve(Slot.Airborne));
            var land = AddState(machine, "Land", clips.Resolve(Slot.Land));

            var toJump = machine.AddAnyStateTransition(jumpBegin);
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            toJump.duration = 0.05f;
            toJump.hasExitTime = false;
            // Without this a jump retriggers itself on the frame it starts.
            toJump.canTransitionToSelf = false;

            Link(jumpBegin, airborne, exitTime: 0.75f, duration: 0.1f);

            // Walking off a ledge is the same air state, just without the push-off.
            var fall = locomotion.AddTransition(airborne);
            fall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            fall.hasExitTime = false;
            fall.duration = 0.15f;

            var touchdown = airborne.AddTransition(land);
            touchdown.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            touchdown.hasExitTime = false;
            touchdown.duration = 0.06f;

            Link(land, locomotion, exitTime: 0.6f, duration: 0.12f);

            AddGripLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// One speed tier as a 2D directional blend nested in the speed blend. Slots are
        /// passed explicitly rather than derived by offsetting the enum, so reordering the
        /// enum cannot quietly point a tier at the wrong clips.
        /// </summary>
        static void AddTier(BlendTree parent, LocomotionClipSet clips, AnimationClip centre,
            float threshold, string tierName, Slot[] slots)
        {
            var tier = parent.CreateBlendTreeChild(threshold);
            tier.name = tierName;
            tier.blendType = BlendTreeType.FreeformDirectional2D;
            tier.blendParameter = "MoveX";
            tier.blendParameterY = "MoveY";

            // Freeform Directional needs a motion at the origin to weight against. The
            // runtime keeps the direction on the unit circle so this is never sampled; it
            // is here to define the centre, not to be played.
            if (centre != null)
            {
                tier.AddChild(centre, Vector2.zero);
            }

            var added = 0;
            for (var i = 0; i < slots.Length; i++)
            {
                var clip = clips.Resolve(slots[i]);
                if (clip == null)
                {
                    Debug.LogWarning($"Locomotion: no clip for {slots[i]}.");
                    continue;
                }

                tier.AddChild(clip, CardinalPositions[i]);
                added++;
            }

            if (added == 0)
            {
                Debug.LogError($"Locomotion tier '{tierName}' has no clips; the blend will freeze.");
            }
        }

        /// <summary>
        /// Sprint, which only ever has a forward clip. The sideways and backward stops fall
        /// back to the run set: nobody sprints backwards, and leaving those directions
        /// empty would smear the forward clip across them.
        /// </summary>
        static void AddSprintTier(BlendTree parent, LocomotionClipSet clips, AnimationClip centre)
        {
            var forward = clips.Resolve(Slot.SprintForward) ?? clips.Resolve(Slot.RunForward);
            if (forward == null)
            {
                return;
            }

            var tier = parent.CreateBlendTreeChild(SprintSpeed);
            tier.name = "Sprint";
            tier.blendType = BlendTreeType.FreeformDirectional2D;
            tier.blendParameter = "MoveX";
            tier.blendParameterY = "MoveY";

            if (centre != null)
            {
                tier.AddChild(centre, Vector2.zero);
            }

            tier.AddChild(forward, new Vector2(0f, 1f));

            foreach (var (slot, position) in new[]
                     {
                         (Slot.RunBackward, new Vector2(0f, -1f)),
                         (Slot.RunLeft, new Vector2(-1f, 0f)),
                         (Slot.RunRight, new Vector2(1f, 0f))
                     })
            {
                var clip = clips.Resolve(slot);
                if (clip != null)
                {
                    tier.AddChild(clip, position);
                }
            }
        }

        /// <summary>
        /// The crouch branch: a small speed blend of its own, so crouch-walking still reads
        /// as movement rather than snapping between two poses.
        /// </summary>
        static AnimatorState BuildCrouch(AnimatorController controller, LocomotionClipSet clips,
            AnimationClip standingIdle)
        {
            var crouchIdle = clips.Resolve(Slot.CrouchIdle);
            var crouchWalk = clips.Resolve(Slot.CrouchWalk);

            if (crouchIdle == null && crouchWalk == null)
            {
                return null;
            }

            var state = controller.CreateBlendTreeInController("Crouch", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            // Falling back to the standing idle is visibly wrong, but far less wrong than a
            // frozen T-pose while someone holds crouch.
            tree.AddChild(crouchIdle ?? standingIdle, 0f);

            if (crouchWalk != null)
            {
                tree.AddChild(crouchWalk, CrouchWalkSpeed);
            }

            return state;
        }

        /// <summary>
        /// An upper-body override layer holding the arms in a grip pose.
        ///
        /// Without it the locomotion clips swing both arms freely, so a hunter sprinting
        /// with a rifle waves it around like they are not holding it. Masking to the upper
        /// body lets the legs keep running while the arms are posed, and the runtime fades
        /// the layer in only when something is actually held.
        /// </summary>
        static void AddGripLayer(AnimatorController controller)
        {
            const string maskPath =
                "Assets/Kevin Iglesias/Human Animations/Models/Avatar Masks/Human Body Upper Mask.mask";
            const string posePath =
                "Assets/Kevin Iglesias/Human Animations/Animations/Masked Poses/Human@ObjectGripHands01.fbx";

            var pose = LocomotionClipSet.LoadClipAt(posePath);
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);

            if (pose == null || mask == null)
            {
                Debug.LogWarning("No grip pose or upper-body mask found; arms will swing freely " +
                                 "while carrying a weapon.");
                return;
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Grip",
                defaultWeight = 0f,      // faded in by PlayerAnimator when an item is held
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine
                {
                    name = "Grip",
                    hideFlags = HideFlags.HideInHierarchy
                }
            });

            var layer = controller.layers[^1];

            // The state machine has to live inside the controller asset or it is lost on
            // reload, and AddLayer does not adopt it for us.
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);

            var state = layer.stateMachine.AddState("Hold");
            state.motion = pose;
            state.writeDefaultValues = false;
            layer.stateMachine.defaultState = state;
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string stateName, Motion motion)
        {
            var state = machine.AddState(stateName);
            state.motion = motion;

            if (motion == null)
            {
                Debug.LogWarning($"Locomotion state '{stateName}' has no clip.");
            }

            return state;
        }

        static void Link(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = duration;
        }
    }
}
