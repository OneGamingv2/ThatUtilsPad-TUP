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
    private static bool wasInRoomForSelection;
    private static HashSet<string> manuallyAdjusted = new HashSet<string>();

    private static Material? lineMat;

    private static Dictionary<VRRig, float> currentVolumes = new Dictionary<VRRig, float>();
    private static Dictionary<string, float> savedVolumes = new Dictionary<string, float>();

    private static string folderPath =
        System.IO.Path.Combine(BepInEx.Paths.PluginPath, "ThatUtilsPad");

    private static string filePath =
        System.IO.Path.Combine(folderPath, "volumes.txt");

    private static string statesFilePath =
        System.IO.Path.Combine(folderPath, "button_states.txt");

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
        public readonly string Normalized;
        public readonly string Compact;
        public readonly bool RequiresWholeToken;

        public PropertySignature(string name, bool isLegal, string section, string normalized, string compact)
        {
            Name = name;
            IsLegal = isLegal;
            Section = section;
            Normalized = normalized;
            Compact = compact;
            RequiresWholeToken = ShouldRequireWholeToken(normalized, section);
        }

        private static bool ShouldRequireWholeToken(string normalized, string section)
        {
            if (string.IsNullOrEmpty(normalized))
                return true;
            if (normalized.Length <= 8 && !IsAllDigits(normalized))
                return true;
            if (!section.Equals("Suspicious property tokens", StringComparison.OrdinalIgnoreCase) &&
                !section.Equals("Heuristic categories", StringComparison.OrdinalIgnoreCase))
                return false;

            switch (normalized)
            {
                case "menu":
                case "client":
                case "auth":
                case "license":
                case "session":
                case "template":
                case "framework":
                case "runtime":
                case "cosmetic":
                case "rpc":
                case "pull":
                case "fly":
                case "inject":
                case "loader":
                case "bypass":
                case "unlocker":
                case "spoof":
                case "serial":
                case "netvar":
                case "void":
                case "dark":
                case "orbit":
                case "bark":
                case "esp":
                case "xray":
                    return true;
                default:
                    return normalized.Length <= 8;
            }
        }

        private static bool IsAllDigits(string value)
        {
            if (value.Length == 0)
                return false;
            for (int i = 0; i < value.Length; i++)
                if (!char.IsDigit(value[i]))
                    return false;
            return true;
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

    private static void OpenSettingsHubPage() => Main.Instance?.OpenSettingsCategory("Settings");
    private static void OpenMenuSettingsPage() => Main.Instance?.OpenSettingsCategory("Menu Settings");
    private static void OpenSoundSettingsPage() => Main.Instance?.OpenSettingsCategory("Sound Settings");
    private static void OpenVrSettingsPage() => Main.Instance?.OpenSettingsCategory("VR Settings");

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
                    { "Copy Room Code", new ModAction(CopyRoomCode, false) },
                    { "Search", new ModAction(StartRoomCodeSearch, false) },
                    { "Region", new ModAction(CycleRegion, false, true, 5f) },
                    { "Region Players", new ModAction(ToggleRegionPlayersAuto, true) },
                    { "Queue", new ModAction(ToggleQueue, false) },
                    { "Mode", new ModAction(ToggleMode, false) },
                    { "Lobby Map", new ModAction(CycleLobbyMap, false) },
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
                    { "Aim Select", new ModAction(ToggleChecker, true) },
                    { "Aim Select Bind", new ModAction(StartAimSelectBindCapture, false) },
                    { "Clear", new ModAction(ClearPlayerSelection, false) },
                    { "Auto Scan", new ModAction(ToggleAutoScan, true) },
                    { "Scan All", new ModAction(PrintPhotonPlayerCustomProperties, false) },
                    { "Scan Mode", new ModAction(CycleScanMode, false) },
                }
            }
        },
        {
            "Lobby",
            new ModCategory("lobby.png")
            {
                Actions = { }
            }
        },
        {
            "Settings",
            new ModCategory("settings.png")
            {
                Actions =
                {
                    { "Menu Settings", new ModAction(OpenMenuSettingsPage, false) },
                    { "Sound Settings", new ModAction(OpenSoundSettingsPage, false) },
                    { "VR Settings", new ModAction(OpenVrSettingsPage, false) },
                    { "Credits", new ModAction(ShowCredits, false) },
                }
            }
        },
        {
            "Menu Settings",
            new ModCategory("settings.png", false)
            {
                Actions =
                {
                    { "Exit Menu Settings", new ModAction(OpenSettingsHubPage, false) },
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
            "Sound Settings",
            new ModCategory("settings.png", false)
            {
                Actions =
                {
                    { "Exit Sound Settings", new ModAction(OpenSettingsHubPage, false) },
                    { "Click Sound", new ModAction(ToggleClickSound, false) },
                    { "Startup Sound", new ModAction(ToggleStartupSound, true) },
                    { "Menu Open Sound", new ModAction(ToggleMenuOpenSound, true) },
                    { "Button Pop Sound", new ModAction(ToggleButtonPopSound, true) },
                    { "Notif Sound", new ModAction(ToggleNotifSound, true) },
                    { "Master Volume", new ModAction(CycleMasterVolume, false) },
                }
            }
        },
        {
            "VR Settings",
            new ModCategory("settings.png", false)
            {
                Actions =
                {
                    { "Exit VR Settings", new ModAction(OpenSettingsHubPage, false) },
                    { "Diagnostics HUD", new ModAction(ToggleVrDiagnosticsHud, true) },
                    { "FPS Counter", new ModAction(ToggleVrFpsCounter, true) },
                    { "Ping Display", new ModAction(ToggleVrPingDisplay, true) },
                    { "Network Stats", new ModAction(ToggleVrNetworkStats, true) },
                    { "Average FPS", new ModAction(ToggleVrFpsAverage, true) },
                    { "Slow FPS", new ModAction(ToggleVrFpsSlow, true) },
                    { "Frametime", new ModAction(ToggleVrFrametime, true) },
                    { "HUD Move Bind", new ModAction(StartHudMoveBindCapture, false) },
                    { "Reset HUD Pos", new ModAction(ResetVrHudPosition, false) },
                    { "Graphics Quality", new ModAction(CycleGraphicsQuality, false) },
                    { "Resolution Scale", new ModAction(CycleResolutionScale, false) },
                    { "MSAA", new ModAction(CycleMsaa, false) },
                    { "Shadow Quality", new ModAction(CycleShadowQuality, false) },
                    { "Render Distance", new ModAction(CycleRenderDistance, false) },
                    { "Controller Haptics", new ModAction(ToggleControllerHaptics, true) },
                    { "Comfort Mode", new ModAction(ToggleComfortMode, true) },
                }
            }
        },
        {
            "Friends",
            new ModCategory("friends.png")
            {
                Actions =
                {
                    { "Refresh Friends", new ModAction(RefreshFriendsList, false) },
                }
            }
        },
        {
            "Camera",
            new ModCategory("camera.png")
            {
                Actions =
                {
                    { "Spawn Camera", new ModAction(ToggleCameraTablet, true) },
                    { "First Person", new ModAction(CameraEnableFpv, false) },
                    { "Third Person", new ModAction(CameraEnableTpv, false) },
                    { "Follow Player", new ModAction(CameraToggleFollow, true) },
                    { "Flip Camera", new ModAction(CameraFlip, false) },
                    { "FOV +", new ModAction(() => CameraChangeFov(5f), false) },
                    { "FOV -", new ModAction(() => CameraChangeFov(-5f), false) },
                    { "Smooth +", new ModAction(() => CameraChangeSmoothing(0.01f), false) },
                    { "Smooth -", new ModAction(() => CameraChangeSmoothing(-0.01f), false) },
                }
            }
        },
        {
            "Anticheat",
            new ModCategory("shield.png")
            {
                Actions =
                {
                    { "Open Panel", new ModAction(ToggleAnticheatHud, true) },
                    { "Scan Lobby", new ModAction(PrintPhotonPlayerCustomProperties, false) },
                    { "Scan Mode", new ModAction(CycleScanMode, false) },
                    { "Auto Scan", new ModAction(ToggleAutoScan, true) },
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
    InitOutfitSlotCaches();
}

private static void ApplyLoadedToggleStates()
{
    if (!nameTagsEnabled)
        DisableNameTags();

    if (!autoScanEnabled)
    {
        autoScanRoomKey = "";
        autoScannedActorNumbers.Clear();
    }

    regionPlayersAutoEnabled = GetSavedToggle("Region Players", regionPlayersAutoEnabled);
    if (regionPlayersAutoEnabled)
        StartRegionPlayersAutoLoop();
}

public static void StartEnabledLoops()
{
    if (checkerEnabled && checkerCoroutine == null && CoroutineHandler.Instance != null)
        checkerCoroutine = CoroutineHandler.Instance.StartCoroutine(CheckerLoop());
}
private static void ShowCredits()
{
    ShowNotification("Made by Onegamingv2 | Version " + Main.PadVersion, NotificationDefaultDuration);
}
}
