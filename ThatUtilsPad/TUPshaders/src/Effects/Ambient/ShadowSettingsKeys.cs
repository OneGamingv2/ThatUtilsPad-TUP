/// <summary>
/// Shadow enhancement settings are applied through AmbientLightingEffect (shadow boost)
/// and optional SSAO. Dedicated shadow-map replacement is intentionally avoided for VR cost.
/// Category "Shadows" in the GUI maps to these keys for discoverability.
/// </summary>
namespace TUPshaders.Effects.Ambient
{
    public static class ShadowSettingsKeys
    {
        public const string ShadowBoost = "fx.ambient.shadowBoost";
        public const string SsaoEnable = "fx.ssao.enabled";
        public const string SsaoIntensity = "fx.ssao.intensity";
    }
}
