using System;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// What EnvironmentGenerator instantiates for one recipe object type.
    /// `prefab` is left null in this bootstrap (no prefabs are hand-authored
    /// yet) -- AssetResolver falls back to `fallbackPrimitive` + `tintColor`.
    /// </summary>
    [Serializable]
    public class AssetDefinition
    {
        public string objectType;
        public GameObject prefab;
        public PrimitiveType fallbackPrimitive = PrimitiveType.Cube;
        public Color tintColor = Color.white;
        public Vector3 defaultScale = Vector3.one;
    }
}
