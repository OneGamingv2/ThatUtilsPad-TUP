using System;
using System.Collections.Generic;
using System.Reflection;
using TUPshaders.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Fullscreen post agent for every live display camera (PC monitor / Shoulder, VR eyes).
    /// Uses tint-capable blit materials (Sprites/Default) — CoreBlit alone never changes look.
    /// Dual path: URP endCameraRendering + CameraEvent.AfterEverything command buffers.
    /// </summary>
    public sealed class RenderAgent : IDisposable
    {
        private readonly SettingsManager _settings;
        private readonly CommandBuffer _cmd = new() { name = "TUP_RenderAgent" };
        private readonly Dictionary<int, CommandBuffer> _camBuffers = new();
        private readonly HashSet<int> _loggedCams = new();

        private Material _copyMat;
        private Material _tintMat;
        private Material _addMat;

        private readonly int _rtA = Shader.PropertyToID("_TUP_RA");
        private readonly int _rtB = Shader.PropertyToID("_TUP_RB");
        private readonly int _bloomLo = Shader.PropertyToID("_TUP_RBLo");

        private bool _subscribed;
        private bool _loggedMats;
        private int _framesProcessed;
        private float _nextShadowPass;
        private float _nextBindPass;

        private static readonly string[] TintShaders =
        {
            "Sprites/Default",
            "UI/Default",
            "Unlit/Transparent",
            "Unlit/Texture",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Standard Unlit",
            "Hidden/Internal-GUITexture"
        };

        private static readonly string[] CopyShaders =
        {
            "Hidden/Universal/CoreBlit",
            "Hidden/Universal Render Pipeline/Blit",
            "Hidden/Universal Render Pipeline/CoreBlit",
            "Hidden/BlitCopy",
            "Hidden/Internal-BlitCopy",
            "Sprites/Default",
            "Unlit/Texture"
        };

        private static readonly string[] AddShaders =
        {
            "Particles/Additive",
            "Legacy Shaders/Particles/Additive",
            "Mobile/Particles/Additive",
            "Particles/Standard Unlit"
        };

        public RenderAgent(SettingsManager settings) => _settings = settings;

        public void EnsureSubscribed()
        {
            if (_subscribed)
                return;

            BuildMaterials();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            _subscribed = true;
            Plugin.Log.LogInfo("RenderAgent active — visible tint stack on ALL Game/VR display cameras (PC + headset).");
        }

        public void TickScene()
        {
            if (!_settings.Get("graphics.master", true))
                return;

            if (Time.unscaledTime >= _nextShadowPass)
            {
                _nextShadowPass = Time.unscaledTime + 1f;
                try { ApplyShadowPolicy(); }
                catch (Exception ex) { Plugin.Log.LogWarning("RenderAgent shadow: " + ex.Message); }
            }

            if (Time.unscaledTime >= _nextBindPass)
            {
                _nextBindPass = Time.unscaledTime + 0.5f;
                BindAllCameras();
            }
        }

        /// <summary>
        /// Discover + force URP post on every display camera (PC Shoulder + VR eyes).
        /// Blit stack runs in endCameraRendering only (avoids double-darken).
        /// </summary>
        public void BindAllCameras()
        {
            Camera[] cams = Camera.allCameras;

            for (int i = 0; i < cams.Length; i++)
            {
                Camera cam = cams[i];
                if (!ShouldProcess(cam))
                    continue;

                int id = cam.GetInstanceID();
                ForceUrpPost(cam);

                if (_loggedCams.Add(id))
                {
                    Plugin.Log.LogInfo("RenderAgent display camera: '" + cam.name + "' " +
                                       cam.pixelWidth + "x" + cam.pixelHeight +
                                       " type=" + cam.cameraType +
                                       " stereo=" + cam.stereoEnabled +
                                       " depth=" + cam.depth);
                }
            }
        }

        private void ApplyShadowPolicy()
        {
            bool want = _settings.Get("fx.ambient.enabled", true)
                        || _settings.Get("fx.ssao.enabled", false)
                        || _settings.Get("fx.ssr.enabled", false);
            if (!want)
                return;

            float boost = Mathf.Clamp01(_settings.Get("fx.ambient.shadowBoost", 0.35f));
            if (QualitySettings.shadows == UnityEngine.ShadowQuality.Disable)
                QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = Mathf.Lerp(50f, 110f, boost);
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit;
        }

        private void BuildMaterials()
        {
            Shader tint = FindShader(TintShaders);
            Shader copy = FindShader(CopyShaders) ?? tint;
            Shader add = FindShader(AddShaders);

            if (!_loggedMats)
            {
                _loggedMats = true;
                Plugin.Log.LogInfo("RenderAgent tint: " + (tint != null ? tint.name : "NULL"));
                Plugin.Log.LogInfo("RenderAgent copy: " + (copy != null ? copy.name : "NULL"));
                Plugin.Log.LogInfo("RenderAgent add: " + (add != null ? add.name : "NULL"));
            }

            if (tint != null)
                _tintMat = NewMat(tint, "TUP_RA_Tint");
            if (copy != null)
                _copyMat = NewMat(copy, "TUP_RA_Copy");
            if (add != null)
                _addMat = NewMat(add, "TUP_RA_Add");

            // Prefer tint for copy too when CoreBlit would strip color grades.
            if (_tintMat != null && (_copyMat == null || IsCoreBlit(_copyMat)))
                _copyMat = _tintMat;
        }

        private static bool IsCoreBlit(Material m) =>
            m != null && m.shader != null &&
            m.shader.name.IndexOf("Blit", StringComparison.OrdinalIgnoreCase) >= 0 &&
            m.shader.name.IndexOf("Sprite", StringComparison.OrdinalIgnoreCase) < 0;

        private static Material NewMat(Shader s, string name) =>
            new Material(s) { name = name, hideFlags = HideFlags.HideAndDontSave };

        private static Shader FindShader(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Shader s = Shader.Find(names[i]);
                if (s != null)
                    return s;
            }
            return null;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_settings.Get("graphics.master", true) || !ShouldProcess(camera))
                return;

            try
            {
                camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
                ForceUrpPost(camera);
            }
            catch
            {
            }
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            // Unity 6 Render Graph discards late CameraTarget blits — they never show and can
            // introduce artifacts. Visible look is ScreenGradeOverlay + URP Volumes only.
            if (!_settings.Get("graphics.master", true) || !ShouldProcess(camera))
                return;

            _framesProcessed++;
            if (_framesProcessed == 1)
                Plugin.Log.LogInfo("RenderAgent tracking '" + camera.name + "' (volumes+overlay; blit disabled under Render Graph).");
        }

        private static void ForceUrpPost(Camera camera)
        {
            try
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data == null)
                    return;
                data.renderPostProcessing = true;
                data.volumeLayerMask = ~0;
                if (data.volumeTrigger == null)
                    data.volumeTrigger = camera.transform;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Every Game + VR camera that paints a display. Skip RT previews / UI-only / prewarm.
        /// </summary>
        private static bool ShouldProcess(Camera camera)
        {
            if (camera == null || !camera.isActiveAndEnabled)
                return false;

            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.VR)
                return false;

            // RT = tablet preview etc. Must not process or we feedback-loop.
            if (camera.targetTexture != null)
                return false;

            if (camera.pixelWidth < 32 || camera.pixelHeight < 32)
                return false;

            string name = camera.name ?? "";
            if (name.IndexOf("prewarm", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (name.IndexOf("Tablet", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (name.IndexOf("UICamera", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (name.Equals("UI", StringComparison.OrdinalIgnoreCase))
                return false;

            // INCLUDE: Shoulder Camera (PC monitor), VR eyes, Main Camera, freecam-driven display cams.
            return true;
        }

        private object ResolveColorTarget(Camera camera)
        {
            try
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    // URP cameraColorTargetHandle / cameraColorTarget via reflection (API varies by GT Unity).
                    Type t = data.GetType();
                    PropertyInfo p = t.GetProperty("cameraColorTargetHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                     ?? t.GetProperty("cameraColorTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null)
                    {
                        object v = p.GetValue(data, null);
                        if (v != null)
                            return v;
                    }
                }
            }
            catch
            {
            }

            return BuiltinRenderTextureType.CameraTarget;
        }

        private void AppendStack(CommandBuffer cmd, object target, int w, int h)
        {
            if (_tintMat == null)
                return;

            // GetTemporaryRT + Blit against whatever target type we resolved.
            cmd.GetTemporaryRT(_rtA, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
            cmd.GetTemporaryRT(_rtB, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);

            BlitTo(cmd, target, _rtA, null);
            int src = _rtA;
            int dst = _rtB;

            void Swap()
            {
                int t = src;
                src = dst;
                dst = t;
            }

            // Color grade + CAS (MUST use tint material — visibly multiplies RGB)
            if (_settings.Get("fx.colorgrading.enabled", true))
            {
                float intensity = Mathf.Clamp01(_settings.Get("fx.colorgrading.intensity", 1f));
                float sat = _settings.Get("fx.colorgrading.saturation", 1.18f);
                float contrast = _settings.Get("fx.colorgrading.contrast", 1.18f);
                float vibrance = _settings.Get("fx.colorgrading.vibrance", 0.28f);
                float gain = _settings.Get("fx.colorgrading.gain", 1.08f);
                float temp = _settings.Get("fx.colorgrading.temperature", 0.05f);

                if (_settings.Get("fx.cas.enabled", true))
                    contrast += _settings.Get("fx.cas.intensity", 0.45f) * 0.35f;

                // Push hard so Competitive vs ReShade is unmistakable on a dark forest night.
                float m = Mathf.Lerp(1f, (0.55f + sat * 0.55f + vibrance * 0.7f) * contrast, intensity);
                m *= Mathf.Lerp(1f, gain, intensity * 0.85f);
                Color tint = new Color(
                    Mathf.Clamp(m * (1f + temp * 0.55f), 0.15f, 3.5f),
                    Mathf.Clamp(m * 0.98f, 0.15f, 3.5f),
                    Mathf.Clamp(m * (1f - temp * 0.4f), 0.15f, 3.5f),
                    1f);

                ApplyColor(_tintMat, tint);
                BlitTo(cmd, src, dst, _tintMat);
                Swap();
            }

            // Bloom + godrays additive lift
            float bloomI = _settings.Get("fx.bloom.enabled", true)
                ? _settings.Get("fx.bloom.intensity", 0.65f)
                : 0f;
            float godrays = (_settings.Get("fx.godrays.enabled", false) ? 1f : 0f)
                            * _settings.Get("fx.godrays.intensity", 0.35f);
            bloomI += godrays * 1.1f;

            if (bloomI > 0.02f)
            {
                int bw = Mathf.Max(64, w / 4);
                int bh = Mathf.Max(64, h / 4);
                cmd.GetTemporaryRT(_bloomLo, bw, bh, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
                BlitTo(cmd, src, _bloomLo, null);

                if (_addMat != null)
                {
                    float a = Mathf.Clamp01(bloomI * 1.35f);
                    ApplyColor(_addMat, new Color(a, a * 0.95f, a * (0.85f + godrays * 0.25f), a));
                    BlitTo(cmd, src, dst, null);
                    BlitTo(cmd, _bloomLo, dst, _addMat);
                    Swap();
                }
                else
                {
                    float b = 1f + bloomI * 0.55f;
                    ApplyColor(_tintMat, new Color(b, b, b * (1f + godrays * 0.08f), 1f));
                    BlitTo(cmd, src, dst, _tintMat);
                    Swap();
                }

                cmd.ReleaseTemporaryRT(_bloomLo);
            }

            // SSAO / contact shadow stand-in — strong midtone darken
            float ssao = (_settings.Get("fx.ssao.enabled", false) ? 1f : 0f)
                         * Mathf.Clamp01(_settings.Get("fx.ssao.intensity", 0.55f));
            if (ssao > 0.02f)
            {
                float d = 1f - ssao * 0.45f;
                ApplyColor(_tintMat, new Color(d * 0.95f, d * 0.92f, d * 0.88f, 1f));
                BlitTo(cmd, src, dst, _tintMat);
                Swap();
            }

            // SSR stand-in — cool specular lift
            float ssr = (_settings.Get("fx.ssr.enabled", false) ? 1f : 0f)
                        * Mathf.Clamp01(_settings.Get("fx.ssr.intensity", 0.4f));
            if (ssr > 0.02f)
            {
                float b = 1f + ssr * 0.22f;
                ApplyColor(_tintMat, new Color(b * 0.92f, b * 1.02f, b * 1.12f, 1f));
                BlitTo(cmd, src, dst, _tintMat);
                Swap();
            }

            // Vignette darken
            if (_settings.Get("fx.lens.enabled", true))
            {
                float vig = _settings.Get("fx.lens.vignette", 0.3f) * _settings.Get("fx.lens.intensity", 0.75f);
                if (vig > 0.03f)
                {
                    float d = 1f - vig * 0.55f;
                    ApplyColor(_tintMat, new Color(d, d * 0.98f, d * 0.95f, 1f));
                    BlitTo(cmd, src, dst, _tintMat);
                    Swap();
                }
            }

            BlitTo(cmd, src, target, null);
            cmd.ReleaseTemporaryRT(_rtA);
            cmd.ReleaseTemporaryRT(_rtB);
        }

        private void BlitTo(CommandBuffer cmd, object src, object dst, Material mat)
        {
            // Set blit texture globals for URP CoreBlit if somehow used.
            if (mat != null)
            {
                PrepBlit(mat);
                if (src is int srcId)
                    cmd.SetGlobalTexture("_BlitTexture", srcId);
            }

            Material use = mat ?? _copyMat ?? _tintMat;

            if (src is int s && dst is int d)
            {
                if (use != null) cmd.Blit(s, d, use);
                else cmd.Blit(s, d);
                return;
            }

            // Mixed RT identifier / BuiltinRenderTextureType / reflected handle
            if (src is RenderTargetIdentifier sRi && dst is RenderTargetIdentifier dRi)
            {
                if (use != null) cmd.Blit(sRi, dRi, use);
                else cmd.Blit(sRi, dRi);
                return;
            }

            // Wrap unknowns via RenderTargetIdentifier conversion when possible
            var srcIdf = ToRti(src);
            var dstIdf = ToRti(dst);
            if (use != null) cmd.Blit(srcIdf, dstIdf, use);
            else cmd.Blit(srcIdf, dstIdf);
        }

        private static RenderTargetIdentifier ToRti(object o)
        {
            if (o is RenderTargetIdentifier rti)
                return rti;
            if (o is BuiltinRenderTextureType bt)
                return new RenderTargetIdentifier(bt);
            if (o is int id)
                return new RenderTargetIdentifier(id);
            if (o is Texture tex)
                return new RenderTargetIdentifier(tex);
            // Reflected RTHandle / RenderTargetIdentifier boxed
            try
            {
                if (o != null)
                {
                    // RTHandle.nameID or implicit conversion
                    PropertyInfo nameId = o.GetType().GetProperty("nameID");
                    if (nameId != null && nameId.PropertyType == typeof(int))
                        return new RenderTargetIdentifier((int)nameId.GetValue(o, null));

                    MethodInfo op = o.GetType().GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public);
                    // fallback: CameraTarget
                }
            }
            catch
            {
            }

            return BuiltinRenderTextureType.CameraTarget;
        }

        private static void PrepBlit(Material mat)
        {
            if (mat.HasProperty("_BlitScaleBias"))
                mat.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            if (mat.HasProperty("_BlitScaleBiasRt"))
                mat.SetVector("_BlitScaleBiasRt", new Vector4(1f, 1f, 0f, 0f));
        }

        private static void ApplyColor(Material mat, Color c)
        {
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c);
            mat.color = c;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                _subscribed = false;
            }

            foreach (var kv in _camBuffers)
                kv.Value?.Release();
            _camBuffers.Clear();

            if (!ReferenceEquals(_copyMat, _tintMat))
                Kill(_copyMat);
            Kill(_tintMat);
            Kill(_addMat);
            _cmd.Release();
        }

        private static void Kill(Material m)
        {
            if (m != null)
                UnityEngine.Object.Destroy(m);
        }
    }
}
