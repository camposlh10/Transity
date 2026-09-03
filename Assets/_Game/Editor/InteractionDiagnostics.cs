using System.Linq;
using Transity.Interaction;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Transity.EditorTools
{
    /// <summary>
    /// Opens the depot and reports, for every interactable, whether a player standing in
    /// front of it would actually be able to aim at it: collider present, correct layer,
    /// and a real SphereCast using the same mask the Interactor uses.
    ///
    /// Faster than reasoning about it, and it catches the cases that look right in the
    /// inspector but fail in the physics query.
    /// </summary>
    public static class InteractionDiagnostics
    {
        const int InteractableLayer = 6;
        const float MaxRange = 3.2f;
        const float SphereRadius = 0.12f;

        [MenuItem("Tools/Transity/Diagnose Interaction", priority = 43)]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene("Assets/_Game/Scenes/TrainHub.unity", OpenSceneMode.Single);

            var interactables = Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<IInteractable>()
                .ToList();

            Debug.Log($"=== {interactables.Count} interactable(s) in the depot ===");

            foreach (var interactable in interactables)
            {
                var behaviour = (MonoBehaviour)interactable;
                var go = behaviour.gameObject;

                var colliders = go.GetComponentsInChildren<Collider>(true);
                var networkObject = go.GetComponentInParent<NetworkObject>();

                var layers = colliders.Length == 0
                    ? "none"
                    : string.Join(",", colliders.Select(c => c.gameObject.layer).Distinct());

                var onRightLayer = colliders.Any(c => c.gameObject.layer == InteractableLayer);

                // Stand in front of the object and aim at it, exactly as the player would.
                var target = colliders.Length > 0 ? colliders[0].bounds.center : go.transform.position;
                var forward = go.transform.forward;
                var eye = target - forward * 1.8f;
                eye.y = 1.65f;

                var direction = (target - eye).normalized;
                var hitSomething = Physics.SphereCast(eye, SphereRadius, direction, out var hit,
                    MaxRange, 1 << InteractableLayer, QueryTriggerInteraction.Collide);

                var resolved = hitSomething
                    ? hit.collider.GetComponentInParent<IInteractable>()
                    : null;

                var verdict = !onRightLayer ? "WRONG LAYER"
                    : colliders.Length == 0 ? "NO COLLIDER"
                    : networkObject == null ? "NO NETWORKOBJECT"
                    : resolved == null ? "CAST MISSES"
                    : ReferenceEquals(resolved, interactable) ? "ok"
                    : "HITS SOMETHING ELSE";

                Debug.Log($"[{verdict}] {go.name} ({behaviour.GetType().Name}) " +
                          $"colliders={colliders.Length} layers={layers} " +
                          $"netobj={(networkObject != null ? networkObject.name : "none")} " +
                          $"range={interactable.InteractionRange}");
            }
        }
    }
}
