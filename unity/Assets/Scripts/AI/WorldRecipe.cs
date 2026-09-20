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
        public string sky = "sunny";
        // Picks the soundtrack (Resources/Audio/Music/music_<mood>); see MusicPlayer.
        public string mood = "energetic";
    }

    [Serializable]
    public class TrackInfo
    {
        public float width;
        public float length;
        public float difficulty;
        public string surface = "concrete";
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
        // How the VLM says this object should be used: "landmark",
        // "roadside", "background" or "scattered". Optional in the schema;
        // null/absent (older backends, DefaultWorldRecipe) means "scattered".
        // See EnvironmentGenerator.ParsePlacement.
        public string placement;
        public ObjectAsset asset;
        // Which extraction call produced this object: "photo", "text", or
        // "filler". Debug/logging only -- never used for placement or
        // gameplay decisions. Optional/null for the backend's canned
        // fallback objects and for DefaultWorldRecipe.
        public string source;
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
