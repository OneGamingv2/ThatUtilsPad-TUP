using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using TUPshaders.Settings;
using UnityEngine;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// ThatUtilsPad.RenderBridge.dll — D3D11 Present hook (clean camera-grade post).
    /// Sliders map 1:1 into soft effects. NOT hardware DXR.
    /// </summary>
    public static class NativeRenderBridge
    {
        private const string DllName = "ThatUtilsPad.RenderBridge.dll";
        private const string EmbeddedResourceName = "ThatUtilsPad.Assets.Native.ThatUtilsPad.RenderBridge.dll";
        private static bool _tried;
        private static bool _loaded;
        private static bool _initOk;
        private static IntPtr _module = IntPtr.Zero;

        private static RB_InitDelegate _init;
        private static RB_ShutdownDelegate _shutdown;
        private static RB_IsActiveDelegate _isActive;
        private static RB_SetLookDelegate _setLook;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RB_InitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RB_ShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RB_IsActiveDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RB_SetLookDelegate(
            float master, float ssao, float ssr, float contrast,
            float coolTint, float vignette, float bloom, float fog,
            float godrays, float grain, float sharpen, float saturation,
            float ca, float heightFog, float fogR, float fogG, float fogB,
            float grass, float bump, float detail, float denoise, float quality);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static bool IsAvailable => _initOk;
        public static bool IsDrawing => _initOk && _isActive != null && _isActive() != 0;

        public static void TryInit()
        {
            if (_tried) return;
            _tried = true;

            try
            {
                string path = EnsureNativeDllPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Plugin.Log.LogWarning("RenderBridge DLL not found (embedded or beside plugin) — native post disabled.");
                    return;
                }

                _module = LoadLibrary(path);
                if (_module == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("RenderBridge LoadLibrary failed (0x" + Marshal.GetLastWin32Error().ToString("X") + "). path=" + path);
                    return;
                }

                _init = LoadFn<RB_InitDelegate>("RB_Init");
                _shutdown = LoadFn<RB_ShutdownDelegate>("RB_Shutdown");
                _isActive = LoadFn<RB_IsActiveDelegate>("RB_IsActive");
                _setLook = LoadFn<RB_SetLookDelegate>("RB_SetLook");
                if (_init == null || _setLook == null)
                {
                    Plugin.Log.LogWarning("RenderBridge missing exports.");
                    return;
                }

                int rc = _init();
                if (rc != 0)
                {
                    Plugin.Log.LogWarning("RenderBridge RB_Init returned " + rc);
                    return;
                }

                _loaded = true;
                _initOk = true;
                Plugin.Log.LogInfo("RenderBridge camera-grade post active (embedded native). Not DXR.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("RenderBridge init failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Prefer embedded native bytes inside ThatUtilsPad.dll — extract beside the plugin
        /// (or to a temp folder if the plugins dir is locked). Falls back to a loose DLL.
        /// </summary>
        private static string EnsureNativeDllPath()
        {
            string pluginDir = Path.GetDirectoryName(typeof(NativeRenderBridge).Assembly.Location) ?? "";
            string beside = Path.Combine(pluginDir, DllName);

            byte[] embedded = null;
            try
            {
                Assembly asm = typeof(NativeRenderBridge).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream != null)
                    {
                        embedded = new byte[stream.Length];
                        int read = 0;
                        while (read < embedded.Length)
                        {
                            int n = stream.Read(embedded, read, embedded.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read != embedded.Length)
                            Array.Resize(ref embedded, read);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("RenderBridge embed read: " + ex.Message);
            }

            if (embedded != null && embedded.Length > 0)
            {
                if (TryWriteNative(beside, embedded))
                    return beside;

                string tempDir = Path.Combine(Path.GetTempPath(), "ThatUtilsPad");
                try { Directory.CreateDirectory(tempDir); } catch { /* ignore */ }
                string tempPath = Path.Combine(tempDir, DllName);
                if (TryWriteNative(tempPath, embedded))
                    return tempPath;
            }

            if (File.Exists(beside))
                return beside;
            string nested = Path.Combine(pluginDir, "native", DllName);
            return File.Exists(nested) ? nested : null;
        }

        private static bool TryWriteNative(string path, byte[] bytes)
        {
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        if (new FileInfo(path).Length == bytes.Length)
                            return true;
                    }
                    catch { /* fall through and overwrite */ }
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { /* in use — try replace */ }
                }
                try
                {
                    File.Move(tmp, path);
                }
                catch
                {
                    if (File.Exists(tmp))
                    {
                        File.Copy(tmp, path, true);
                        try { File.Delete(tmp); } catch { /* ignore */ }
                    }
                }
                return File.Exists(path);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("RenderBridge extract to " + path + ": " + ex.Message);
                return false;
            }
        }

        public static void Tick(SettingsManager settings)
        {
            if (!_initOk || _setLook == null || settings == null)
                return;

            float master = settings.Get("graphics.master", true) ? 1f : 0f;
            if (!settings.Get("graphics.nativeBridge", true))
                master = 0f;

            bool irl = settings.Get("fx.look.irl", false);
            bool neon = settings.Get("fx.neon.enabled", false);
            float boost = settings.Get("fx.bridge.boost", irl ? 1.05f : 1f);
            // Keep overall soft — crushing master made everything look noisy
            master *= Mathf.Clamp(boost, 0.6f, 1.35f);

            float ssao = On(settings, "fx.ssao.enabled", false)
                         * Soft(settings.Get("fx.ssao.intensity", 0.45f), 1.2f);
            float ssr = On(settings, "fx.ssr.enabled", false)
                        * Soft(settings.Get("fx.ssr.intensity", 0.35f), 1.1f);
            float contrast = Mathf.Clamp(settings.Get("fx.colorgrading.contrast", 1.1f), 0.7f, 1.55f);

            // +cool blue / -magenta neon. IRL golden-hour stays near-neutral
            // (warm air comes from fog color + shafts, not a blue wash).
            float tint;
            if (neon)
                tint = -Mathf.Clamp(settings.Get("fx.neon.intensity", 0.7f), 0f, 1.2f);
            else if (irl)
            {
                int fogStyleEarly = settings.Get("fx.fog.style", 2);
                tint = fogStyleEarly == 2 ? 0.02f : 0.12f;
            }
            else
                tint = ssr > 0.05f ? 0.12f : 0.04f;

            float vig = On(settings, "fx.lens.enabled", true)
                        * Soft(settings.Get("fx.lens.vignette", 0.22f), 0.9f)
                        * Mathf.Clamp01(settings.Get("fx.lens.intensity", 0.7f));
            float bloom = On(settings, "fx.bloom.enabled", true)
                          * Soft(settings.Get("fx.bloom.intensity", 0.35f), 1.4f);

            float fog = On(settings, "fx.fog.enabled", false)
                        * Soft(settings.Get("fx.fog.intensity", 0.4f), 1.1f);
            fog *= Mathf.Clamp01(settings.Get("fx.fog.distanceDensity", 0.008f) / 0.012f + 0.25f);

            float godrays = On(settings, "fx.godrays.enabled", false)
                            * Soft(settings.Get("fx.godrays.intensity", 0.35f), 1.35f);

            // Film grain: soft cap — high sliders used to look like pixel garbage
            float grain = On(settings, "fx.lens.enabled", true)
                          * Soft(settings.Get("fx.lens.grain", 0.04f), 0.35f);
            if (irl) grain = 0f;

            float sharpen = On(settings, "fx.cas.enabled", true)
                            * Soft(settings.Get("fx.cas.intensity", 0.4f), 0.95f);
            float sat = Mathf.Clamp(settings.Get("fx.colorgrading.saturation", 1.05f), 0.5f, 1.6f);

            // CA soft cap — Neon used to dump 0.2 and fringe every edge
            float ca = On(settings, "fx.lens.enabled", true)
                       * Soft(settings.Get("fx.lens.ca", 0.02f), 0.4f);
            if (irl) ca *= 0.35f;
            if (neon) ca = Mathf.Min(ca, 0.08f);

            float heightFog = On(settings, "fx.fog.enabled", false)
                              * Mathf.Clamp01(settings.Get("fx.fog.heightDensity", 0.02f) / 0.05f)
                              * Soft(settings.Get("fx.fog.intensity", 0.4f), 1.0f);

            float fogR = settings.Get("fx.fog.color.r", 0.62f);
            float fogG = settings.Get("fx.fog.color.g", 0.72f);
            float fogB = settings.Get("fx.fog.color.b", 0.82f);

            int style = settings.Get("fx.fog.style", irl ? 2 : 0);
            if (style == 1) { fogR = 0.55f; fogG = 0.70f; fogB = 0.86f; fog *= 1.1f; heightFog *= 1.15f; }
            else if (style == 2)
            {
                // Soft golden haze — don't hard-replace if preset already set warm RGB
                if (fogR < 0.7f) { fogR = 0.92f; fogG = 0.74f; fogB = 0.48f; }
                fog *= 1.12f;
                heightFog *= 1.15f;
            }
            else if (style == 3) { fogR = 0.75f; fogG = 0.80f; fogB = 0.88f; fog *= 1.35f; heightFog *= 1.3f; }

            // Soft bump for golden shafts when IRL / realistic
            if (irl && godrays > 0.05f)
                godrays = Mathf.Min(godrays * 1.15f, 1.25f);

            float grass = On(settings, "fx.grass.enabled", irl)
                          * Soft(settings.Get("fx.grass.intensity", irl ? 0.55f : 0.25f), 1.0f);
            float bump = On(settings, "fx.bump.enabled", irl)
                         * Soft(settings.Get("fx.bump.intensity", irl ? 0.45f : 0.2f), 0.9f);
            float detail = Soft(settings.Get("fx.detail.intensity", irl ? 0.4f : 0.15f), 0.85f);
            // Always a little denoise when bridge is on — cleans residual pixel mess
            float denoise = Mathf.Max(0.2f, Soft(settings.Get("fx.denoise.intensity", irl ? 0.45f : 0.3f), 1f));
            float quality = settings.Get("fx.bridge.quality", irl ? 2 : 1);

            try
            {
                _setLook(master, ssao, ssr, contrast, tint, vig, bloom,
                    fog, godrays, grain, sharpen, sat, ca, heightFog, fogR, fogG, fogB,
                    grass, bump, detail, denoise, quality);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("RenderBridge SetLook: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (_loaded && _shutdown != null)
                    _shutdown();
            }
            catch { /* ignore */ }

            if (_module != IntPtr.Zero)
            {
                FreeLibrary(_module);
                _module = IntPtr.Zero;
            }
            _loaded = false;
            _initOk = false;
        }

        private static float On(SettingsManager s, string key, bool def)
            => s.Get(key, def) ? 1f : 0f;

        /// Soft response curve so mid/high slider values stay readable without crushing.
        private static float Soft(float v, float maxOut)
        {
            v = Mathf.Max(0f, v);
            // sqrt-ish ease: 0.5 slider ≈ 0.7 of max perceived, hard stop at maxOut
            float n = 1f - Mathf.Exp(-v * 1.4f);
            return Mathf.Min(n * maxOut, maxOut);
        }

        private static T LoadFn<T>(string name) where T : class
        {
            IntPtr p = GetProcAddress(_module, name);
            if (p == IntPtr.Zero) return null;
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
    }
}
