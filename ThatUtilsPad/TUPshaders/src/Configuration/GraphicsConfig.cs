using System;
using System.Collections.Generic;

namespace TUPshaders.Configuration
{
    /// <summary>
    /// Serializable snapshot of all graphics settings for presets / import-export.
    /// </summary>
    [Serializable]
    public sealed class GraphicsConfig
    {
        public string Name = "Custom";
        public string Version = PluginInfo.Version;
        public Dictionary<string, bool> Bools = new();
        public Dictionary<string, float> Floats = new();
        public Dictionary<string, int> Ints = new();
        public Dictionary<string, string> Strings = new();
        public Dictionary<string, float[]> Colors = new();

        public void SetBool(string key, bool v) => Bools[key] = v;
        public void SetFloat(string key, float v) => Floats[key] = v;
        public void SetInt(string key, int v) => Ints[key] = v;
        public void SetString(string key, string v) => Strings[key] = v;
        public void SetColor(string key, float r, float g, float b, float a = 1f)
            => Colors[key] = new[] { r, g, b, a };

        public bool GetBool(string key, bool def = false) => Bools.TryGetValue(key, out var v) ? v : def;
        public float GetFloat(string key, float def = 0f) => Floats.TryGetValue(key, out var v) ? v : def;
        public int GetInt(string key, int def = 0) => Ints.TryGetValue(key, out var v) ? v : def;
        public string GetString(string key, string def = "") => Strings.TryGetValue(key, out var v) ? v : def;

        public bool TryGetColor(string key, out float r, out float g, out float b, out float a)
        {
            if (Colors.TryGetValue(key, out var c) && c != null && c.Length >= 3)
            {
                r = c[0]; g = c[1]; b = c[2]; a = c.Length > 3 ? c[3] : 1f;
                return true;
            }
            r = g = b = 0f; a = 1f;
            return false;
        }

        public GraphicsConfig Clone()
        {
            return new GraphicsConfig
            {
                Name = Name,
                Version = Version,
                Bools = new Dictionary<string, bool>(Bools),
                Floats = new Dictionary<string, float>(Floats),
                Ints = new Dictionary<string, int>(Ints),
                Strings = new Dictionary<string, string>(Strings),
                Colors = new Dictionary<string, float[]>(Colors)
            };
        }
    }
}
