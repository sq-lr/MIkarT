using System;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// What EnvironmentGenerator instantiates for one recipe object type.
    /// `prefab` is left null in this bootstrap (no prefabs are hand-authored
    /// yet) -- AssetResolver falls back to `fallbackPrimitive` + `tintColor`.
    /// The primitive is always spawned first; when `meshTaskId` is set,
    /// GeneratedMeshLoader later swaps the generated mesh in over it.
    /// </summary>
    [Serializable]
    public class AssetDefinition
    {
        public string objectType;
        public GameObject prefab;
        public PrimitiveType fallbackPrimitive = PrimitiveType.Cube;
        public Color tintColor = Color.white;
        public Vector3 defaultScale = Vector3.one;

        // GeneratedMeshLoader.PlaceOver normally scales a swapped-in mesh by
        // whichever of height-match or footprint-match is smaller, so a
        // wide-but-short GLB can't spill past the footprint the placeholder
        // reserved (see EnvironmentGenerator.FootprintFor). The background
        // wall deliberately doesn't reserve exclusive footprint at all (it
        // overlaps on purpose), so capping it the same way can needlessly
        // shrink a real mountain mesh below the tall placeholder height it
        // was supposed to match -- set false there to skip the cap.
        public bool capMeshToFootprint = true;

        // Backstop for when capMeshToFootprint is false: the real mesh's
        // dimensions are only known once it's actually downloaded, long
        // after EnvironmentGenerator already committed to a placement
        // distance based on the placeholder's assumed size -- a naturally
        // wide-but-short GLB scaled up to hit a tall minimum height (see
        // EnvironmentGenerator.backgroundMinHeight) can end up far wider
        // than the placeholder ever was. A positive value here caps the
        // mesh's horizontal extent independently of its height (non-uniform
        // scale), so it can never reach back onto the road no matter how
        // wide the real asset naturally is. <= 0 means no such cap.
        public float maxHorizontalExtent = -1f;

        // Backend mesh task for this object type, or null when no generated
        // mesh is coming (mock provider, offline fallback, submit failure).
        public string meshTaskId;

        public bool HasGeneratedMesh => !string.IsNullOrEmpty(meshTaskId);
    }
}
