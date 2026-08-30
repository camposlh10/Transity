using Unity.Netcode;
using UnityEngine;

namespace Transity.Interaction
{
    /// <summary>
    /// Base for interactables that own replicated state. Subclasses override
    /// <see cref="OnServerInteract"/> and can assume it only ever runs on the host.
    /// </summary>
    public abstract class NetworkInteractable : NetworkBehaviour, IInteractable
    {
        [Header("Interaction")]
        [SerializeField] string prompt = "Use";
        [SerializeField] float interactionRange = 2.5f;
        [SerializeField] bool interactable = true;

        public Transform Transform => transform;
        public float InteractionRange => interactionRange;

        protected string Prompt
        {
            get => prompt;
            set => prompt = value;
        }

        public bool Interactable
        {
            get => interactable;
            protected set => interactable = value;
        }

        public virtual bool CanInteract(Interactor interactor) => interactable;

        public virtual string GetPrompt(Interactor interactor) => prompt;

        public abstract void OnServerInteract(Interactor interactor);
    }
}
