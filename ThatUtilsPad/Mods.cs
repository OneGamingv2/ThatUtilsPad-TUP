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
    private const float NotificationBaseY = -0.38f;
    private const float NotificationStackSpacing = 0.0325f;
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
    private static bool        autoScanEnabled;
    private static string      autoScanRoomKey = "";
    private static readonly HashSet<int> autoScannedActorNumbers = new HashSet<int>();
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
    private static readonly Dictionary<VRRig, SpeedSample> speedSamples = new Dictionary<VRRig, SpeedSample>();
    private static readonly Dictionary<VRRig, int> speedFlagCounts = new Dictionary<VRRig, int>();
    private static readonly Dictionary<VRRig, float> nextSpeedNotifyTimes = new Dictionary<VRRig, float>();

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
            lines.Add("themeIndex=" + themeIndex);
            lines.Add("doubleClickOpenEnabled=" + doubleClickOpenEnabled);
            lines.Add("menuOpenBind=" + menuOpenBindCode);
            lines.Add("scanModeIndex=" + scanModeIndex);
            lines.Add("speedStrictnessIndex=" + speedStrictnessIndex);
            lines.Add("nameTagScaleIndex=" + nameTagScaleIndex);
            lines.Add("nameTagFadeDistanceIndex=" + nameTagFadeDistanceIndex);
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
            SavedToggleStates["3 Flag Notify"] = requireThreeSpeedFlags;
            SavedToggleStates["Speedboost Check"] = speedboostCheckEnabled;
            SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
            SavedToggleStates["Auto Scan"] = autoScanEnabled;
            SavedToggleStates["Nametags"] = nameTagsEnabled;
            SavedToggleStates["Select User"] = checkerEnabled;
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
                else if (line.StartsWith("themeIndex="))
                {
                    if (int.TryParse(line.Substring(11), out int idx))
                        themeIndex = Mathf.Clamp(idx, 0, themeNames.Length - 1);
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
                else if (line.StartsWith("scanModeIndex="))
                {
                    if (int.TryParse(line.Substring(14), out int idx))
                        scanModeIndex = Mathf.Clamp(idx, 0, scanModeNames.Length - 1);
                }
                else if (line.StartsWith("speedStrictnessIndex="))
                {
                    if (int.TryParse(line.Substring(21), out int idx))
                        speedStrictnessIndex = Mathf.Clamp(idx, 0, speedStrictnessNames.Length - 1);
                }
                else if (line.StartsWith("nameTagScaleIndex="))
                {
                    if (int.TryParse(line.Substring(18), out int idx))
                        nameTagScaleIndex = Mathf.Clamp(idx, 0, nameTagScaleSteps.Length - 1);
                }
                else if (line.StartsWith("nameTagFadeDistanceIndex="))
                {
                    if (int.TryParse(line.Substring(25), out int idx))
                        nameTagFadeDistanceIndex = Mathf.Clamp(idx, 0, nameTagFadeDistanceValues.Length - 1);
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
        if (!SavedToggleStates.ContainsKey("3 Flag Notify")) SavedToggleStates["3 Flag Notify"] = requireThreeSpeedFlags;
        if (!SavedToggleStates.ContainsKey("Speedboost Check")) SavedToggleStates["Speedboost Check"] = speedboostCheckEnabled;
        if (!SavedToggleStates.ContainsKey("Nametag Distance Fade")) SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
        if (!SavedToggleStates.ContainsKey("Auto Scan")) SavedToggleStates["Auto Scan"] = autoScanEnabled;
        if (!SavedToggleStates.ContainsKey("Nametags")) SavedToggleStates["Nametags"] = nameTagsEnabled;
        if (!SavedToggleStates.ContainsKey("Select User")) SavedToggleStates["Select User"] = checkerEnabled;
        requireThreeSpeedFlags = SavedToggleStates["3 Flag Notify"];
        speedboostCheckEnabled = SavedToggleStates["Speedboost Check"];
        nameTagDistanceFadeEnabled = SavedToggleStates["Nametag Distance Fade"];
        autoScanEnabled = SavedToggleStates["Auto Scan"];
        nameTagsEnabled = SavedToggleStates["Nametags"];
        checkerEnabled = SavedToggleStates["Select User"];
        targetNameTagScale = nameTagScaleSteps[Mathf.Clamp(nameTagScaleIndex, 0, nameTagScaleSteps.Length - 1)];
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
                    { "Nametag Distance Fade", new ModAction(ToggleNameTagDistanceFade, true) },
                    { "Nametag Fade Distance", new ModAction(CycleNameTagFadeDistance, false) },
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
                    { "Auto Scan", new ModAction(ToggleAutoScan, true) },
                    { "Scan Lobby", new ModAction(PrintPhotonPlayerCustomProperties, false) },
                    { "Scan Mode", new ModAction(CycleScanMode, false) }
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
        {
            "Anticheat",
            new ModCategory("settings.png")
            {
                Actions =
                {
                    { "Speedboost Check", new ModAction(ToggleSpeedboostCheck, true) },
                    { "3 Flag Notify", new ModAction(ToggleSpeedFlagRequirement, true) },
                    { "Speed Strictness", new ModAction(CycleSpeedStrictness, false) }
                }
            }
        },
        {
            "Spotify",
            new ModCategory("settings.png")
            {
                Actions = { }
            }
        },
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
    ApplyLoadedToggleStates();
    InitOutfitSlotActions();
}

private static void ApplyLoadedToggleStates()
{
    if (checkerEnabled && checkerCoroutine == null && CoroutineHandler.Instance != null)
        checkerCoroutine = CoroutineHandler.Instance.StartCoroutine(CheckerLoop());

    if (!nameTagsEnabled)
        DisableNameTags();

    if (!autoScanEnabled)
    {
        autoScanRoomKey = "";
        autoScannedActorNumbers.Clear();
    }
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
    notifObject.transform.localScale = Vector3.one * 0.026f;
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

    Main.PlayNotificationSound();
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
        0.6f
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
    Debug.Log($"[TUP] Volume slider -> {newVol * 100f}%");
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
    Debug.Log($"[TUP] Volume UP -> {newVol * 100f}%");
}

private static void VolumeDown()
{
    if (selectedRig == null) return;

    float newVol = Mathf.Clamp(GetRigVolume(selectedRig) - 0.1f, 0f, MaxSliderVolume);
    SetRigVolume(selectedRig, newVol, true);
    mutedPreviousVolumes.Remove(selectedRig);
    UpdateVolumeUI(newVol);
    SaveVolumes();
    Debug.Log($"[TUP] Volume DOWN -> {newVol * 100f}%");
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
    Debug.Log($"[TUP] {(shouldMute ? "Muted" : "Unmuted")} {selectedRig.playerNameVisible}");
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
        int fps = RigHelper.GetFPS(rig);
        string platform = GetPlatform(rig);

        string colorStr =
            $"RGB({Mathf.RoundToInt(color.r * 9)}, {Mathf.RoundToInt(color.g * 9)}, {Mathf.RoundToInt(color.b * 9)})";

        Debug.Log($"[TUP CHECKER]\nName: {name}\nColor: {colorStr}\nFPS: {fps}\nPlatform: {platform}");

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
    
    private static bool speedboostCheckEnabled = true;
    private static bool requireThreeSpeedFlags = true;
    private static readonly string[] speedStrictnessNames = { "Strict", "Moderate", "Low" };
    private static readonly float[] speedStrictnessBuffers = { 0f, 1.25f, 2.75f };
    private static int speedStrictnessIndex = 1;
    private const float SpeedSampleMinDelta = 0.08f;
    private const float SpeedSampleMaxDelta = 0.5f;
    private const float SpeedNotifyCooldown = 5f;
    private const float SpeedTeleportIgnoreDistance = 8f;

    public static string GetSpeedStrictnessLabel() => speedStrictnessNames[Mathf.Clamp(speedStrictnessIndex, 0, speedStrictnessNames.Length - 1)];

    private static void ToggleSpeedboostCheck()
    {
        speedboostCheckEnabled = !speedboostCheckEnabled;
        SavedToggleStates["Speedboost Check"] = speedboostCheckEnabled;
        SaveButtonStates();
        if (!speedboostCheckEnabled)
        {
            speedSamples.Clear();
            speedFlagCounts.Clear();
        }
    }

    private static void ToggleSpeedFlagRequirement()
    {
        requireThreeSpeedFlags = !requireThreeSpeedFlags;
        SavedToggleStates["3 Flag Notify"] = requireThreeSpeedFlags;
        SaveButtonStates();
    }

    private static void CycleSpeedStrictness()
    {
        speedStrictnessIndex = (speedStrictnessIndex + 1) % speedStrictnessNames.Length;
        SaveButtonStates();

        if (Main.speedStrictnessText != null)
            Main.speedStrictnessText.text = "Speed Strictness  :  " + GetSpeedStrictnessLabel();
    }

    public static void AntiCheatLoop()
    {
        if (!speedboostCheckEnabled)
            return;

        float now = Time.time;
        HashSet<VRRig> active = new HashSet<VRRig>();

        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (rig == null || rig.isLocal) continue;
            active.Add(rig);

            Vector3 position = GetSpeedCheckPosition(rig);
            if (!speedSamples.TryGetValue(rig, out SpeedSample sample))
            {
                speedSamples[rig] = new SpeedSample(position, now);
                continue;
            }

            float delta = now - sample.Time;
            if (delta < SpeedSampleMinDelta)
                continue;

            speedSamples[rig] = new SpeedSample(position, now);
            if (delta > SpeedSampleMaxDelta)
                continue;

            float distance = Vector3.Distance(position, sample.Position);
            if (distance > SpeedTeleportIgnoreDistance)
            {
                speedFlagCounts[rig] = 0;
                continue;
            }

            float speed = distance / Mathf.Max(delta, 0.001f);
            float cap = GetSpeedCap() + speedStrictnessBuffers[Mathf.Clamp(speedStrictnessIndex, 0, speedStrictnessBuffers.Length - 1)];

            if (speed <= cap)
            {
                speedFlagCounts[rig] = 0;
                continue;
            }

            speedFlagCounts.TryGetValue(rig, out int flags);
            flags++;
            speedFlagCounts[rig] = flags;

            int requiredFlags = requireThreeSpeedFlags ? 3 : 1;
            nextSpeedNotifyTimes.TryGetValue(rig, out float nextNotify);
            if (flags < requiredFlags || now < nextNotify)
                continue;

            string playerName = GetSafeRigName(rig);
            ShowNotification($"{playerName} flagged for speedboost ({speed:0.0}/{cap:0.0})", 3f);
            nextSpeedNotifyTimes[rig] = now + SpeedNotifyCooldown;
            speedFlagCounts[rig] = 0;
        }

        foreach (VRRig rig in speedSamples.Keys.Where(rig => rig == null || !active.Contains(rig)).ToList())
        {
            speedSamples.Remove(rig);
            speedFlagCounts.Remove(rig);
            nextSpeedNotifyTimes.Remove(rig);
        }
    }

    private static Vector3 GetSpeedCheckPosition(VRRig rig)
    {
        if (rig?.bodyTransform != null) return rig.bodyTransform.position;
        if (rig?.headMesh != null) return rig.headMesh.transform.position;
        return rig != null ? rig.transform.position : Vector3.zero;
    }

    private static float GetSpeedCap()
    {
        try
        {
            if (GTPlayer.Instance != null && GTPlayer.Instance.maxJumpSpeed > 0f)
                return GTPlayer.Instance.maxJumpSpeed;
        }
        catch { }

        return 6.5f;
    }

    private static string GetSafeRigName(VRRig rig)
    {
        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null && !string.IsNullOrWhiteSpace(player.NickName))
                return player.NickName;
        }
        catch { }

        return !string.IsNullOrWhiteSpace(rig?.playerNameVisible) ? rig.playerNameVisible : "Player";
    }

    private readonly struct SpeedSample
    {
        public readonly Vector3 Position;
        public readonly float Time;

        public SpeedSample(Vector3 position, float time)
        {
            Position = position;
            Time = time;
        }
    }
    public readonly struct SpotifyTrackInfo
{
    public readonly string Song;
    public readonly string Artist;
    public readonly string Duration;
    public readonly string Status;
    public readonly bool HasTrack;
    public readonly Texture2D Icon;
    public readonly string ThumbnailBase64;
    public readonly float StartTime;
    public readonly float EndTime;
    public readonly float ElapsedTime;

    public SpotifyTrackInfo(string song, string artist, string duration, string status, bool hasTrack)
    {
        Song = song;
        Artist = artist;
        Duration = duration;
        Status = status;
        HasTrack = hasTrack;
        Icon = null;
        ThumbnailBase64 = "";
        StartTime = 0f;
        EndTime = 0f;
        ElapsedTime = 0f;
    }

    public SpotifyTrackInfo(string song, string artist, string duration, string status, bool hasTrack, Texture2D icon, float startTime, float endTime, float elapsedTime, string thumbnailBase64 = "")
    {
        Song = song;
        Artist = artist;
        Duration = duration;
        Status = status;
        HasTrack = hasTrack;
        Icon = icon;
        ThumbnailBase64 = thumbnailBase64 ?? "";
        StartTime = startTime;
        EndTime = endTime;
        ElapsedTime = elapsedTime;
    }
}
private const byte SpotifyKeyPrevious = 0xB1;
private const byte SpotifyKeyPlayPause = 0xB3;
private const byte SpotifyKeyNext = 0xB0;
private const uint SpotifyKeyUp = 0x0002;

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
private static extern int GetWindowTextLength(IntPtr hWnd);

public static void InvalidateSpotifyCache()
{
    nextSpotifyInfoQueryTime = 0f;
}

public static void SpotifyPlayPause()
{
    SendSpotifyMediaKey(SpotifyKeyPlayPause);
    InvalidateSpotifyCache();
    Main.Instance?.RefreshSpotifyHudDelayed(0.35f);
}

public static void SpotifyNext()
{
    SendSpotifyMediaKey(SpotifyKeyNext);
    InvalidateSpotifyCache();
    Main.Instance?.RefreshSpotifyHudDelayed(0.35f);
}

public static void SpotifyPrevious()
{
    SendSpotifyMediaKey(SpotifyKeyPrevious);
    InvalidateSpotifyCache();
    Main.Instance?.RefreshSpotifyHudDelayed(0.35f);
}

public static void SpotifyRefresh()
{
    InvalidateSpotifyCache();
    Main.Instance?.RefreshSpotifyHud();
}

private static void SendSpotifyMediaKey(byte virtualKey)
{
    try
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, SpotifyKeyUp, UIntPtr.Zero);
    }
    catch (Exception e)
    {
        Debug.LogWarning("[TUP] Spotify media key failed: " + e.Message);
    }
}

private static SpotifyTrackInfo cachedSpotifyInfo = new SpotifyTrackInfo("No song", "Spotify not open", "--:--", "Offline", false);
private static float nextSpotifyInfoQueryTime;
private static string quickSongPath;
private static Texture2D cachedSpotifyIcon;
private static string lastSpotifyDebugMessage;
private static float nextSpotifyDebugRepeatTime;
private static float nextForcedSpotifyDebugTime;
private static readonly object spotifyQueryLock = new object();
private static bool spotifyQueryRunning;

public static void SpotifyDebugLoop() { }
public static SpotifyTrackInfo GetSpotifyTrackInfo()
{
    if (Time.realtimeSinceStartup >= nextSpotifyInfoQueryTime)
    {
        nextSpotifyInfoQueryTime = Time.realtimeSinceStartup + 1f;
        StartSpotifyInfoQuery();
    }

    lock (spotifyQueryLock)
        return cachedSpotifyInfo;
}

private static void StartSpotifyInfoQuery()
{
    lock (spotifyQueryLock)
    {
        if (spotifyQueryRunning)
            return;
        spotifyQueryRunning = true;
    }

    string helperPath = GetQuickSongPath();
    System.Threading.Tasks.Task.Run(() =>
    {
        SpotifyTrackInfo result;
        try
        {
            result = QueryQuickSongTrackInfo(helperPath);
            if (!result.HasTrack && result.Status != "Paused")
            {
                SpotifyTrackInfo windowInfo = GetSpotifyWindowTitleTrackInfo();
                result = windowInfo.HasTrack ? windowInfo : result;
            }
        }
        catch (Exception e)
        {
            result = new SpotifyTrackInfo("No song", "Spotify error: " + e.Message, "--:--", "Offline", false);
        }

        lock (spotifyQueryLock)
        {
            cachedSpotifyInfo = result;
            spotifyQueryRunning = false;
        }
    });
}

private static SpotifyTrackInfo QueryQuickSongTrackInfo(string helperPath)
{
    if (string.IsNullOrEmpty(helperPath) || !System.IO.File.Exists(helperPath))
        return new SpotifyTrackInfo("No song", "QuickSong.exe missing", "--:--", "Offline", false);

    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = helperPath,
        Arguments = "-all",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using System.Diagnostics.Process process = new System.Diagnostics.Process { StartInfo = startInfo };
    process.Start();
    if (!process.WaitForExit(900))
    {
        try { process.Kill(); } catch { }
        return new SpotifyTrackInfo("No song", "QuickSong timeout", "--:--", "Offline", false);
    }

    string output = process.StandardOutput.ReadToEnd();
    if (string.IsNullOrWhiteSpace(output))
        return new SpotifyTrackInfo("No song", "Spotify not open", "--:--", "Offline", false);

    int jsonStart = output.IndexOf('{');
    if (jsonStart > 0) output = output.Substring(jsonStart);
    JObject data = JObject.Parse(output);

    string title = CleanSpotifyText((string)data["Title"]);
    string artist = CleanSpotifyText((string)data["Artist"]);
    string status = CleanSpotifyText((string)data["Status"]);
    float startTime = (float?)data["StartTime"] ?? 0f;
    float endTime = (float?)data["EndTime"] ?? 0f;
    float elapsedTime = (float?)data["ElapsedTime"] ?? 0f;
    string thumbnailBase64 = CleanSpotifyText((string)data["ThumbnailBase64"]);

    if (IsEmptySpotifyTitle(title) || IsIdleSpotifyTitle(title) || IsJunkSpotifyTitle(title)) title = "No song";
    if (IsEmptySpotifyTitle(artist)) artist = "Unknown Artist";

    bool paused = !status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
    bool hasTrack = !title.Equals("No song", StringComparison.OrdinalIgnoreCase);
    string duration = endTime > 0f ? FormatSpotifyTimeNoUnity(elapsedTime) + " / " + FormatSpotifyTimeNoUnity(endTime) : "--:--";
    return new SpotifyTrackInfo(title, artist, duration, paused ? "Paused" : "Playing", hasTrack, null, startTime, endTime, elapsedTime, thumbnailBase64);
}
private static SpotifyTrackInfo GetQuickSongTrackInfo()
{
    try
    {
        string helperPath = GetQuickSongPath();
        if (string.IsNullOrEmpty(helperPath) || !System.IO.File.Exists(helperPath))
            return new SpotifyTrackInfo("No song", "QuickSong.exe missing", "--:--", "Offline", false);

        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = "-all",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using System.Diagnostics.Process process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();
        if (!process.WaitForExit(1600))
        {
            try { process.Kill(); } catch { }
            return cachedSpotifyInfo;
        }

        string output = process.StandardOutput.ReadToEnd();
        if (string.IsNullOrWhiteSpace(output))
            return new SpotifyTrackInfo("No song", "Spotify not open", "--:--", "Offline", false);

        int jsonStart = output.IndexOf('{' );
        if (jsonStart > 0) output = output.Substring(jsonStart);
        JObject data = JObject.Parse(output);
        string title = CleanSpotifyText((string)data["Title"]);
        string artist = CleanSpotifyText((string)data["Artist"]);
        string status = CleanSpotifyText((string)data["Status"]);
        float startTime = (float?)data["StartTime"] ?? 0f;
        float endTime = (float?)data["EndTime"] ?? 0f;
        float elapsedTime = (float?)data["ElapsedTime"] ?? 0f;

        if (IsEmptySpotifyTitle(title) || IsIdleSpotifyTitle(title) || IsJunkSpotifyTitle(title)) title = "No song";
        if (IsEmptySpotifyTitle(artist)) artist = "Unknown Artist";

        Texture2D icon = cachedSpotifyIcon;
        string thumbnailBase64 = (string)data["ThumbnailBase64"];
        if (!string.IsNullOrWhiteSpace(thumbnailBase64))
        {
            byte[] bytes = Convert.FromBase64String(thumbnailBase64);
            if (cachedSpotifyIcon == null)
                cachedSpotifyIcon = new Texture2D(2, 2);
            cachedSpotifyIcon.LoadImage(bytes);
            icon = cachedSpotifyIcon;
        }

        bool paused = !status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
        bool hasTrack = !title.Equals("No song", StringComparison.OrdinalIgnoreCase);
        string duration = endTime > 0f ? FormatSpotifyTime(elapsedTime) + " / " + FormatSpotifyTime(endTime) : "--:--";
        DebugSpotify("QuickSong parsed: Title=" + title + ", Artist=" + artist + ", Status=" + status + ", HasTrack=" + hasTrack + ", End=" + endTime + ", Elapsed=" + elapsedTime + ", Thumb=" + !string.IsNullOrWhiteSpace(thumbnailBase64) + ", ThumbLen=" + (thumbnailBase64 == null ? 0 : thumbnailBase64.Length) + ", Icon=" + (icon == null ? "null" : icon.width + "x" + icon.height));
        return new SpotifyTrackInfo(title, artist, duration, paused ? "Paused" : "Playing", hasTrack, icon, startTime, endTime, elapsedTime);
    }
    catch (Exception e)
    {
        Debug.LogWarning("[TUP] QuickSong query failed: " + e.Message);
        return new SpotifyTrackInfo("No song", "QuickSong error", "--:--", "Offline", false);
    }
}

private static string GetQuickSongPath()
{
    if (!string.IsNullOrEmpty(quickSongPath) && System.IO.File.Exists(quickSongPath))
        return quickSongPath;

    string configDir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "ThatUtilsPad");
    string configPath = System.IO.Path.Combine(configDir, "QuickSong.exe");
    if (System.IO.File.Exists(configPath))
    {
        quickSongPath = configPath;
        return quickSongPath;
    }

    string pluginPath = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "QuickSong.exe");
    if (System.IO.File.Exists(pluginPath))
    {
        quickSongPath = pluginPath;
        return quickSongPath;
    }

    Assembly assembly = Assembly.GetExecutingAssembly();
    string resourceName = assembly.GetManifestResourceNames()
        .FirstOrDefault(name => name.EndsWith("QuickSong.exe", StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrEmpty(resourceName))
    {
        try
        {
            System.IO.Directory.CreateDirectory(configDir);
            using System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using (System.IO.FileStream file = new System.IO.FileStream(configPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    stream.CopyTo(file);

                quickSongPath = configPath;
                return quickSongPath;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP SPOTIFY] helper extract failed: " + e.Message);
        }
    }

    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuickSong.exe");
    if (System.IO.File.Exists(tempPath))
    {
        quickSongPath = tempPath;
        return quickSongPath;
    }

    return null;
}
private static SpotifyTrackInfo GetSpotifyWindowTitleTrackInfo()
{
    try
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("Spotify");
        List<string> titles = GetSpotifyWindowTitles(processes);
        string title = titles.FirstOrDefault(windowTitle => IsUsefulSpotifyWindowTitle(windowTitle));
        if (string.IsNullOrWhiteSpace(title))
            title = processes.Select(process => process.MainWindowTitle)
                .FirstOrDefault(windowTitle => IsUsefulSpotifyWindowTitle(windowTitle));

        if (string.IsNullOrWhiteSpace(title))
            return new SpotifyTrackInfo("No song", "Spotify not open", "--:--", "Offline", false);

        title = title.Trim();
        string artist = "Unknown Artist";
        string song = title;
        int splitIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (splitIndex > 0)
        {
            artist = title.Substring(0, splitIndex).Trim();
            song = title.Substring(splitIndex + 3).Trim();
        }

        if (IsEmptySpotifyTitle(song) || IsIdleSpotifyTitle(song) || IsJunkSpotifyTitle(song))
            return new SpotifyTrackInfo("No song", "Spotify not open", "--:--", "Offline", false);
        if (IsEmptySpotifyTitle(artist))
            artist = "Unknown Artist";

        return new SpotifyTrackInfo(song, artist, "--:--", "Playing", true);
    }
    catch (Exception e)
    {
        return new SpotifyTrackInfo("Spotify error", e.Message, "--:--", "Error", false);
    }
}

private static List<string> GetSpotifyWindowTitles(System.Diagnostics.Process[] processes)
{
    HashSet<uint> processIds = new HashSet<uint>(processes.Select(process => (uint)process.Id));
    List<string> titles = new List<string>();
    EnumWindows((hWnd, lParam) =>
    {
        GetWindowThreadProcessId(hWnd, out uint processId);
        if (!processIds.Contains(processId))
            return true;

        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return true;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        string title = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(title) && !titles.Contains(title))
            titles.Add(title);
        return true;
    }, IntPtr.Zero);
    return titles;
}
private static void DebugSpotify(string message)
{
    float now = Time.realtimeSinceStartup;
    if (message == lastSpotifyDebugMessage && now < nextSpotifyDebugRepeatTime)
        return;

    lastSpotifyDebugMessage = message;
    nextSpotifyDebugRepeatTime = now + 10f;
    Debug.Log("[TUP SPOTIFY] " + message);
}

private static string TrimForLog(string value)
{
    if (string.IsNullOrEmpty(value)) return "<empty>";
    value = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return value.Length > 300 ? value.Substring(0, 300) + "..." : value;
}
private static string FormatSpotifyTimeNoUnity(float seconds)
{
    seconds = Math.Max(0f, seconds);
    return ((int)(seconds / 60f)) + ":" + ((int)(seconds % 60f)).ToString("00");
}

private static string FormatSpotifyTime(float seconds)
{
    seconds = Mathf.Max(0f, seconds);
    return Mathf.FloorToInt(seconds / 60f) + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00");
}

private static string CleanSpotifyText(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace("\r", " ").Replace("\n", " ");
}

private static bool IsJunkSpotifyTitle(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return true;
    value = value.Trim();
    if (value.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase)) return true;
    if (value.IndexOf("Window (Spotify.exe)", StringComparison.OrdinalIgnoreCase) >= 0) return true;
    if (value.IndexOf("Spotify.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
    if (value.StartsWith("GDI+", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}
private static bool IsUsefulSpotifyWindowTitle(string value)
{
    if (IsEmptySpotifyTitle(value) || IsIdleSpotifyTitle(value) || IsJunkSpotifyTitle(value)) return false;
    value = value.Trim();
    if (value.IndexOf("Window (Spotify.exe)", StringComparison.OrdinalIgnoreCase) >= 0) return false;
    if (value.IndexOf("Spotify.exe", StringComparison.OrdinalIgnoreCase) >= 0) return false;
    if (value.StartsWith("GDI+", StringComparison.OrdinalIgnoreCase)) return false;
    if (value.Contains("Default IME")) return false;
    return true;
}
private static bool IsIdleSpotifyTitle(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return true;
    value = value.Trim();
    return value.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase);
}
private static bool IsEmptySpotifyTitle(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return true;
    value = value.Trim();
    return value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("unknown artist", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("null unknown artist", StringComparison.OrdinalIgnoreCase);
}
private enum ScanLobbyMode
    {
        CheatsOnly,
        ModsOnly,
        ModsAndCheats
    }

    private static readonly string[] scanModeNames = { "Cheats", "Mods", "Mods + Cheats" };
    private static int scanModeIndex;

    public static string GetScanModeLabel() => scanModeNames[Mathf.Clamp(scanModeIndex, 0, scanModeNames.Length - 1)];

    private static void CycleScanMode()
    {
        scanModeIndex = (scanModeIndex + 1) % scanModeNames.Length;
        SaveButtonStates();

        if (Main.scanModeText != null)
            Main.scanModeText.text = "Scan Mode  :  " + GetScanModeLabel();

        Debug.Log("Scan Mode : " + GetScanModeLabel());
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
    
    private static void ToggleAutoScan()
    {
        autoScanEnabled = !autoScanEnabled;
        SavedToggleStates["Auto Scan"] = autoScanEnabled;
        SaveButtonStates();

        if (!autoScanEnabled)
        {
            autoScanRoomKey = "";
            autoScannedActorNumbers.Clear();
        }
        else
        {
            autoScanRoomKey = "";
        }

        Debug.Log("Auto Scan : " + (autoScanEnabled ? "ON" : "OFF"));
    }

    public static void AutoScanLoop()
    {
        if (!autoScanEnabled)
            return;

        if (!PhotonNetwork.InRoom)
        {
            autoScanRoomKey = "";
            autoScannedActorNumbers.Clear();
            return;
        }

        string roomKey = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "room";
        if (!roomKey.Equals(autoScanRoomKey, StringComparison.Ordinal))
        {
            autoScanRoomKey = roomKey;
            autoScannedActorNumbers.Clear();
        }

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player == PhotonNetwork.LocalPlayer)
                continue;

            if (!autoScannedActorNumbers.Add(player.ActorNumber))
                continue;

            ScanPhotonPlayerAndNotify(player, true);
        }
    }
    public static void PrintPhotonPlayerCustomProperties()
    {
        if (!PhotonNetwork.InRoom)
        {
            Debug.Log("[TUP] Cannot scan lobby: not in a room.");
            ShowNotification("No players detected");
            return;
        }

        int detectedPlayers = 0;
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player == PhotonNetwork.LocalPlayer)
                continue;

            if (ScanPhotonPlayerAndNotify(player, false))
                detectedPlayers++;
        }

        if (detectedPlayers == 0)
            ShowNotification("No players detected");
    }

    private static bool ScanPhotonPlayerAndNotify(Player player, bool suppressEmpty)
    {
        if (player == null)
            return false;

        EnsurePropertySignatureDictionary();
        Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);
        if (hits.Count == 0)
            return false;

        int modCount = 0;
        int cheatCount = 0;
        foreach (string key in hits.Keys)
        {
            if (!propertySignatures.TryGetValue(key, out PropertySignature signature))
                continue;

            if (signature.IsLegal)
                modCount++;
            else
                cheatCount++;
        }

        ScanLobbyMode mode = (ScanLobbyMode)Mathf.Clamp(scanModeIndex, 0, scanModeNames.Length - 1);
        string message = BuildScanNotificationMessage(player, modCount, cheatCount, mode);
        if (string.IsNullOrEmpty(message))
            return false;

        ShowNotification(message);
        return true;
    }

    private static string BuildScanNotificationMessage(Player player, int modCount, int cheatCount, ScanLobbyMode mode)
    {
        string playerName = string.IsNullOrWhiteSpace(player.NickName) ? "Player " + player.ActorNumber : player.NickName;

        switch (mode)
        {
            case ScanLobbyMode.CheatsOnly:
                return cheatCount > 0 ? $"{playerName} has {cheatCount} Cheats installed" : "";
            case ScanLobbyMode.ModsOnly:
                return modCount > 0 ? $"{playerName} has {modCount} Mods installed" : "";
            default:
                if (modCount > 0 && cheatCount > 0)
                    return $"{playerName} has {modCount} Mods and {cheatCount} Cheats installed";
                if (modCount > 0)
                    return $"{playerName} has {modCount} Mods installed";
                if (cheatCount > 0)
                    return $"{playerName} has {cheatCount} Cheats installed";
                return "";
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

    private static readonly string[] smoothingStrengthNames = { "Soft", "Normal", "Sharp" };
    private static readonly float[] smoothingStrengthValues = { 10f, 30f, 60f };
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
                SaveButtonStates();
        UpdateSettingsLabels();
        Debug.Log("Theme : " + GetThemeLabel());
    }

    private static void ApplyTheme()
    {
        themeIndex = Mathf.Clamp(themeIndex, 0, themePalettes.Length - 1);
        if (Main.menuObj != null)
            MenuTheme.Assign(Main.menuObj, themePalettes[themeIndex]);
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

        if (Main.scanModeText != null)
            Main.scanModeText.text = "Scan Mode  :  " + GetScanModeLabel();

        if (Main.nameTagSizeText != null)
            Main.nameTagSizeText.text = "Nametag Size  :  " + GetNameTagSizeLabel();

        if (Main.nameTagFadeDistanceText != null)
            Main.nameTagFadeDistanceText.text = "Nametag Fade Distance  :  " + GetNameTagFadeDistanceLabel();
    }
    
    
    private static bool nameTagsEnabled = false;
    private static readonly Dictionary<VRRig, GameObject> nametags = new();
    public static AssetBundle nameBundle;
    private static GameObject nameTagPrefab;
    private static bool loggedMissingNameTagPrefab;
    private static readonly Dictionary<VRRig, NameTagModStatus> cachedNameTagModStatuses = new Dictionary<VRRig, NameTagModStatus>();
    private static readonly Dictionary<VRRig, float> nextNameTagModScanTimes = new Dictionary<VRRig, float>();
    private static readonly Dictionary<VRRig, float> nextNameTagInfoUpdateTimes = new Dictionary<VRRig, float>();
    private const float NameTagInfoRefreshSeconds = 0.5f;
    private const float NameTagModScanSeconds = 4f;
    private static float targetNameTagScale = 0.0725f;
    private static bool nameTagDistanceFadeEnabled = true;
    private static readonly float[] nameTagFadeDistanceValues = { 2f, 4f, 8f, 16f, 32f };
    private static readonly string[] nameTagFadeDistanceNames = { "2m", "4m", "8m", "16m", "32m" };
    private static int nameTagFadeDistanceIndex = 2;
    private static readonly Color NameTagGoodColor = new Color(0.35f, 0.9f, 0.45f, 1f);
    private static readonly Color NameTagWarnColor = new Color(1f, 0.86f, 0.25f, 1f);
    private static readonly Color NameTagBadColor = new Color(1f, 0.25f, 0.25f, 1f);
    private static readonly Color NameTagNeutralColor = Color.white;

    public static void UpdateNameTags()
    {
        var copy = nametags.ToList();
        foreach (var pair in copy)
        {
            if (pair.Key == null || !VRRigCache.ActiveRigs.Contains(pair.Key))
            {
                Object.Destroy(pair.Value);
                nametags.Remove(pair.Key);
                nextNameTagInfoUpdateTimes.Remove(pair.Key);
                cachedNameTagModStatuses.Remove(pair.Key);
                nextNameTagModScanTimes.Remove(pair.Key);
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
                nextNameTagInfoUpdateTimes.Remove(rig);
                cachedNameTagModStatuses.Remove(rig);
                nextNameTagModScanTimes.Remove(rig);
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
            float visibleScale = targetNameTagScale;
            if (nameTagDistanceFadeEnabled && camera != null)
            {
                float distance = Vector3.Distance(camera.transform.position, anchor.position);
                visibleScale = distance <= nameTagFadeDistanceValues[nameTagFadeDistanceIndex] ? targetNameTagScale : 0f;
            }
            nametag.transform.localScale = Vector3.Lerp(nametag.transform.localScale, Vector3.one * visibleScale, Time.deltaTime * 10f);
            if (!nametag.activeSelf)
                nametag.SetActive(true);

            if (visibleScale <= 0.0001f)
                continue;

            float nextInfoUpdate;
            if (nextNameTagInfoUpdateTimes.TryGetValue(rig, out nextInfoUpdate) && Time.time < nextInfoUpdate)
                continue;
            nextNameTagInfoUpdateTimes[rig] = Time.time + NameTagInfoRefreshSeconds + (Mathf.Abs(rig.GetInstanceID() % 8) * 0.02f);

            NetPlayer netPlayer = GetPlayerFromVRRig(rig);
            SetTagText(nametag.transform.Find("Name"), netPlayer != null ? netPlayer.NickName : rig.playerNameVisible);

            int fps = RigHelper.GetFPS(rig);
            int ping = GetPingThrottled(rig);
            NameTagModStatus modStatus = GetDetectedModStatus(rig);

            SetTagText(nametag.transform.Find("FPS"), fps + "Hz");
            SetTagMetricColor(nametag.transform, "FPS", GetFpsStatusColor(fps));

            SetTagText(nametag.transform.Find("Ping"), ping + "Ms");
            SetTagMetricColor(nametag.transform, "Ping", GetPingStatusColor(ping));

            SetTagText(nametag.transform.Find("Mods"), FormatNameTagModStatus(modStatus));
            SetTagMetricColor(nametag.transform, "Mods", GetModStatusColor(modStatus));

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

    private static NameTagModStatus GetDetectedModStatus(VRRig rig)
    {
        if (rig == null) return default;

        if (cachedNameTagModStatuses.TryGetValue(rig, out NameTagModStatus cached) &&
            nextNameTagModScanTimes.TryGetValue(rig, out float nextScan) &&
            Time.time < nextScan)
            return cached;

        if (!cachedNameTagModStatuses.ContainsKey(rig) && !nextNameTagModScanTimes.ContainsKey(rig))
        {
            nextNameTagModScanTimes[rig] = Time.time + (Mathf.Abs(rig.GetInstanceID() % 100) * 0.03f);
            return default;
        }

        NameTagModStatus status = default;
        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null)
            {
                Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);
                foreach (string key in hits.Keys)
                {
                    if (!propertySignatures.TryGetValue(key, out PropertySignature signature))
                        continue;

                    if (signature.IsLegal)
                        status.LegalCount++;
                    else
                        status.IllegalCount++;
                }
            }
        }
        catch
        {
            status = default;
        }

        cachedNameTagModStatuses[rig] = status;
        nextNameTagModScanTimes[rig] = Time.time + NameTagModScanSeconds + (Mathf.Abs(rig.GetInstanceID() % 10) * 0.1f);
        return status;
    }

    private static string FormatNameTagModStatus(NameTagModStatus status)
    {
        if (status.IllegalCount > 0)
            return status.IllegalCount == 1 ? "1 Illegal Mod" : status.IllegalCount + " Illegal Mods";

        if (status.LegalCount > 0)
            return status.LegalCount == 1 ? "1 Mod" : status.LegalCount + " Mods";

        return "0 Mods";
    }

    private static Color GetFpsStatusColor(int fps)
    {
        if (fps < 60) return NameTagBadColor;
        if (fps <= 80) return NameTagWarnColor;
        return NameTagGoodColor;
    }

    private static Color GetPingStatusColor(int ping)
    {
        if (ping < 50) return NameTagGoodColor;
        if (ping <= 120) return NameTagWarnColor;
        return NameTagBadColor;
    }

    private static Color GetModStatusColor(NameTagModStatus status)
    {
        if (status.IllegalCount > 0) return NameTagBadColor;
        if (status.LegalCount > 0) return NameTagGoodColor;
        return NameTagNeutralColor;
    }

    private static void SetTagMetricColor(Transform root, string metricName, Color color)
    {
        if (root == null) return;

        SetTagColor(root.Find(metricName), color);

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string name = child.name ?? "";
            bool metricMatch = name.IndexOf(metricName, StringComparison.OrdinalIgnoreCase) >= 0;
            bool iconMatch = name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0;

            if (metricMatch && iconMatch)
                SetTagColor(child, color);
        }
    }

    private static void SetTagColor(Transform target, Color color)
    {
        if (target == null) return;

        TMP_Text tmp = target.GetComponent<TMP_Text>();
        if (tmp != null) tmp.color = color;

        UnityEngine.UI.Text uiText = target.GetComponent<UnityEngine.UI.Text>();
        if (uiText != null) uiText.color = color;

        foreach (Image image in target.GetComponentsInChildren<Image>(true))
            image.color = color;

        foreach (RawImage rawImage in target.GetComponentsInChildren<RawImage>(true))
            rawImage.color = color;

        SpriteRenderer spriteRenderer = target.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.color = color;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            foreach (Material material in renderer.materials)
                material.color = color;
        }
    }

    private struct NameTagModStatus
    {
        public int LegalCount;
        public int IllegalCount;
    }
    private static readonly float[] nameTagScaleSteps = { 0.0475f, 0.06f, 0.0725f, 0.085f, 0.0975f, 0.11f };
    private static readonly string[] nameTagScaleNames = { "Tiny", "Small", "Normal", "Large", "XL", "XXL" };
    private static int nameTagScaleIndex = 2;

    public static string GetNameTagSizeLabel() => nameTagScaleNames[Mathf.Clamp(nameTagScaleIndex, 0, nameTagScaleNames.Length - 1)];
    public static string GetNameTagFadeDistanceLabel() => nameTagFadeDistanceNames[Mathf.Clamp(nameTagFadeDistanceIndex, 0, nameTagFadeDistanceNames.Length - 1)];

    private static void CycleNameTagSize()
    {
        nameTagScaleIndex = (nameTagScaleIndex + 1) % nameTagScaleSteps.Length;
        targetNameTagScale = nameTagScaleSteps[nameTagScaleIndex];
        SaveButtonStates();
        if (Main.nameTagSizeText != null)
            Main.nameTagSizeText.text = "Nametag Size  :  " + GetNameTagSizeLabel();
    }

    private static void ToggleNameTagDistanceFade()
    {
        nameTagDistanceFadeEnabled = !nameTagDistanceFadeEnabled;
        SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
        SaveButtonStates();
    }

    private static void CycleNameTagFadeDistance()
    {
        nameTagFadeDistanceIndex = (nameTagFadeDistanceIndex + 1) % nameTagFadeDistanceValues.Length;
        SaveButtonStates();
        if (Main.nameTagFadeDistanceText != null)
            Main.nameTagFadeDistanceText.text = "Nametag Fade Distance  :  " + GetNameTagFadeDistanceLabel();
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
        nextNameTagInfoUpdateTimes.Clear();
        cachedNameTagModStatuses.Clear();
        nextNameTagModScanTimes.Clear();
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
        spinnerObj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform);
        spinnerObj.transform.localScale = Vector3.one;
        
        
        var buttons =
            root.transform.Find("LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/SatelliteWardrobe/UI/OutfitButtons/");
        
        GameObject btnsOj = GameObject.Instantiate(buttons.gameObject);
        
        btnsOj.transform.position =
            Camera.main.transform.position + Camera.main.transform.forward * 0.4f - Camera.main.transform.up * 0.4f;
        btnsOj.transform.localRotation = Quaternion.identity;
        btnsOj.transform.LookAt(GTPlayer.Instance.bodyCollider.transform);
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
        int count = 5;

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

        Main.Instance?.ConfigureKeyboardMenuFollow();

        Main.ShowKeyboard(keyboard);
        Main.Instance?.SetKeyboardPreview(keyboard, currentRenameText, false);
    }

    public static void TypeChar(char c)
    {
        if (!isRenaming || currentRenameText.Length >= 10) return;
        currentRenameText += c;
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
        Main.Instance?.SetKeyboardPreview(renameKeyboard, currentRenameText, true);
    }

    public static void DeleteChar()
    {
        if (!isRenaming || currentRenameText.Length == 0) return;
        currentRenameText = currentRenameText[..^1];
        if (renamingLabel != null) renamingLabel.text = currentRenameText;
        Main.Instance?.SetKeyboardPreview(renameKeyboard, currentRenameText, false);
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
        Main.Instance?.RestoreMenuFollowAfterKeyboard();

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