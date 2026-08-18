using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TUPshaders.Configuration;
using TUPshaders.Core;
using TUPshaders.Settings;
using TUPshaders.Utilities;

namespace TUPshaders.Presets
{
    /// <summary>
    /// Built-in cinematic / competitive presets plus user JSON import/export.
    /// </summary>
    public sealed class PresetManager : IPresetProvider
    {
        private readonly SettingsManager _settings;
        private readonly Dictionary<string, GraphicsConfig> _builtIn;
        private readonly List<string> _userNames = new();

        public IReadOnlyList<string> BuiltInPresetNames { get; }
        public IReadOnlyList<string> UserPresetNames => _userNames;

        public PresetManager(SettingsManager settings)
        {
            _settings = settings;
            PathUtil.EnsureDirectories();
            _builtIn = BuiltinPresets.CreateAll();
            // Always refresh plugin Presets\*.json from the ship set (keeps GT install current).
            SyncBuiltinPresetsToDisk();
            BuiltInPresetNames = new List<string>(_builtIn.Keys);
            RefreshUserList();
        }

        /// <summary>
        /// Keep BepInEx/plugins/ThatUtilsPad/TUPshaders/Presets in sync with the ship set.
        /// </summary>
        private void SyncBuiltinPresetsToDisk()
        {
            try
            {
                Directory.CreateDirectory(PathUtil.BuiltinPresetsDir);
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _builtIn)
                {
                    var copy = kv.Value.Clone();
                    copy.Name = kv.Key;
                    copy.Version = PluginInfo.Version;
                    string path = Path.Combine(PathUtil.BuiltinPresetsDir, Sanitize(kv.Key) + ".json");
                    File.WriteAllText(path, SimpleJson.SerializeConfig(copy), Encoding.UTF8);
                    keep.Add(Sanitize(kv.Key) + ".json");
                }

                // Drop old Neon / Ultra / etc. so they can't reappear as loose JSON.
                foreach (string file in Directory.GetFiles(PathUtil.BuiltinPresetsDir, "*.json"))
                {
                    string name = Path.GetFileName(file);
                    if (!keep.Contains(name))
                        File.Delete(file);
                }

                Plugin.Log.LogInfo($"Synced {_builtIn.Count} builtin presets → {PathUtil.BuiltinPresetsDir}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Builtin preset sync: {ex.Message}");
            }
        }

        public void ApplyStartupPreset()
        {
            var name = _settings.Get("preset.startup", "Realistic");
            if (_builtIn.TryGetValue(name, out var cfg))
                Apply(cfg.Clone());
            else if (TryLoadUser(name, out var user))
                Apply(user);
        }

        public GraphicsConfig GetBuiltIn(string name) =>
            _builtIn.TryGetValue(name, out var c) ? c.Clone() : BuiltinPresets.Realistic();

        public bool TryLoadUser(string name, out GraphicsConfig config)
        {
            config = null!;
            try
            {
                var path = Path.Combine(PathUtil.UserPresetsDir, Sanitize(name) + ".json");
                if (!File.Exists(path)) return false;
                return SimpleJson.TryDeserializeConfig(File.ReadAllText(path), out config!) && config != null;
            }
            catch
            {
                return false;
            }
        }

        public void SaveUser(string name, GraphicsConfig config)
        {
            PathUtil.EnsureDirectories();
            config.Name = name;
            var path = Path.Combine(PathUtil.UserPresetsDir, Sanitize(name) + ".json");
            File.WriteAllText(path, SimpleJson.SerializeConfig(config), Encoding.UTF8);
            RefreshUserList();
        }

        public void DeleteUser(string name)
        {
            var path = Path.Combine(PathUtil.UserPresetsDir, Sanitize(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
            RefreshUserList();
        }

        public string ExportJson(GraphicsConfig config) => SimpleJson.SerializeConfig(config);

        public bool TryImportJson(string json, out GraphicsConfig config) =>
            SimpleJson.TryDeserializeConfig(json, out config!);

        public void Apply(GraphicsConfig config)
        {
            if (config == null)
                return;

            _settings.ApplyPresetConfig(config);
            _settings.Save();
            Plugin.Log.LogInfo($"Applied preset: {config.Name}");
        }

        public void CycleNext()
        {
            var names = BuiltInPresetNames;
            if (names == null || names.Count == 0)
                return;

            string current = _settings.Get("preset.startup", "Realistic");
            int idx = 0;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            string next = names[(idx + 1) % names.Count];
            if (_builtIn.TryGetValue(next, out var cfg))
            {
                Apply(cfg.Clone());
            }
        }

        public GraphicsConfig CaptureCurrent() => _settings.ToConfig("Captured");

        private void RefreshUserList()
        {
            _userNames.Clear();
            PathUtil.EnsureDirectories();
            foreach (var file in Directory.GetFiles(PathUtil.UserPresetsDir, "*.json"))
                _userNames.Add(Path.GetFileNameWithoutExtension(file));
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
