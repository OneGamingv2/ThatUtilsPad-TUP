using System;
using System.IO;
using System.Text;
using BepInEx;

namespace TUPshaders.Utilities
{
    /// <summary>
    /// Plugin data directories under BepInEx — nested with ThatUtilsPad.
    /// </summary>
    public static class PathUtil
    {
        public static string PluginDir => Path.Combine(Paths.PluginPath, "ThatUtilsPad", "TUPshaders");
        public static string ConfigDir => Path.Combine(Paths.ConfigPath, "ThatUtilsPad", "TUPshaders");
        public static string UserPresetsDir => Path.Combine(ConfigDir, "Presets");
        public static string ShaderDir => Path.Combine(PluginDir, "Shaders");
        public static string BuiltinPresetsDir => Path.Combine(PluginDir, "Presets");
        public static string LutDir => Path.Combine(ConfigDir, "LUTs");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(PluginDir);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(UserPresetsDir);
            Directory.CreateDirectory(ShaderDir);
            Directory.CreateDirectory(LutDir);
            Directory.CreateDirectory(BuiltinPresetsDir);
        }
    }
}
