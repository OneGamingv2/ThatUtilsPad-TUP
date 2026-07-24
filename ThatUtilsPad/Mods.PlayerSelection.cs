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
private sealed class VoiceComponents
{
    public Speaker Speaker;
    public AudioSource Source;
}

private static readonly Dictionary<VRRig, VoiceComponents> voiceComponentsByRig = new Dictionary<VRRig, VoiceComponents>();
private static readonly List<VRRig> deadVolumeRigs = new List<VRRig>();
private static readonly RaycastHit[] checkerRaycastBuffer = new RaycastHit[64];
private static LineRenderer checkerLineRenderer;
private static bool checkerLineConfigured;
private static Material checkerSphereMaterial;
private static float nextSelectedPlayerRefreshTime;
private const float SelectedPlayerRefreshInterval = 0.5f;

private const float MinSliderVolume = 0.1f;
private const float MaxSliderVolume = 2f;
private static bool muteElseToggled;
private static readonly Dictionary<VRRig, float> mutedPreviousVolumes = new Dictionary<VRRig, float>();
private static readonly Dictionary<VRRig, float> muteElsePreviousVolumes = new Dictionary<VRRig, float>();

private static VoiceComponents GetVoiceComponents(VRRig rig)
{
    if (rig == null) return null;
    if (voiceComponentsByRig.TryGetValue(rig, out VoiceComponents cached) &&
        cached.Speaker != null && cached.Source != null)
        return cached;

    Speaker speaker = rig.GetComponentInChildren<Speaker>();
    if (speaker == null)
        return null;
    AudioSource source = speaker.GetComponent<AudioSource>();
    if (source == null)
        return null;

    cached = new VoiceComponents { Speaker = speaker, Source = source };
    voiceComponentsByRig[rig] = cached;
    return cached;
}

private static void SetVoiceVolume(VRRig rig, float volume)
{
    if (rig == null) return;

    VoiceComponents components = GetVoiceComponents(rig);
    if (components == null) return;
    components.Source.volume = Mathf.Clamp(volume, 0f, 2f);
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
    
    deadVolumeRigs.Clear();
    foreach (VRRig rig in volumes.Keys)
        if (rig == null)
            deadVolumeRigs.Add(rig);
    for (int i = 0; i < deadVolumeRigs.Count; i++)
    {
        VRRig r = deadVolumeRigs[i];
        volumes.Remove(r);
        currentVolumes.Remove(r);
        voiceComponentsByRig.Remove(r);
    }
    deadVolumeRigs.Clear();
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
    if (Mathf.Abs(displayedVolume - targetVolume) < 0.001f)
        return;
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
    private static bool selectTriggerWasHeld;
    private static VRRig highlightedAimRig;
    private static readonly Color32 SelectedBoneColor = new Color32(203, 166, 247, 255);
    private static readonly Color32 AimBoneColor = new Color32(161, 98, 237, 180);

    private static IEnumerator CheckerLoop()
    {
        while (checkerEnabled)
        {
            UpdateChecker();
            yield return null;
        }

        CleanupCheckerVisuals(false);
        checkerCoroutine = null;
    }

    private static void CleanupCheckerVisuals(bool clearSelection)
    {
        if (checkerLine != null)
        {
            Object.Destroy(checkerLine);
            checkerLine = null;
            checkerLineRenderer = null;
            checkerLineConfigured = false;
        }

        if (checkerSphere != null)
        {
            Object.Destroy(checkerSphere);
            checkerSphere = null;
        }

        ClearAimHighlight();
        SetBoneHighlightVisible(selectedRig, false);

        if (clearSelection)
            ClearSelectedPlayer(true);

        lastTargetRig = null;
        selectTriggerWasHeld = false;
    }

    private static void ToggleChecker()
    {
        checkerEnabled = GetSavedToggle("Select User", checkerEnabled);
        SavedToggleStates["Select User"] = checkerEnabled;
        SaveButtonStates();

        if (checkerEnabled)
        {
            if (checkerCoroutine == null && CoroutineHandler.Instance != null)
                checkerCoroutine = CoroutineHandler.Instance.StartCoroutine(CheckerLoop());
        }
        else
        {
            CleanupCheckerVisuals(false);
        }
    }

    private static bool SelectJustPressed(bool selectHeld)
    {
        bool pressed = selectHeld && !selectTriggerWasHeld;
        selectTriggerWasHeld = selectHeld;
        return pressed;
    }

    private static void ClearAimHighlight()
    {
        if (highlightedAimRig != null && highlightedAimRig != selectedRig)
            SetBoneHighlightVisible(highlightedAimRig, false);
        highlightedAimRig = null;
        snappedRig = null;
    }

    private static void ClearSelectedPlayer(bool clearUi)
    {
        if (selectedRig != null)
            SetBoneHighlightVisible(selectedRig, false);

        selectedRig = null;
        selectedUserId = null;

        if (clearUi)
            Main.Instance?.ClearCheckerSelectionUi();
    }

    private static bool TryResolveSelectedRig()
    {
        if (IsRigValid(selectedRig))
            return true;

        if (string.IsNullOrEmpty(selectedUserId))
        {
            selectedRig = null;
            return false;
        }

        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (!IsRigValid(rig) || rig.isLocal)
                continue;
            if (!GetRigID(rig, out string id) || id != selectedUserId)
                continue;

            selectedRig = rig;
            return true;
        }

        selectedRig = null;
        return false;
    }

    private static void ApplyPlayerSelection(VRRig rig)
    {
        if (!IsRigValid(rig) || rig.isLocal)
            return;

        if (selectedRig != null && selectedRig != rig)
            SetBoneHighlightVisible(selectedRig, false);

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

        UpdateVolumeUI(volumes.ContainsKey(rig) ? volumes[rig] : 1f);
        ShowPlayerInfo(rig);
        BoneHighlight(rig, SelectedBoneColor, 0.004f);
    }

    private static void CopyRoomCode()
    {
        if (PhotonNetwork.InRoom)
        {
            string roomCode = PhotonNetwork.CurrentRoom.Name;
            GUIUtility.systemCopyBuffer = roomCode;
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
    ApplyPlayerSelection(rig);
    Main.Instance?.RefreshSelectUserButtonStates();
}

public static bool IsSelectedPlayerButton(string buttonName)
{
    if (string.IsNullOrEmpty(buttonName) || !IsRigValid(selectedRig))
        return false;
    return string.Equals(buttonName, GetRigDisplayName(selectedRig), StringComparison.Ordinal);
}

public static void ClearSelectionOnMenuClose()
{
    ClearSelectedPlayer(true);
    ClearAimHighlight();
    ClearAllBoneHighlights();
}

public static void UpdateChecker()
{
    if (!checkerEnabled)
        return;

    ApplyVolumes();
    TryResolveSelectedRig();

    bool snapHeld;
    bool selectHeld;

    if (XRSettings.isDeviceActive)
    {
        snapHeld = ControllerInputPoller.instance.rightControllerGripFloat > 0.75f;
        selectHeld = ControllerInputPoller.instance.rightControllerIndexFloat > 0.75f;
    }
    else
    {
        snapHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
        selectHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    bool selectPressed = SelectJustPressed(selectHeld);

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
    {
        checkerLine = new GameObject("CheckerLine");
        checkerLineRenderer = checkerLine.AddComponent<LineRenderer>();
    }
    if (checkerLineRenderer == null)
        checkerLineRenderer = checkerLine.GetComponent<LineRenderer>() ?? checkerLine.AddComponent<LineRenderer>();

    LineRenderer line = checkerLineRenderer;
    if (!checkerLineConfigured)
    {
        if (lineMat == null)
            lineMat = new Material(ShaderCache.TextShader);
        line.sharedMaterial = lineMat;
        line.startWidth = 0.002f;
        line.endWidth = 0.002f;
        line.positionCount = 2;
        line.startColor = new Color32(161, 98, 237, 255);
        line.endColor = new Color32(203, 166, 247, 255);
        checkerLineConfigured = true;
    }

    int hitCount = Physics.RaycastNonAlloc(ray, checkerRaycastBuffer, 512f, NoInvisLayerMask());
    RaycastHit[] hits = checkerRaycastBuffer;
    if (hitCount == checkerRaycastBuffer.Length)
    {
        hits = Physics.RaycastAll(ray, 512f, NoInvisLayerMask());
        hitCount = hits.Length;
    }

    float minDistance = float.MaxValue;
    float firstHitDistance = float.MaxValue;
    VRRig targetRig = null;

    RaycastHit firstHit = default;
    bool firstHitFound = false;

    for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
    {
        RaycastHit h = hits[hitIndex];
        if (h.collider == null)
            continue;

        if (h.collider.GetComponentInParent<ButtonPresser>() != null)
            continue;

        VRRig hitRig = h.collider.GetComponentInParent<VRRig>();
        if (hitRig != null && (!IsRigValid(hitRig) || hitRig.isLocal))
            continue;

        if (h.distance < firstHitDistance)
        {
            firstHit = h;
            firstHitFound = true;
            firstHitDistance = h.distance;
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

        if (!IsRigValid(snappedRig) && targetRig != null)
            snappedRig = targetRig;

        if (IsRigValid(snappedRig))
        {
            Transform head = snappedRig.headMesh != null
                ? snappedRig.headMesh.transform
                : snappedRig.transform;
            if (head != null)
                endPos = head.position;

            if (highlightedAimRig != snappedRig && highlightedAimRig != selectedRig)
                SetBoneHighlightVisible(highlightedAimRig, false);

            highlightedAimRig = snappedRig;
            if (snappedRig != selectedRig)
                BoneHighlight(snappedRig, AimBoneColor, 0.003f);

            if (selectPressed)
            {
                if (selectedRig == snappedRig)
                    ClearSelectedPlayer(true);
                else
                    ApplyPlayerSelection(snappedRig);
            }
        }
        else
        {
            ClearAimHighlight();
            if (selectPressed)
                ClearSelectedPlayer(true);
        }
    }
    else
    {
        ClearAimHighlight();
        if (selectPressed && targetRig != null)
            ApplyPlayerSelection(targetRig);
        else if (selectPressed && targetRig == null && IsRigValid(selectedRig))
            ClearSelectedPlayer(true);
    }

    if (TryResolveSelectedRig())
    {
        BoneHighlight(selectedRig, SelectedBoneColor, 0.004f);

        if (Time.time >= nextSelectedPlayerRefreshTime)
        {
            nextSelectedPlayerRefreshTime = Time.time + SelectedPlayerRefreshInterval;
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
                UpdateSelectedPlayerProperties(selectedRig);
            }
            catch
            {

            }
        }
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
            if (checkerSphereMaterial == null)
            {
                checkerSphereMaterial = new Material(ShaderCache.TextShader);
                checkerSphereMaterial.color = new Color32(203, 166, 247, 255);
            }
            rend.sharedMaterial = checkerSphereMaterial;
        }
    }

    checkerSphere.transform.position = currentBeamEnd;
    lastTargetRig = targetRig;
}

private static FieldInfo _historyField;
private static PropertyInfo _countProp;
private static PropertyInfo _indexerProp;
private static FieldInfo _timeField;
private static readonly object[] PingFirstIndexArguments = { 0 };

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
        if (_historyField == null)
            return int.MaxValue;

        object history = _historyField.GetValue(rig);
        if (history == null)
            return int.MaxValue;

        Type historyType = history.GetType();
        if (_countProp == null || _countProp.DeclaringType == null || !_countProp.DeclaringType.IsAssignableFrom(historyType))
            _countProp = historyType.GetProperty("Count");
        if (_indexerProp == null || _indexerProp.DeclaringType == null || !_indexerProp.DeclaringType.IsAssignableFrom(historyType))
            _indexerProp = historyType.GetProperty("Item");
        if (_countProp == null || _indexerProp == null)
            return int.MaxValue;

        int currentCount = (int)(_countProp.GetValue(history) ?? 0);
        if (currentCount <= 0)
            return int.MaxValue;

        object item = _indexerProp.GetValue(history, PingFirstIndexArguments);
        if (item == null)
            return int.MaxValue;

        Type itemType = item.GetType();
        if (_timeField == null || _timeField.DeclaringType == null || !_timeField.DeclaringType.IsAssignableFrom(itemType))
        {
            _timeField = itemType.GetField(
                "time",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
        if (_timeField == null)
            return int.MaxValue;

        double time = Convert.ToDouble(_timeField.GetValue(item) ?? 0d);
        double ping = Math.Abs((time - PhotonNetwork.Time) * 1000);
        return (int)Math.Clamp(Math.Round(ping), 0, int.MaxValue);
    }
    catch
    {
        return int.MaxValue;
    }
}

private struct RigPingCache
{
    public float NextUpdateTime;
    public int Ping;
}

private static readonly Dictionary<VRRig, RigPingCache> pingCacheByRig = new Dictionary<VRRig, RigPingCache>();

private static int GetPingThrottled(VRRig rig)
{
    if (rig == null)
        return int.MaxValue;

    float now = UnityEngine.Time.time;
    if (pingCacheByRig.TryGetValue(rig, out RigPingCache cached) && now < cached.NextUpdateTime)
        return cached.Ping;

    int ping = GetPing(rig);
    pingCacheByRig[rig] = new RigPingCache
    {
        Ping = ping,
        NextUpdateTime = now + 1f
    };
    return ping;
}

private static void RemovePingCache(VRRig rig)
{
    if (!ReferenceEquals(rig, null))
        pingCacheByRig.Remove(rig);
}

private static void ClearPingCaches()
{
    pingCacheByRig.Clear();
    _historyField = null;
    _countProp = null;
    _indexerProp = null;
    _timeField = null;
}

private sealed class BoneHighlightCache
{
    public readonly LineRenderer[] Lines = new LineRenderer[19];
    public readonly Transform[] Starts = new Transform[19];
    public readonly Transform[] Ends = new Transform[19];
    public bool Visible;
}

private static readonly Dictionary<VRRig, BoneHighlightCache> boneESP =
    new Dictionary<VRRig, BoneHighlightCache>();
private static Material boneHighlightMaterial;

public static readonly int[] bones = {
    4, 3, 5, 4, 19, 18, 20, 19, 3, 18,
    21, 20, 22, 21, 25, 21, 29, 21, 31, 29,
    27, 25, 24, 22, 6, 5, 7, 6, 10, 6,
    14, 6, 16, 14, 12, 10, 9, 7
};

private static void EnsureBoneHighlightMaterial()
{

    if (boneHighlightMaterial != null &&
        ShaderCache.TextShader != null &&
        boneHighlightMaterial.shader == ShaderCache.TextShader)
    {
        ClearAllBoneHighlights();
        UnityEngine.Object.Destroy(boneHighlightMaterial);
        boneHighlightMaterial = null;
    }

    if (boneHighlightMaterial != null)
        return;

    Shader shader = ShaderCache.UberShader != null ? ShaderCache.UberShader : Shader.Find("GorillaTag/UberShader");
    if (shader == null)
        shader = Shader.Find("Universal Render Pipeline/Unlit");
    if (shader == null)
        shader = Shader.Find("Sprites/Default");

    boneHighlightMaterial = new Material(shader);
    if (boneHighlightMaterial.HasProperty("_Color"))
        boneHighlightMaterial.color = Color.white;
}

private static void BoneHighlight(VRRig rig, Color color, float width = 0.02f)
{
    if (rig == null || rig.isLocal || !IsRigValid(rig))
        return;

    EnsureBoneHighlightMaterial();

    if (!boneESP.TryGetValue(rig, out BoneHighlightCache cache))
    {
        cache = new BoneHighlightCache();
        Transform[] skinBones = rig.mainSkin.bones;
        for (int i = 0; i < 19; i++)
        {
            int startIndex = bones[i * 2];
            int endIndex = bones[i * 2 + 1];
            if (startIndex >= skinBones.Length || endIndex >= skinBones.Length)
                continue;

            cache.Starts[i] = skinBones[startIndex];
            cache.Ends[i] = skinBones[endIndex];
            if (cache.Starts[i] == null || cache.Ends[i] == null)
                continue;

            GameObject lineObject = new GameObject("TUP_BoneLine_" + i);
            lineObject.transform.SetParent(null, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = boneHighlightMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.allowOcclusionWhenDynamic = true;
            cache.Lines[i] = line;
        }
        boneESP[rig] = cache;
    }

    cache.Visible = true;
    for (int i = 0; i < 19; i++)
    {
        LineRenderer line = cache.Lines[i];
        Transform start = cache.Starts[i];
        Transform end = cache.Ends[i];
        if (line == null || start == null || end == null)
            continue;

        if (!line.enabled)
            line.enabled = true;

        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.SetPosition(0, start.position);
        line.SetPosition(1, end.position);
    }
}

private static void SetBoneHighlightVisible(VRRig rig, bool visible)
{
    if (rig == null || !boneESP.TryGetValue(rig, out BoneHighlightCache cache))
        return;

    cache.Visible = visible;
    for (int i = 0; i < cache.Lines.Length; i++)
    {
        LineRenderer line = cache.Lines[i];
        if (line != null)
            line.enabled = visible;
    }
}

public static void DisableBoneHighlight(VRRig rig)
{
    if (rig == null || !boneESP.TryGetValue(rig, out BoneHighlightCache cache))
        return;

    for (int i = 0; i < cache.Lines.Length; i++)
    {
        if (cache.Lines[i] != null)
            Object.Destroy(cache.Lines[i].gameObject);
    }

    boneESP.Remove(rig);
}

private static void ClearAllBoneHighlights()
{
    foreach (var kvp in boneESP)
    {
        BoneHighlightCache cache = kvp.Value;
        if (cache == null)
            continue;
        for (int i = 0; i < cache.Lines.Length; i++)
        {
            if (cache.Lines[i] != null)
                Object.Destroy(cache.Lines[i].gameObject);
        }
    }
    boneESP.Clear();
}
    
    private static void ShowPlayerInfo(VRRig rig)
    {
        nextSelectedPlayerRefreshTime = Time.time + SelectedPlayerRefreshInterval;
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
        Main.Instance?.SetMoreInfoVisible(true);
        Main.Instance?.StartPlayerModelPreview(rig);
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
                InvalidatePropertyScanCache(player);
                Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);

                List<string> legalMods = hits.Keys
                    .Select(key => propertySignatures[key])
                    .Where(signature => signature.IsLegal)
                    .Select(signature => FormatDetectedModName(signature.Name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                List<string> illegalMods = hits.Keys
                    .Select(key => propertySignatures[key])
                    .Where(signature => !signature.IsLegal)
                    .Select(signature => FormatDetectedModName(signature.Name))
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
