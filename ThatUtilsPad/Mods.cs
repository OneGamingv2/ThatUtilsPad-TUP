using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GorillaGameModes;
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
using Photon.Voice.Unity;
using TMPro;
using ThatUtilsPad.MenuComponents;

namespace ThatUtilsPad;

public class ModAction
{
    public Action Action;
    public bool IsToggle;
    public float Cooldown;

    public ModAction(Action action, bool isToggle = false, bool hasCooldown = false, float cooldown = 0f)
    {
        Action = action;
        IsToggle = isToggle;
        Cooldown = hasCooldown ? cooldown : 0f;
    }

    public static implicit operator ModAction(Action action) => new ModAction(action);
}

public class ModCategory
{
    public string ImageName;
    public Dictionary<string, ModAction> Actions;
    public bool ShowInMenu;

    public ModCategory(string imageName, bool showInMenu = true)
    {
        ImageName = imageName;
        ShowInMenu = showInMenu;
        Actions = new Dictionary<string, ModAction>();
    }
}

public static class Mods
{
    private static Coroutine? queueCoroutine;
    public static  int        ReconnectDelay = 1;

    public static Dictionary<string, ModCategory> Actions;

    private static int currentOutfitIndex = 0;
    private static int savedOutfitCount   = 0;
    private static readonly Dictionary<int, int> savedOutfitSlots = new Dictionary<int, int>();

    private static Coroutine   pulseRoutine;
    private static bool        checkerEnabled;
    private static GameObject? checkerLine;
    private static GameObject? checkerSphere;
    private static VRRig?      lastTargetRig;
    private static bool        isMutingAll;
    
    private static Vector3 currentBeamEnd;
    private static Vector3 beamVelocity;

    private static VRRig snappedRig;
    private static VRRig selectedRig;
    private static string selectedUserId;
    private static HashSet<string> manuallyAdjusted = new HashSet<string>();
    
    private static Material? lineMat;
    
    private static Dictionary<VRRig, float> currentVolumes = new Dictionary<VRRig, float>();
    private static Dictionary<string, float> savedVolumes = new Dictionary<string, float>();

    private static string folderPath =
        System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ThatUtilsPad");

    private static string filePath =
        System.IO.Path.Combine(folderPath, "volumes.txt");

    private static string statesFilePath =
        System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ThatUtilsPad"), "button_states.txt");

    public static Dictionary<string, bool> SavedToggleStates = new Dictionary<string, bool>();

    public  static bool           IsRenaming => isRenaming;
    private static bool           isRenaming;
    private static TMP_Text       renamingLabel;
    private static GameObject     renameKeyboard;
    private static string         currentRenameText = "";
    private static string         originalLabel     = "";
    private static int            renamingOutfitN   = -1;

    private static readonly Dictionary<int, string> outfitSlotNames = new Dictionary<int, string>();

    public static void SaveToggleState(string identifier, bool isOn)
    {
        SavedToggleStates[identifier] = isOn;
        SaveButtonStates();
    }

    private static void SaveButtonStates()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(statesFilePath));
            var lines = new System.Collections.Generic.List<string>();
            foreach (var kvp in SavedToggleStates)
                lines.Add("toggle:" + kvp.Key + "=" + kvp.Value);
            lines.Add("clickSoundIndex=" + currentClickIndex);
            foreach (var kvp in savedOutfitSlots)
                lines.Add("savedOutfit:" + kvp.Key + "=" + kvp.Value);
            foreach (var kvp in outfitSlotNames)
                lines.Add("outfitName:" + kvp.Key + "=" + kvp.Value);
            System.IO.File.WriteAllLines(statesFilePath, lines);
        }
        catch (Exception e) { Debug.LogWarning("[TUP] Failed to save button states: " + e.Message); }
    }

    private static void LoadButtonStates()
    {
        SavedToggleStates.Clear();
        savedOutfitSlots.Clear();
        outfitSlotNames.Clear();
        savedOutfitCount = 0;
        if (!System.IO.File.Exists(statesFilePath)) return;
        try
        {
            foreach (var line in System.IO.File.ReadAllLines(statesFilePath))
            {
                if (line.StartsWith("toggle:"))
                {
                    var inner = line.Substring(7);
                    int eq = inner.LastIndexOf('=');
                    if (eq < 0) continue;
                    string id  = inner.Substring(0, eq);
                    if (bool.TryParse(inner.Substring(eq + 1), out bool val))
                        SavedToggleStates[id] = val;
                }
                else if (line.StartsWith("clickSoundIndex="))
                {
                    if (int.TryParse(line.Substring(16), out int idx))
                        currentClickIndex = Mathf.Clamp(idx, 0, clickSounds.Count - 1);
                }
                else if (line.StartsWith("savedOutfit:"))
                {
                    var inner = line.Substring(12);
                    int eq = inner.IndexOf('=');
                    if (eq < 0) continue;
                    if (int.TryParse(inner.Substring(0, eq), out int n) &&
                        int.TryParse(inner.Substring(eq + 1), out int slotIdx))
                    {
                        savedOutfitSlots[n] = slotIdx;
                        if (n > savedOutfitCount) savedOutfitCount = n;
                    }
                }
                else if (line.StartsWith("outfitName:"))
                {
                    var inner = line.Substring(11);
                    int eq = inner.IndexOf('=');
                    if (eq < 0) continue;
                    if (int.TryParse(inner.Substring(0, eq), out int n))
                        outfitSlotNames[n] = inner.Substring(eq + 1);
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[TUP] Failed to load button states: " + e.Message); }
    }
    
    
    public static bool TryGetAction(string identifier, out ModAction? action)
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
                    { "Disconnect", new ModAction(Disconnect, false, true, 3f) },
                    { "Join Random", new ModAction(JoinRandom, false, true, 5f) },
                    { "Lobby Hop", new ModAction(LobbyHop, false, true, 5f) },
                    { "Queue", new ModAction(ToggleQueue, false) },
                    { "Mode", new ModAction(ToggleMode, false) },
                }
            }
        },
        {
            "Room",
            new ModCategory("playersicon.png")
            {
                Actions =
                {
                    { "Nametags", new ModAction(ToggleNameTags, true) },
                }
            }
        },
        {
            "Cosmetics",
            new ModCategory("shirtIcon.png")
            {
                Actions = { }
            }
        },
        {
            "SelectUser",
            new ModCategory("selectuser.png")
            {
                Actions =
                {
                    { "Select User", new ModAction(ToggleChecker, true) },
                }
            }
        },
        {
            "Settings",
            new ModCategory("settings.png")
            {
                Actions =
                {
                    { "ab", new ModAction(Nothing, false, true, 3f) },
                    { "ge", new ModAction(Nothing, false, true, 3f) },
                    { "fg", new ModAction(Nothing, false, true, 3f) },
                    //{ "Click Sound", new ModAction(ToggleClickSound, false) },
                    //{ "Sound Test", new ModAction(Nothing, false) },
                    //{ "Menu Smoothing", new ModAction(SmoothMenu, true) },
                    { "e", new ModAction(Nothing, false, true, 3f) },
                    { "a", new ModAction(Nothing, false, true, 3f) },
                    { "g", new ModAction(Nothing, false, true, 3f) },
                    { "f", new ModAction(Nothing, false, true, 3f) },
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
        
        //Checker (hidden)
        {
            "ModChecker",
            new ModCategory("trevis-placeholder.png", false)
            {
                Actions =
                {
                    { "VolumeUp", new ModAction(VolumeUp, false) },
                    { "VolumeDown", new ModAction(VolumeDown, false) },
                    { "Mute", new ModAction(Mute, false) },
                    { "MuteElse", new ModAction(MuteElse, false) },
                }
            }
        }
    };
    
    LoadVolumes();
    LoadButtonStates();
    InitOutfitSlotActions();
}
    
    private static Dictionary<VRRig, float> volumes = new Dictionary<VRRig, float>();

private static bool muteToggled;
private static bool muteElseToggled;

private static Speaker GetSpeaker(VRRig rig)
{
    if (rig == null) return null;
    return rig.GetComponentInChildren<Speaker>();
}

private static void SetVoiceVolume(VRRig rig, float volume)
{
    if (rig == null) return;

    Speaker speaker = GetSpeaker(rig);
    if (speaker == null) return;

    AudioSource source = speaker.GetComponent<AudioSource>();
    if (source == null) return;

    source.volume = Mathf.Clamp(volume, 0f, 2f);
}

private static void ApplyVolumes()
{
    foreach (var kvp in volumes)
    {
        VRRig rig = kvp.Key;
        if (rig == null) continue;

        float target = kvp.Value;

        if (!currentVolumes.ContainsKey(rig))
            currentVolumes[rig] = target;
        
        float t = 1f - Mathf.Exp(-10f * Time.deltaTime);

        currentVolumes[rig] = Mathf.Lerp(
            currentVolumes[rig],
            target,
            t
        );

        SetVoiceVolume(rig, currentVolumes[rig]);
        
        if (rig == selectedRig)
            UpdateVolumeUI(currentVolumes[rig]);
    }
    
    var dead = volumes.Keys.Where(r => r == null).ToList();
    foreach (var r in dead)
    {
        volumes.Remove(r);
        currentVolumes.Remove(r);
    }
}

private static float displayedVolume = 1f;
private static void UpdateVolumeUI(float targetVolume)
{
    if (Main.volumeText == null) return;

    float t = 1f - Mathf.Exp(-12f * Time.deltaTime);
    displayedVolume = Mathf.Lerp(displayedVolume, targetVolume, t);

    int percent = Mathf.RoundToInt(displayedVolume * 100f);
    Main.volumeText.text = percent + "%";
}

private static void SaveVolumes()
{
    try
    {
        if (!System.IO.Directory.Exists(folderPath))
            System.IO.Directory.CreateDirectory(folderPath);

        List<string> lines = new List<string>();

        foreach (var kvp in volumes)
        {
            if (kvp.Key == null) continue;

            if (GetRigID(kvp.Key, out string id))
            {
                if (!manuallyAdjusted.Contains(id)) continue;

                lines.Add($"{id}:{kvp.Value}");
            }
        }

        System.IO.File.WriteAllLines(filePath, lines);
    }
    catch (Exception e)
    {
        Debug.LogError("[TUP] Failed to save volumes: " + e);
    }
}

private static void LoadVolumes()
{
    try
    {
        if (!System.IO.File.Exists(filePath)) return;

        foreach (var line in System.IO.File.ReadAllLines(filePath))
        {
            string[] split = line.Split(':');
            if (split.Length != 2) continue;

            if (float.TryParse(split[1], out float vol))
            {
                savedVolumes[split[0]] = vol;
            }
        }
    }
    catch (Exception e)
    {
        Debug.LogError("[TUP] Failed to load volumes: " + e);
    }
}

private static void VolumeUp()
{
    if (selectedRig == null) return;

    float current = volumes.ContainsKey(selectedRig) ? volumes[selectedRig] : 1f;
    float newVol = Mathf.Clamp(current + 0.1f, 0f, 2f);

    volumes[selectedRig] = newVol;

    Debug.Log($"[TUP] Volume UP -> {newVol * 100f}%");
    
    if (GetRigID(selectedRig, out string id))
    {
        manuallyAdjusted.Add(id);
    }

    SaveVolumes();
}

private static void VolumeDown()
{
    if (selectedRig == null) return;

    float current = volumes.ContainsKey(selectedRig) ? volumes[selectedRig] : 1f;
    float newVol = Mathf.Clamp(current - 0.1f, 0f, 2f);

    volumes[selectedRig] = newVol;

    Debug.Log($"[TUP] Volume DOWN -> {newVol * 100f}%");
    
    if (GetRigID(selectedRig, out string id))
    {
        manuallyAdjusted.Add(id);
    }
    
    SaveVolumes();
}

private static void Mute()
{
    if (selectedRig == null) return;

    muteToggled = !muteToggled;

    if (muteToggled)
    {
        if (!volumes.ContainsKey(selectedRig))
            volumes[selectedRig] = 1f;

        volumes[selectedRig] = 0f;
    }
    else
    {
        volumes[selectedRig] = 1f;
    }

    Debug.Log($"[TUP] {(muteToggled ? "Muted" : "Unmuted")} {selectedRig.playerNameVisible}");
    
    if (GetRigID(selectedRig, out string id))
    {
        manuallyAdjusted.Add(id);
    }
    
    SaveVolumes();
}

private static void MuteElse()
{
    if (selectedRig == null) return;

    muteElseToggled = !muteElseToggled;

    foreach (VRRig rig in Object.FindObjectsOfType<VRRig>())
    {
        if (rig == null || rig.isLocal) continue;

        if (rig == selectedRig)
        {
            volumes[rig] = 1f;
        }
        else
        {
            volumes[rig] = muteElseToggled ? 0f : 1f;
        }
    }

    Debug.Log($"[TUP] MuteElse {(muteElseToggled ? "ENABLED" : "DISABLED")}");
}

    public static bool GetRigID(VRRig rig, out string userId)
    {
        userId = null;

        if (rig == null || rig.OwningNetPlayer == null)
            return false;

        userId = rig.OwningNetPlayer.UserId;
        return true;
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
            //removeSkeletonHighlight(lastTargetRig);
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
        else {}
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
        return rig != null
               && rig.gameObject != null
               && rig.isActiveAndEnabled
               && !rig.isOfflineVRRig
               && rig.mainSkin != null
               && rig.mainSkin.bones != null;
    }
    
public static void UpdateChecker()
{
    if (!checkerEnabled)
        return;

    ApplyVolumes();

    void ClearSnapped()
    {
        if (snappedRig != null)
        {
            DisableBoneHighlight(snappedRig);
            removeSkeletonHighlight(snappedRig);
        }
        snappedRig = null;
    }

    void ClearSelected()
    {
        selectedRig = null;
    }

    if (!IsRigValid(snappedRig))
        ClearSnapped();

    if (!IsRigValid(selectedRig))
        ClearSelected();

    bool snapHeld;
    bool selectPressed;

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

    Vector3 startPos;
    Vector3 forward;
    Ray ray;

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

        startPos = GTPlayer.Instance.bodyCollider.transform.position - Vector3.up * 0.25f;
        forward = ray.direction;
    }

    if (checkerLine == null)
        checkerLine = new GameObject("CheckerLine");

    LineRenderer line = checkerLine.GetComponent<LineRenderer>();
    if (line == null)
    {
        line = checkerLine.AddComponent<LineRenderer>();
        if (lineMat == null)
            lineMat = new Material(ShaderCache.TextShader);
        line.material = lineMat;
        line.material = lineMat;
        line.startWidth = 0.002f;
        line.endWidth = 0.002f;
        line.positionCount = 2;
        line.startColor = new Color32(161, 98, 237, 255);
        line.endColor = new Color32(203, 166, 247, 255);
    }

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

        if (!IsRigValid(rig) || rig.isLocal)
            continue;

        if (h.distance < minDistance)
        {
            minDistance = h.distance;
            targetRig = rig;
        }
    }

    if (!IsRigValid(targetRig))
        targetRig = null;

    Vector3 endPos = firstHitFound
        ? firstHit.point
        : startPos + forward * 100f;

    if (snapHeld)
    {
        if (snappedRig == null && targetRig != null)
            snappedRig = targetRig;

        if (IsRigValid(snappedRig))
        {
            try
            {
                BoneHighlight(snappedRig, new Color32(203, 166, 247, 255), 0.005f);
                skeletonHighlight(snappedRig);
            }
            catch { }

            Transform head = null;

            if (snappedRig.headMesh != null)
                head = snappedRig.headMesh.transform;
            else if (snappedRig.transform != null)
                head = snappedRig.transform;

            if (head != null)
                endPos = head.position;

            if (selectPressed)
            {
                selectedRig = snappedRig;

                if (GetRigID(snappedRig, out string id))
                {
                    selectedUserId = id;

                    if (savedVolumes.ContainsKey(id))
                    {
                        volumes[snappedRig] = savedVolumes[id];
                        currentVolumes[snappedRig] = savedVolumes[id];
                    }
                }

                displayedVolume = volumes.ContainsKey(snappedRig)
                    ? volumes[snappedRig]
                    : 1f;

                ShowPlayerInfo(snappedRig);
            }
        }
        else
        {
            ClearSnapped();
        }
    }
    else
    {
        ClearSnapped();
    }

    if (IsRigValid(selectedRig))
    {
        try
        {
            string name = selectedRig.playerNameVisible;
            Color color = selectedRig.playerColor;

            int fps = RigHelper.GetFPS(selectedRig);

            string platform;
            try { platform = GetPlatform(selectedRig); }
            catch { platform = "Unknown"; }

            string creationDate = GetCreationDate(GetPlayerFromVRRig(selectedRig).UserId);

            string colorStr =
                $"({Mathf.RoundToInt(color.r * 9)} {Mathf.RoundToInt(color.g * 9)} {Mathf.RoundToInt(color.b * 9)})";

            Color colorBaseForm = selectedRig.playerColor;

            int plrPing = GetPingThrottled(selectedRig);

            ThatUtilsPad.Main.Instance.UpdateCheckerText(name, fps, platform, creationDate, colorStr, colorBaseForm, plrPing);
        }
        catch
        {
            ClearSelected();
        }
    }
    else
    {
        ClearSelected();
    }

    if (currentBeamEnd == Vector3.zero)
        currentBeamEnd = endPos;

    if (IsRigValid(snappedRig))
        currentBeamEnd = endPos;
    else
        currentBeamEnd = Vector3.Lerp(currentBeamEnd, endPos, 15f * Time.deltaTime);

    line.SetPosition(0, startPos);
    line.SetPosition(1, currentBeamEnd);

    if (checkerSphere == null)
    {
        checkerSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        checkerSphere.transform.localScale = Vector3.one * 0.01f;

        var collider = checkerSphere.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.Destroy(collider);

        var rend = checkerSphere.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.shader = ShaderCache.TextShader;
            rend.material.color = new Color32(203, 166, 247, 255);
        }
    }

    checkerSphere.transform.position = currentBeamEnd;

    if (IsRigValid(targetRig))
    {
        if (lastTargetRig != null && lastTargetRig != targetRig)
        {
        }

        if (lastTargetRig != targetRig)
        {
            lastTargetRig = targetRig;
        }
    }
    else if (lastTargetRig != null)
    {
        lastTargetRig = null;
    }
}


private static FieldInfo _historyField;
private static PropertyInfo _countProp;
private static PropertyInfo _indexerProp;
private static FieldInfo _timeField;

private static bool _initialized = false;

private static void InitReflection(object historySample, object itemSample)
{
    if (_initialized) return;

    _historyField = typeof(VRRig).GetField(
        "velocityHistoryList",
        BindingFlags.NonPublic | BindingFlags.Instance
    );

    if (historySample != null)
    {
        var historyType = historySample.GetType();

        _countProp = historyType.GetProperty("Count");
        _indexerProp = historyType.GetProperty("Item");
    }

    if (itemSample != null)
    {
        _timeField = itemSample.GetType().GetField("time");
    }

    _initialized = true;
}

private static int GetPing(VRRig rig)
{
    try
    {
        if (rig == null)
            return int.MaxValue;
        
        if (_historyField == null)
        {
            _historyField = typeof(VRRig).GetField(
                "velocityHistoryList",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
        }

        var history = _historyField?.GetValue(rig);
        if (history == null)
            return int.MaxValue;
        
        if (!_initialized)
        {
            object firstItem = null;

            var historyType = history.GetType();
            _countProp = historyType.GetProperty("Count");
            _indexerProp = historyType.GetProperty("Item");

            int count = (int)(_countProp?.GetValue(history) ?? 0);
            if (count > 0)
            {
                firstItem = _indexerProp?.GetValue(history, new object[] { 0 });
                if (firstItem != null)
                {
                    _timeField = firstItem.GetType().GetField("time");
                }
            }

            _initialized = true;
        }

        int currentCount = (int)(_countProp?.GetValue(history) ?? 0);
        if (currentCount <= 0)
            return int.MaxValue;

        var item = _indexerProp?.GetValue(history, new object[] { 0 });
        if (item == null)
            return int.MaxValue;

        double time = (double)(_timeField?.GetValue(item) ?? 0d);

        double ping = Math.Abs((time - PhotonNetwork.Time) * 1000);
        return (int)Math.Clamp(Math.Round(ping), 0, int.MaxValue);
    }
    catch
    {
        return int.MaxValue;
    }
}

private static float _nextUpdateTime = 0f;
private static int _cachedPing = int.MaxValue;

private static int GetPingThrottled(VRRig rig)
{
    if (UnityEngine.Time.time >= _nextUpdateTime)
    {
        _cachedPing = GetPing(rig);
        _nextUpdateTime = UnityEngine.Time.time + 1f;
    }

    return _cachedPing;
}


private static readonly Dictionary<VRRig, List<LineRenderer>> boneESP 
    = new Dictionary<VRRig, List<LineRenderer>>();

public static readonly int[] bones = {
    4, 3, 5, 4, 19, 18, 20, 19, 3, 18,
    21, 20, 22, 21, 25, 21, 29, 21, 31, 29,
    27, 25, 24, 22, 6, 5, 7, 6, 10, 6,
    14, 6, 16, 14, 12, 10, 9, 7
};
private static void BoneHighlight(VRRig rig, Color color, float width = 0.02f)
{
    if (rig == null || rig.isLocal) return;

    if (!boneESP.TryGetValue(rig, out List<LineRenderer> lines))
    {
        lines = new List<LineRenderer>();

        // Head line
        //LineRenderer headLine = rig.head.rigTarget.gameObject.GetOrAddComponent<LineRenderer>();
        //headLine.material = new Material(Shader.Find("GUI/Text Shader"));
        //lines.Add(headLine);

        // Bone lines
        for (int i = 0; i < 19; i++)
        {
            LineRenderer line = rig.mainSkin.bones[bones[i * 2]].gameObject.GetOrAddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("GUI/Text Shader"));
            lines.Add(line);
        }

        boneESP.Add(rig, lines);
    }

    // HEAD
    //LineRenderer head = lines[0];
    //head.startWidth = width;
    //head.endWidth = width;
    //head.startColor = color;
    //head.endColor = color;

    //head.SetPosition(0, rig.head.rigTarget.position + new Vector3(0f, 0.16f, 0f));
    //head.SetPosition(1, rig.head.rigTarget.position - new Vector3(0f, 0.4f, 0f));

    // BONES
    for (int i = 0; i < 19; i++)
    {
        LineRenderer line = lines[i];

        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;

        line.SetPosition(0, rig.mainSkin.bones[bones[i * 2]].position);
        line.SetPosition(1, rig.mainSkin.bones[bones[i * 2 + 1]].position);
    }
}

public static void DisableBoneHighlight(VRRig rig)
{
    foreach (var renderer in boneESP.SelectMany(bones => bones.Value))
        Object.Destroy(renderer);

    boneESP.Clear();
}

    private static void skeletonHighlight(VRRig rig)
    {
        if (rig == null) return;
        if (rig.skeleton == null) return;
        if (rig.skeleton.renderer == null) return;
        if (rig.skeleton.renderer.material == null) return;
        
        rig.skeleton.renderer.enabled = true;
        rig.skeleton.renderer.material.shader = ShaderCache.TextShader;
        rig.skeleton.renderer.material.color = rig.playerColor;
        
        Color skeletonColor = new Color(rig.skeleton.renderer.material.color.r, rig.skeleton.renderer.material.color.g, rig.skeleton.renderer.material.color.b, 0.3f);
        Color themeColor = new Color(0.796f, 0.651f, 0.969f, 0.1f);
        rig.skeleton.renderer.material.color = themeColor;
    }
    
    private static void removeSkeletonHighlight(VRRig rig)
    {
        if (rig?.skeleton?.renderer == null) return;
        rig.skeleton.renderer.enabled = false;
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
    
    public static readonly Dictionary<string, float> waitingForCreationDate = new Dictionary<string, float>();
    public static readonly Dictionary<string, string> creationDateCache = new Dictionary<string, string>();
    public static string GetCreationDate(string input, Action<string> onTranslated = null, string format = "MM/dd/yyyy")
    {
        if (creationDateCache.TryGetValue(input, out string date))
            return date;
        if (!waitingForCreationDate.ContainsKey(input))
        {
            waitingForCreationDate[input] = Time.time + 10f;
            GetCreationCoroutine(input, onTranslated, format);
        }
        else
        {
            if (!(Time.time > waitingForCreationDate[input])) return "Loading...";
            waitingForCreationDate[input] = Time.time + 10f;
            GetCreationCoroutine(input, onTranslated, format);
        }

        return "Loading...";
    }
    
    public static void GetCreationCoroutine(string userId, Action<string> onTranslated = null, string format = "MMMM dd, yyyy h:mm tt")
    {
        if (creationDateCache.TryGetValue(userId, out string date))
        {
            onTranslated?.Invoke(date);
            return;
        }

        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest { PlayFabId = userId }, delegate (GetAccountInfoResult result)
        {
            string creationDate = result.AccountInfo.Created.ToString(format);
            creationDateCache[userId] = creationDate;

            onTranslated?.Invoke(creationDate);
        }, delegate { creationDateCache[userId] = "Error"; onTranslated?.Invoke("Error"); });
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
            Main.queueText.text = "Queue  :  " + gorillaComputer.currentQueue;
        
        Debug.Log("Current Queue: " + gorillaComputer.currentQueue);
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
            Main.modeText.text = "Mode  :  " + watchableStringSo.Value;
        
        currentModeIndex = (currentModeIndex + 1) % modes.Count;
        Debug.Log(watchableStringSo.Value);
    }

    
    
    private static string FormatDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        
        return char.ToUpper(name[0]) + name.Substring(1).ToLower();
    }
    

    public static List<ClickSoundEntry> clickSounds = new List<ClickSoundEntry>
    {
        new ClickSoundEntry("watch", Main.Instance.watchSound),
        new ClickSoundEntry("creamy", Main.Instance.creamySound),
        new ClickSoundEntry("destiny", Main.Instance.destinySound),
        new ClickSoundEntry("minecraft", Main.Instance.minecraftSound),
        new ClickSoundEntry("Wii", Main.Instance.wiiSound),
        new ClickSoundEntry("untitled", Main.Instance.untitledClickSound)
    };
    public static int currentClickIndex = 0;
    
    private static void ToggleClickSound()
    {
        if (clickSounds.Count == 0) return;

        var entry = clickSounds[currentClickIndex];
        Main.Instance.currentClickSound = entry.Clip;

        string displayName = FormatDisplayName(entry.Name);

        Debug.Log("Click Sound : " + displayName);

        currentClickIndex = (currentClickIndex + 1) % clickSounds.Count;
        SaveButtonStates();

        if (Main.clickSoundText != null)
            Main.clickSoundText.text = "Click Sound  :  " + displayName;
    }


    private static bool smoothMenuEnabled = true;
    private static void SmoothMenu()
    {
        smoothMenuEnabled = !smoothMenuEnabled;
        Main.Instance.SetMenuSmoothing(smoothMenuEnabled);
    }
    
    private static void Nothing()
    {
        NotificationLib.DisplayMode = NotificationDisplayMode.Pc;
        NotificationLib.SendNotification("TUP Test Message", "PC only random messaege idkl dnsjandjandaj", 3f);
    }
    
    
    private static bool nameTagsEnabled = false;
    private static readonly Dictionary<VRRig, GameObject> nametags = new();
    public static AssetBundle nameBundle;

    public static void UpdateNameTags()
    {
        var copy = nametags.ToList();
        foreach (var pair in copy)
        {
            if (pair.Key == null || !VRRigCache.ActiveRigs.Contains(pair.Key))
            {
                Object.Destroy(pair.Value);
                nametags.Remove(pair.Key);
            }
        }
        
        foreach (var rig in VRRigCache.ActiveRigs)
        {
            if (rig == null) continue;
            if (rig.isLocal) continue;
            
            if (!nametags.ContainsKey(rig))
            {
                GameObject prefab = nameBundle.LoadAsset<GameObject>("assets/prefabs/nametag.prefab");

                GameObject tag = Object.Instantiate(prefab, rig.transform, true);
                nametags.Add(rig, tag);
            }

            GameObject nametag = nametags[rig];
            
            if (nametag == null)
            {
                nametags.Remove(rig);
                continue;
            }
            
            Transform anchor = rig.headMesh != null ? rig.headMesh.transform : rig.transform;
            nametag.transform.position = anchor.position + Vector3.up * 0.62f;
            
            nametag.transform.LookAt(Main.ThirdPersonCamera.transform.position);
            nametag.transform.Rotate(0f, 180f, 0f);
            nametag.transform.localScale = Vector3.one * 0.1f;
            
            var color = nametag.transform.Find("Color")?.GetComponent<SpriteRenderer>();
            if (color != null)
                color.color = rig.playerColor;

            var name = nametag.transform.Find("TextCanvas/Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null)
                name.text = GetPlayerFromVRRig(rig).NickName;
            
            var fps = nametag.transform.Find("TextCanvas/FPS")?.GetComponent<TextMeshProUGUI>();
            if (fps != null)
                fps.text = "FPS: " + RigHelper.GetFPS(rig) + "Hz";
            
            var ping = nametag.transform.Find("TextCanvas/Ping")?.GetComponent<TextMeshProUGUI>();
            if (ping != null)
                ping.text = "PING: " + GetPingThrottled(rig) + "Ms";
            
            
            // platform
            var IconsLeft = nametag.transform.Find("TextCanvas/Name/IconsLeft");
            var IconsRight = nametag.transform.Find("TextCanvas/Name/IconsRight");

            string platform;
            try { platform = GetPlatform(rig); }
            catch { platform = "Unknown"; }

            void SetPlatformIcons(Transform parent, string plat)
            {
                if (parent == null) return;
                
                var meta = parent.Find("IconMeta");
                var pc = parent.Find("IconPC");
                var unknown = parent.Find("IconUnknown");
                var steam = parent.Find("IconSteam");

                if (meta != null) meta.gameObject.SetActive(false);
                if (pc != null) pc.gameObject.SetActive(false);
                if (unknown != null) unknown.gameObject.SetActive(false);
                if (steam != null) steam.gameObject.SetActive(false);
                
                switch (plat)
                {
                    case "Standalone":
                        if (meta != null) meta.gameObject.SetActive(true);
                        break;

                    case "PC":
                        if (pc != null) pc.gameObject.SetActive(true);
                        break;

                    case "Steam":
                        if (steam != null) steam.gameObject.SetActive(true);
                        break;

                    default:
                        if (unknown != null) unknown.gameObject.SetActive(true);
                        break;
                }
            }
            
            SetPlatformIcons(IconsLeft, platform);
            SetPlatformIcons(IconsRight, platform);
        }
    }
    
    public static void ToggleNameTags()
    {
        nameTagsEnabled = !nameTagsEnabled;
        
        if (!nameTagsEnabled)
        {
            DisableNameTags();
        }
    }
    
    public static void DisableNameTags()
    {
        foreach (var tag in nametags.Values)
        {
            if (tag != null)
                Object.Destroy(tag);
        }

        nametags.Clear();
    }
    
    
    public static void NameTagsLoop()
    {
        if (!nameTagsEnabled)
            return;

        UpdateNameTags();
    }

    
    private static IEnumerator JoinRandomDelay()
    {
        yield return new WaitForSeconds(1.5f);
        queueCoroutine = null;
        JoinRandom();
    }

    private static void OutfitSpinner()
    {
        var root = GameObject.Find("Environment Objects");
        var spinner =
            root.transform.Find("LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/WornDisplay");
        
        GameObject spinnerObj = GameObject.Instantiate(spinner.gameObject);
        spinnerObj.transform.SetParent(GTPlayer.Instance.transform);
        spinnerObj.transform.position =
            Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        spinnerObj.transform.localRotation = Quaternion.identity;
        spinnerObj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform); //GTPlayer.Instance.transform
        spinnerObj.transform.localScale = Vector3.one;
        
        
        // Switch buttons

        var buttons =
            root.transform.Find("LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/UI/OutfitButtons/");
        
        GameObject btnsOj = GameObject.Instantiate(buttons.gameObject);
        //btnsOj.transform.SetParent(GTPlayer.Instance.transform);
        //var follow = btnsOj.transform.AddComponent<MenuComponents.FollowMenu>();
        //follow.Target = GTPlayer.Instance.transform;
        //follow.Position = Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        //follow.Rotation = Quaternion.identity;
        
        btnsOj.transform.position =
            Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        btnsOj.transform.localRotation = Quaternion.identity;
        btnsOj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform); //GTPlayer.Instance.transform
        btnsOj.transform.localScale = Vector3.one;
    }

    private static void RotateOutfits()
    {
        var controller = CosmeticsController.instance;
        if (controller == null)
        {
            Debug.Log("[TUP] CosmeticsController not found");
            return;
        }

        FieldInfo savedField = controller.GetType().GetField(
            "savedOutfits",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
        );

        if (savedField == null)
        {
            Debug.LogError("[TUP] 'savedOutfits' field not found");
            return;
        }

        var outfits = savedField.GetValue(controller) as Array;
        if (outfits == null || outfits.Length == 0)
            return;

        currentOutfitIndex = (currentOutfitIndex + 1) % outfits.Length;
        controller.LoadSavedOutfit(currentOutfitIndex);
        Debug.Log($"[TUP] Rotated to outfit slot {currentOutfitIndex + 1}");
    }

    private static int NextOutfitNumber()
    {
        for (int i = 1; i <= 10; i++)
            if (!savedOutfitSlots.ContainsKey(i)) return i;
        return -1;
    }

    private static void SaveCurrentOutfit()
    {
        if (savedOutfitSlots.Count >= 10) return;

        int n = NextOutfitNumber();
        if (n < 0) return;
        int slotIdx = currentOutfitIndex;

        savedOutfitSlots[n] = slotIdx;
        Actions["Cosmetics"].Actions["Saved Outfit #" + n] = new ModAction(() => LoadOutfitSlot(slotIdx));

        SaveButtonStates();
        Main.Instance?.AppendOutfitButton(n);

        Debug.Log($"[TUP] Saved outfit slot {slotIdx + 1} as 'Saved Outfit #{n}'");
    }

    private static void LoadOutfitSlot(int index)
    {
        var controller = CosmeticsController.instance;
        if (controller == null) return;

        controller.LoadSavedOutfit(index);
        currentOutfitIndex = index;
        Debug.Log($"[TUP] Loaded saved outfit slot {index + 1}");
    }

    public static void InitOutfitSlotActions()
    {
        int count = 5; // GT default fallback

        var controller = CosmeticsController.instance;
        if (controller != null)
        {
            FieldInfo savedField = controller.GetType().GetField(
                "savedOutfits",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            );
            if (savedField != null)
            {
                var outfits = savedField.GetValue(controller) as Array;
                if (outfits != null && outfits.Length > 0)
                    count = outfits.Length;
            }
        }

        Actions["Cosmetics"].Actions.Clear();
        Actions["Cosmetics"].Actions["Rotate Outfit"] = new ModAction(RotateOutfits, false);
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            int n    = i + 1;
            Actions["Cosmetics"].Actions["Saved Outfit #" + n] = new ModAction(() => LoadOutfitSlot(slot));
        }
    }

    public static string GetOutfitDisplayName(int n)
        => outfitSlotNames.TryGetValue(n, out string name) ? name : "Outfit Slot #" + n;

    public static void StartRename(int outfitN, TMP_Text label, GameObject keyboard)
    {
        if (isRenaming) return;
        isRenaming        = true;
        renamingOutfitN   = outfitN;
        renamingLabel     = label;
        renameKeyboard    = keyboard;
        originalLabel     = label.text;
        currentRenameText = outfitSlotNames.TryGetValue(outfitN, out string saved) ? saved : "";
        if (renamingLabel != null) renamingLabel.text = currentRenameText;

        SmoothFollowMenu smooth = Main.menuObj?.GetComponent<SmoothFollowMenu>();
        if (smooth != null) smooth.Frozen = true;

        Main.ShowKeyboard(keyboard);
    }

    public static void TypeChar(char c)
    {
        if (!isRenaming || currentRenameText.Length >= 10) return;
        currentRenameText += c;
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
    }

    public static void DeleteChar()
    {
        if (!isRenaming || currentRenameText.Length == 0) return;
        currentRenameText = currentRenameText[..^1];
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
    }

    public static void ConfirmRename()
    {
        if (!isRenaming) return;
        if (currentRenameText.Length > 0)
        {
            outfitSlotNames[renamingOutfitN] = currentRenameText;
            if (renamingLabel != null) renamingLabel.text = currentRenameText;
            SaveButtonStates();
        }
        else
        {
            if (renamingLabel != null) renamingLabel.text = GetOutfitDisplayName(renamingOutfitN);
        }
        CloseRename();
    }

    public static void CancelRename()
    {
        if (!isRenaming) return;
        if (renamingLabel != null) renamingLabel.text = originalLabel;
        CloseRename();
    }

    private static void CloseRename()
    {
        SmoothFollowMenu smooth = Main.menuObj?.GetComponent<SmoothFollowMenu>();
        if (smooth != null) smooth.Frozen = false;

        Main.HideKeyboard(renameKeyboard);

        isRenaming      = false;
        renamingLabel   = null;
        renameKeyboard  = null;
        renamingOutfitN = -1;
    }

    public static void DeleteSavedOutfit(int n)
    {
        savedOutfitSlots.Remove(n);
        outfitSlotNames.Remove(n);
        Actions["Cosmetics"].Actions.Remove("Saved Outfit #" + n);
        SaveButtonStates();
        Main.Instance?.RefreshCurrentPage();
        Debug.Log($"[TUP] Deleted 'Saved Outfit #{n}'");
    }
}


public static class ComponentExtensions
{
    public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
    {
        T comp = obj.GetComponent<T>();
        if (comp == null)
            comp = obj.AddComponent<T>();
        return comp;
    }
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

public class ClickSoundEntry
{
    public string Name;
    public AudioClip Clip;

    public ClickSoundEntry(string name, AudioClip clip)
    {
        Name = name;
        Clip = clip;
    }
}
