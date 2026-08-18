using TUPshaders.Configuration;

namespace TUPshaders.Presets
{
    /// <summary>
    /// Only three shipped looks: Realistic, Competitive, Disabled.
    /// </summary>
    public static class BuiltinPresets
    {
        public static System.Collections.Generic.Dictionary<string, GraphicsConfig> CreateAll()
        {
            return new System.Collections.Generic.Dictionary<string, GraphicsConfig>
            {
                ["Realistic"] = Realistic(),
                ["Competitive"] = Competitive(),
                ["Disabled"] = Disabled(),
            };
        }

        public static GraphicsConfig Realistic()
        {
            // Golden-hour forest: soft warm shafts, haze, bloom — not crushed indoors.
            var c = FullBase("Realistic");
            c.SetBool("graphics.master", true);
            Grade(c, on: true, intensity: 1f, vibrance: 0.08f, sat: 1.02f, contrast: 1.12f, temp: 0.14f, gain: 1.06f, tonemap: 1, q: 3);
            c.SetFloat("fx.colorgrading.tint", 0.04f);
            c.SetFloat("fx.colorgrading.gamma", 1.02f);
            Bloom(c, on: true, intensity: 0.34f, threshold: 0.78f, knee: 0.55f, q: 3);
            Cas(c, on: true, intensity: 0.3f, q: 2);
            Ambient(c, on: true, intensity: 0.48f, shadowBoost: 0.42f, q: 3);
            Sky(c, on: true, intensity: 0.55f, q: 2);
            Lens(c, on: true, intensity: 0.5f, vignette: 0.16f, grain: 0f, dirt: 0.04f, flare: 0.08f, ca: 0.012f, q: 2);
            Fog(c, on: true, intensity: 0.34f, density: 0.0075f, q: 2);
            Ssao(c, on: true, intensity: 0.42f, q: 2);
            Ssr(c, on: true, intensity: 0.28f, q: 2);
            Godrays(c, on: true, intensity: 0.58f, q: 2);
            Neon(c, on: false, intensity: 0f);
            Bridge(c, boost: 1.08f, quality: 2, irl: true, grass: 0.55f, bump: 0.4f, detail: 0.3f, denoise: 0.45f);
            // Warm golden haze (style 2) — matches sunset shafts through trees
            c.SetInt("fx.fog.style", 2);
            c.SetFloat("fx.fog.heightDensity", 0.028f);
            c.SetFloat("fx.fog.color.r", 0.95f);
            c.SetFloat("fx.fog.color.g", 0.74f);
            c.SetFloat("fx.fog.color.b", 0.48f);
            Perf(c, adaptive: true, maxGpuMs: 3.2f, targetFps: 90);
            return c;
        }

        public static GraphicsConfig Competitive()
        {
            var c = FullBase("Competitive");
            c.SetBool("graphics.master", true);
            Grade(c, on: true, intensity: 0.8f, vibrance: 0.02f, sat: 0.98f, contrast: 1.18f, temp: 0f, gain: 1.02f, tonemap: 2, q: 1);
            Bloom(c, on: false, intensity: 0f, threshold: 1.2f, knee: 0.2f, q: 0);
            Cas(c, on: true, intensity: 0.7f, q: 2);
            Ambient(c, on: false, intensity: 0.1f, shadowBoost: 0f, q: 0);
            Sky(c, on: false, intensity: 0.1f, q: 0);
            Lens(c, on: false, intensity: 0f, vignette: 0f, grain: 0f, dirt: 0f, flare: 0f, ca: 0f, q: 0);
            Fog(c, on: false, intensity: 0f, density: 0f, q: 0);
            Ssao(c, on: false, intensity: 0f, q: 0);
            Ssr(c, on: false, intensity: 0f, q: 0);
            Godrays(c, on: false, intensity: 0f, q: 0);
            Neon(c, on: false, intensity: 0f);
            Bridge(c, boost: 0.95f, quality: 0, irl: false, grass: 0f, bump: 0f, detail: 0f, denoise: 0.25f);
            c.SetBool("fx.grass.enabled", false);
            c.SetBool("fx.bump.enabled", false);
            c.SetBool("graphics.nativeBridge", false);
            Perf(c, adaptive: true, maxGpuMs: 1.4f, targetFps: 120);
            return c;
        }

        /// <summary>Master Enable off — all TUPshaders / Present post stopped.</summary>
        public static GraphicsConfig Disabled()
        {
            var c = FullBase("Disabled");
            c.SetBool("graphics.master", false);
            c.SetBool("graphics.nativeBridge", false);
            Grade(c, on: false, intensity: 0f, vibrance: 0f, sat: 1f, contrast: 1f, temp: 0f, gain: 1f, tonemap: 0, q: 0);
            Bloom(c, on: false, intensity: 0f, threshold: 1f, knee: 0f, q: 0);
            Cas(c, on: false, intensity: 0f, q: 0);
            Ambient(c, on: false, intensity: 0f, shadowBoost: 0f, q: 0);
            Sky(c, on: false, intensity: 0f, q: 0);
            Lens(c, on: false, intensity: 0f, vignette: 0f, grain: 0f, dirt: 0f, flare: 0f, ca: 0f, q: 0);
            Fog(c, on: false, intensity: 0f, density: 0f, q: 0);
            Ssao(c, on: false, intensity: 0f, q: 0);
            Ssr(c, on: false, intensity: 0f, q: 0);
            Godrays(c, on: false, intensity: 0f, q: 0);
            Neon(c, on: false, intensity: 0f);
            Bridge(c, boost: 1f, quality: 0, irl: false, grass: 0f, bump: 0f, detail: 0f, denoise: 0f);
            c.SetBool("fx.grass.enabled", false);
            c.SetBool("fx.bump.enabled", false);
            c.SetBool("graphics.nativeBridge", false);
            c.SetBool("graphics.master", false);
            Perf(c, adaptive: false, maxGpuMs: 1f, targetFps: 90);
            return c;
        }

        private static GraphicsConfig FullBase(string name)
        {
            var c = new GraphicsConfig { Name = name };
            c.SetBool("graphics.master", true);
            c.SetBool("graphics.nativeBridge", true);
            c.SetBool("performance.autoDisable", true);
            c.SetBool("fx.look.irl", false);
            return c;
        }

        private static void Bridge(GraphicsConfig c, float boost, int quality, bool irl,
            float grass, float bump, float detail, float denoise)
        {
            c.SetBool("graphics.nativeBridge", true);
            c.SetFloat("fx.bridge.boost", boost);
            c.SetInt("fx.bridge.quality", quality);
            c.SetBool("fx.look.irl", irl);
            c.SetBool("fx.grass.enabled", grass > 0.01f);
            c.SetFloat("fx.grass.intensity", grass);
            c.SetBool("fx.bump.enabled", bump > 0.01f);
            c.SetFloat("fx.bump.intensity", bump);
            c.SetFloat("fx.detail.intensity", detail);
            c.SetFloat("fx.denoise.intensity", denoise);
        }

        private static void Grade(GraphicsConfig c, bool on, float intensity, float vibrance, float sat,
            float contrast, float temp, float gain, int tonemap, int q)
        {
            c.SetBool("fx.colorgrading.enabled", on);
            c.SetFloat("fx.colorgrading.intensity", intensity);
            c.SetInt("fx.colorgrading.quality", q);
            c.SetFloat("fx.colorgrading.vibrance", vibrance);
            c.SetFloat("fx.colorgrading.saturation", sat);
            c.SetFloat("fx.colorgrading.contrast", contrast);
            c.SetFloat("fx.colorgrading.gamma", 1f);
            c.SetFloat("fx.colorgrading.temperature", temp);
            c.SetFloat("fx.colorgrading.tint", 0f);
            c.SetFloat("fx.colorgrading.lift", 0f);
            c.SetFloat("fx.colorgrading.gain", gain);
            c.SetInt("fx.colorgrading.tonemap", tonemap);
            c.SetFloat("fx.colorgrading.lutStrength", 0f);
        }

        private static void Bloom(GraphicsConfig c, bool on, float intensity, float threshold, float knee, int q)
        {
            c.SetBool("fx.bloom.enabled", on);
            c.SetFloat("fx.bloom.intensity", intensity);
            c.SetInt("fx.bloom.quality", q);
            c.SetFloat("fx.bloom.threshold", threshold);
            c.SetFloat("fx.bloom.softKnee", knee);
            c.SetFloat("fx.bloom.radius", 1f);
            c.SetFloat("fx.bloom.dirtIntensity", on ? intensity * 0.15f : 0f);
            c.SetInt("fx.bloom.downsample", q >= 2 ? 4 : 2);
        }

        private static void Cas(GraphicsConfig c, bool on, float intensity, int q)
        {
            c.SetBool("fx.cas.enabled", on);
            c.SetFloat("fx.cas.intensity", intensity);
            c.SetInt("fx.cas.quality", q);
            c.SetBool("fx.cas.adaptive", true);
        }

        private static void Ambient(GraphicsConfig c, bool on, float intensity, float shadowBoost, int q)
        {
            c.SetBool("fx.ambient.enabled", on);
            c.SetFloat("fx.ambient.intensity", intensity);
            c.SetInt("fx.ambient.quality", q);
            c.SetFloat("fx.ambient.shadowBoost", shadowBoost);
        }

        private static void Sky(GraphicsConfig c, bool on, float intensity, int q)
        {
            c.SetBool("fx.sky.enabled", on);
            c.SetFloat("fx.sky.intensity", intensity);
            c.SetInt("fx.sky.quality", q);
        }

        private static void Lens(GraphicsConfig c, bool on, float intensity, float vignette, float grain,
            float dirt, float flare, float ca, int q)
        {
            c.SetBool("fx.lens.enabled", on);
            c.SetFloat("fx.lens.intensity", intensity);
            c.SetInt("fx.lens.quality", q);
            c.SetFloat("fx.lens.vignette", vignette);
            c.SetFloat("fx.lens.grain", grain);
            c.SetFloat("fx.lens.dirt", dirt);
            c.SetFloat("fx.lens.flare", flare);
            c.SetFloat("fx.lens.ca", ca);
        }

        private static void Fog(GraphicsConfig c, bool on, float intensity, float density, int q)
        {
            c.SetBool("fx.fog.enabled", on);
            c.SetFloat("fx.fog.intensity", intensity);
            c.SetInt("fx.fog.quality", q);
            c.SetFloat("fx.fog.distanceDensity", density);
            c.SetFloat("fx.fog.height", 8f);
            c.SetFloat("fx.fog.heightDensity", on ? 0.018f : 0f);
            c.SetInt("fx.fog.style", 0);
        }

        private static void Ssao(GraphicsConfig c, bool on, float intensity, int q)
        {
            c.SetBool("fx.ssao.enabled", on);
            c.SetFloat("fx.ssao.intensity", intensity);
            c.SetInt("fx.ssao.quality", q);
            c.SetInt("fx.ssao.samples", q >= 2 ? 12 : 8);
            c.SetFloat("fx.ssao.radius", 0.35f);
        }

        private static void Ssr(GraphicsConfig c, bool on, float intensity, int q)
        {
            c.SetBool("fx.ssr.enabled", on);
            c.SetFloat("fx.ssr.intensity", intensity);
            c.SetInt("fx.ssr.quality", q);
        }

        private static void Godrays(GraphicsConfig c, bool on, float intensity, int q)
        {
            c.SetBool("fx.godrays.enabled", on);
            c.SetFloat("fx.godrays.intensity", intensity);
            c.SetInt("fx.godrays.quality", q);
        }

        private static void Neon(GraphicsConfig c, bool on, float intensity)
        {
            c.SetBool("fx.neon.enabled", on);
            c.SetFloat("fx.neon.intensity", intensity);
            c.SetFloat("fx.neon.pixelate", 0f);
        }

        private static void Perf(GraphicsConfig c, bool adaptive, float maxGpuMs, int targetFps)
        {
            c.SetBool("performance.adaptive", adaptive);
            c.SetFloat("performance.maxGpuMs", maxGpuMs);
            c.SetInt("performance.targetFps", targetFps);
        }
    }
}
