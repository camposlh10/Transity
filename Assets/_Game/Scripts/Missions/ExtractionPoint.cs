using Transity.Interaction;

namespace Transity.Missions
{
    /// <summary>
    /// The way home. In the finished game this is the train's boarding step; for the slice
    /// it is a marker in the forest that ends the run.
    /// </summary>
    public sealed class ExtractionPoint : NetworkInteractable
    {
        public override bool CanInteract(Interactor interactor) =>
            base.CanInteract(interactor) &&
            MissionDirector.Instance != null &&
            MissionDirector.Instance.Phase == MissionPhase.Expedition;

        public override string GetPrompt(Interactor interactor) => "Board the train";

        public override void OnServerInteract(Interactor interactor)
        {
            MissionDirector.Instance?.Extract(successful: true);
        }
    }
}
