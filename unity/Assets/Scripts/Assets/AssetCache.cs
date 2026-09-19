using System.Collections.Generic;
using UnityEngine;

namespace MarioKart.AssetsSystem
{
    /// <summary>
    /// Simple in-memory caches for the assets system:
    /// - AssetDefinition per object type, so AssetResolver doesn't rebuild one
    ///   every time the same type is resolved.
    /// - Imported GLB template per mesh task, so GeneratedMeshLoader imports
    ///   each generated mesh once no matter how many placeholders use it.
    /// </summary>
    public class AssetCache
    {
        private readonly Dictionary<string, AssetDefinition> definitions = new Dictionary<string, AssetDefinition>();
        private readonly Dictionary<string, GameObject> meshTemplates = new Dictionary<string, GameObject>();

        public bool TryGet(string objectType, out AssetDefinition definition) => definitions.TryGetValue(objectType, out definition);

        public void Store(string objectType, AssetDefinition definition) => definitions[objectType] = definition;

        public bool TryGetMeshTemplate(string taskId, out GameObject template) => meshTemplates.TryGetValue(taskId, out template);

        public void StoreMeshTemplate(string taskId, GameObject template) => meshTemplates[taskId] = template;

        public void ClearMeshTemplates() => meshTemplates.Clear();
    }
}
