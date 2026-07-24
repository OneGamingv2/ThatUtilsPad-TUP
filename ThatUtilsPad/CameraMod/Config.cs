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
        public static string configsFolder = "CameraMod\\Configs";
        public string fileName { get; private set; }
        
        public string filePath => configsFolder + "\\" + fileName;
        
        public T CurrentSettings { get; private set; }
        private FileSystemWatcher _watcher;

        public Config(string name) {
            fileName = name+".json";
            LoadSettings();
            SetupFileWatcher();
        }

        private void LoadSettings() {
            if (!Directory.Exists(configsFolder)) {
                Directory.CreateDirectory(configsFolder);
            }
            
            if (!File.Exists(filePath)) {
                WriteDefaultConfig();
            }
            
            var json = File.ReadAllText(filePath);
            CurrentSettings = JsonConvert.DeserializeObject<T>(json);
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
            var defaultSettings = new ConfigType();

            var json = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
    }
}