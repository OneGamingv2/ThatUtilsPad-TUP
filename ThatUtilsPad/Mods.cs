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
using Debug = UnityEngine.Debug;

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

    private const string NotificationPrefabPath = "assets/prefabs/Notification-PLS.prefab";
    private const float NotificationBaseWidth = 11.55f;
    private const float NotificationWidthPerCharacter = 2.025f;
    private const float NotificationBaseY = -0.08f;
    private const float NotificationStackSpacing = 0.025f;
    private const float NotificationDefaultDuration = 3f;
    private const float NotificationWidthAnimDuration = 0.35f;
    private const float NotificationTextFadeInDuration = 0.25f;
    private const float NotificationTextFadeOutDuration = 0.125f;
    private const float NotificationTextScaleDelay = 0.03f;
    private const float NotificationMoveDuration = 0.15f;
    private const float NotificationMoveWaveDelay = 0.05f;
    private const int NotificationMaxVisible = 5;
    private static readonly List<NotificationEntry> activeNotifications = new List<NotificationEntry>();
    private static readonly Queue<NotificationRequest> queuedNotifications = new Queue<NotificationRequest>();
    private static Coroutine notificationQueueRoutine;
    private static float notificationStackSettlesAt;
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
    private static Dictionary<string, bool> propertyLegality;
    private static Dictionary<string, PropertySignature> propertySignatures;

    private sealed class PropertySignature
    {
        public readonly string Name;
        public readonly bool IsLegal;
        public readonly string Section;

        public PropertySignature(string name, bool isLegal, string section)
        {
            Name = name;
            IsLegal = isLegal;
            Section = section;
        }
    }

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
            lines.Add("smoothMenuEnabled=" + smoothMenuEnabled);
            lines.Add("smoothingStrengthIndex=" + smoothingStrengthIndex);
            lines.Add("menuScaleIndex=" + menuScaleIndex);
            lines.Add("headDistanceIndex=" + headDistanceIndex);
            lines.Add("doubleClickOpenEnabled=" + doubleClickOpenEnabled);
            lines.Add("menuOpenBind=" + menuOpenBindCode);
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
        if (!System.IO.File.Exists(statesFilePath))
        {
            SavedToggleStates["Menu Smoothing"] = smoothMenuEnabled;
            SavedToggleStates["Double Open"] = doubleClickOpenEnabled;
            return;
        }
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
                else if (line.StartsWith("smoothMenuEnabled="))
                {
                    if (bool.TryParse(line.Substring(18), out bool val))
                        smoothMenuEnabled = val;
                }
                else if (line.StartsWith("smoothingStrengthIndex="))
                {
                    if (int.TryParse(line.Substring(23), out int idx))
                        smoothingStrengthIndex = Mathf.Clamp(idx, 0, smoothingStrengthNames.Length - 1);
                }
                else if (line.StartsWith("menuScaleIndex="))
                {
                    if (int.TryParse(line.Substring(15), out int idx))
                        menuScaleIndex = Mathf.Clamp(idx, 0, menuScaleNames.Length - 1);
                }
                else if (line.StartsWith("headDistanceIndex="))
                {
                    if (int.TryParse(line.Substring(18), out int idx))
                        headDistanceIndex = Mathf.Clamp(idx, 0, headDistanceNames.Length - 1);
                }
                else if (line.StartsWith("doubleClickOpenEnabled="))
                {
                    if (bool.TryParse(line.Substring(23), out bool val))
                        doubleClickOpenEnabled = val;
                }
                else if (line.StartsWith("menuOpenBind="))
                {
                    string bind = line.Substring(13).Trim();
                    if (!string.IsNullOrWhiteSpace(bind))
                        menuOpenBindCode = bind;
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

        SavedToggleStates["Menu Smoothing"] = smoothMenuEnabled;
        SavedToggleStates["Double Open"] = doubleClickOpenEnabled;
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
                    { "Region", new ModAction(CycleRegion, false, true, 5f) },
                    { "Copy Room Code", new ModAction(CopyRoomCode, false) },
                    { "Queue", new ModAction(ToggleQueue, false) },
                    { "Mode", new ModAction(ToggleMode, false) },
                    { "Custom Prop Test", new ModAction(PrintPhotonPlayerCustomProperties, false) }
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
                    { "Nametag Size", new ModAction(CycleNameTagSize, false) },
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
                    { "Click Sound", new ModAction(ToggleClickSound, false) },
                    { "Test Sound", new ModAction(Nothing, false) },
                    { "Menu Smoothing", new ModAction(SmoothMenu, true) },
                    { "Smooth Strength", new ModAction(CycleMenuSmoothingStrength, false) },
                    { "Menu Scale", new ModAction(CycleMenuScale, false) },
                    { "PC Distance", new ModAction(CycleHeadDistance, false) },
                    { "Double Open", new ModAction(ToggleDoubleOpen, true) },
                    { "Open Bind", new ModAction(StartOpenBindCapture, false) },
                    { "Theme", new ModAction(CycleTheme, false) },
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

private static void Nothing()
{
    ShowNotification("testing yipee", NotificationDefaultDuration);
}

public static void ShowNotification(string text, float duration)
{
    text ??= "";
    duration = Mathf.Max(0f, duration);

    queuedNotifications.Enqueue(new NotificationRequest(text, duration));
    if (notificationQueueRoutine == null)
        notificationQueueRoutine = GetNotificationCoroutineHandler().StartCoroutine(ProcessNotificationQueue());
}

public static void ShowNotification(string text)
{
    ShowNotification(text, NotificationDefaultDuration);
}

private static IEnumerator ProcessNotificationQueue()
{
    while (queuedNotifications.Count > 0)
    {
        while (activeNotifications.Count >= NotificationMaxVisible || Time.time < notificationStackSettlesAt)
            yield return null;

        NotificationRequest request = queuedNotifications.Dequeue();
        TryShowNotificationNow(request.Text, request.Duration);
        yield return null;
    }

    notificationQueueRoutine = null;
}

private static void TryShowNotificationNow(string text, float duration)
{
    if (Main.notifBundle == null)
    {
        Debug.LogWarning("[TUP] Notification bundle is not loaded.");
        return;
    }

    Transform headTransform = GTPlayer.Instance?.headCollider?.transform;
    if (headTransform == null)
    {
        Debug.LogWarning("[TUP] Cannot show notification without a head transform.");
        return;
    }

    GameObject prefab = Main.notifBundle.LoadAsset<GameObject>(NotificationPrefabPath);
    if (prefab == null)
    {
        Debug.LogWarning("[TUP] Notification prefab not found: " + NotificationPrefabPath);
        return;
    }

    GameObject notifObject = Object.Instantiate(prefab);
    notifObject.transform.localScale = Vector3.one * 0.02f;
    NotificationFollower follower = notifObject.AddComponent<NotificationFollower>();

    TextMeshProUGUI notifText = notifObject.transform.Find("Base/Text")?.GetComponent<TextMeshProUGUI>();
    RectTransform notifImageRect = notifObject.transform.Find("Base")?.GetComponent<RectTransform>();
    if (notifText == null || notifImageRect == null)
    {
        Debug.LogWarning("[TUP] Notification prefab is missing Base/Text or Base RectTransform.");
        Object.Destroy(notifObject);
        return;
    }

    float targetWidth = NotificationBaseWidth + (text.Length * NotificationWidthPerCharacter);
    notifText.text = text;
    notifText.alpha = 0f;
    notifImageRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);

    var entry = new NotificationEntry(notifObject, notifText, notifImageRect, follower, targetWidth);
    activeNotifications.Add(entry);
    RestackNotifications(false);

    GetNotificationCoroutineHandler().StartCoroutine(NotificationLifetime(entry, duration));
}
private static CoroutineHandler GetNotificationCoroutineHandler()
{
    if (CoroutineHandler.Instance != null)
        return CoroutineHandler.Instance;

    return new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
}

private static IEnumerator NotificationLifetime(NotificationEntry entry, float duration)
{
    yield return AnimateNotificationWidth(entry, 0f, entry.TargetWidth, NotificationWidthAnimDuration);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return FadeNotificationText(entry, 0f, 1f, NotificationTextFadeInDuration);
    yield return new WaitForSeconds(duration);

    while (entry.Object != null && activeNotifications.Count > 0 && activeNotifications[0] != entry)
        yield return null;
    yield return MoveNotification(entry, GetNotificationStackPosition(0), NotificationMoveDuration, 0f);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return FadeNotificationText(entry, 1f, 0f, NotificationTextFadeOutDuration);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return AnimateNotificationWidth(entry, entry.TargetWidth, 0f, NotificationWidthAnimDuration);

    RemoveNotification(entry);

    RestackNotifications(true);
    MarkNotificationStackSettling();
}

private static IEnumerator AnimateNotificationWidth(NotificationEntry entry, float from, float to, float duration)
{
    if (entry.Rect == null)
        yield break;

    if (duration <= 0f)
    {
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, to);
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Rect != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        t = 1f - ((1f - t) * (1f - t));
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Lerp(from, to, t));
        yield return null;
    }

    if (entry.Rect != null)
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, to);
}

private static IEnumerator FadeNotificationText(NotificationEntry entry, float from, float to, float duration)
{
    if (entry.Text == null)
        yield break;

    if (duration <= 0f)
    {
        entry.Text.alpha = to;
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Text != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        entry.Text.alpha = Mathf.Lerp(from, to, t);
        yield return null;
    }

    if (entry.Text != null)
        entry.Text.alpha = to;
}

private static void RestackNotifications(bool animate)
{
    activeNotifications.RemoveAll(entry => entry.Object == null);

    for (int i = 0; i < activeNotifications.Count; i++)
    {
        NotificationEntry entry = activeNotifications[i];
        GameObject notifObject = entry.Object;
        if (notifObject == null) continue;

        Vector3 targetPosition = GetNotificationStackPosition(i);

        if (!animate)
        {
            entry.Follower.LocalPosition = targetPosition;
            continue;
        }

        if (entry.MoveRoutine != null && CoroutineHandler.Instance != null)
            CoroutineHandler.Instance.StopCoroutine(entry.MoveRoutine);

        entry.MoveRoutine = GetNotificationCoroutineHandler().StartCoroutine(
            MoveNotification(entry, targetPosition, NotificationMoveDuration, NotificationMoveWaveDelay * i)
        );
    }
}

private static void RemoveNotification(NotificationEntry entry)
{
    activeNotifications.Remove(entry);
    if (entry.Object != null)
        Object.Destroy(entry.Object);
}

private static void MarkNotificationStackSettling()
{
    if (activeNotifications.Count <= 0)
    {
        notificationStackSettlesAt = Time.time;
        return;
    }

    notificationStackSettlesAt = Time.time + NotificationMoveDuration + (NotificationMoveWaveDelay * (activeNotifications.Count - 1));
}

private static Vector3 GetNotificationStackPosition(int index)
{
    return new Vector3(
        0f,
        NotificationBaseY + (NotificationStackSpacing * index),
        0.3f
    );
}
private static IEnumerator MoveNotification(NotificationEntry entry, Vector3 targetPosition, float duration, float delay)
{
    if (delay > 0f)
        yield return new WaitForSeconds(delay);

    if (entry.Object == null)
        yield break;

    NotificationFollower follower = entry.Follower;
    Vector3 startPosition = follower.LocalPosition;

    if (duration <= 0f)
    {
        follower.LocalPosition = targetPosition;
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Object != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        t = t * t * (3f - (2f * t));
        follower.LocalPosition = Vector3.Lerp(startPosition, targetPosition, t);
        yield return null;
    }

    if (entry.Object != null)
        follower.LocalPosition = targetPosition;

    entry.MoveRoutine = null;
}

private sealed class NotificationEntry
{
    public readonly GameObject Object;
    public readonly TextMeshProUGUI Text;
    public readonly RectTransform Rect;
    public readonly NotificationFollower Follower;
    public readonly float TargetWidth;
    public Coroutine MoveRoutine;

    public NotificationEntry(GameObject obj, TextMeshProUGUI text, RectTransform rect, NotificationFollower follower, float targetWidth)
    {
        Object = obj;
        Text = text;
        Rect = rect;
        Follower = follower;
        TargetWidth = targetWidth;
    }
}

private readonly struct NotificationRequest
{
    public readonly string Text;
    public readonly float Duration;

    public NotificationRequest(string text, float duration)
    {
        Text = text;
        Duration = duration;
    }
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
    
    public static void PrintPhotonPlayerCustomProperties()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.Log("[TUP] Cannot print Photon custom properties: not in a room.");
            return;
        }

        EnsurePropertySignatureDictionary();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            string playerLabel = $"actor={player.ActorNumber}, name={player.NickName}, userId={player.UserId}";

            if (player.CustomProperties == null || player.CustomProperties.Count == 0)
            {
                Debug.Log($"[TUP] Photon custom properties for {playerLabel}: <none>");
                continue;
            }

            List<string> props = new List<string>();
            foreach (object key in player.CustomProperties.Keys)
            {
                object value = player.CustomProperties[key];
                props.Add($"{key}={FormatPhotonCustomPropertyValue(value)}");
            }

            Debug.Log($"[TUP] Photon custom properties for {playerLabel}: {string.Join(", ", props)}");
            PrintDetectedPropertySignatures(player, playerLabel);
        }
    }

    public static Dictionary<string, bool> GetPropertyLegalityDictionary()
    {
        EnsurePropertySignatureDictionary();
        return propertyLegality;
    }

    private static void PrintDetectedPropertySignatures(Player player, string playerLabel)
    {
        Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);

        if (hits.Count == 0)
        {
            Debug.Log($"[TUP PROP SCAN] No property-list signatures detected for {playerLabel}");
            return;
        }

        foreach (var hit in hits)
        {
            PropertySignature signature = propertySignatures[hit.Key];
            string verdict = signature.IsLegal ? "LEGAL" : "ILLEGAL";
            Debug.Log($"[TUP PROP SCAN] {verdict} signature detected for {playerLabel}: \"{signature.Name}\" ({signature.Section}) in {string.Join(", ", hit.Value)}");
        }
    }

    private static Dictionary<string, List<string>> FindPropertySignatureHits(Player player)
    {
        EnsurePropertySignatureDictionary();

        Dictionary<string, List<string>> hits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in BuildPropertyScanSources(player))
        {
            foreach (PropertySignature signature in propertySignatures.Values)
            {
                if (!TextContainsSignature(source.Value, signature.Name))
                    continue;

                if (!hits.TryGetValue(signature.Name, out List<string> sources))
                {
                    sources = new List<string>();
                    hits[signature.Name] = sources;
                }

                if (!sources.Contains(source.Key))
                    sources.Add(source.Key);
            }
        }

        return hits;
    }

    private static Dictionary<string, string> BuildPropertyScanSources(Player player)
    {
        Dictionary<string, string> sources = new Dictionary<string, string>();
        sources["PhotonPlayer"] = $"{player.NickName} {player.UserId} {player.ActorNumber}";

        if (player.CustomProperties != null)
        {
            List<string> props = new List<string>();
            foreach (object key in player.CustomProperties.Keys)
            {
                object value = player.CustomProperties[key];
                props.Add($"{key} {FormatPhotonCustomPropertyValue(value)}");
            }
            sources["CustomProperties"] = string.Join(" ", props);
        }

        VRRig rig = GetRigForPhotonPlayer(player);
        if (rig != null)
        {
            try { sources["Cosmetics"] = rig.Cosmetics(); } catch { }
            try { sources["Platform"] = rig.GetPlatform(); } catch { }
            try { sources["FPS"] = RigHelper.GetFPS(rig).ToString(); } catch { }
        }

        return sources;
    }

    private static VRRig GetRigForPhotonPlayer(Player player)
    {
        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (rig == null || rig.isLocal) continue;
            try
            {
                if (rig.GetPhotonPlayer() == player)
                    return rig;
            }
            catch { }
        }

        return null;
    }

    private static void EnsurePropertySignatureDictionary()
    {
        if (propertySignatures != null && propertyLegality != null)
            return;

        propertySignatures = new Dictionary<string, PropertySignature>(StringComparer.OrdinalIgnoreCase);
        propertyLegality = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        string text = LoadPropertyListText();
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[TUP PROP SCAN] PropertyList.txt not found or empty.");
            return;
        }

        string section = "Uncategorized";
        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (IsPropertyListSectionHeader(line))
            {
                section = line;
                continue;
            }

            if (section.Equals("Report severities", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] entries = line.Contains(",") ? line.Split(',') : new[] { line };
            foreach (string rawEntry in entries)
            {
                string entry = rawEntry.Trim();
                if (string.IsNullOrEmpty(entry))
                    continue;

                bool isLegal = IsLegalPropertySignature(section, entry);
                propertySignatures[entry] = new PropertySignature(entry, isLegal, section);
                propertyLegality[entry] = isLegal;
            }
        }

        Debug.Log($"[TUP PROP SCAN] Loaded {propertySignatures.Count} property signatures.");
    }

    private static string LoadPropertyListText()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using System.IO.Stream stream = assembly.GetManifestResourceStream("ThatUtilsPad.PropertyList.txt");
        if (stream == null)
            return "";

        using System.IO.StreamReader reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool IsPropertyListSectionHeader(string line)
    {
        switch (line.ToLowerInvariant())
        {
            case "runtime anti-cheat (red)":
            case "runtime anti-cheat (warning)":
            case "behavioral mod detections":
            case "property count flags":
            case "heuristic categories":
            case "suspicious property tokens":
            case "high-risk property keys":
            case "illegal mod hints (mods tab red flag)":
            case "report severities":
            case "signature keywords":
            case "built-in signatures":
                return true;
            default:
                return false;
        }
    }

    private static bool IsLegalPropertySignature(string section, string entry)
    {
        if (!section.Equals("Built-in signatures", StringComparison.OrdinalIgnoreCase))
            return false;

        string normalized = NormalizeForPropertyScan(entry);
        string[] illegalTokens =
        {
            "cheat", "menu", "seralyth", "seralith", "void", "shiba", "mango", "nebula", "pulsar",
            "cosmos", "hydra", "spectre", "viper", "eclipse", "phantom", "sentinel", "oblivion",
            "resurgence", "elixir", "orbit", "rexon", "cosmetx"
        };

        return !illegalTokens.Any(token => normalized.Contains(token));
    }

    private static bool TextContainsSignature(string text, string signature)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(signature))
            return false;

        string normalizedText = NormalizeForPropertyScan(text);
        string normalizedSignature = NormalizeForPropertyScan(signature);
        if (string.IsNullOrEmpty(normalizedSignature))
            return false;

        if (normalizedSignature.Length <= 3 && !normalizedSignature.All(char.IsDigit))
            return normalizedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(token => token.Equals(normalizedSignature, StringComparison.OrdinalIgnoreCase));

        if (normalizedText.Contains(normalizedSignature))
            return true;

        string compactText = normalizedText.Replace(" ", "");
        string compactSignature = normalizedSignature.Replace(" ", "");
        return compactSignature.Length > 3 && compactText.Contains(compactSignature);
    }

    private static string NormalizeForPropertyScan(string text)
    {
        char[] chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatPhotonCustomPropertyValue(object value)
    {
        if (value == null) return "null";
        if (value is string) return value.ToString();
        if (value is System.Collections.IEnumerable values)
            return "[" + string.Join(", ", values.Cast<object>().Select(FormatPhotonCustomPropertyValue)) + "]";

        return value.ToString();
    }

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

    private static readonly string[] regionCodes = { "eu", "us", "usw" };
    private static readonly string[] regionNames = { "EU", "NA East", "NA West" };
    private static int regionIndex = -1;
    private static Coroutine? regionCoroutine;

    private static void EnsureRegionIndex()
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
        EnsureRegionIndex();
        return regionNames[regionIndex];
    }

    private static void CycleRegion()
    {
        if (regionCoroutine != null)
            return;

        EnsureRegionIndex();
        regionIndex = (regionIndex + 1) % regionCodes.Length;

        if (Main.regionText != null)
            Main.regionText.text = "Region  :  " + regionNames[regionIndex];

        regionCoroutine = CoroutineHandler.Instance.StartCoroutine(ChangeRegion(regionCodes[regionIndex]));
    }

    private static IEnumerator ChangeRegion(string regionCode)
    {
        Debug.Log("[TUP] Changing Photon region to " + regionCode);

        if (PhotonNetwork.InRoom)
        {
            NetworkSystem.Instance.ReturnToSinglePlayer();
            float leaveTimeout = Time.time + 5f;
            while (PhotonNetwork.InRoom && Time.time < leaveTimeout)
                yield return null;
        }

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            float disconnectTimeout = Time.time + 10f;
            while (PhotonNetwork.IsConnected && Time.time < disconnectTimeout)
                yield return null;
        }

        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = regionCode;

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();

        regionCoroutine = null;
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

        currentClickIndex = (currentClickIndex + 1) % clickSounds.Count;
        var entry = clickSounds[currentClickIndex];
        Main.Instance.currentClickSound = entry.Clip;

        string displayName = FormatDisplayName(entry.Name);

        Debug.Log("Click Sound : " + displayName);

        SaveButtonStates();

        if (Main.clickSoundText != null)
            Main.clickSoundText.text = "Click Sound  :  " + displayName;
    }


    private static bool smoothMenuEnabled = true;

    private static readonly string[] smoothingStrengthNames = { "Soft", "Normal", "Sharp", "Locked" };
    private static readonly float[] smoothingStrengthValues = { 10f, 30f, 60f, 120f };
    private static int smoothingStrengthIndex = 1;

    private static readonly string[] menuScaleNames = { "Compact", "Normal", "Large" };
    private static readonly float[] menuScaleValues = { 0.32f, 0.375f, 0.43f };
    private static int menuScaleIndex = 1;

    private static readonly string[] headDistanceNames = { "Close", "Normal", "Far" };
    private static readonly float[] headDistanceValues = { 0.48f, 0.6f, 0.75f };
    private static int headDistanceIndex = 1;

    private static bool doubleClickOpenEnabled;
    private static string menuOpenBindCode = Main.DefaultMenuOpenBindCode;
    private static Coroutine openBindCaptureCoroutine;

    private static readonly string[] themeNames = { "Default", "Sakura" };
    private static readonly MenuThemePalette[] themePalettes =
    {
        MenuTheme.Themes.Default,
        MenuTheme.Themes.Sakura
    };
    private static int themeIndex = 1;


    private static void ToggleDoubleOpen()
    {
        doubleClickOpenEnabled = !doubleClickOpenEnabled;
        ApplySavedSettings();
        SaveButtonStates();
        Debug.Log("Double Open : " + (doubleClickOpenEnabled ? "ON" : "OFF"));
    }

    private static void StartOpenBindCapture()
    {
        if (Main.Instance == null)
            return;

        if (openBindCaptureCoroutine != null)
            Main.Instance.StopCoroutine(openBindCaptureCoroutine);

        openBindCaptureCoroutine = Main.Instance.StartCoroutine(CaptureOpenBind());
    }

    private static IEnumerator CaptureOpenBind()
    {
        HashSet<string> ignored = Main.GetPressedMenuOpenBinds();

        if (Main.openBindText != null)
            Main.openBindText.text = "Open Bind  :  Listening...";

        while (true)
        {
            if (Main.TryGetPressedMenuOpenBind(ignored, out string bindCode, out string displayName))
            {
                menuOpenBindCode = bindCode;
                ApplySavedSettings();
                SaveButtonStates();
                Debug.Log("Open Bind : " + displayName);
                break;
            }

            HashSet<string> currentlyPressed = Main.GetPressedMenuOpenBinds();
            ignored.RemoveWhere(code => !currentlyPressed.Contains(code));

            yield return null;
        }

        openBindCaptureCoroutine = null;
    }

    private static void SmoothMenu()
    {
        smoothMenuEnabled = !smoothMenuEnabled;
        ApplySavedSettings();
        SaveButtonStates();
        Debug.Log("Menu Smoothing : " + (smoothMenuEnabled ? "ON" : "OFF"));
    }

    private static void CycleMenuSmoothingStrength()
    {
        smoothingStrengthIndex = (smoothingStrengthIndex + 1) % smoothingStrengthNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
        Debug.Log("Smooth Strength : " + smoothingStrengthNames[smoothingStrengthIndex]);
    }

    private static void CycleMenuScale()
    {
        menuScaleIndex = (menuScaleIndex + 1) % menuScaleNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
        Debug.Log("Menu Scale : " + menuScaleNames[menuScaleIndex]);
    }

    private static void CycleHeadDistance()
    {
        headDistanceIndex = (headDistanceIndex + 1) % headDistanceNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
        Debug.Log("Head Distance : " + headDistanceNames[headDistanceIndex]);
    }

    private static void CycleTheme()
    {
        themeIndex = (themeIndex + 1) % themePalettes.Length;
        ApplyTheme();
        UpdateSettingsLabels();
        Debug.Log("Theme : " + GetThemeLabel());
    }

    private static void ApplyTheme()
    {
        themeIndex = Mathf.Clamp(themeIndex, 0, themePalettes.Length - 1);
        if (Main.menuObj != null)
            MenuTheme.Assign(Main.menuObj, themePalettes[themeIndex]);
        Main.Instance?.RefreshCurrentPage();
    }

    public static string GetThemeLabel() => themeNames[Mathf.Clamp(themeIndex, 0, themeNames.Length - 1)];

    public static void ApplySavedSettings()
    {
        if (Main.Instance == null) return;

        if (clickSounds.Count > 0)
        {
            currentClickIndex = Mathf.Clamp(currentClickIndex, 0, clickSounds.Count - 1);
            Main.Instance.currentClickSound = clickSounds[currentClickIndex].Clip;
        }

        Main.Instance.SetMenuSmoothing(smoothMenuEnabled, smoothingStrengthValues[smoothingStrengthIndex]);
        Main.Instance.SetMenuScale(menuScaleValues[menuScaleIndex]);
        Main.Instance.SetHeadDistance(headDistanceValues[headDistanceIndex]);
        Main.Instance.SetMenuOpenOptions(doubleClickOpenEnabled, menuOpenBindCode);
        ApplyTheme();
        UpdateSettingsLabels();
    }

    public static string GetSmoothingStrengthLabel() => smoothingStrengthNames[smoothingStrengthIndex];
    public static string GetMenuScaleLabel() => menuScaleNames[menuScaleIndex];
    public static string GetHeadDistanceLabel() => headDistanceNames[headDistanceIndex];

    public static void UpdateSettingsLabels()
    {
        if (Main.smoothingText != null)
            Main.smoothingText.text = "Menu Smoothing  :  " + (smoothMenuEnabled ? "On" : "Off");

        if (Main.smoothingStrengthText != null)
            Main.smoothingStrengthText.text = "Smooth Strength  :  " + GetSmoothingStrengthLabel();

        if (Main.menuScaleText != null)
            Main.menuScaleText.text = "Menu Scale  :  " + GetMenuScaleLabel();

        if (Main.headDistanceText != null)
            Main.headDistanceText.text = "PC Distance  :  " + GetHeadDistanceLabel();

        if (Main.doubleOpenText != null)
            Main.doubleOpenText.text = "Double Open  :  " + (doubleClickOpenEnabled ? "On" : "Off");

        if (Main.openBindText != null)
            Main.openBindText.text = "Open Bind  :  " + Main.GetMenuOpenBindDisplayName(menuOpenBindCode);

        if (Main.themeText != null)
            Main.themeText.text = "Theme  :  " + GetThemeLabel();
    }
    
    
    private static bool nameTagsEnabled = false;
    private static readonly Dictionary<VRRig, GameObject> nametags = new();
    public static AssetBundle nameBundle;
    private static GameObject nameTagPrefab;
    private static bool loggedMissingNameTagPrefab;
    private static readonly Dictionary<VRRig, int> cachedNameTagModCounts = new Dictionary<VRRig, int>();
    private static readonly Dictionary<VRRig, float> nextNameTagModScanTimes = new Dictionary<VRRig, float>();
    private static float targetNameTagScale = 0.0725f;

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
                GameObject prefab = GetNameTagPrefab();
                if (prefab == null) return;

                GameObject tag = Object.Instantiate(prefab, rig.transform, true);
                tag.transform.localScale = Vector3.one * targetNameTagScale;
                tag.SetActive(true);
                nametags.Add(rig, tag);
            }

            GameObject nametag = nametags[rig];
            
            if (nametag == null)
            {
                nametags.Remove(rig);
                continue;
            }
            
            Transform anchor = rig.headMesh != null ? rig.headMesh.transform : rig.transform;
            nametag.transform.position = anchor.position + Vector3.up * 0.87f;
            
            Camera camera = Main.GetActiveCamera();
            if (camera != null)
            {
                nametag.transform.LookAt(camera.transform.position);
                nametag.transform.Rotate(0f, 450f, 0f);
            }
            nametag.transform.localScale = Vector3.Lerp(nametag.transform.localScale, Vector3.one * targetNameTagScale, Time.deltaTime * 12f);
            if (!nametag.activeSelf)
                nametag.SetActive(true);
            
            SetTagText(nametag.transform.Find("Name"), GetPlayerFromVRRig(rig).NickName);
            SetTagText(nametag.transform.Find("FPS"), RigHelper.GetFPS(rig) + "Hz");
            SetTagText(nametag.transform.Find("Ping"), GetPingThrottled(rig) + "Ms");
            SetTagText(nametag.transform.Find("Mods"), GetDetectedModCount(rig) + "Mods");

            var iconsLeft = nametag.transform.Find("IconsLeft");

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
            
            SetPlatformIcons(iconsLeft, platform);
        }
    }
    

    private static GameObject GetNameTagPrefab()
    {
        if (nameTagPrefab != null)
            return nameTagPrefab;

        if (nameBundle == null)
            return null;

        string[] assetNames = nameBundle.GetAllAssetNames();
        string prefabPath = assetNames.FirstOrDefault(name =>
            name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
            name.IndexOf("nametag", StringComparison.OrdinalIgnoreCase) >= 0);

        if (!string.IsNullOrEmpty(prefabPath))
            nameTagPrefab = nameBundle.LoadAsset<GameObject>(prefabPath);

        if (nameTagPrefab == null)
            nameTagPrefab = nameBundle.LoadAsset<GameObject>("assets/prefabs/tup-nametagui.prefab");

        if (nameTagPrefab == null)
            nameTagPrefab = nameBundle.LoadAsset<GameObject>("assets/prefabs/nametag.prefab");

        if (nameTagPrefab == null && !loggedMissingNameTagPrefab)
        {
            loggedMissingNameTagPrefab = true;
            Debug.LogError("[TUP] Nametag prefab missing. Bundle assets: " + string.Join(", ", assetNames));
        }

        return nameTagPrefab;
    }

    private static void SetTagText(Transform textTransform, string value)
    {
        if (textTransform == null) return;

        TMP_Text tmp = textTransform.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = value;
            return;
        }

        UnityEngine.UI.Text uiText = textTransform.GetComponent<UnityEngine.UI.Text>();
        if (uiText != null)
            uiText.text = value;
    }

    private static int GetDetectedModCount(VRRig rig)
    {
        if (rig == null) return 0;

        if (cachedNameTagModCounts.TryGetValue(rig, out int cached) &&
            nextNameTagModScanTimes.TryGetValue(rig, out float nextScan) &&
            Time.time < nextScan)
            return cached;

        int count = 0;
        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null)
                count = FindPropertySignatureHits(player).Count;
        }
        catch
        {
            count = 0;
        }

        cachedNameTagModCounts[rig] = count;
        nextNameTagModScanTimes[rig] = Time.time + 1f;
        return count;
    }

    private static readonly float[] nameTagScaleSteps = { 0.0475f, 0.06f, 0.0725f, 0.085f, 0.0975f, 0.11f };
    private static int nameTagScaleIndex = 2;

    private static void CycleNameTagSize()
    {
        nameTagScaleIndex = (nameTagScaleIndex + 1) % nameTagScaleSteps.Length;
        targetNameTagScale = nameTagScaleSteps[nameTagScaleIndex];
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
                tag.SetActive(false);
        }
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

public class NotificationFollower : MonoBehaviour
{
    private static readonly Quaternion NotificationRotation = Quaternion.Euler(0f, -90f, 0f);
    public Vector3 LocalPosition;

    private void LateUpdate()
    {
        Transform head = GTPlayer.Instance?.headCollider?.transform;
        if (head == null) return;

        Transform body = GTPlayer.Instance?.bodyCollider?.transform;
        float yaw = body != null ? body.eulerAngles.y : head.eulerAngles.y;
        Quaternion anchorRotation = Quaternion.Euler(0f, yaw, 0f);

        transform.position = head.position + (anchorRotation * LocalPosition);
        transform.rotation = anchorRotation * NotificationRotation;
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





