using System.Collections;
using CameraMod.Camera.Comps;
using CameraMod.Camera.Networking;
using HarmonyLib;
using UnityEngine;
using TupMain = ThatUtilsPad.Main;

namespace CameraMod.Camera.Patches
{
    [HarmonyPatch(typeof(GorillaTagger), "Start")]
    public class StartPatch
    {
        public static string UserID = "";
        private static bool started;

        public static void EnsureStarted()
        {
            if (started)
                return;

            if (GorillaTagger.Instance != null)
            {
                GorillaTagger.Instance.StartCoroutine(Boot());
                return;
            }

            if (TupMain.Instance != null)
                TupMain.Instance.StartCoroutine(Boot());
        }

        private static IEnumerator Boot()
        {
            if (started)
                yield break;
            started = true;

            while (GorillaTagger.Instance == null)
                yield return null;

            var mainGO = new GameObject("TUP_CameraMod");
            UnityEngine.Object.DontDestroyOnLoad(mainGO);
            mainGO.AddComponent<InputManager>();

            var ui = mainGO.AddComponent<UI>();
            ui.watermarkEnabled = false;

            mainGO.AddComponent<CameraController>();
            CameraController.Instance.Init();
            ReplicationHandler.Initialize();
        }

        public static void Postfix()
        {
            EnsureStarted();
        }
    }
}
