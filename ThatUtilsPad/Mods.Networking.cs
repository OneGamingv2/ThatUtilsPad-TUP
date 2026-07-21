using System;
using System.Collections;
using GorillaLocomotion;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GorillaGameModes;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PlayFab;
using PlayFab.ClientModels;
using Photon.Voice.Unity;
using TMPro;
using ThatUtilsPad.MenuComponents;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public static partial class Mods
{
    private static void JoinRandom()
    {
        if (PhotonNetwork.InRoom)
        {
            queueCoroutine ??= CoroutineHandler.Instance.StartCoroutine(JoinRandomDelay());
            NetworkSystem.Instance.ReturnToSinglePlayer();
            return;
        }

        if (PhotonNetworkController.Instance == null)
            return;

        GorillaNetworkJoinTrigger? currentTrigger = PhotonNetworkController.Instance.currentJoinTrigger;

        GorillaNetworkJoinTrigger trigger =
                currentTrigger == null || currentTrigger.name.ToLower().Contains("private")
                        ? GorillaComputer.instance.GetJoinTriggerForZone("forest")
                        : currentTrigger;

        PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
    }

    private static void LobbyHop()
    {
        queueCoroutine ??= CoroutineHandler.Instance.StartCoroutine(JoinRandomDelay());
        NetworkSystem.Instance.ReturnToSinglePlayer();

        if (PhotonNetworkController.Instance == null)
            return;

        GorillaNetworkJoinTrigger? currentTrigger = PhotonNetworkController.Instance.currentJoinTrigger;

        GorillaNetworkJoinTrigger trigger =
                currentTrigger == null || currentTrigger.name.ToLower().Contains("private")
                        ? GorillaComputer.instance.GetJoinTriggerForZone("forest")
                        : currentTrigger;

        PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
    }

    private static void Disconnect()
    {
        if (NetworkSystem.Instance.InRoom)
            NetworkSystem.Instance.ReturnToSinglePlayer();
    }

    private static readonly string[] regionCodes = { "eu", "us", "usw" };
    private static readonly string[] regionNames = { "EU", "NA East", "NA West" };
    private static int regionIndex = -1;
    private static Coroutine? regionCoroutine;

    private static void PatchRegionIndex()
    {
        if (regionIndex >= 0)
            return;

        string currentRegion = PhotonNetwork.CloudRegion;
        if (string.IsNullOrWhiteSpace(currentRegion))
            currentRegion = PhotonNetwork.PhotonServerSettings?.AppSettings?.FixedRegion;

        currentRegion = currentRegion?.Split('/')[0].Trim().ToLowerInvariant();
        regionIndex = Array.FindIndex(regionCodes, code => code == currentRegion);
        if (regionIndex < 0)
            regionIndex = 0;
    }

    public static string GetRegionLabel()
    {
        PatchRegionIndex();
        return regionNames[regionIndex];
    }

    private static void CycleRegion()
    {
        if (regionCoroutine != null)
            return;

        PatchRegionIndex();
        regionIndex = (regionIndex + 1) % regionCodes.Length;

        if (Main.regionText != null)
            Main.regionText.text = "Region  :  " + regionNames[regionIndex];

        regionCoroutine = CoroutineHandler.Instance.StartCoroutine(ChangeRegion(regionCodes[regionIndex]));
    }

    private static IEnumerator ChangeRegion(string regionCode)
    {

        if (PhotonNetwork.InRoom)
        {
            NetworkSystem.Instance.ReturnToSinglePlayer();
            float leaveTimeout = Time.time + 5f;
            while (PhotonNetwork.InRoom && Time.time < leaveTimeout)
                yield return null;
        }

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            float disconnectTimeout = Time.time + 10f;
            while (PhotonNetwork.IsConnected && Time.time < disconnectTimeout)
                yield return null;
        }

        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = regionCode;

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();

        regionCoroutine = null;
    }
    
    
    
    
    public static List<string> queues = new List<string>
    {
        "CASUAL",
        "COMPETITIVE",
        "MINIGAMES"
    };
    public static int currentIndex = 0;
    private static void ToggleQueue()
    {
        GorillaComputer gorillaComputer = GorillaComputer.instance;
        
        gorillaComputer.currentQueue = queues[currentIndex];
        currentIndex = (currentIndex + 1) % queues.Count;
        
        if (Main.queueText != null)
            Main.queueText.text = "Queue  :  " + FormatCycleLabel(gorillaComputer.currentQueue);
        
    }
    
    
    
    
    public static List<string> modes = new List<string>
    {
        "Casual",
        "Infection",
        "Paintbrawl",
        "Guardian"
    };

    private static WatchableStringSO watchableStringSo = ScriptableObject.CreateInstance<WatchableStringSO>();
    public static int currentModeIndex = 0;
    private static void ToggleMode()
    {
        GorillaComputer gorillaComputer = GorillaComputer.instance;
        watchableStringSo.Value = modes[currentModeIndex];
        gorillaComputer.currentGameMode = watchableStringSo;
        gorillaComputer.lastPressedGameMode = watchableStringSo.ToString();
        gorillaComputer.lastPressedGameModeType =
            (GameModeType)Enum.Parse(typeof(GameModeType), watchableStringSo.Value, true);
        
        if (Main.modeText != null)
            Main.modeText.text = "Mode  :  " + FormatCycleLabel(watchableStringSo.Value);
        
        currentModeIndex = (currentModeIndex + 1) % modes.Count;
    }

    
    
    private static string FormatCycleLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";
        value = value.Trim();
        return value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }

    private static IEnumerator JoinRandomDelay()
    {
        yield return new WaitForSeconds(1.5f);
        queueCoroutine = null;
        JoinRandom();
    }
}
