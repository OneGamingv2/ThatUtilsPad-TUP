using System;
using System.Collections;
using System.Collections.Generic;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using Debug = UnityEngine.Debug;
using FriendInfo = PlayFab.ClientModels.FriendInfo;

namespace ThatUtilsPad;

public static partial class Mods
{
    public sealed class PadFriendEntry
    {
        public string PlayFabId;
        public string DisplayName;
        public bool IsOnline;
        public string RoomCode;
        public bool IsInRoom;
    }

    private static readonly List<PadFriendEntry> cachedFriends = new List<PadFriendEntry>();
    private static readonly Dictionary<string, PadFriendEntry> friendsById =
        new Dictionary<string, PadFriendEntry>(StringComparer.OrdinalIgnoreCase);

    private static string selectedFriendId;
    private static bool friendsLoading;
    private static float lastFriendsFetchTime = -999f;
    private static Coroutine friendsPresenceRoutine;

    public static bool HasSelectedFriend() => !string.IsNullOrEmpty(selectedFriendId);

    public static string GetSelectedFriendDisplayName()
    {
        if (string.IsNullOrEmpty(selectedFriendId))
            return "Friend";
        if (friendsById.TryGetValue(selectedFriendId, out PadFriendEntry entry) && entry != null)
            return string.IsNullOrWhiteSpace(entry.DisplayName) ? selectedFriendId : entry.DisplayName;
        return selectedFriendId;
    }

    public static IReadOnlyList<PadFriendEntry> GetCachedFriends() => cachedFriends;

    public static void ClearSelectedFriend()
    {
        selectedFriendId = null;
    }

    public static void RefreshFriendsList()
    {
        if (friendsLoading)
        {
            ShowNotification("Friends still loading...", NotificationDefaultDuration);
            return;
        }

        RequestFriendsList(force: true);
    }

    public static void OpenFriendActions(string playFabId)
    {
        if (string.IsNullOrWhiteSpace(playFabId))
            return;
        selectedFriendId = playFabId.Trim();
        Main.Instance?.RefreshCurrentPage();
    }

    public static void BackToFriendsList()
    {
        selectedFriendId = null;
        Main.Instance?.RefreshCurrentPage();
    }

    public static void JoinSelectedFriendRoom()
    {
        if (!TryGetSelectedFriend(out PadFriendEntry friend))
            return;

        if (!friend.IsOnline || !friend.IsInRoom || string.IsNullOrWhiteSpace(friend.RoomCode))
        {
            ShowNotification(friend.DisplayName + " is not in a joinable room", NotificationDefaultDuration);
            return;
        }

        string code = friend.RoomCode.Trim().ToUpperInvariant();
        ShowNotification("Joining " + friend.DisplayName + " (" + code + ")", NotificationDefaultDuration);
        JoinRoomCode(code);
    }

    public static void InviteSelectedFriendHere()
    {
        if (!TryGetSelectedFriend(out PadFriendEntry friend))
            return;

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            ShowNotification("Join a room first to invite", NotificationDefaultDuration);
            return;
        }

        string code = PhotonNetwork.CurrentRoom.Name ?? "";
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowNotification("Current room has no code", NotificationDefaultDuration);
            return;
        }

        GUIUtility.systemCopyBuffer = code;
        ShowNotification("Room " + code + " copied - send to " + friend.DisplayName, 4.5f);
        ShowNotification("GT has no silent invite API; share the code", 4f);
    }

    public static void MakePrivateCodeForFriend()
    {
        if (!TryGetSelectedFriend(out PadFriendEntry friend))
            return;

        string code = GeneratePrivateRoomCode();
        GUIUtility.systemCopyBuffer = code;
        ShowNotification("Private code " + code + " ready for " + friend.DisplayName, 4.5f);
        JoinRoomCode(code);
    }

    public static void EnsureFriendsWarm()
    {
        if (Time.unscaledTime - lastFriendsFetchTime > 25f)
            RequestFriendsList(force: false);

        if (friendsPresenceRoutine == null && CoroutineHandler.Instance != null)
            friendsPresenceRoutine = CoroutineHandler.Instance.StartCoroutine(FriendsPresenceLoop());
    }

    private static bool TryGetSelectedFriend(out PadFriendEntry friend)
    {
        friend = null;
        if (string.IsNullOrEmpty(selectedFriendId))
        {
            ShowNotification("Pick a friend first", NotificationDefaultDuration);
            return false;
        }

        if (!friendsById.TryGetValue(selectedFriendId, out friend) || friend == null)
        {
            ShowNotification("Friend not found — refresh list", NotificationDefaultDuration);
            return false;
        }

        return true;
    }

    private static void RequestFriendsList(bool force)
    {
        if (friendsLoading)
            return;
        if (!force && Time.unscaledTime - lastFriendsFetchTime < 8f)
            return;

        friendsLoading = true;
        lastFriendsFetchTime = Time.unscaledTime;

        try
        {
            PlayFabClientAPI.GetFriendsList(new GetFriendsListRequest
            {
                IncludeSteamFriends = true,
                IncludeFacebookFriends = false,
                XboxToken = null
            }, OnFriendsListSuccess, OnFriendsListError);
        }
        catch (Exception ex)
        {
            friendsLoading = false;
            Debug.LogWarning("[TUP] Friends fetch failed: " + ex.Message);
            ShowNotification("Friends fetch failed", NotificationDefaultDuration);
        }
    }

    private static void OnFriendsListSuccess(GetFriendsListResult result)
    {
        friendsLoading = false;
        cachedFriends.Clear();
        friendsById.Clear();

        if (result?.Friends != null)
        {
            for (int i = 0; i < result.Friends.Count; i++)
            {
                FriendInfo info = result.Friends[i];
                if (info == null || string.IsNullOrWhiteSpace(info.FriendPlayFabId))
                    continue;

                string name = !string.IsNullOrWhiteSpace(info.TitleDisplayName)
                    ? info.TitleDisplayName
                    : (!string.IsNullOrWhiteSpace(info.Username) ? info.Username : info.FriendPlayFabId);

                var entry = new PadFriendEntry
                {
                    PlayFabId = info.FriendPlayFabId,
                    DisplayName = name,
                    IsOnline = false,
                    IsInRoom = false,
                    RoomCode = ""
                };
                cachedFriends.Add(entry);
                friendsById[entry.PlayFabId] = entry;
            }
        }

        cachedFriends.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        QueryPhotonFriendPresence();
        TryRefreshFriendsPageIfOpen();
    }

    private static void TryRefreshFriendsPageIfOpen()
    {
        try
        {
            Main.Instance?.RefreshFriendsPageIfOpen();
        }
        catch
        {
        }
    }

    private static void OnFriendsListError(PlayFabError error)
    {
        friendsLoading = false;
        string msg = error != null ? error.GenerateErrorReport() : "unknown";
        Debug.LogWarning("[TUP] PlayFab friends error: " + msg);
        ShowNotification("Could not load PlayFab friends", NotificationDefaultDuration);
    }

    private static void QueryPhotonFriendPresence()
    {
        if (cachedFriends.Count == 0)
            return;
        if (PhotonNetwork.NetworkClientState != ClientState.ConnectedToMasterServer &&
            PhotonNetwork.NetworkClientState != ClientState.JoinedLobby)
            return;

        string[] ids = new string[cachedFriends.Count];
        for (int i = 0; i < cachedFriends.Count; i++)
            ids[i] = cachedFriends[i].PlayFabId;

        try
        {
            PhotonNetwork.FindFriends(ids);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] FindFriends failed: " + ex.Message);
        }
    }

    public static void ApplyPhotonFriendInfo(List<Photon.Realtime.FriendInfo> friends)
    {
        if (friends == null)
            return;

        for (int i = 0; i < friends.Count; i++)
        {
            Photon.Realtime.FriendInfo info = friends[i];
            if (info == null || string.IsNullOrEmpty(info.UserId))
                continue;
            if (!friendsById.TryGetValue(info.UserId, out PadFriendEntry entry) || entry == null)
                continue;

            entry.IsOnline = info.IsOnline;
            entry.IsInRoom = info.IsInRoom;
            entry.RoomCode = info.IsInRoom ? (info.Room ?? "") : "";
        }

        TryRefreshFriendsPageIfOpen();
    }

    private static IEnumerator FriendsPresenceLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(12f);
            if (cachedFriends.Count == 0)
                continue;
            QueryPhotonFriendPresence();
        }
    }

    private static string GeneratePrivateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        char[] chars = new char[6];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
        return new string(chars);
    }

    public static string FormatFriendButtonLabel(PadFriendEntry entry)
    {
        if (entry == null)
            return "Friend";

        string status;
        if (!entry.IsOnline)
            status = "Offline";
        else if (entry.IsInRoom && !string.IsNullOrWhiteSpace(entry.RoomCode))
            status = entry.RoomCode;
        else
            status = "Online";

        return entry.DisplayName + "  ·  " + status;
    }
}
