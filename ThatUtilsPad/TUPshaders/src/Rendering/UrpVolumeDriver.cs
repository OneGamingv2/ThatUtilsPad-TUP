using System;
using TUPshaders.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// URP Volume driver. Deferred init — creating Volumes in Awake crashes Unity 6 / GT.
    /// Only uses stable Volume components; optional ones are skipped if they throw.
    /// </summary>
    public sealed class UrpVolumeDriver : IDisposable
    {
        private readonly SettingsManager _settings;

        private GameObject? _volumeGo;
        private Volume? _volume;
        private VolumeProfile? _profile;

        private Bloom? _bloom;
        private ColorAdjustments? _color;
        private Tonemapping? _tonemap;
        private WhiteBalance? _whiteBalance;
        private Vignette? _vignette;
        private ChromaticAberration? _ca;

        private bool _ready;
        private bool _failed;
        private bool _dirty = true;
        private int _initAttempts;

        private Color _defaultAmbient;
        private bool _capturedDefaults;
        private bool _defaultFog;
        private Color _defaultFogColor;
        private float _defaultFogDensity;
        private float _defaultAmbientIntensity = 1f;

        public bool IsReady => _ready;
        public bool HasFailed => _failed;

        public UrpVolumeDriver(SettingsManager settings)
        {
            _settings = settings;
        }

        public void MarkDirty() => _dirty = true;

        /// <summary>
        /// Call from LateUpdate only. Safe no-op until URP pipeline exists and a few frames pass.
        /// </summary>
        public void Tick(bool forceSync = false)
        {
            if (_failed) return;

            if (!_ready)
            {
                if (Time.frameCount < 8) return;
                if (GraphicsSettings.currentRenderPipeline == null) return;
                if (_initAttempts++ > 30)
                {
                    _failed = true;
                    Plugin.Log.LogError("URP Volume init failed after retries — visuals disabled.");
                    return;
                }

                try
                {
                    EnsureInternal();
                    _ready = true;
                    _dirty = true;
                    Plugin.Log.LogInfo("URP Volume driver online.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"URP Volume init attempt {_initAttempts}: {ex.Message}");
                    CleanupPartial();
                    return;
                }
            }

            if (_dirty || forceSync)
            {
                try
                {
                    SyncInternal();
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"URP Volume sync error: {ex.Message}");
                }
            }
        }

        private void EnsureInternal()
        {
            if (_volume != null) return;

            _volumeGo = new GameObject("TUPshaders_GlobalVolume");
            UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
            _volumeGo.hideFlags = HideFlags.HideAndDontSave;

            _volume = _volumeGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 999f; // Win over any game default volumes
            _volume.weight = 1f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "TUPshaders_Profile";
            _profile.hideFlags = HideFlags.HideAndDontSave;
            _volume.sharedProfile = _profile;

            // Core set only — flare/grain/lift/SMH have crashed on GT Unity 6
            _bloom = TryAdd<Bloom>();
            _color = TryAdd<ColorAdjustments>();
            _tonemap = TryAdd<Tonemapping>();
            _whiteBalance = TryAdd<WhiteBalance>();
            _vignette = TryAdd<Vignette>();
            _ca = TryAdd<ChromaticAberration>();

            if (_bloom == null && _color == null)
                throw new InvalidOperationException("Could not add any Volume components.");
        }

        private T? TryAdd<T>() where T : VolumeComponent
        {
            try
            {
                var c = _profile!.Add<T>(true);
                c.active = false;
                Plugin.Log.LogInfo($"Volume +{typeof(T).Name}");
                return c;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Skip Volume {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        private void CleanupPartial()
        {
            try
            {
                if (_volumeGo != null) UnityEngine.Object.Destroy(_volumeGo);
            }
            catch { /* ignore */ }
            _volumeGo = null;
            _volume = null;
            _profile = null;
            _bloom = null;
            _color = null;
            _tonemap = null;
            _whiteBalance = null;
            _vignette = null;
            _ca = null;
        }

        public void EnablePostProcessingOnCamera(Camera camera)
        {
            if (camera == null) return;
            try
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data == null) return;
                bool changed = false;
                if (!data.renderPostProcessing)
                {
                    data.renderPostProcessing = true;
                    changed = true;
                }
                data.volumeLayerMask = ~0;
                if (data.volumeTrigger == null)
                    data.volumeTrigger = camera.transform;
                if (changed)
                    Plugin.Log.LogInfo($"Enabled URP post-processing on '{camera.name}'.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Camera post enable failed: {ex.Message}");
            }
        }

        private void SyncInternal()
        {
            if (_volume == null) return;
            CaptureDefaultsOnce();

            bool master = _settings.Get("graphics.master", true);
            _volume.enabled = master;
            _volume.weight = master ? 1f : 0f;

            if (!master)
            {
                RestoreDefaults();
                SetActive(_bloom, false);
                SetActive(_color, false);
                SetActive(_tonemap, false);
                SetActive(_whiteBalance, false);
                SetActive(_vignette, false);
                SetActive(_ca, false);
                return;
            }

            SyncColor();
            SyncBloom();
            SyncLens();
            // Ambient/fog belong to CodeLookComposer so we don't fight RenderSettings each frame.
        }

        /// <summary>
        /// Extra volume punches mapping SSAO / SSR / godrays / etc. into stock URP knobs.
        /// Called every frame from CodeLookComposer after SyncInternal.
        /// </summary>
        public void ApplyCodeStandIns(SettingsManager s)
        {
            if (!_ready || _volume == null || s == null) return;
            if (!s.Get("graphics.master", true)) return;

            float ssao = (s.Get("fx.ssao.enabled", false) ? 1f : 0f) * s.Get("fx.ssao.intensity", 0.5f);
            float ssr = (s.Get("fx.ssr.enabled", false) ? 1f : 0f) * s.Get("fx.ssr.intensity", 0.35f);
            float godrays = (s.Get("fx.godrays.enabled", false) ? 1f : 0f) * s.Get("fx.godrays.intensity", 0.25f);
            float sky = (s.Get("fx.sky.enabled", true) ? 1f : 0f) * s.Get("fx.sky.intensity", 0.5f);

            if (_bloom != null && godrays > 0.01f)
            {
                _bloom.active = true;
                float baseBloom = s.Get("fx.bloom.enabled", true)
                    ? s.Get("fx.bloom.intensity", 0.55f) * 2.4f
                    : 0f;
                SafeOverride(_bloom.intensity, Mathf.Max(0.05f, baseBloom + godrays * 1.8f));
                SafeOverride(_bloom.threshold, Mathf.Clamp(0.55f - godrays * 0.15f, 0.2f, 2f));
                SafeOverride(_bloom.scatter, Mathf.Clamp01(0.65f + godrays * 0.2f));
            }

            if (_vignette != null && ssao > 0.05f)
            {
                float vig = s.Get("fx.lens.enabled", true)
                    ? s.Get("fx.lens.vignette", 0.3f) * s.Get("fx.lens.intensity", 0.75f)
                    : 0f;
                _vignette.active = true;
                SafeOverride(_vignette.intensity, Mathf.Clamp01(Mathf.Max(vig, 0.12f) + ssao * 0.22f));
                SafeOverride(_vignette.smoothness, 0.45f);
            }

            if (_ca != null && (ssr > 0.05f || sky > 0.4f))
            {
                float ca = s.Get("fx.lens.enabled", true)
                    ? s.Get("fx.lens.ca", 0.05f) * s.Get("fx.lens.intensity", 0.75f)
                    : 0f;
                _ca.active = true;
                SafeOverride(_ca.intensity, Mathf.Clamp01(Mathf.Max(ca, 0.02f) + ssr * 0.12f + sky * 0.04f));
            }

            if (_color != null && (ssao > 0.01f || sky > 0.01f || s.Get("fx.cas.enabled", true)))
            {
                bool grading = s.Get("fx.colorgrading.enabled", true);
                float intensity = Mathf.Clamp01(s.Get("fx.colorgrading.intensity", 1f));
                float satMul = s.Get("fx.colorgrading.saturation", 1.12f);
                float contrastMul = s.Get("fx.colorgrading.contrast", 1.12f);
                float vibrance = s.Get("fx.colorgrading.vibrance", 0.25f);
                float gain = s.Get("fx.colorgrading.gain", 1.05f);
                if (s.Get("fx.cas.enabled", true))
                    contrastMul += s.Get("fx.cas.intensity", 0.45f) * 0.14f;
                contrastMul += ssao * 0.2f;
                satMul += sky * 0.08f;
                gain += godrays * 0.15f;

                _color.active = grading;
                if (grading)
                {
                float sat = ((satMul - 1f) * 100f + vibrance * 70f) * intensity;
                float contrast = (contrastMul - 1f) * 100f * intensity + ssao * 25f;
                float exposure = (gain - 1f) * 3f * intensity + godrays * 0.55f;
                sat = Mathf.Clamp(sat * 2.0f + sky * 8f, -90f, 90f);
                contrast = Mathf.Clamp(contrast * 1.9f, -90f, 90f);
                SafeOverride(_color.saturation, sat);
                SafeOverride(_color.contrast, contrast);
                SafeOverride(_color.postExposure, exposure);

                    try
                    {
                        // Warm/cool color filter as code-only LUT stand-in
                        float temp = s.Get("fx.colorgrading.temperature", 0.05f) + sky * 0.08f;
                        Color filter = Color.Lerp(
                            new Color(0.92f, 0.95f, 1.05f),
                            new Color(1.08f, 1.0f, 0.88f),
                            Mathf.Clamp01(0.5f + temp));
                        SafeOverride(_color.colorFilter, filter);
                    }
                    catch { /* some URP builds omit colorFilter */ }
                }
            }
        }

        private void SyncColor()
        {
            bool on = _settings.Get("fx.colorgrading.enabled", true);
            float intensity = Mathf.Clamp01(_settings.Get("fx.colorgrading.intensity", 1f));
            float satMul = _settings.Get("fx.colorgrading.saturation", 1.18f);
            float contrastMul = _settings.Get("fx.colorgrading.contrast", 1.18f);
            float vibrance = _settings.Get("fx.colorgrading.vibrance", 0.28f);
            float temp = _settings.Get("fx.colorgrading.temperature", 0.05f);
            float tint = _settings.Get("fx.colorgrading.tint", 0f);
            float gain = _settings.Get("fx.colorgrading.gain", 1.08f);
            int tonemapMode = _settings.Get("fx.colorgrading.tonemap", 1);

            // Mild CAS → extra contrast
            if (_settings.Get("fx.cas.enabled", true))
                contrastMul += _settings.Get("fx.cas.intensity", 0.45f) * 0.12f;

            if (_color != null)
            {
                _color.active = on;
                if (on)
                {
                    // Aggressive stock URP grade — must be obvious on PC monitor + VR nights.
                    float sat = ((satMul - 1f) * 100f + vibrance * 70f) * intensity;
                    float contrast = (contrastMul - 1f) * 100f * intensity;
                    float exposure = (gain - 1f) * 3.2f * intensity;
                    sat = Mathf.Clamp(sat * 2.1f, -90f, 90f);
                    contrast = Mathf.Clamp(contrast * 2.0f, -90f, 90f);
                    SafeOverride(_color.saturation, sat);
                    SafeOverride(_color.contrast, contrast);
                    SafeOverride(_color.postExposure, exposure);
                }
            }

            if (_tonemap != null)
            {
                _tonemap.active = on && tonemapMode > 0;
                if (_tonemap.active)
                    SafeOverride(_tonemap.mode, tonemapMode >= 2 ? TonemappingMode.Neutral : TonemappingMode.ACES);
            }

            if (_whiteBalance != null)
            {
                bool wb = on && (Mathf.Abs(temp) > 0.001f || Mathf.Abs(tint) > 0.001f);
                _whiteBalance.active = wb;
                if (wb)
                {
                    SafeOverride(_whiteBalance.temperature, temp * 45f * intensity);
                    SafeOverride(_whiteBalance.tint, tint * 45f * intensity);
                }
            }

            // Sky-ish warm/cool via white balance when sky effect on and WB already used
            if (_settings.Get("fx.sky.enabled", true) && _whiteBalance != null && on)
            {
                float sky = _settings.Get("fx.sky.intensity", 0.5f);
                if (!_whiteBalance.active && sky > 0.05f)
                {
                    _whiteBalance.active = true;
                    SafeOverride(_whiteBalance.temperature, 10f * sky * intensity);
                }
            }
        }

        private void SyncBloom()
        {
            if (_bloom == null) return;
            bool on = _settings.Get("fx.bloom.enabled", true);
            float intensity = _settings.Get("fx.bloom.intensity", 0.55f);
            float threshold = _settings.Get("fx.bloom.threshold", 0.75f);
            float softKnee = _settings.Get("fx.bloom.softKnee", 0.5f);

            _bloom.active = on && intensity > 0.01f;
            if (!_bloom.active) return;

            SafeOverride(_bloom.intensity, Mathf.Max(0f, intensity * 4.2f));
            SafeOverride(_bloom.threshold, Mathf.Clamp(threshold * 0.65f, 0f, 2f));
            SafeOverride(_bloom.scatter, Mathf.Clamp01(0.6f + softKnee * 0.45f));
        }

        private void SyncLens()
        {
            bool on = _settings.Get("fx.lens.enabled", true);
            float intensity = _settings.Get("fx.lens.intensity", 0.75f);
            float vig = _settings.Get("fx.lens.vignette", 0.3f);
            float ca = _settings.Get("fx.lens.ca", 0.05f);

            if (_vignette != null)
            {
                _vignette.active = on && vig * intensity > 0.01f;
                if (_vignette.active)
                {
                    SafeOverride(_vignette.intensity, Mathf.Clamp01(vig * intensity * 1.55f));
                    SafeOverride(_vignette.smoothness, 0.35f);
                }
            }

            if (_ca != null)
            {
                _ca.active = on && ca * intensity > 0.01f;
                if (_ca.active)
                    SafeOverride(_ca.intensity, Mathf.Clamp01(ca * intensity * 1.8f));
            }
        }

        private static void SetActive(VolumeComponent? c, bool on)
        {
            if (c != null) c.active = on;
        }

        private static void SafeOverride<T>(VolumeParameter<T> param, T value)
        {
            try { param.Override(value); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Override failed: {ex.Message}"); }
        }

        private void CaptureDefaultsOnce()
        {
            if (_capturedDefaults) return;
            _capturedDefaults = true;
            try
            {
                _defaultAmbient = RenderSettings.ambientLight;
                _defaultAmbientIntensity = RenderSettings.ambientIntensity;
                _defaultFog = RenderSettings.fog;
                _defaultFogColor = RenderSettings.fogColor;
                _defaultFogDensity = RenderSettings.fogDensity;
            }
            catch { /* ignore */ }
        }

        private void RestoreDefaults()
        {
            if (!_capturedDefaults) return;
            try
            {
                RenderSettings.ambientLight = _defaultAmbient;
                RenderSettings.ambientIntensity = _defaultAmbientIntensity;
                RenderSettings.fog = _defaultFog;
                RenderSettings.fogColor = _defaultFogColor;
                RenderSettings.fogDensity = _defaultFogDensity;
            }
            catch { /* ignore */ }
        }

        public int CountActiveComponents()
        {
            int n = 0;
            if (_bloom != null && _bloom.active) n++;
            if (_color != null && _color.active) n++;
            if (_tonemap != null && _tonemap.active) n++;
            if (_whiteBalance != null && _whiteBalance.active) n++;
            if (_vignette != null && _vignette.active) n++;
            if (_ca != null && _ca.active) n++;
            return n;
        }

        // Compatibility stubs for old call sites
        public void Ensure() { /* deferred via Tick */ }
        public void Sync() => MarkDirty();

        public void Dispose()
        {
            RestoreDefaults();
            CleanupPartial();
            _ready = false;
        }
    }
}
