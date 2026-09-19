using System;
using System.Collections.Generic;

namespace MarioKart.AI
{
    // Mirrors schemas/world_recipe.schema.json field-for-field. Deserialized
    // via Newtonsoft.Json (com.unity.nuget.newtonsoft-json) in
    // WorldRecipeClient. Keep this in sync with the JSON Schema by hand.

    [Serializable]
    public class WorldInfo
    {
        public string name;
        public string theme;
        public string terrain;
        public string weather;
        public string time_of_day;
    }

    [Serializable]
    public class TrackInfo
    {
        public float width;
        public float length;
        public float difficulty;
    }

    /// <summary>
    /// Handle to a mesh the backend is generating asynchronously for one
    /// object type. Null when no mesh was requested (mock provider, offline
    /// DefaultWorldRecipe) -- the object then keeps its primitive placeholder.
    /// </summary>
    [Serializable]
    public class ObjectAsset
    {
        public string task_id;
        public string provider;
    }

    [Serializable]
    public class WorldObjectEntry
    {
        public string type;
        public float density;
        public ObjectAsset asset;
    }

    [Serializable]
    public class WorldRecipe
    {
        public int version = 1;
        public int seed;
        public WorldInfo world;
        public TrackInfo track;
        public List<WorldObjectEntry> objects = new List<WorldObjectEntry>();
        public List<string> palette = new List<string>();
    }

    [Serializable]
    public class WorldRecipeResponse
    {
        public WorldRecipe world_recipe;
    }
}
