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
    private static Coroutine? queueCoroutine;
    public static  int        ReconnectDelay = 1;

    public static Dictionary<string, ModCategory> Actions;

    private const string NotificationPrefabPath = "assets/prefabs/NotificationDarkMode.prefab";
    private const float NotificationBaseWidth = 7.7f;
    private const float NotificationWidthPerCharacter = 1.35f;
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
                    { "Distance Fade", new ModAction(ToggleNameTagDistanceFade, true) },
                    { "Fade Distance", new ModAction(CycleNameTagFadeDistance, false) },
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
                { }
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
}
