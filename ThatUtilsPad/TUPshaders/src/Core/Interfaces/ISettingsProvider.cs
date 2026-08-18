namespace TUPshaders.Core
{
    /// <summary>
    /// Read/write access to persisted graphics settings.
    /// </summary>
    public interface ISettingsProvider
    {
        T Get<T>(string key, T defaultValue = default!);
        void Set<T>(string key, T value);
        bool Has(string key);
        void Save();
        void Load();
        void ResetAll();
    }
}
