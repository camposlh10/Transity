using System.Collections.Generic;
using UnityEngine;

namespace Transity.Train
{
    /// <summary>
    /// The gameplay stations of the depot lobby. Each one is its own prefab, placed on an
    /// anchor rather than welded into the architecture, so art and layout can change
    /// without touching the systems that use them.
    /// </summary>
    public enum StationKind
    {
        Mission,
        Trophy,
        Loadout,
        Wardrobe,
        CommonTable,
        Tavern,
        TrainPlatform
    }

    /// <summary>
    /// Marks where a station lives. Systems that arrive later (contract board in week 10,
    /// shop and loadout in week 11) locate their station through this rather than by
    /// hunting for objects by name.
    /// </summary>
    public sealed class StationAnchor : MonoBehaviour
    {
        [SerializeField] StationKind kind = StationKind.Mission;

        [Tooltip("Footprint in metres, for blockout gizmos and layout checks.")]
        [SerializeField] Vector2 footprint = new(5f, 3f);

        static readonly List<StationAnchor> Registered = new();

        public StationKind Kind => kind;
        public Vector2 Footprint => footprint;

        void OnEnable()
        {
            if (!Registered.Contains(this))
            {
                Registered.Add(this);
            }
        }

        void OnDisable() => Registered.Remove(this);

        public static StationAnchor Find(StationKind kind)
        {
            foreach (var anchor in Registered)
            {
                if (anchor != null && anchor.kind == kind)
                {
                    return anchor;
                }
            }

            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(footprint.x, 2f, footprint.y));
        }
    }
}
