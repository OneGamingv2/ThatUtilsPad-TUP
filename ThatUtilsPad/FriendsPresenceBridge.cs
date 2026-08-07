using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ThatUtilsPad;

public sealed class FriendsPresenceBridge : MonoBehaviour, IMatchmakingCallbacks
{
    private static FriendsPresenceBridge instance;

    public static void Ensure()
    {
        if (instance != null)
            return;

        GameObject go = new GameObject("TUP_FriendsPresence");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<FriendsPresenceBridge>();
    }

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    public void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        Mods.ApplyPhotonFriendInfo(friendList);
    }

    public void OnCreatedRoom() { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinedRoom() { }
    public void OnJoinRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }
    public void OnLeftRoom() { }
    public void OnPreLeavingRoom() { }
}
