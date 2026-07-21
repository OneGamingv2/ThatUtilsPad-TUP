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

private static Dictionary<VRRig, float> volumes = new Dictionary<VRRig, float>();

private const float MinSliderVolume = 0.1f;
private const float MaxSliderVolume = 2f;
private static bool muteElseToggled;
private static readonly Dictionary<VRRig, float> mutedPreviousVolumes = new Dictionary<VRRig, float>();
private static readonly Dictionary<VRRig, float> muteElsePreviousVolumes = new Dictionary<VRRig, float>();

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
private static float Slider01ToVolume(float value) => Mathf.Lerp(MinSliderVolume, MaxSliderVolume, Mathf.Clamp01(value));
private static float VolumeToSlider01(float volume)
{
    if (volume <= 0.001f)
        return 0f;

    return Mathf.InverseLerp(MinSliderVolume, MaxSliderVolume, Mathf.Clamp(volume, MinSliderVolume, MaxSliderVolume));
}

private static void UpdateVolumeUI(float targetVolume)
{
    targetVolume = Mathf.Clamp(targetVolume, 0f, MaxSliderVolume);
    displayedVolume = targetVolume;
    Main.Instance?.SetVolumeDisplayVolume(targetVolume);

    if (Main.volumeText == null) return;
    int percent = Mathf.RoundToInt(targetVolume * 100f);
    Main.volumeText.text = $"Volume - {percent}%";
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

public static void SetSelectedVolumeFromSlider(float value)
{
    if (selectedRig == null) { Debug.LogWarning("[TUP VOLUME SLIDER] ignored: no selected player rig"); return; }

    float newVol = Slider01ToVolume(value);
    SetRigVolume(selectedRig, newVol, true);
    mutedPreviousVolumes.Remove(selectedRig);
    UpdateVolumeUI(newVol);
    SaveVolumes();
}

public static float GetSelectedVolume01()
{
    if (selectedRig == null) return VolumeToSlider01(1f);
    return VolumeToSlider01(volumes.ContainsKey(selectedRig) ? volumes[selectedRig] : 1f);
}

public static float GetSelectedVolume()
{
    if (selectedRig == null) return 1f;
    return volumes.TryGetValue(selectedRig, out float volume) ? volume : 1f;
}

private static void SetRigVolume(VRRig rig, float volume, bool saveManual)
{
    if (rig == null) return;

    float clamped = Mathf.Clamp(volume, 0f, MaxSliderVolume);
    volumes[rig] = clamped;
    if (!currentVolumes.ContainsKey(rig))
        currentVolumes[rig] = clamped;

    if (saveManual && GetRigID(rig, out string id))
        manuallyAdjusted.Add(id);
}

private static float GetRigVolume(VRRig rig)
{
    if (rig == null) return 1f;
    return volumes.TryGetValue(rig, out float volume) ? volume : 1f;
}

private static void VolumeUp()
{
    if (selectedRig == null) return;

    float newVol = Mathf.Clamp(GetRigVolume(selectedRig) + 0.1f, MinSliderVolume, MaxSliderVolume);
    SetRigVolume(selectedRig, newVol, true);
    mutedPreviousVolumes.Remove(selectedRig);
    UpdateVolumeUI(newVol);
    SaveVolumes();
}

private static void VolumeDown()
{
    if (selectedRig == null) return;

    float newVol = Mathf.Clamp(GetRigVolume(selectedRig) - 0.1f, 0f, MaxSliderVolume);
    SetRigVolume(selectedRig, newVol, true);
    mutedPreviousVolumes.Remove(selectedRig);
    UpdateVolumeUI(newVol);
    SaveVolumes();
}

private static void Mute()
{
    if (selectedRig == null) return;

    float current = GetRigVolume(selectedRig);
    bool shouldMute = current > 0.001f;
    if (shouldMute)
    {
        mutedPreviousVolumes[selectedRig] = Mathf.Clamp(current, MinSliderVolume, MaxSliderVolume);
        SetRigVolume(selectedRig, 0f, true);
        UpdateVolumeUI(0f);
    }
    else
    {
        float restore = mutedPreviousVolumes.TryGetValue(selectedRig, out float previous) ? previous : 1f;
        mutedPreviousVolumes.Remove(selectedRig);
        SetRigVolume(selectedRig, Mathf.Clamp(restore, MinSliderVolume, MaxSliderVolume), true);
        UpdateVolumeUI(restore);
    }

    SaveVolumes();
}

private static void MuteElse()
{
    if (selectedRig == null) return;

    muteElseToggled = !muteElseToggled;
    if (muteElseToggled)
        muteElsePreviousVolumes.Clear();

    foreach (VRRig rig in Object.FindObjectsOfType<VRRig>())
    {
        if (rig == null || rig.isLocal || rig == selectedRig) continue;

        if (muteElseToggled)
        {
            float current = GetRigVolume(rig);
            if (current > 0.001f)
                muteElsePreviousVolumes[rig] = Mathf.Clamp(current, MinSliderVolume, MaxSliderVolume);
            SetRigVolume(rig, 0f, true);
        }
        else
        {
            float restore = muteElsePreviousVolumes.TryGetValue(rig, out float previous) ? previous : 1f;
            SetRigVolume(rig, Mathf.Clamp(restore, MinSliderVolume, MaxSliderVolume), true);
        }
    }

    if (!muteElseToggled)
        muteElsePreviousVolumes.Clear();

    SaveVolumes();
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
            lastTargetRig = null;
        }
        checkerCoroutine = null;
    }
    
    private static void ToggleChecker()
    {
        checkerEnabled = !checkerEnabled;

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
        }
        else
        {
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
    
    private static bool IsLocalPlayerCollider(Collider collider)
    {
        if (collider == null)
            return false;

        Transform hit = collider.transform;
        if (GTPlayer.Instance != null && hit.IsChildOf(GTPlayer.Instance.transform))
            return true;

        if (GorillaTagger.Instance != null)
        {
            if (GorillaTagger.Instance.offlineVRRig != null && hit.IsChildOf(GorillaTagger.Instance.offlineVRRig.transform))
                return true;

            if (GorillaTagger.Instance.bodyCollider != null && (hit == GorillaTagger.Instance.bodyCollider.transform || hit.IsChildOf(GorillaTagger.Instance.bodyCollider.transform)))
                return true;
        }

        return false;
    }
public static List<VRRig> GetSelectableRigs()
{
    List<VRRig> rigs = new List<VRRig>();
    foreach (VRRig rig in VRRigCache.ActiveRigs)
    {
        if (!IsRigValid(rig) || rig.isLocal)
            continue;

        rigs.Add(rig);
    }

    return rigs;
}

public static string GetRigDisplayName(VRRig rig)
{
    if (!IsRigValid(rig))
        return "Unknown";

    string name = rig.playerNameVisible;
    if (string.IsNullOrWhiteSpace(name))
        name = "Player";

    name = name.Replace("\r", " ").Replace("\n", " ").Trim();
    return name.Length > 16 ? name.Substring(0, 16) : name;
}

public static void SelectRigFromMenu(VRRig rig)
{
    if (!IsRigValid(rig) || rig.isLocal)
        return;

    selectedRig = rig;
    snappedRig = rig;

    if (GetRigID(rig, out string id))
    {
        selectedUserId = id;
        if (savedVolumes.ContainsKey(id))
        {
            volumes[rig] = savedVolumes[id];
            currentVolumes[rig] = savedVolumes[id];
        }
    }

    displayedVolume = volumes.ContainsKey(rig) ? volumes[rig] : 1f;
    ShowPlayerInfo(rig);
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
        if (selectedRig != null)
            Main.Instance?.UpdateCheckerProperties("None", "None");

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

    foreach (RaycastHit h in hits.OrderBy(hit => hit.distance))
    {
        if (h.collider == null)
            continue;

        if (h.collider.GetComponentInParent<ButtonPresser>() != null)
            continue;

        VRRig hitRig = h.collider.GetComponentInParent<VRRig>();
        if (hitRig != null && (!IsRigValid(hitRig) || hitRig.isLocal))
            continue;

        if (!firstHitFound)
        {
            firstHit = h;
            firstHitFound = true;
        }

        if (IsRigValid(hitRig) && h.distance < minDistance)
        {
            minDistance = h.distance;
            targetRig = hitRig;
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

            int fps = RigBits.GetFPS(selectedRig);

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
        currentBeamEnd = Vector3.SmoothDamp(currentBeamEnd, endPos, ref beamVelocity, 0.055f, 80f, Time.deltaTime);

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

private static bool doorsOpenAlready = false;

private static void InitReflection(object historySample, object itemSample)
{
    if (doorsOpenAlready) return;

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

    doorsOpenAlready = true;
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
        
        if (!doorsOpenAlready)
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

            doorsOpenAlready = true;
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
private static int pingInPocket = int.MaxValue;

private static int GetPingThrottled(VRRig rig)
{
    if (UnityEngine.Time.time >= _nextUpdateTime)
    {
        pingInPocket = GetPing(rig);
        _nextUpdateTime = UnityEngine.Time.time + 1f;
    }

    return pingInPocket;
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


        for (int i = 0; i < 19; i++)
        {
            LineRenderer line = rig.mainSkin.bones[bones[i * 2]].gameObject.GetOrAddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("GUI/Text Shader"));
            lines.Add(line);
        }

        boneESP.Add(rig, lines);
    }



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
        int fps = RigBits.GetFPS(rig);
        string platform = GetPlatform(rig);

        string colorStr =
            $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";


        string creationDate = "Unknown";
        try { creationDate = GetCreationDate(GetPlayerFromVRRig(rig).UserId); }
        catch (Exception e) { Debug.LogWarning("[TUP CHECKER] Failed to get creation date: " + e.Message); }

        int ping = 0;
        try { ping = GetPingThrottled(rig); }
        catch (Exception e) { Debug.LogWarning("[TUP CHECKER] Failed to get ping: " + e.Message); }

        Main.Instance?.UpdateCheckerText(name, fps, platform, creationDate, colorStr, color, ping);
        UpdateSelectedPlayerProperties(rig);
    }

    private static void UpdateSelectedPlayerProperties(VRRig rig)
    {
        string legalText = "None";
        string illegalText = "None";

        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null)
            {
                Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);

                List<string> legalMods = hits.Keys
                    .Select(key => propertySignatures[key])
                    .Where(signature => signature.IsLegal)
                    .Select(signature => signature.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                List<string> illegalMods = hits.Keys
                    .Select(key => propertySignatures[key])
                    .Where(signature => !signature.IsLegal)
                    .Select(signature => signature.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (legalMods.Count > 0)
                    legalText = string.Join(", ", legalMods);

                if (illegalMods.Count > 0)
                    illegalText = string.Join(", ", illegalMods);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP PROP SCAN] Failed to scan selected player: " + e.Message);
        }

        Main.Instance?.UpdateCheckerProperties(legalText, illegalText);
    }
}
