using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Builds a locomotion Animator Controller from the Kevin Iglesias Human Basic Motions
    /// clips, one per playable character.
    ///
    /// The shape is a 1D blend on speed (idle → walk → run → sprint) whose three moving
    /// stops are each a 2D directional blend over eight compass directions. That split is
    /// the whole trick: the runtime feeds <c>Speed</c> as metres per second and
    /// <c>MoveX/MoveY</c> as a *normalised* direction, so the 2D trees are only ever
    /// sampled out on the rim where the clips actually sit, and neither number has to know
    /// anything about the other.
    ///
    /// The in-place clips are used, never the [RM] root-motion ones: movement is driven by
    /// the CharacterController, and root motion would fight it.
    /// </summary>
    public static class LocomotionControllerBuilder
    {
        const string PackageRoot = "Assets/Kevin Iglesias/Human Animations/Animations";
        const string OutputFolder = "Assets/_Game/Animation";

        // Where each speed tier sits on the blend, in metres per second. Matched to
        // FirstPersonController: crouch 1.8, walk 3.6, sprint 6.4. The clips are authored
        // at roughly these speeds, so feet stay near the ground rather than skating.
        const float WalkSpeed = 1.9f;
        const float RunSpeed = 4.2f;
        const float SprintSpeed = 6.4f;

        /// <summary>The eight compass directions, as the clip suffix and its blend position.</summary>
        static readonly (string Suffix, Vector2 Position)[] Directions =
        {
            ("Forward", new Vector2(0f, 1f)),
            ("ForwardLeft", new Vector2(-0.7071f, 0.7071f)),
            ("ForwardRight", new Vector2(0.7071f, 0.7071f)),
            ("Left", new Vector2(-1f, 0f)),
            ("Right", new Vector2(1f, 0f)),
            ("Backward", new Vector2(0f, -1f)),
            ("BackwardLeft", new Vector2(-0.7071f, -0.7071f)),
            ("BackwardRight", new Vector2(0.7071f, -0.7071f))
        };

        public static bool PackagePresent => AssetDatabase.IsValidFolder(PackageRoot);

        /// <summary>
        /// Builds a controller for one character. <paramref name="clipSet"/> is "Male" or
        /// "Female" -- both retarget onto any Humanoid rig, so this only picks the style of
        /// the motion, never whether it fits.
        /// </summary>
        public static AnimatorController Build(string characterId, string clipSet)
        {
            if (!PackagePresent)
            {
                Debug.LogWarning($"Human Basic Motions not found at {PackageRoot}; " +
                                 "characters will keep their single-idle controllers.");
                return null;
            }

            GrayboxKit.EnsureFolder(OutputFolder);

            var prefix = clipSet == "Female" ? "HumanF" : "HumanM";
            var path = $"{OutputFolder}/{characterId}_Locomotion.controller";

            // Rebuilt from scratch each run: patching an existing graph in place is how you
            // end up with orphaned states nobody can see in the window.
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            var machine = controller.layers[0].stateMachine;

            // ---- locomotion ------------------------------------------------
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            var idle = LoadClip($"{PackageRoot}/{clipSet}/Idles/{prefix}@Idle01.fbx");
            if (idle != null)
            {
                tree.AddChild(idle, 0f);
            }

            AddDirectionalTier(tree, clipSet, prefix, "Walk", "Walk01", WalkSpeed, idle);
            AddDirectionalTier(tree, clipSet, prefix, "Run", "Run01", RunSpeed, idle);
            AddDirectionalTier(tree, clipSet, prefix, "Sprint", "Sprint01", SprintSpeed, idle);

            machine.defaultState = locomotion;

            // ---- jump and fall ---------------------------------------------
            var jumpBegin = AddState(machine, "JumpBegin",
                LoadClip($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Jump01 - Begin.fbx"));
            var airborne = AddState(machine, "Airborne",
                LoadClip($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Fall01.fbx"));
            var land = AddState(machine, "Land",
                LoadClip($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Jump01 - Land.fbx"));

            // A jump can start from anything, including mid-land.
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
        /// An upper-body override layer holding the arms in a grip pose.
        ///
        /// Without it the locomotion clips swing both arms freely, so a hunter sprinting
        /// with a rifle waves it around like they are not holding it. Masking to the upper
        /// body lets the legs keep running while the arms are posed, and the runtime fades
        /// the layer in only when something is actually held.
        /// </summary>
        static void AddGripLayer(AnimatorController controller)
        {
            var pose = LoadClip($"{PackageRoot}/Masked Poses/Human@ObjectGripHands01.fbx");
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
                "Assets/Kevin Iglesias/Human Animations/Models/Avatar Masks/Human Body Upper Mask.mask");

            if (pose == null || mask == null)
            {
                Debug.LogWarning("No grip pose or upper-body mask found; arms will swing freely " +
                                 "while carrying a weapon.");
                return;
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Grip",
                defaultWeight = 0f,      // faded in by PlayerEquipment when an item is held
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

        /// <summary>
        /// Adds one speed tier as a 2D directional blend nested in the speed blend.
        /// </summary>
        static void AddDirectionalTier(BlendTree parent, string clipSet, string prefix,
            string folder, string clipStem, float threshold, AnimationClip centre)
        {
            var tier = parent.CreateBlendTreeChild(threshold);
            tier.name = folder;
            tier.blendType = BlendTreeType.FreeformDirectional2D;
            tier.blendParameter = "MoveX";
            tier.blendParameterY = "MoveY";

            // Freeform Directional needs a motion at the origin to compute its weights
            // against. The runtime keeps the direction on the unit circle so this is never
            // actually sampled -- it is here to define the centre, not to be played.
            if (centre != null)
            {
                tier.AddChild(centre, Vector2.zero);
            }

            var added = 0;

            foreach (var (suffix, position) in Directions)
            {
                var clip = LoadClip($"{PackageRoot}/{clipSet}/Movement/{folder}/{prefix}@{clipStem}_{suffix}.fbx");

                // The free sprint set ships forwards and sideways only. Backing away at a
                // sprint is not a thing the pack covers, so those directions borrow the run
                // clips rather than leaving a hole the blend would smear across.
                if (clip == null && folder == "Sprint")
                {
                    clip = LoadClip($"{PackageRoot}/{clipSet}/Movement/Run/{prefix}@Run01_{suffix}.fbx");
                }

                if (clip == null)
                {
                    Debug.LogWarning($"Locomotion: no clip for {folder} {suffix} ({prefix}).");
                    continue;
                }

                tier.AddChild(clip, position);
                added++;
            }

            if (added == 0)
            {
                Debug.LogError($"Locomotion tier '{folder}' has no clips; the blend will freeze there.");
            }
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

        /// <summary>
        /// The clip is a sub-asset of the FBX, so it has to be dug out. Unity also creates
        /// a __preview__ clip alongside the real one, which must be skipped.
        /// </summary>
        static AnimationClip LoadClip(string fbxPath)
        {
            if (!File.Exists(fbxPath))
            {
                return null;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>Reports which of the clips the controller wants are missing.</summary>
        public static List<string> MissingClips(string clipSet)
        {
            var prefix = clipSet == "Female" ? "HumanF" : "HumanM";
            var missing = new List<string>();

            void Check(string path)
            {
                if (LoadClip(path) == null)
                {
                    missing.Add(Path.GetFileNameWithoutExtension(path));
                }
            }

            Check($"{PackageRoot}/{clipSet}/Idles/{prefix}@Idle01.fbx");
            Check($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Jump01 - Begin.fbx");
            Check($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Fall01.fbx");
            Check($"{PackageRoot}/{clipSet}/Movement/Jump/{prefix}@Jump01 - Land.fbx");

            foreach (var (suffix, _) in Directions)
            {
                Check($"{PackageRoot}/{clipSet}/Movement/Walk/{prefix}@Walk01_{suffix}.fbx");
                Check($"{PackageRoot}/{clipSet}/Movement/Run/{prefix}@Run01_{suffix}.fbx");
            }

            return missing;
        }
    }
}
