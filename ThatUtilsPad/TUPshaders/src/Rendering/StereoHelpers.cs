using UnityEngine;
using UnityEngine.Rendering;

namespace TUPshaders.Rendering
{
    /// <summary>
    /// Helpers for Single-Pass Stereo / Multi-Pass VR rendering paths.
    /// </summary>
    public static class StereoHelpers
    {
        public static bool IsStereo(Camera cam)
        {
            if (cam == null) return false;
            return cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.None;
        }

        public static void SetStereoKeywords(Material mat, bool stereo)
        {
            if (mat == null) return;
            if (stereo) mat.EnableKeyword("TUP_STEREO");
            else mat.DisableKeyword("TUP_STEREO");
        }

        /// <summary>
        /// Half-res safe dimensions that stay even for stereo packing.
        /// </summary>
        public static void HalfSize(int w, int h, int divisor, out int ow, out int oh)
        {
            divisor = Mathf.Max(1, divisor);
            ow = Mathf.Max(2, (w / divisor) & ~1);
            oh = Mathf.Max(2, (h / divisor) & ~1);
        }
    }
}
