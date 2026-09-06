using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// The animation slots a locomotion controller needs, and where to find them.
    ///
    /// Two libraries can fill these: the Mixamo downloads in Art/Animations/Mixamo, and the
    /// Kevin Iglesias package. They are named nothing like each other, so the controller
    /// builder asks for slots and this decides what a slot means for the library in use.
    ///
    /// Mixamo names drift -- the same motion is "Running Backward" one day and "Run
    /// Backwards" the next, and the library is not consistent about it -- so every slot
    /// accepts a list of aliases and takes the first that is actually on disk.
    /// </summary>
    public abstract class LocomotionClipSet
    {
        public enum Slot
        {
            Idle,
            WalkForward, WalkBackward, WalkLeft, WalkRight,
            RunForward, RunBackward, RunLeft, RunRight,
            SprintForward,
            JumpBegin, Airborne, Land,
            CrouchIdle, CrouchWalk
        }

        /// <summary>Slots without which the controller is not worth building.</summary>
        public static readonly Slot[] Required =
        {
            Slot.Idle,
            Slot.WalkForward, Slot.WalkBackward, Slot.WalkLeft, Slot.WalkRight,
            Slot.RunForward, Slot.RunBackward, Slot.RunLeft, Slot.RunRight,
            Slot.JumpBegin, Slot.Airborne, Slot.Land
        };

        public abstract string Name { get; }

        public abstract AnimationClip Resolve(Slot slot);

        public bool IsComplete => Missing().Count == 0;

        public List<Slot> Missing() => Required.Where(s => Resolve(s) == null).ToList();

        /// <summary>
        /// The clip is a sub-asset of the FBX, and Unity keeps a __preview__ copy beside
        /// the real one that must be skipped.
        /// </summary>
        public static AnimationClip LoadClipAt(string fbxPath) => LoadFrom(fbxPath);

        protected static AnimationClip LoadFrom(string fbxPath)
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
    }

    /// <summary>Clips downloaded from Mixamo, matched by file name.</summary>
    public sealed class MixamoClipSet : LocomotionClipSet
    {
        // First match wins. Mixamo's own naming is the reason these are lists.
        static readonly Dictionary<Slot, string[]> Aliases = new()
        {
            [Slot.Idle] = new[] { "Idle", "Breathing Idle", "Standing Idle" },
            [Slot.WalkForward] = new[] { "Walking", "Walk Forward" },
            [Slot.WalkBackward] = new[] { "Walking Backwards", "Walking Backward", "Walk Backwards" },
            [Slot.WalkLeft] = new[] { "Left Strafe Walking", "Left Strafe Walk", "Walk Strafe Left" },
            [Slot.WalkRight] = new[] { "Right Strafe Walking", "Right Strafe Walk", "Walk Strafe Right" },
            [Slot.RunForward] = new[] { "Running", "Run Forward", "Run" },
            [Slot.RunBackward] = new[] { "Running Backward", "Running Backwards", "Run Backwards" },
            [Slot.RunLeft] = new[] { "Left Strafe", "Running Strafe Left", "Run Strafe Left" },
            [Slot.RunRight] = new[] { "Right Strafe", "Running Strafe Right", "Run Strafe Right" },
            [Slot.SprintForward] = new[] { "Fast Run", "Sprint", "Sprinting" },
            [Slot.JumpBegin] = new[] { "Jumping Up", "Jump", "Jumping" },
            [Slot.Airborne] = new[] { "Falling Idle", "Falling", "Fall" },
            [Slot.Land] = new[] { "Hard Landing", "Falling To Landing", "Landing", "Jump Landing" },
            [Slot.CrouchIdle] = new[] { "Crouch Idle", "Crouching Idle", "Crouched Idle" },
            [Slot.CrouchWalk] = new[] { "Crouched Walking", "Crouch Walk", "Crouched Walk" }
        };

        public override string Name => "Mixamo";

        public override AnimationClip Resolve(Slot slot)
        {
            if (!Aliases.TryGetValue(slot, out var names))
            {
                return null;
            }

            foreach (var candidate in names)
            {
                var clip = LoadFrom($"{MixamoImportSettings.Folder}/{candidate}.fbx");
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>The file names still to be downloaded, for a message worth reading.</summary>
        public string DescribeMissing()
        {
            return string.Join(", ", Missing().Select(s => Aliases[s][0]));
        }
    }

    /// <summary>
    /// The Kevin Iglesias package, which ships a full eight-direction set. Only the four
    /// cardinals are used here so both libraries build the same controller shape.
    /// </summary>
    public sealed class KevinIglesiasClipSet : LocomotionClipSet
    {
        const string Root = "Assets/Kevin Iglesias/Human Animations/Animations";

        readonly string m_Set;
        readonly string m_Prefix;

        public KevinIglesiasClipSet(string clipSet)
        {
            m_Set = clipSet;
            m_Prefix = clipSet == "Female" ? "HumanF" : "HumanM";
        }

        public override string Name => $"Kevin Iglesias ({m_Set})";

        public static bool PackagePresent => AssetDatabase.IsValidFolder(Root);

        public override AnimationClip Resolve(Slot slot)
        {
            var path = slot switch
            {
                Slot.Idle => $"Idles/{m_Prefix}@Idle01",
                Slot.WalkForward => $"Movement/Walk/{m_Prefix}@Walk01_Forward",
                Slot.WalkBackward => $"Movement/Walk/{m_Prefix}@Walk01_Backward",
                Slot.WalkLeft => $"Movement/Walk/{m_Prefix}@Walk01_Left",
                Slot.WalkRight => $"Movement/Walk/{m_Prefix}@Walk01_Right",
                Slot.RunForward => $"Movement/Run/{m_Prefix}@Run01_Forward",
                Slot.RunBackward => $"Movement/Run/{m_Prefix}@Run01_Backward",
                Slot.RunLeft => $"Movement/Run/{m_Prefix}@Run01_Left",
                Slot.RunRight => $"Movement/Run/{m_Prefix}@Run01_Right",
                Slot.SprintForward => $"Movement/Sprint/{m_Prefix}@Sprint01_Forward",
                Slot.JumpBegin => $"Movement/Jump/{m_Prefix}@Jump01 - Begin",
                Slot.Airborne => $"Movement/Jump/{m_Prefix}@Fall01",
                Slot.Land => $"Movement/Jump/{m_Prefix}@Jump01 - Land",

                // The package has no crouch at all; that gap is why the Mixamo crouch
                // clips were worth downloading.
                _ => null
            };

            return path == null ? null : LoadFrom($"{Root}/{m_Set}/{path}.fbx");
        }
    }
}
