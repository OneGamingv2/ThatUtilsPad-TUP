using System;
using System.Collections;
using System.Collections.Generic;
using GorillaGameModes;
using GorillaNetworking;
using Photon.Pun;
using UnityEngine;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static readonly string[] regionCodes = { "eu", "us", "usw" };
    private static readonly string[] regionNames = { "EU", "NA East", "NA West" };
    private static readonly string[] lobbyZoneCodes = { "forest", "canyon", "city", "beach", "mountain" };
    private static readonly string[] lobbyZoneNames = { "Forest", "Canyon", "City", "Beach", "Mountain" };

    private static int regionIndex = -1;
    private static int lobbyZoneIndex;
    private static Coroutine? regionCoroutine;
    private static Coroutine? regionStatsCoroutine;

    private static bool isRoomCodeSearch;
    private static string roomCodeDraft = "";
    private static bool regionPlayersAutoEnabled;
    private static Coroutine? regionPlayersAutoCoroutine;

    private static void JoinRandom()
    {
        if (queueCoroutine != null || CoroutineHandler.Instance == null)
            return;

        queueCoroutine = CoroutineHandler.Instance.StartCoroutine(LeaveThenJoinPublic());
    }

    private static void LobbyHop()
    {
        JoinRandom();
    }

    private static void Disconnect()
    {
        if (NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom)
            NetworkSystem.Instance.ReturnToSinglePlayer();
    }

    private static IEnumerator LeaveThenJoinPublic()
    {
        try
        {
            if (PhotonNetwork.InRoom || (NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom))
            {
                if (NetworkSystem.Instance != null)
                    NetworkSystem.Instance.ReturnToSinglePlayer();

                float leaveTimeout = Time.time + 2.5f;
                while ((PhotonNetwork.InRoom || (NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom))
                       && Time.time < leaveTimeout)
                    yield return null;

                yield return null;
                yield return null;
            }

            AttemptJoinSelectedPublic();
        }
        finally
        {
            queueCoroutine = null;
        }
    }

    private static void AttemptJoinSelectedPublic()
    {
        if (PhotonNetworkController.Instance == null || GorillaComputer.instance == null)
            return;

        GorillaNetworkJoinTrigger? currentTrigger = PhotonNetworkController.Instance.currentJoinTrigger;
        string zone = lobbyZoneCodes[Mathf.Clamp(lobbyZoneIndex, 0, lobbyZoneCodes.Length - 1)];

        GorillaNetworkJoinTrigger trigger =
            currentTrigger == null || currentTrigger.name.ToLowerInvariant().Contains("private")
                ? GorillaComputer.instance.GetJoinTriggerForZone(zone) ?? GorillaComputer.instance.GetJoinTriggerForZone("forest")
                : currentTrigger;

        if (trigger != null)
            PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger);
    }

    private static void CycleLobbyMap()
    {
        lobbyZoneIndex = (lobbyZoneIndex + 1) % lobbyZoneCodes.Length;
        if (Main.lobbyMapText != null)
            Main.lobbyMapText.text = "Lobby Map  :  " + GetLobbyMapLabel();
        ShowNotification("Lobby map: " + GetLobbyMapLabel(), 1.5f);
    }

    public static string GetLobbyMapLabel()
        => lobbyZoneNames[Mathf.Clamp(lobbyZoneIndex, 0, lobbyZoneNames.Length - 1)];

    public static void StartRoomCodeSearch()
    {
        if (Main.Instance == null || Main.menuObj == null)
            return;

        GameObject keyboard = Main.menuObj.transform.Find("Keyboard")?.gameObject;
        if (keyboard == null)
        {
            ShowNotification("Keyboard missing", NotificationDefaultDuration);
            return;
        }

        isRoomCodeSearch = true;
        isRenaming = true;
        renamingOutfitN = -1;
        renamingLabel = Main.roomCodeSearchText;
        renameKeyboard = keyboard;
        roomCodeDraft = "";
        currentRenameText = "";
        originalLabel = "Search Room";

        if (renamingLabel != null)
            renamingLabel.text = "Code  :  _";

        keyboard.SetActive(true);
        Main.ShowKeyboard(keyboard);
        Main.Instance.PutMenuInTypingSpot();
        Main.Instance.SetKeyboardPreview(keyboard, "", false);
        ShowNotification("Type a room code, then Enter", 2f);
    }

    private static void ConfirmRoomCodeJoin()
    {
        string code = (currentRenameText ?? roomCodeDraft ?? "").Trim().ToUpperInvariant();
        CloseRoomCodeSearchUi();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowNotification("No room code entered", NotificationDefaultDuration);
            return;
        }

        if (queueCoroutine != null || CoroutineHandler.Instance == null)
            return;

        queueCoroutine = CoroutineHandler.Instance.StartCoroutine(LeaveThenJoinRoomCode(code));
    }

    public static void JoinRoomCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;
        CoroutineHandler.Instance?.StartCoroutine(LeaveThenJoinRoomCode(code.Trim().ToUpperInvariant()));
    }

    private static IEnumerator LeaveThenJoinRoomCode(string code)
    {
        try
        {
            if (PhotonNetwork.InRoom || (NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom))
            {
                if (NetworkSystem.Instance != null)
                    NetworkSystem.Instance.ReturnToSinglePlayer();

                float leaveTimeout = Time.time + 2.5f;
                while ((PhotonNetwork.InRoom || (NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom))
                       && Time.time < leaveTimeout)
                    yield return null;
                yield return null;
            }

            if (PhotonNetworkController.Instance != null)
            {
                PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(code, JoinType.Solo);
                ShowNotification("Joining " + code + "...", 2f);
            }
        }
        finally
        {
            queueCoroutine = null;
        }
    }

    private static void CloseRoomCodeSearchUi()
    {
        isRoomCodeSearch = false;
        isRenaming = false;
        renamingLabel = null;
        if (renameKeyboard != null)
            Main.HideKeyboard(renameKeyboard);
        renameKeyboard = null;
        if (Main.Instance != null)
            Main.Instance.RestoreMenuFollowAfterKeyboard();
        if (Main.roomCodeSearchText != null)
            Main.roomCodeSearchText.text = "Search Room";
    }

    public static void CloseRoomCodeSearchUiPublic() => CloseRoomCodeSearchUi();

    public static bool HandleTypedInput(string key)
    {
        if (!isRenaming)
            return false;

        if (isRoomCodeSearch)
        {
            switch (key)
            {
                case "Enter":
                    ConfirmRoomCodeJoin();
                    return true;
                case "Delete":
                    if (currentRenameText.Length > 0)
                        currentRenameText = currentRenameText.Substring(0, currentRenameText.Length - 1);
                    RefreshRoomCodePreview();
                    return true;
                case "Space":
                    return true;
                case "Close":
                    CloseRoomCodeSearchUi();
                    return true;
                default:
                    if (key.Length == 1 && currentRenameText.Length < 10)
                    {
                        char c = char.ToUpperInvariant(key[0]);
                        if (char.IsLetterOrDigit(c))
                        {
                            currentRenameText += c;
                            RefreshRoomCodePreview();
                        }
                    }
                    return true;
            }
        }

        return false;
    }

    private static void RefreshRoomCodePreview()
    {
        roomCodeDraft = currentRenameText;
        string shown = string.IsNullOrEmpty(currentRenameText) ? "_" : currentRenameText;
        if (renamingLabel != null)
            renamingLabel.text = "Code  :  " + shown;
        if (Main.Instance != null && renameKeyboard != null)
            Main.Instance.SetKeyboardPreview(renameKeyboard, currentRenameText, true);
    }

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
        string name = regionNames[regionIndex];
        if (!TryGetConnectedRegionPlayerCount(out int players, out int inRooms, out _))
            return name;

        if (players <= 0)
            return name + " · …";

        return name + " · " + players.ToString("N0") + " online";
    }

    private static bool TryGetConnectedRegionPlayerCount(out int players, out int inRooms, out int rooms)
    {
        players = 0;
        inRooms = 0;
        rooms = 0;

        if (!PhotonNetwork.IsConnected)
            return false;

        string connected = PhotonNetwork.CloudRegion?.Split('/')[0].Trim().ToLowerInvariant();
        PatchRegionIndex();
        if (!string.IsNullOrEmpty(connected) && connected != regionCodes[regionIndex])
            return false;

        players = PhotonNetwork.CountOfPlayers;
        inRooms = PhotonNetwork.CountOfPlayersInRooms;
        rooms = PhotonNetwork.CountOfRooms;
        return true;
    }

    public static void UpdateRegionLabel()
    {
        if (Main.regionText != null)
            Main.regionText.text = "Region  :  " + GetRegionLabel();
    }

    public static void StartRegionStatsRefresh()
    {
        if (regionStatsCoroutine != null || CoroutineHandler.Instance == null)
            return;

        regionStatsCoroutine = CoroutineHandler.Instance.StartCoroutine(RefreshRegionStatsLoop());
    }

    private static IEnumerator RefreshRegionStatsLoop()
    {
        while (true)
        {
            UpdateRegionLabel();
            yield return new WaitForSeconds(8f);
        }
    }

    private static void ShowRegionPlayers()
    {
        PatchRegionIndex();
        string name = regionNames[regionIndex];

        if (!TryGetConnectedRegionPlayerCount(out int players, out int inRooms, out int rooms))
        {
            ShowNotification(name + " - connect to see region players", NotificationDefaultDuration);
            return;
        }

        int onMaster = Mathf.Max(0, players - inRooms);
        ShowNotification(
            name + " - " + players.ToString("N0") + " online · " + inRooms.ToString("N0") + " in rooms · " +
            onMaster.ToString("N0") + " in lobby · " + rooms.ToString("N0") + " rooms",
            NotificationDefaultDuration);
    }

    private static void ToggleRegionPlayersAuto()
    {
        regionPlayersAutoEnabled = GetSavedToggle("Region Players", regionPlayersAutoEnabled);

        if (regionPlayersAutoEnabled)
        {
            Main.Instance?.OpenNetworkingActionPage("Region Players");
            ShowRegionPlayers();
            StartRegionPlayersAutoLoop();
        }
        else
        {
            StopRegionPlayersAutoLoop();
            ShowNotification("Region Players off", 1.2f);
        }
    }

    private static void StartRegionPlayersAutoLoop()
    {
        StopRegionPlayersAutoLoop();
        if (CoroutineHandler.Instance == null)
            return;
        regionPlayersAutoCoroutine = CoroutineHandler.Instance.StartCoroutine(RegionPlayersAutoLoop());
    }

    private static void StopRegionPlayersAutoLoop()
    {
        if (regionPlayersAutoCoroutine != null && CoroutineHandler.Instance != null)
            CoroutineHandler.Instance.StopCoroutine(regionPlayersAutoCoroutine);
        regionPlayersAutoCoroutine = null;
    }

    private static IEnumerator RegionPlayersAutoLoop()
    {
        while (regionPlayersAutoEnabled)
        {
            ShowRegionPlayers();
            yield return new WaitForSeconds(8f);
        }
        regionPlayersAutoCoroutine = null;
    }

    private static void CycleRegion()
    {
        if (regionCoroutine != null)
            return;

        PatchRegionIndex();
        regionIndex = (regionIndex + 1) % regionCodes.Length;
        UpdateRegionLabel();
        regionCoroutine = CoroutineHandler.Instance.StartCoroutine(ChangeRegion(regionCodes[regionIndex]));
    }

    private static IEnumerator ChangeRegion(string regionCode)
    {
        try
        {
            if (PhotonNetwork.InRoom)
            {
                NetworkSystem.Instance.ReturnToSinglePlayer();
                float leaveTimeout = Time.time + 3f;
                while (PhotonNetwork.InRoom && Time.time < leaveTimeout)
                    yield return null;
            }

            if (PhotonNetwork.IsConnected)
            {
                PhotonNetwork.Disconnect();
                float disconnectTimeout = Time.time + 6f;
                while (PhotonNetwork.IsConnected && Time.time < disconnectTimeout)
                    yield return null;
            }

            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = regionCode;

            if (!PhotonNetwork.IsConnected)
                PhotonNetwork.ConnectUsingSettings();

            float connectTimeout = Time.time + 12f;
            while (!PhotonNetwork.IsConnected && Time.time < connectTimeout)
                yield return null;

            UpdateRegionLabel();
            ShowRegionPlayers();
        }
        finally
        {
            regionCoroutine = null;
        }
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
        if (gorillaComputer == null)
            return;

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
        if (gorillaComputer == null)
            return;

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
}
