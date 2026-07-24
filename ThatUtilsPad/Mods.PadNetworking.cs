using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using ThatUtilsPad.MenuComponents;
using UnityEngine;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ThatUtilsPad;

public static partial class Mods
{
    public const string PadNetworkPresenceKey = "thatutilspad";
    public const string PadNetworkHandKey = "ThatUtilsPad";
    public const string PadNetworkVersion = "1.0.0";

    private static readonly Dictionary<VRRig, GameObject> remotePads = new Dictionary<VRRig, GameObject>();
    private static string lastBroadcastHandState = "";
    private static string lastBroadcastRoom = "";
    private static float nextRemotePadScanTime;
    private const float RemotePadScanSeconds = 0.2f;

    public static void PadNetworkingLoop()
    {
        try
        {
            if (!PhotonNetwork.InRoom)
            {
                ClearRemotePads();
                lastBroadcastHandState = "";
                lastBroadcastRoom = "";
                return;
            }

            string room = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "";
            if (!string.Equals(room, lastBroadcastRoom, StringComparison.Ordinal))
            {
                lastBroadcastRoom = room;
                lastBroadcastHandState = "";
                BroadcastPadNetworkState(force: true);
            }

            if (Time.time >= nextRemotePadScanTime)
            {
                nextRemotePadScanTime = Time.time + RemotePadScanSeconds;
                SyncRemotePads();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] PadNetworkingLoop: " + ex.Message);
        }
    }

    public static void BroadcastPadNetworkState(bool force = false)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
            return;

        string handState = "0";
        if (Main.Instance != null && Main.Instance.IsHandPadNetworkVisible(out bool leftHand))
            handState = leftHand ? "L" : "R";

        if (!force && string.Equals(handState, lastBroadcastHandState, StringComparison.Ordinal))
            return;

        lastBroadcastHandState = handState;

        try
        {
            Hashtable props = new Hashtable
            {
                { PadNetworkPresenceKey, PadNetworkVersion },
                { PadNetworkHandKey, handState }
            };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] BroadcastPadNetworkState: " + ex.Message);
        }
    }

    public static void ClearPadNetworkState()
    {
        lastBroadcastHandState = "";
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
            return;

        try
        {
            Hashtable props = new Hashtable
            {
                { PadNetworkHandKey, "0" }
            };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
        catch { }
    }

    private static void SyncRemotePads()
    {
        HashSet<VRRig> seen = new HashSet<VRRig>();

        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (rig == null || rig.isLocal)
                continue;

            Player player = null;
            try { player = rig.GetPhotonPlayer(); }
            catch { }

            if (player == null || player.IsLocal)
                continue;

            seen.Add(rig);

            if (!TryGetRemotePadHand(player, out bool leftHand))
            {
                RemoveRemotePad(rig);
                continue;
            }

            EnsureRemotePad(rig, leftHand);
        }

        List<VRRig> stale = null;
        foreach (KeyValuePair<VRRig, GameObject> pair in remotePads)
        {
            if (pair.Key == null || !seen.Contains(pair.Key) || pair.Value == null)
            {
                stale ??= new List<VRRig>();
                stale.Add(pair.Key);
            }
        }

        if (stale != null)
        {
            for (int i = 0; i < stale.Count; i++)
                RemoveRemotePad(stale[i]);
        }
    }

    private static bool TryGetRemotePadHand(Player player, out bool leftHand)
    {
        leftHand = true;
        if (player?.CustomProperties == null)
            return false;

        if (!player.CustomProperties.TryGetValue(PadNetworkHandKey, out object raw) || raw == null)
            return false;

        string value = raw.ToString().Trim().ToUpperInvariant();
        if (value == "L" || value == "LEFT" || value == "1")
        {
            leftHand = true;
            return true;
        }

        if (value == "R" || value == "RIGHT")
        {
            leftHand = false;
            return true;
        }

        return false;
    }

    private static void EnsureRemotePad(VRRig rig, bool leftHand)
    {
        Transform attach = GetRemoteHandAttach(rig, leftHand);
        if (attach == null)
            return;

        if (!remotePads.TryGetValue(rig, out GameObject pad) || pad == null)
        {
            pad = Main.Instance != null ? Main.Instance.CreateNetworkPadVisual() : null;
            if (pad == null)
                return;

            pad.name = "TUP_NetworkPad";
            remotePads[rig] = pad;
        }

        if (pad.transform.parent != attach)
            pad.transform.SetParent(attach, false);

        pad.transform.localPosition = Main.GetNetworkedPadLocalPosition();
        pad.transform.localRotation = Main.GetNetworkedPadLocalRotation();
        pad.transform.localScale = Vector3.one * Main.GetNetworkedPadScale();
        if (!pad.activeSelf)
            pad.SetActive(true);
    }

    private static Transform GetRemoteHandAttach(VRRig rig, bool leftHand)
    {
        if (rig == null)
            return null;

        try
        {
            Transform handRoot = leftHand ? rig.leftHandTransform : rig.rightHandTransform;
            if (handRoot != null && handRoot.parent != null)
            {
                Transform palm = handRoot.parent.Find(leftHand ? "palm.01.L" : "palm.01.R");
                if (palm != null)
                    return palm;
            }

            VRMap map = leftHand ? rig.leftHand : rig.rightHand;
            if (map != null && map.rigTarget != null)
                return map.rigTarget;

            return handRoot;
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveRemotePad(VRRig rig)
    {
        if (rig == null)
        {
            ClearRemotePads();
            return;
        }

        if (remotePads.TryGetValue(rig, out GameObject pad))
        {
            if (pad != null)
                Object.Destroy(pad);
            remotePads.Remove(rig);
        }
    }

    private static void ClearRemotePads()
    {
        foreach (KeyValuePair<VRRig, GameObject> pair in remotePads)
        {
            if (pair.Value != null)
                Object.Destroy(pair.Value);
        }

        remotePads.Clear();
    }
}
