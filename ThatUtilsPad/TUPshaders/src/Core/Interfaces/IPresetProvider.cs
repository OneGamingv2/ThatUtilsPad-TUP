using System.Collections.Generic;
using TUPshaders.Configuration;

namespace TUPshaders.Core
{
    /// <summary>
    /// Load, save, import, and export graphics presets.
    /// </summary>
    public interface IPresetProvider
    {
        IReadOnlyList<string> BuiltInPresetNames { get; }
        IReadOnlyList<string> UserPresetNames { get; }

        GraphicsConfig GetBuiltIn(string name);
        bool TryLoadUser(string name, out GraphicsConfig config);
        void SaveUser(string name, GraphicsConfig config);
        void DeleteUser(string name);
        string ExportJson(GraphicsConfig config);
        bool TryImportJson(string json, out GraphicsConfig config);
        void Apply(GraphicsConfig config);
        GraphicsConfig CaptureCurrent();
    }
}
