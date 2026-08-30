using Unity.Netcode;
using UnityEngine;

namespace Transity.Interaction
{
    /// <summary>
    /// Graybox door. Exists mainly to prove the interaction path end to end: a client
    /// asks, the server flips the state, every client animates from the replicated value.
    /// </summary>
    public sealed class SimpleDoor : NetworkInteractable
    {
        [Header("Motion")]
        [SerializeField] Transform hinge;
        [SerializeField] float openAngle = 95f;
        [SerializeField] float openSpeed = 220f;

        readonly NetworkVariable<bool> m_IsOpen = new();

        float m_ClosedYaw;

        public bool IsOpen => m_IsOpen.Value;

        void Awake()
        {
            if (hinge == null)
            {
                hinge = transform;
            }

            m_ClosedYaw = hinge.localEulerAngles.y;
        }

        public override string GetPrompt(Interactor interactor) => m_IsOpen.Value ? "Close" : "Open";

        public override void OnServerInteract(Interactor interactor)
        {
            m_IsOpen.Value = !m_IsOpen.Value;
        }

        void Update()
        {
            var targetYaw = m_ClosedYaw + (m_IsOpen.Value ? openAngle : 0f);
            var current = hinge.localEulerAngles;
            current.y = Mathf.MoveTowardsAngle(current.y, targetYaw, openSpeed * Time.deltaTime);
            hinge.localEulerAngles = current;
        }
    }
}
