using GorillaNetworking;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GorillaLocomotion;

namespace ThatUtilsPad
{
    public static class Mods
    {
        public static Coroutine queueCoroutine;
        public static int reconnectDelay = 1;

        public static Dictionary<string, Action> Actions;

        public static bool checkerEnabled = false;
        private static GameObject checkerLine;
        private static VRRig lastTargetRig;

        public static void Init()
        {
            Actions = new Dictionary<string, Action>
            {
                { "Disconnect", Disconnect },
                { "Join Random", JoinRandom },
                { "Lobby Hop", LobbyHop },
                { "Toggle Checker", ToggleChecker },
                { "Copy Room Code", CopyRoomCode }
            };
        }

        // Mod functions (place them here jelly as public static void)
        // then call them in the dictionary above, where the string is the BtnIdentifier (name as made in function) and thingy on right is the function anme in here

        public static void ToggleChecker()
        {
            checkerEnabled = !checkerEnabled;
            Debug.Log($"[TUP] Checker: {(checkerEnabled ? "ON" : "OFF")}");

            if (!checkerEnabled && checkerLine != null)
            {
                UnityEngine.Object.Destroy(checkerLine);
                checkerLine = null;
                if (lastTargetRig != null)
                {
                    ResetRigMaterial(lastTargetRig);
                    lastTargetRig = null;
                }
            }
        }

        public static void CopyRoomCode()
        {
            if (PhotonNetwork.InRoom)
            {
                string roomCode = PhotonNetwork.CurrentRoom.Name;
                Debug.Log($"[TUP] Room Code: {roomCode}");
            }
            else
            {
                Debug.Log("[TUP] Not in a room");
            }
        }

        public static void UpdateChecker()
        {
            if (!checkerEnabled) return;

            Transform rightHand = GorillaTagger.Instance.rightHandTransform;
            Vector3 startPos = rightHand.position;
            Vector3 forward = rightHand.forward;

            if (checkerLine == null)
                checkerLine = new GameObject("CheckerLine");

            LineRenderer line = checkerLine.GetComponent<LineRenderer>();
            if (line == null)
            {
                line = checkerLine.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("GUI/Text Shader"));
                line.startWidth = 0.01f;
                line.endWidth = 0.01f;
                line.positionCount = 2;
                line.startColor = Color.cyan;
                line.endColor = Color.cyan;
            }

            Physics.Raycast(startPos, forward, out RaycastHit hit, 100f);
            Vector3 endPos = hit.point != Vector3.zero ? hit.point : startPos + forward * 100f;

            line.SetPosition(0, startPos);
            line.SetPosition(1, endPos);

            VRRig targetRig = hit.collider?.GetComponentInParent<VRRig>();

            if (targetRig != null && !targetRig.isOfflineVRRig)
            {
                if (lastTargetRig != null && lastTargetRig != targetRig)
                {
                    ResetRigMaterial(lastTargetRig);
                }

                if (lastTargetRig != targetRig)
                {
                    HighlightRig(targetRig);
                    ShowPlayerInfo(targetRig);
                    lastTargetRig = targetRig;
                }
            }
            else if (lastTargetRig != null)
            {
                ResetRigMaterial(lastTargetRig);
                lastTargetRig = null;
            }
        }

        private static void HighlightRig(VRRig rig)
        {
            var renderer = rig.mainSkin;
            if (renderer != null)
            {
                renderer.material.shader = Shader.Find("GUI/Text Shader");
                renderer.material.color = Color.cyan;
            }
        }

        private static void ResetRigMaterial(VRRig rig)
        {
            var renderer = rig.mainSkin;
            if (renderer != null)
            {
                renderer.material.shader = Shader.Find("GorillaTag/UberShader");
                if (renderer.material.name.Contains("gorilla_body"))
                    renderer.material.color = rig.playerColor;
            }
        }

        private static void ShowPlayerInfo(VRRig rig)
        {
            string name = rig.playerNameVisible;
            Color color = rig.playerColor;
            string colorStr = $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";

            Debug.Log($"[TUP CHECKER]\nName: {name}\nColor: {colorStr}");
        }

        public static void JoinRandom()
        {
            if (PhotonNetwork.InRoom)
            {
                if (queueCoroutine == null)
                    queueCoroutine = CoroutineHandler.Instance.StartCoroutine(JoinRandomDelay());

                NetworkSystem.Instance.ReturnToSinglePlayer();
                return;
            }

            if (PhotonNetworkController.Instance == null)
                return;

            GorillaNetworkJoinTrigger trigger = PhotonNetworkController.Instance.currentJoinTrigger ?? GorillaComputer.instance.GetJoinTriggerForZone("forest"); // <-- change to the current map you are in later
            PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
        }

        public static void LobbyHop()
        {
            if (queueCoroutine == null)
                queueCoroutine = CoroutineHandler.Instance.StartCoroutine(JoinRandomDelay());

            NetworkSystem.Instance.ReturnToSinglePlayer();

            if (PhotonNetworkController.Instance == null)
                return;

            GorillaNetworkJoinTrigger trigger = PhotonNetworkController.Instance.currentJoinTrigger ?? GorillaComputer.instance.GetJoinTriggerForZone("forest"); // <-- change to the current map you are in later
            PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
        }

        public static void Disconnect()
        {
            Debug.Log("[TUP] Attempting to Leave Current Room");
            if (PhotonNetwork.InRoom)
            {
                NetworkSystem.Instance.ReturnToSinglePlayer();
            }
        }

        public static IEnumerator JoinRandomDelay()
        {
            yield return new WaitForSeconds(1.5f);

            queueCoroutine = null;
            JoinRandom();
        }
    }

    public class CoroutineHandler : MonoBehaviour
    {
        public static CoroutineHandler Instance;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

}