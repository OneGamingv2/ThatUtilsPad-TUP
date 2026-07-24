using System.Collections;
using System.Collections.Generic;
using CameraMod.Camera.Comps;
using CameraMod.Camera.Networking;
using HarmonyLib;
using UnityEngine;

namespace CameraMod.Camera.Patches
{
    [HarmonyPatch(typeof(GorillaTagger), "Start")]
    public class StartPatch
    {
        public static HashSet<string> owners = new HashSet<string>();
        public static string UserID = "";
        private static bool started;

        public static void EnsureStarted()
        {
            if (started || GorillaTagger.Instance == null)
                return;
            GorillaTagger.Instance.StartCoroutine(Main());
        }

        private static IEnumerator Main()
        {
            if (started)
                yield break;
            started = true;

            var mainGO = new GameObject("TUP_CameraMod");
            Object.DontDestroyOnLoad(mainGO);
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
