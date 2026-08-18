using System;
using UnityEngine;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Auto-repairs GT Shoulder / PC monitor framing when TUPshaders boots or
    /// applies looks. Camera hijacks from the camera tablet used to fight Cinemachine
    /// and leave a "super zoomed" PC overview — shaders trigger this restore so users
    /// don't have to hunt Tab menus.
    /// </summary>
    public static class PcMonitorAutoFix
    {
        private static float _nextPeriodic;
        private static int _bootFixesLeft = 8;

        public static void Request(string reason = null)
        {
            try
            {
                var cam = CameraMod.Camera.CameraController.Instance;
                if (cam == null)
                    return;

                cam.ForceFixZoomedPcView();
                if (!string.IsNullOrEmpty(reason))
                    Plugin.Log.LogInfo("PC monitor auto-reset (" + reason + ")");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PC monitor auto-reset failed: " + ex.Message);
            }
        }

        /// <summary>Called from FrameworkRunner LateUpdate — burst after boot, then light watch.</summary>
        public static void TickPeriodic()
        {
            if (Time.unscaledTime < _nextPeriodic)
                return;

            // Burst: every 2s for first ~16s after shaders load, then every 20s as a safety net.
            if (_bootFixesLeft > 0)
            {
                _bootFixesLeft--;
                _nextPeriodic = Time.unscaledTime + 2f;
                if (IsMonitorBroken())
                    Request(_bootFixesLeft == 7 ? "shaders boot" : null);
                return;
            }

            _nextPeriodic = Time.unscaledTime + 20f;
            if (IsMonitorBroken())
                Request("monitor watchdog");
        }

        /// <summary>
        /// True only for the actual bug: on desktop (no HMD), the GT Shoulder Camera
        /// is enabled while TUP is not driving the monitor, so it overdraws Main
        /// Camera and jams the monitor into a zoomed close-up. Never fires while a
        /// TUP camera mode is live, so it can't stomp intentional use.
        /// </summary>
        private static bool IsMonitorBroken()
        {
            try
            {
                var cam = CameraMod.Camera.CameraController.Instance;
                if (cam == null || cam.DrivePcMonitor || cam.IsLivePreviewMode())
                    return false;
                if (!CameraMod.Camera.CameraController.IsDesktopNoHmd())
                    return false;
                var shoulder = cam.thirdPersonCamera;
                return shoulder != null && shoulder.enabled;
            }
            catch { return false; }
        }
    }
}
