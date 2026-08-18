using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace CameraMod {
    [Serializable]
    public class ConfigType {
        public string activateBind = "RP";
    }

    public class Configs {
        public static Config<ConfigType> controls = new Config<ConfigType>("Controls");
    }
    public class Config<T> {
        public event Action<T> Changed;
        public static string configsFolder
        {
            get
            {
                try
                {
                    string root = Application.dataPath;
                    if (!string.IsNullOrEmpty(root))
                    {
                        string parent = Directory.GetParent(root)?.FullName;
                        if (!string.IsNullOrEmpty(parent))
                            return Path.Combine(parent, "BepInEx", "config", "ThatUtilsPad", "CameraMod");
                    }
                }
                catch
                {
                }

                return Path.Combine(Environment.CurrentDirectory, "BepInEx", "config", "ThatUtilsPad", "CameraMod");
            }
        }

        public string fileName { get; private set; }
        public string filePath => Path.Combine(configsFolder, fileName);
        public T CurrentSettings { get; private set; }
        private FileSystemWatcher _watcher;

        public Config(string name) {
            fileName = name + ".json";
            try
            {
                LoadSettings();
                SetupFileWatcher();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP Camera] Config load failed: " + ex.Message);
                CurrentSettings = Activator.CreateInstance<T>();
            }
        }

        private void LoadSettings() {
            if (!Directory.Exists(configsFolder))
                Directory.CreateDirectory(configsFolder);

            if (!File.Exists(filePath))
                WriteDefaultConfig();

            string json = File.ReadAllText(filePath);
            CurrentSettings = JsonConvert.DeserializeObject<T>(json) ?? Activator.CreateInstance<T>();
        }
        
        private void SetupFileWatcher()
        {
            _watcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(filePath),
                Filter = Path.GetFileName(filePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };

            _watcher.Changed += OnConfigFileChanged;

            _watcher.EnableRaisingEvents = true;
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Configuration file changed ({e.ChangeType}). Reloading settings...");
            LoadSettings();
            Changed?.Invoke(CurrentSettings);
        }
        
        private void WriteDefaultConfig() {
            T defaultSettings = Activator.CreateInstance<T>();
            string json = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void Save(T settings) {
            if (settings == null)
                settings = Activator.CreateInstance<T>();
            CurrentSettings = settings;
            try
            {
                if (!Directory.Exists(configsFolder))
                    Directory.CreateDirectory(configsFolder);
                string json = JsonConvert.SerializeObject(CurrentSettings, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP Camera] Config save failed: " + ex.Message);
            }

            Changed?.Invoke(CurrentSettings);
        }
    }
}