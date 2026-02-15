using GorillaNetworking;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System.Collections;

namespace ThatUtilsPad
{
    public static class Mods
    {
        public static Coroutine queueCoroutine;
        public static int reconnectDelay = 1;

        public static Dictionary<string, Action> Actions;

        public static void Init()
        {
            Actions = new Dictionary<string, Action>
            {
                { "Disconnect", Disconnect },
                { "Join Random", JoinRandom },
                { "Lobby Hop", LobbyHop }
            };
        }

        // Mod functions (place them here jelly as public static void)
        // then call them in the dictionary above, where the string is the BtnIdentifier (name as made in function) and thingy on right is the function anme in here
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
