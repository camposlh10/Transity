using UnityEngine;

namespace Transity.Inventory
{
    public enum ToggleKind : byte
    {
        Flashlight = 0,
        NightVision = 1,
        UltraViolet = 2,
        Thermal = 3
    }

    /// <summary>
    /// Worn equipment with an on/off state: torches and optics. The Flashlight key toggles
    /// the first of these in the pack without selecting it, so a rifle and a torch can be
    /// carried together. Slot state is battery seconds remaining.
    /// </summary>
    [CreateAssetMenu(menuName = "Transity/Behaviours/Toggle", fileName = "Toggle_New")]
    public sealed class ToggleBehaviour : ItemBehaviour
    {
        public ToggleKind kind = ToggleKind.Flashlight;

        [Tooltip("Seconds of use before it dies. Zero for unlimited.")]
        public float batterySeconds = 600f;

        [Header("Beam (flashlight / UV)")]
        public float beamRange = 22f;
        public float beamAngle = 48f;
        public float intensity = 900f;
        public Color color = new(1f, 0.93f, 0.8f);

        [Header("Detection")]
        [Tooltip("How much easier a creature sees a player using this. 1 = no change.")]
        public float visibilityMultiplier = 1.6f;

        [Header("Optics")]
        [Tooltip("Thermal: metres within which creatures are marked through cover.")]
        public float thermalRange = 45f;
        [Tooltip("NVG: extra ambient brightness while on.")]
        public float nightVisionGain = 2.2f;

        public override ItemUseKind UseKind => ItemUseKind.Toggle;

        public override int InitialState(ItemDefinition definition) => Mathf.RoundToInt(batterySeconds);

        public override string DescribeState(int state)
        {
            if (batterySeconds <= 0f)
            {
                return string.Empty;
            }

            var fraction = Mathf.Clamp01(state / batterySeconds);
            return $"{Mathf.RoundToInt(fraction * 100f)}%";
        }
    }
}
