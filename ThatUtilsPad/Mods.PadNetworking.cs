using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using ThatUtilsPad.MenuComponents;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ThatUtilsPad;

public static partial class Mods
{
    public const string PadNetworkPresenceKey = "thatutilspad";
    public const string PadNetworkHandKey = "ThatUtilsPad";
    public const string PadNetworkGuiKey = "ThatUtilsPadGui";
    // Free aliases so free clients can see paid pads / older free builds stay visible.
    public const string PadNetworkHandKeyFree = "ThatUtilsPadFree";
    public const string PadNetworkGuiKeyFree = "ThatUtilsPadFreeGui";
    public const string PadNetworkVersion = "1.0.0";

    private sealed class RemotePadView
    {
        public GameObject Root = null!;
        public bool LeftHand = true;
        public string GuiState = "";
        public TMP_Text? StatusLabel;
        public readonly List<TMP_Text> RowLabels = new List<TMP_Text>();
    }

    private static readonly Dictionary<VRRig, RemotePadView> remotePads =
        new Dictionary<VRRig, RemotePadView>();

    private static string lastBroadcastHandState = "";
    private static string lastBroadcastGuiState = "";
    private static string lastBroadcastRoom = "";
    private static float nextRemotePadScanTime;
    private const float RemotePadScanSeconds = 0.15f;

    private static readonly Vector3 RemotePadHandLocalPos = new Vector3(0.055f, 0.02f, 0.01f);
    private static readonly Vector3 RemotePadHandLocalEuler = new Vector3(-15f, 195f, 90f);

    public static void PadNetworkingLoop()
    {
        try
        {
            if (!PhotonNetwork.InRoom)
            {
                ClearRemotePads();
                lastBroadcastHandState = "";
                lastBroadcastGuiState = "";
                lastBroadcastRoom = "";
                return;
            }

            string room = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "";
            if (!string.Equals(room, lastBroadcastRoom, StringComparison.Ordinal))
            {
                lastBroadcastRoom = room;
                lastBroadcastHandState = "";
                lastBroadcastGuiState = "";
                BroadcastPadNetworkState(force: true);
            }
            else
            {
                BroadcastPadNetworkState(force: false);
            }

            if (Time.time >= nextRemotePadScanTime)
            {
                nextRemotePadScanTime = Time.time + RemotePadScanSeconds;
                SyncRemotePads();
            }

            UpdateRemotePadPoses();
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

        string guiState = "0";
        if (handState != "0" && Main.Instance != null)
            guiState = Main.Instance.GetPadNetworkGuiState();

        if (!force &&
            string.Equals(handState, lastBroadcastHandState, StringComparison.Ordinal) &&
            string.Equals(guiState, lastBroadcastGuiState, StringComparison.Ordinal))
            return;

        lastBroadcastHandState = handState;
        lastBroadcastGuiState = guiState;

        try
        {
            Hashtable props = new Hashtable
            {
                { PadNetworkPresenceKey, PadNetworkVersion },
                { PadNetworkHandKey, handState },
                { PadNetworkGuiKey, guiState },
                { PadNetworkHandKeyFree, handState },
                { PadNetworkGuiKeyFree, guiState }
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
        lastBroadcastGuiState = "";
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
            return;

        try
        {
            Hashtable props = new Hashtable
            {
                { PadNetworkHandKey, "0" },
                { PadNetworkGuiKey, "0" },
                { PadNetworkHandKeyFree, "0" },
                { PadNetworkGuiKeyFree, "0" }
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

            string guiState = ReadRemoteGuiState(player);
            EnsureRemotePad(rig, leftHand, guiState);
        }

        List<VRRig> stale = null;
        foreach (KeyValuePair<VRRig, RemotePadView> pair in remotePads)
        {
            if (pair.Key == null || !seen.Contains(pair.Key) || pair.Value?.Root == null)
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

    private static string ReadRemoteGuiState(Player player)
    {
        if (player?.CustomProperties == null)
            return "0";

        if (TryReadCustomProp(player, PadNetworkGuiKey, out string shared) && shared != "0")
            return shared;
        if (TryReadCustomProp(player, PadNetworkGuiKeyFree, out string free) && free != "0")
            return free;
        if (TryReadCustomProp(player, PadNetworkGuiKey, out shared))
            return shared;
        if (TryReadCustomProp(player, PadNetworkGuiKeyFree, out free))
            return free;
        return "0";
    }

    private static bool TryGetRemotePadHand(Player player, out bool leftHand)
    {
        leftHand = true;
        if (player?.CustomProperties == null)
            return false;

        if (TryParseHandState(player, PadNetworkHandKey, out leftHand))
            return true;
        if (TryParseHandState(player, PadNetworkHandKeyFree, out leftHand))
            return true;
        return false;
    }

    private static bool TryReadCustomProp(Player player, string key, out string value)
    {
        value = "0";
        if (player?.CustomProperties == null || string.IsNullOrEmpty(key))
            return false;
        if (!player.CustomProperties.TryGetValue(key, out object raw) || raw == null)
            return false;
        string text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text.Trim();
        return true;
    }

    private static bool TryParseHandState(Player player, string key, out bool leftHand)
    {
        leftHand = true;
        if (!TryReadCustomProp(player, key, out string value))
            return false;

        value = value.ToUpperInvariant();
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

    private static void EnsureRemotePad(VRRig rig, bool leftHand, string guiState)
    {
        if (!remotePads.TryGetValue(rig, out RemotePadView view) || view == null || view.Root == null)
        {
            GameObject pad = Main.Instance != null ? Main.Instance.CreateNetworkPadVisual() : null;
            if (pad == null)
                return;

            pad.name = "TUP_NetworkPad";
            view = new RemotePadView
            {
                Root = pad,
                LeftHand = leftHand,
                GuiState = ""
            };
            PrepareRemotePadDisplay(view);
            remotePads[rig] = view;
        }

        view.LeftHand = leftHand;

        if (!string.Equals(view.GuiState, guiState, StringComparison.Ordinal))
        {
            view.GuiState = guiState;
            ApplyRemotePadGui(view, guiState);
        }
    }

    private static void PrepareRemotePadDisplay(RemotePadView view)
    {
        if (view?.Root == null)
            return;

        Transform anchor = view.Root.transform.Find("MainMenu") ?? view.Root.transform;
        GameObject labelGo = new GameObject("TUP_RemoteStatus");
        labelGo.transform.SetParent(anchor, false);
        labelGo.transform.localPosition = new Vector3(0f, 0.02f, -0.01f);
        labelGo.transform.localRotation = Quaternion.identity;
        labelGo.transform.localScale = Vector3.one * 0.012f;

        TextMeshPro tmp = labelGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 6f;
        tmp.enableWordWrapping = false;
        tmp.richText = true;
        tmp.text = "";
        tmp.color = Color.white;
        view.StatusLabel = tmp;

        for (int i = 0; i < 7; i++)
        {
            GameObject rowGo = new GameObject("TUP_RemoteRow_" + i);
            rowGo.transform.SetParent(anchor, false);
            rowGo.transform.localPosition = new Vector3(0f, -0.01f - (i * 0.018f), -0.01f);
            rowGo.transform.localRotation = Quaternion.identity;
            rowGo.transform.localScale = Vector3.one * 0.01f;

            TextMeshPro row = rowGo.AddComponent<TextMeshPro>();
            row.alignment = TextAlignmentOptions.Left;
            row.fontSize = 4.5f;
            row.enableWordWrapping = false;
            row.richText = true;
            row.text = "";
            row.color = new Color(0.9f, 0.9f, 0.95f, 1f);
            view.RowLabels.Add(row);
        }

        ImproveRemotePadRendering(view.Root);
    }

    private static void ImproveRemotePadRendering(GameObject root)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material[] mats = renderer.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                if (ShaderCache.UberShader != null && mat.shader != ShaderCache.UberShader)
                    mat.shader = ShaderCache.UberShader;
            }
        }

        Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null)
                canvases[i].enabled = true;
        }

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
            {
                graphics[i].enabled = true;
                graphics[i].raycastTarget = false;
            }
        }

        Transform side = root.transform.Find("SideHolder");
        if (side != null)
            side.gameObject.SetActive(false);

        Transform keyboard = root.transform.Find("Keyboard");
        if (keyboard != null)
            keyboard.gameObject.SetActive(false);
    }

    private static void ApplyRemotePadGui(RemotePadView view, string guiState)
    {
        if (view == null)
            return;

        if (string.IsNullOrWhiteSpace(guiState) || guiState == "0")
        {
            if (view.StatusLabel != null)
                view.StatusLabel.text = "";
            for (int i = 0; i < view.RowLabels.Count; i++)
            {
                if (view.RowLabels[i] != null)
                    view.RowLabels[i].text = "";
            }
            return;
        }

        string category = "Networking";
        int page = 0;
        int split = guiState.LastIndexOf(':');
        if (split > 0)
        {
            category = guiState.Substring(0, split);
            int.TryParse(guiState.Substring(split + 1), out page);
        }
        else
        {
            category = guiState;
        }

        page = Mathf.Max(0, page);
        if (view.StatusLabel != null)
            view.StatusLabel.text = "<color=#CBA6F7>" + category + "</color>  <color=#BDBDBD>p" + (page + 1) + "</color>";

        List<string> labels = Main.GetNetworkPadPageLabels(category, page);
        for (int i = 0; i < view.RowLabels.Count; i++)
        {
            TMP_Text row = view.RowLabels[i];
            if (row == null)
                continue;

            if (i < labels.Count)
            {
                row.gameObject.SetActive(true);
                row.text = "• " + labels[i];
            }
            else
            {
                row.text = "";
                row.gameObject.SetActive(false);
            }
        }
    }

    private static void UpdateRemotePadPoses()
    {
        float scale = Main.GetNetworkedPadScale();
        foreach (KeyValuePair<VRRig, RemotePadView> pair in remotePads)
        {
            VRRig rig = pair.Key;
            RemotePadView view = pair.Value;
            if (rig == null || view?.Root == null)
                continue;

            Transform hand = GetRemoteHandAttach(rig, view.LeftHand);
            if (hand == null)
                continue;

            Vector3 pos = hand.TransformPoint(RemotePadHandLocalPos);
            Quaternion rot = hand.rotation * Quaternion.Euler(RemotePadHandLocalEuler);
            view.Root.transform.SetPositionAndRotation(pos, rot);
            view.Root.transform.localScale = Vector3.one * scale;
        }
    }

    private static Transform GetRemoteHandAttach(VRRig rig, bool leftHand)
    {
        if (rig == null)
            return null;

        try
        {
            VRMap map = leftHand ? rig.leftHand : rig.rightHand;
            if (map != null && map.rigTarget != null)
                return map.rigTarget;

            Transform handRoot = leftHand ? rig.leftHandTransform : rig.rightHandTransform;
            if (handRoot != null)
                return handRoot;
        }
        catch { }

        return null;
    }

    private static void RemoveRemotePad(VRRig rig)
    {
        if (rig == null)
        {
            ClearRemotePads();
            return;
        }

        if (remotePads.TryGetValue(rig, out RemotePadView view))
        {
            if (view?.Root != null)
                Object.Destroy(view.Root);
            remotePads.Remove(rig);
        }
    }

    private static void ClearRemotePads()
    {
        foreach (KeyValuePair<VRRig, RemotePadView> pair in remotePads)
        {
            if (pair.Value?.Root != null)
                Object.Destroy(pair.Value.Root);
        }

        remotePads.Clear();
    }
}
