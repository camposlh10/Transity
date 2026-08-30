using Transity.Interaction;
using Transity.Missions;

namespace Transity.Train
{
    /// <summary>
    /// The "we are ready, take us out" control aboard the train. Only meaningful while the
    /// crew is preparing; the server refuses it in every other phase.
    /// </summary>
    public sealed class DepartureLever : NetworkInteractable
    {
        public override bool CanInteract(Interactor interactor) =>
            base.CanInteract(interactor) &&
            MissionDirector.Instance != null &&
            MissionDirector.Instance.Phase == MissionPhase.Preparing;

        public override string GetPrompt(Interactor interactor) => "Depart on expedition";

        public override void OnServerInteract(Interactor interactor)
        {
            MissionDirector.Instance?.BeginExpedition();
        }
    }
}
