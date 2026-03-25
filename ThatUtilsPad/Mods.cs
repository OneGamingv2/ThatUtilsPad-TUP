using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GorillaLocomotion;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PlayFab;
using PlayFab.ClientModels;

namespace ThatUtilsPad;

public class ModCategory
{
    public string ImageName;
    public Dictionary<string, Action> Actions;
    public bool ShowInMenu;

    public ModCategory(string imageName, bool showInMenu = true)
    {
        ImageName = imageName;
        ShowInMenu = showInMenu;
        Actions = new Dictionary<string, Action>();
    }
}

public static class Mods
{
    private static Coroutine? queueCoroutine;
    public static  int        ReconnectDelay = 1;

    public static Dictionary<string, ModCategory> Actions;
    
    private static bool        checkerEnabled;
    private static GameObject? checkerLine;
    private static GameObject? checkerSphere;
    private static VRRig?      lastTargetRig;
    private static bool        isMutingAll;
    
    private static Vector3 currentBeamEnd;
    private static Vector3 beamVelocity;

    private static VRRig snappedRig;
    private static VRRig selectedRig;
    private const float SNAP_RADIUS = 0.25f;
    
    public static bool TryGetAction(string identifier, out Action? action)
    {
        foreach (var category in Actions.Values)
        {
            if (category.Actions.TryGetValue(identifier, out action))
                return true;
        }

        action = null;
        return false;
    }

    public static void Init()
    {
        Actions = new Dictionary<string, ModCategory>
        {
            {
                "Networking",
                new ModCategory("cableIcon.png")
                {
                    Actions =
                    {
                        { "Disconnect", Disconnect },
                        { "Join Random", JoinRandom },
                        { "Lobby Hop", LobbyHop },
                        { "Placeholder", Disconnect },
                        { "Placeholder2", JoinRandom },
                        { "Placeholder3", LobbyHop },
                    }
                }
            },
            {
                "Room",
                new ModCategory("roomIcon.png")
                {
                    Actions =
                    {
                        { "Copy Room", CopyRoomCode },
                    }
                }
            },
            {
                "Cosmetics",
                new ModCategory("shirtIcon.png")
                {
                    Actions =
                    {
                        //{ "Rotate Outfit", RotateOutfits },
                    }
                }
            },
            {
                "SelectUser",
                new ModCategory("selectuser.png")
                {
                    Actions =
                    {
                        { "Select User", ToggleChecker },
                    }
                }
            },
            {
                "Settings",
                new ModCategory("settings.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-6",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-7",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-8",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-9",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-10",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-11",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-12",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-13",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            {
                "Placeholder-14",
                new ModCategory("trevis-placeholder.png")
                {
                    Actions =
                    {
                    }
                }
            },
            
            
            //Checker (hidden)
            {
                "ModChecker",
                new ModCategory("trevis-placeholder.png", false)
                {
                    Actions =
                    {
                        { "VolumeUp", VolumeUp },
                        { "VolumeDown", VolumeDown },
                        { "Mute", Mute },
                        { "MuteElse", MuteElse },
                    }
                }
            }
        };
    }

    private static void VolumeUp()
    {
        Debug.Log("VolumeUp");
    }
    
    private static void VolumeDown()
    {
        Debug.Log("VolumeDown");
    }
    
    private static void Mute()
    {
        Debug.Log("Mute");
    }
    
    private static void MuteElse()
    {
        Debug.Log("MuteElse");
    }
    
    
    private static bool SnapHeld()
    {
        if (XRSettings.isDeviceActive)
            return ControllerInputPoller.instance.rightControllerGripFloat > 0.75f;

        return Mouse.current != null && Mouse.current.rightButton.isPressed;
    }

    private static bool SelectPressed()
    {
        if (XRSettings.isDeviceActive)
            return ControllerInputPoller.instance.rightControllerIndexFloat > 0.75f;

        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }
    
    
    private static Coroutine? checkerCoroutine;
    private static IEnumerator CheckerLoop()
    {
        while (checkerEnabled)
        {
            UpdateChecker();
            yield return null;
        }
        if (checkerLine != null)
        {
            Object.Destroy(checkerLine);
            checkerLine = null;
        }
        if (checkerSphere != null)
        {
            Object.Destroy(checkerSphere);
            checkerSphere = null;
        }
        if (lastTargetRig != null)
        {
            //ResetRigMaterial(lastTargetRig);
            lastTargetRig = null;
        }
        checkerCoroutine = null;
    }
    
    private static void ToggleChecker()
    {
        checkerEnabled = !checkerEnabled;
        Debug.Log($"[TUP] Checker: {(checkerEnabled ? "ON" : "OFF")}");

        if (checkerEnabled)
        {
            if (checkerCoroutine == null)
                checkerCoroutine = CoroutineHandler.Instance.StartCoroutine(CheckerLoop());
        }
        else
        {
            // Coroutine will automatically exit on next frame
            // Cleanup handled in CheckerLoop
        }
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

    private static int? noInvisLayerMask;
    public static int NoInvisLayerMask()
    {
        noInvisLayerMask ??= ~(
            1 << LayerMask.NameToLayer("TransparentFX") |
            1 << LayerMask.NameToLayer("Ignore Raycast") | 
            1 << LayerMask.NameToLayer("Zone") |
            1 << LayerMask.NameToLayer("Gorilla Trigger") |
            1 << LayerMask.NameToLayer("Gorilla Boundary") |
            1 << LayerMask.NameToLayer("GorillaCosmetics") |
            1 << LayerMask.NameToLayer("GorillaParticle"));

        return noInvisLayerMask ?? GTPlayer.Instance.locomotionEnabledLayers;
    }
    
    private static bool IsRigValid(VRRig rig)
    {
        return rig != null && rig.gameObject != null;
    }
    
public static void UpdateChecker()
{
    if (!checkerEnabled)
        return;

    Vector3 startPos;
    Vector3 forward;
    Ray ray;

    bool snapHeld;
    bool selectPressed;

    // VR / PC input
    if (XRSettings.isDeviceActive)
    {
        snapHeld = ControllerInputPoller.instance.rightControllerGripFloat > 0.75f;
        selectPressed = ControllerInputPoller.instance.rightControllerIndexFloat > 0.75f;
    }
    else
    {
        snapHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
        selectPressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    // ray origin
    if (XRSettings.isDeviceActive)
    {
        startPos = GTPlayer.Instance.RightHand.controllerTransform.position +
                   GTPlayer.Instance.RightHand.controllerTransform.rotation *
                   GTPlayer.Instance.RightHand.handOffset;

        Quaternion handRot =
            GTPlayer.Instance.RightHand.controllerTransform.rotation *
            GTPlayer.Instance.RightHand.handRotOffset;

        forward = handRot * Vector3.forward;
        ray = new Ray(startPos, forward);
    }
    else
    {
        Camera cam = Main.ThirdPersonCamera;
        if (cam == null || Mouse.current == null)
            return;

        ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        Vector3 bodyBottom =
            GTPlayer.Instance.bodyCollider.transform.position - Vector3.up * 0.25f;

        startPos = bodyBottom;
        forward = ray.direction;
    }

    // --- VALIDATION (prevents softlock) ---
    if (snappedRig != null && (snappedRig.gameObject == null))
        snappedRig = null;

    if (selectedRig != null && (selectedRig.gameObject == null))
        selectedRig = null;

    // line renderer setup
    if (checkerLine == null)
        checkerLine = new GameObject("CheckerLine");

    LineRenderer line = checkerLine.GetComponent<LineRenderer>();
    if (line == null)
    {
        line = checkerLine.AddComponent<LineRenderer>();
        line.material = new Material(ShaderCache.TextShader);
        line.startWidth = 0.0065f;
        line.endWidth = 0.0045f;
        line.positionCount = 2;
        line.startColor = new Color32(161, 98, 237, 255);
        line.endColor = new Color32(203, 166, 247, 255);
    }

    // raycast
    RaycastHit[] hits = Physics.RaycastAll(ray, 512f, NoInvisLayerMask());

    float minDistance = float.MaxValue;
    VRRig targetRig = null;

    RaycastHit firstHit = default;
    bool firstHitFound = false;

    foreach (RaycastHit h in hits)
    {
        if (!firstHitFound || h.distance < firstHit.distance)
        {
            firstHit = h;
            firstHitFound = true;
        }

        VRRig rig = h.collider.GetComponentInParent<VRRig>();
        if (rig == null || rig.isOfflineVRRig)
            continue;

        if (h.distance < minDistance)
        {
            minDistance = h.distance;
            targetRig = rig;
        }
    }

    Vector3 endPos = firstHitFound
        ? firstHit.point
        : startPos + forward * 100f;

    // snapping
    if (snapHeld)
    {
        if (snappedRig == null && targetRig != null && !targetRig.isLocal)
        {
            snappedRig = targetRig;
        }

        if (snappedRig != null)
        {
            Transform head =
                snappedRig.headMesh != null
                    ? snappedRig.headMesh.transform
                    : snappedRig.transform;

            endPos = head.position;

            // select player (one frame trigger)
            if (selectPressed)
            {
                selectedRig = snappedRig;
                ShowPlayerInfo(snappedRig);
            }
        }
    }
    else
    {
        snappedRig = null;
    }


    if (selectedRig != null && selectedRig.gameObject != null && !selectedRig.isOfflineVRRig)
    {
        string name  = selectedRig.playerNameVisible;
        Color  color = selectedRig.playerColor;
        
        int fps = RigHelper.GetFPS(selectedRig);
        string platform = "Unknown";
        try
        {
            platform = GetPlatform(selectedRig);
        }
        catch
        {
            platform = "Unknown";
        }

        string colorStr =
            $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";

        ThatUtilsPad.Main.Instance.UpdateCheckerText(name, fps, platform, colorStr);
    }
    else
    {
        selectedRig = null;
    }

    // beam smoothing
    if (currentBeamEnd == Vector3.zero)
        currentBeamEnd = endPos;

    currentBeamEnd = Vector3.Lerp(currentBeamEnd, endPos, 15f * Time.deltaTime);

    line.SetPosition(0, startPos);
    line.SetPosition(1, currentBeamEnd);

    // sphere
    if (checkerSphere == null)
    {
        checkerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        checkerSphere.transform.localScale = Vector3.one * 0.02f;

        var collider = checkerSphere.GetComponent<Collider>();
        if (collider != null)
            Object.Destroy(collider);

        var rend = checkerSphere.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.shader = ShaderCache.TextShader;
            rend.material.color = new Color32(203, 166, 247, 255);
        }
    }

    checkerSphere.transform.position = currentBeamEnd;

    // target tracking (unchanged)
    if (targetRig != null && !targetRig.isLocal)
    {
        if (lastTargetRig != null && lastTargetRig != targetRig)
        {
            //ResetRigMaterial(lastTargetRig);
        }

        if (lastTargetRig != targetRig)
        {
            //HighlightRig(targetRig);
            lastTargetRig = targetRig;
        }
    }
    else if (lastTargetRig != null)
    {
        //ResetRigMaterial(lastTargetRig);
        lastTargetRig = null;
    }
}
    
    private static void HighlightRig(VRRig rig)
    {
        SkinnedMeshRenderer? renderer = rig.mainSkin;

        if (renderer == null)
            return;

        renderer.material.shader = ShaderCache.TextShader;
        renderer.material.color= new Color32(203, 166, 247, 255);
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
        int fps = RigHelper.GetFPS(rig);
        string platform = GetPlatform(rig);

        string colorStr =
            $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";

        Debug.Log($"[TUP CHECKER]\nName: {name}\nColor: {colorStr}\nFPS: {fps}\nPlatform: {platform}");
    }
    
    private static FieldInfo rigSerializerField = typeof(VRRig).GetField(
        "rigSerializer",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy
    );

    public static object GetRigSerializer(this VRRig rig)
    {
        if (rigSerializerField == null) return null;
        return rigSerializerField.GetValue(rig);
    }
    
    public static Photon.Realtime.Player GetPhotonPlayer(this VRRig rig) =>
        NetPlayerToPlayer(GetPlayerFromVRRig(rig));
    
    public static NetPlayer GetPlayerFromVRRig(VRRig p) =>
        p.Creator ?? NetworkSystem.Instance.GetPlayer(NetworkSystem.Instance.GetOwningPlayerID(p.GetRigSerializer() is Component serializerComponent ? serializerComponent.gameObject : null));
    
    public static Player NetPlayerToPlayer(NetPlayer p) =>
        p.GetPlayerRef();
    
    public static string GetPlatform(this VRRig rig)
    {
        int suspiciouslySteam = 0;
        int suspiciouslyPC = 0;
        int suspiciouslyQuest = 0;
        string concatStringOfCosmeticsAllowed = rig.Cosmetics();

        if (concatStringOfCosmeticsAllowed.Contains("S. FIRST LOGIN"))
            suspiciouslySteam++;

        if (concatStringOfCosmeticsAllowed.Contains("FIRST LOGIN") || rig.GetPhotonPlayer().CustomProperties.Count >= 2)
            suspiciouslyPC++;

        if (RigHelper.GetPCTier(rig) > 0)
            suspiciouslySteam++;
        else if (RigHelper.GetQuestTier(rig) > 0)
            suspiciouslyQuest++;


        if (suspiciouslySteam > suspiciouslyPC && suspiciouslySteam > suspiciouslyQuest) return "Steam";
        if (suspiciouslyPC > suspiciouslySteam && suspiciouslyPC > suspiciouslyQuest) return "PC";
        if (suspiciouslyQuest > suspiciouslySteam && suspiciouslyQuest > suspiciouslyPC) return "Standalone";

        return "Standalone";
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

    //private static void RotateOutfits()
    //{
    //    CosmeticsController controller = CosmeticsController.instance;
    //    if (controller == null)
    //    {
    //        Debug.Log("[TUP] CosmeticsController not found");
    //        return;
    //    }
    //
    //    FieldInfo configField = typeof(CosmeticsController).GetField("outfitSystemConfig", 
    //       BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    //   FieldInfo selectedField = typeof(CosmeticsController).GetField("selectedOutfit", 
    //       BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    //
    //  if (configField == null || selectedField == null)
    //  {
    //      Debug.Log("[TUP] Could not find outfit fields");
    //      return;
    //  }

    //  var config = (CosmeticOutfitSystemConfig)configField.GetValue(controller);
    //   int currentSelected = (int)selectedField.GetValue(controller);

    //   if (config == null)
    //  {
    //      Debug.Log("[TUP] Outfit config not found");
    //      return;
    //    }
    //
    //   int nextOutfit = (currentSelected + 1) % config.maxOutfits;
    //   controller.LoadSavedOutfit(nextOutfit);
    //
    //    Debug.Log($"[TUP] Swapped to Outfit Slot: {nextOutfit + 1}");
    //}
}

public static class CosmeticSystemHelper
{
    private static MethodInfo isTempCosmeticAllowed;

    static CosmeticSystemHelper()
    {
        Assembly assembly = typeof(VRRig).Assembly;
        Type cosmeticsType = assembly.GetType("PlayerCosmeticsSystem");
        
        isTempCosmeticAllowed = cosmeticsType.GetMethod(
            "IsTemporaryCosmeticAllowed",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
    }

    public static bool IsTemporaryCosmeticAllowed(VRRig rig, string cosmetic)
    {
        if (isTempCosmeticAllowed == null) return false;

        object result = isTempCosmeticAllowed.Invoke(null, new object[] { rig, cosmetic });
        return (bool)result;
    }
}

public static class VRRigCosmeticsExtensions
{
    private static FieldInfo cosmeticsField = typeof(VRRig).GetField(
        "_playerOwnedCosmetics",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    public static string Cosmetics(this VRRig rig)
    {
        if (cosmeticsField == null) return "";
        object value = cosmeticsField.GetValue(rig);
        if (value == null) return "";
        
        if (value is System.Collections.IEnumerable enumerable)
        {
            return string.Join(",", enumerable.Cast<object>());
        }

        return value.ToString();
    }
}


public static class RigHelper
{
    private static FieldInfo fpsField;

    static RigHelper()
    {
        fpsField = typeof(VRRig).GetField(
            "fps",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
    }

    public static int GetFPS(VRRig rig)
    {
        if (fpsField == null) return -1;

        return (int)fpsField.GetValue(rig);
    }
    
    private static FieldInfo pcTierField = typeof(VRRig).GetField(
        "currentRankedSubTierPC",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    private static FieldInfo questTierField = typeof(VRRig).GetField(
        "currentRankedSubTierQuest",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    public static int GetPCTier(VRRig rig)
    {
        return pcTierField != null ? (int)pcTierField.GetValue(rig) : 0;
    }

    public static int GetQuestTier(VRRig rig)
    {
        return questTierField != null ? (int)questTierField.GetValue(rig) : 0;
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
