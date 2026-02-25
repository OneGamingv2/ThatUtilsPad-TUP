using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GorillaLocomotion;
using GorillaNetworking;
using Photon.Pun;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ThatUtilsPad;

public static class Mods
{
    private static Coroutine? queueCoroutine;
    public static  int        ReconnectDelay = 1;

    public static Dictionary<string, Dictionary<string, Action>> Actions;

    private static bool        checkerEnabled;
    private static GameObject? checkerLine;
    private static VRRig?      lastTargetRig;
    private static bool        isMutingAll;
    
    public static bool TryGetAction(string identifier, out Action? action)
    {
        foreach (var category in Actions.Values)
        {
            if (category.TryGetValue(identifier, out action))
                return true;
        }

        action = null;
        return false;
    }

    public static void Init()
    {
        Actions = new Dictionary<string, Dictionary<string, Action>>
        {
            {
                "Networking", new Dictionary<string, Action>
                {
                    { "Disconnect", Disconnect },
                    { "Join Random", JoinRandom },
                    { "Lobby Hop", LobbyHop },
                }
            },
            {
                "Room", new Dictionary<string, Action>
                {
                    { "Copy Room", CopyRoomCode },
                }
            },
            {
                "Cosmetics", new Dictionary<string, Action>
                {
                    { "Rotate Outfit", RotateOutfits },
                }
            },
            {
                "PlaceHolder1", new Dictionary<string, Action>
                {
                }
            },
            {
                "PlaceHolder2", new Dictionary<string, Action>
                {
                }
            },
            {
                "PlaceHolder3", new Dictionary<string, Action>
                {
                }
            },
            {
                "PlaceHolder4", new Dictionary<string, Action>
                {
                }
            },
        };
    }

    private static void ToggleChecker()
    {
        checkerEnabled = !checkerEnabled;
        Debug.Log($"[TUP] Checker: {(checkerEnabled ? "ON" : "OFF")}");

        if (checkerEnabled || checkerLine == null)
            return;

        Object.Destroy(checkerLine);
        checkerLine = null;

        if (lastTargetRig == null)
            return;

        ResetRigMaterial(lastTargetRig);
        lastTargetRig = null;
    }

    private static void CopyRoomCode()
    {
        if (PhotonNetwork.InRoom)
        {
            string roomCode = PhotonNetwork.CurrentRoom.Name;
            GUIUtility.systemCopyBuffer = roomCode;
            Debug.Log($"[TUP] Room Code Copied: {roomCode}");
        }
        else
        {
            Debug.Log("[TUP] Not in a room");
        }
    }

    public static void UpdateChecker()
    {
        if (!checkerEnabled)
            return;

        Vector3 startPos = GTPlayer.Instance.RightHand.controllerTransform.position +
                           GTPlayer.Instance.RightHand.controllerTransform.rotation *
                           GTPlayer.Instance.RightHand.handOffset;

        Quaternion handRot = GTPlayer.Instance.RightHand.controllerTransform.rotation *
                             GTPlayer.Instance.RightHand.handRotOffset;

        Vector3 forward = handRot * Vector3.forward;

        if (checkerLine == null)
            checkerLine = new GameObject("CheckerLine");

        LineRenderer line = checkerLine.GetComponent<LineRenderer>();
        if (line == null)
        {
            line               = checkerLine.AddComponent<LineRenderer>();
            line.material      = new Material(ShaderCache.TextShader);
            line.startWidth    = 0.01f;
            line.endWidth      = 0.01f;
            line.positionCount = 2;
            line.startColor    = Color.cyan;
            line.endColor      = Color.cyan;
        }

        Ray ray = new(startPos, forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);

        RaycastHit closestHit  = default;
        float      minDistance = float.MaxValue;
        VRRig      targetRig   = null;

        foreach (RaycastHit h in hits)
        {
            VRRig rig = h.collider.GetComponentInParent<VRRig>();

            if (rig == null || rig.isOfflineVRRig)
                continue;

            if (!(h.distance < minDistance))
                continue;

            minDistance = h.distance;
            closestHit  = h;
            targetRig   = rig;
        }

        Vector3 endPos;

        if (targetRig != null)
            endPos = closestHit.point;
        else
            endPos = startPos + forward * 100f;

        line.SetPosition(0, startPos);
        line.SetPosition(1, endPos);

        if (targetRig != null && !targetRig.isLocal)
        {
            if (lastTargetRig != null && lastTargetRig != targetRig)
                ResetRigMaterial(lastTargetRig);

            if (lastTargetRig == targetRig)
                return;

            HighlightRig(targetRig);
            ShowPlayerInfo(targetRig);
            lastTargetRig = targetRig;
        }
        else if (lastTargetRig != null)
        {
            ResetRigMaterial(lastTargetRig);
            lastTargetRig = null;
        }
    }

    private static void HighlightRig(VRRig rig)
    {
        SkinnedMeshRenderer? renderer = rig.mainSkin;

        if (renderer == null)
            return;

        renderer.material.shader = ShaderCache.TextShader;
        renderer.material.color  = Color.cyan;
    }

    private static void ResetRigMaterial(VRRig rig)
    {
        SkinnedMeshRenderer? renderer = rig.mainSkin;

        if (renderer == null)
            return;

        renderer.material.shader = ShaderCache.TextShader;
        if (renderer.material.name.Contains("gorilla_body"))
            renderer.material.color = rig.playerColor;
    }

    private static void ShowPlayerInfo(VRRig rig)
    {
        string name  = rig.playerNameVisible;
        Color  color = rig.playerColor;
        string colorStr =
                $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";

        Debug.Log($"[TUP CHECKER]\nName: {name}\nColor: {colorStr}");
    }

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
        Debug.Log("[TUP] Attempting to Leave Current Room");
        if (NetworkSystem.Instance.InRoom)
            NetworkSystem.Instance.ReturnToSinglePlayer();
    }

    private static IEnumerator JoinRandomDelay()
    {
        yield return new WaitForSeconds(1.5f);
        queueCoroutine = null;
        JoinRandom();
    }

    private static void RotateOutfits()
    {
        CosmeticsController controller = CosmeticsController.instance;
        if (controller == null)
        {
            Debug.Log("[TUP] CosmeticsController not found");
            return;
        }

        FieldInfo configField = typeof(CosmeticsController).GetField("outfitSystemConfig", 
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        FieldInfo selectedField = typeof(CosmeticsController).GetField("selectedOutfit", 
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        if (configField == null || selectedField == null)
        {
            Debug.Log("[TUP] Could not find outfit fields");
            return;
        }

        var config = (CosmeticOutfitSystemConfig)configField.GetValue(controller);
        int currentSelected = (int)selectedField.GetValue(controller);

        if (config == null)
        {
            Debug.Log("[TUP] Outfit config not found");
            return;
        }

        int nextOutfit = (currentSelected + 1) % config.maxOutfits;
        controller.LoadSavedOutfit(nextOutfit);

        Debug.Log($"[TUP] Swapped to Outfit Slot: {nextOutfit + 1}");
    }
}

public class CoroutineHandler : MonoBehaviour
{
    public static CoroutineHandler Instance;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}