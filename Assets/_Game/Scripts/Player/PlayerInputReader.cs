using Transity.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Transity.Player
{
    /// <summary>
    /// Wraps the shared InputActionAsset so gameplay code never touches action lookups
    /// by string. Only enabled on the locally owned player.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;
        [SerializeField] string actionMapName = "Player";

        InputActionMap m_Map;
        InputAction m_Move;
        InputAction m_Look;
        InputAction m_Jump;
        InputAction m_Sprint;
        InputAction m_Crouch;
        InputAction m_Interact;
        InputAction m_Attack;
        InputAction m_Next;
        InputAction m_Previous;

        public Vector2 Move => m_Move?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Look => m_Look?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool SprintHeld => m_Sprint?.IsPressed() ?? false;
        public bool CrouchHeld => m_Crouch?.IsPressed() ?? false;
        public bool JumpPressed => m_Jump?.WasPressedThisFrame() ?? false;
        public bool InteractPressed => m_Interact?.WasPressedThisFrame() ?? false;
        public bool InteractReleased => m_Interact?.WasReleasedThisFrame() ?? false;
        public bool InteractHeld => m_Interact?.IsPressed() ?? false;
        public bool AttackPressed => m_Attack?.WasPressedThisFrame() ?? false;
        public bool NextSlotPressed => m_Next?.WasPressedThisFrame() ?? false;
        public bool PreviousSlotPressed => m_Previous?.WasPressedThisFrame() ?? false;

        /// <summary>Suppresses gameplay input while a menu or the results screen is open.</summary>
        public bool Suppressed { get; private set; }

        void Awake()
        {
            if (actions == null)
            {
                GameLog.Error($"{nameof(PlayerInputReader)} on '{name}' has no InputActionAsset assigned.");
                enabled = false;
                return;
            }

            m_Map = actions.FindActionMap(actionMapName, throwIfNotFound: false);
            if (m_Map == null)
            {
                GameLog.Error($"Action map '{actionMapName}' not found in '{actions.name}'.");
                enabled = false;
                return;
            }

            m_Move = m_Map.FindAction("Move");
            m_Look = m_Map.FindAction("Look");
            m_Jump = m_Map.FindAction("Jump");
            m_Sprint = m_Map.FindAction("Sprint");
            m_Crouch = m_Map.FindAction("Crouch");
            m_Interact = m_Map.FindAction("Interact");
            m_Attack = m_Map.FindAction("Attack");
            m_Next = m_Map.FindAction("Next");
            m_Previous = m_Map.FindAction("Previous");
        }

        void OnEnable() => m_Map?.Enable();

        void OnDisable() => m_Map?.Disable();

        public void SetSuppressed(bool suppressed)
        {
            Suppressed = suppressed;
            if (suppressed)
            {
                m_Map?.Disable();
            }
            else
            {
                m_Map?.Enable();
            }
        }
    }
}
