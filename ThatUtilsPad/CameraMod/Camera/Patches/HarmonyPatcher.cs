using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CameraMod.Camera.Patches
{
    public class HarmonyPatcher : MonoBehaviour
    {
        public static Harmony instance;
        public static bool IsPatched { get; private set; }

        internal static void ApplyHarmonyPatches()
        {
            if (IsPatched)
                return;

            if (instance == null)
                instance = new Harmony("that.utils.pad.cameramod");
            instance.PatchAll(typeof(CameraController).Assembly);
            IsPatched = true;
        }

        internal static void RemoveHarmonyPatches()
        {
            if (instance != null && IsPatched)
            {
                instance.UnpatchSelf();
                IsPatched = false;
            }
        }
    }
}
