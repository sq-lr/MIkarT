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

        // Backend mesh task for this object type, or null when no generated
        // mesh is coming (mock provider, offline fallback, submit failure).
        public string meshTaskId;

        public bool HasGeneratedMesh => !string.IsNullOrEmpty(meshTaskId);
    }
}
