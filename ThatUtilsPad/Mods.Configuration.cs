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
    public static void SaveToggleState(string identifier, bool isOn)
    {
        SavedToggleStates[identifier] = isOn;
        SaveButtonStates();
    }

    public static bool GetSavedToggle(string identifier, bool fallback = false)
    {
        return SavedToggleStates.TryGetValue(identifier, out bool value) ? value : fallback;
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
            lines.Add("clickSoundEnabled=" + clickSoundEnabled);
            lines.Add("startupSoundEnabled=" + startupSoundEnabled);
            lines.Add("menuOpenSoundEnabled=" + menuOpenSoundEnabled);
            lines.Add("buttonPopSoundEnabled=" + buttonPopSoundEnabled);
            lines.Add("notifSoundEnabled=" + notifSoundEnabled);
            lines.Add("smoothMenuEnabled=" + smoothMenuEnabled);
            lines.Add("smoothingStrengthIndex=" + smoothingStrengthIndex);
            lines.Add("menuScaleIndex=" + menuScaleIndex);
            lines.Add("headDistanceIndex=" + headDistanceIndex);
            lines.Add("themeIndex=" + themeIndex);
            lines.Add("doubleClickOpenEnabled=" + doubleClickOpenEnabled);
            lines.Add("menuOpenBind=" + menuOpenBindCode);
            lines.Add("scanModeIndex=" + scanModeIndex);
            lines.Add("nameTagScaleIndex=" + nameTagScaleIndex);
            lines.Add("nameTagFadeDistanceIndex=" + nameTagFadeDistanceIndex);
            AppendVrSettingsToSave(lines);
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
            SavedToggleStates["Startup Sound"] = startupSoundEnabled;
            SavedToggleStates["Menu Open Sound"] = menuOpenSoundEnabled;
            SavedToggleStates["Button Pop Sound"] = buttonPopSoundEnabled;
            SavedToggleStates["Notif Sound"] = notifSoundEnabled;
            SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
            SavedToggleStates["Distance Fade"] = nameTagDistanceFadeEnabled;
            SavedToggleStates["Auto Scan"] = autoScanEnabled;
            SavedToggleStates["Nametags"] = nameTagsEnabled;
            SavedToggleStates["Aim Select"] = checkerEnabled;
            SavedToggleStates["Select User"] = checkerEnabled;
            EnsureVrToggleDefaults();
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
                        currentClickIndex = idx;
                }
                else if (line.StartsWith("clickSoundEnabled="))
                {
                    if (bool.TryParse(line.Substring(18), out bool val))
                        clickSoundEnabled = val;
                }
                else if (line.StartsWith("startupSoundEnabled="))
                {
                    if (bool.TryParse(line.Substring(20), out bool val))
                    {
                        startupSoundEnabled = val;
                        SavedToggleStates["Startup Sound"] = val;
                    }
                }
                else if (line.StartsWith("menuOpenSoundEnabled="))
                {
                    if (bool.TryParse(line.Substring(21), out bool val))
                    {
                        menuOpenSoundEnabled = val;
                        SavedToggleStates["Menu Open Sound"] = val;
                    }
                }
                else if (line.StartsWith("buttonPopSoundEnabled="))
                {
                    if (bool.TryParse(line.Substring(22), out bool val))
                    {
                        buttonPopSoundEnabled = val;
                        SavedToggleStates["Button Pop Sound"] = val;
                    }
                }
                else if (line.StartsWith("notifSoundEnabled="))
                {
                    if (bool.TryParse(line.Substring(18), out bool val))
                    {
                        notifSoundEnabled = val;
                        SavedToggleStates["Notif Sound"] = val;
                    }
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
                else
                {
                    ParseVrSetting(line);
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[TUP] Failed to load button states: " + e.Message); }

        SavedToggleStates["Menu Smoothing"] = smoothMenuEnabled;
        SavedToggleStates["Double Open"] = doubleClickOpenEnabled;
        if (SavedToggleStates.TryGetValue("Startup Sound", out bool startupToggle))
            startupSoundEnabled = startupToggle;
        else
            SavedToggleStates["Startup Sound"] = startupSoundEnabled;
        if (SavedToggleStates.TryGetValue("Menu Open Sound", out bool menuOpenToggle))
            menuOpenSoundEnabled = menuOpenToggle;
        else
            SavedToggleStates["Menu Open Sound"] = menuOpenSoundEnabled;
        if (SavedToggleStates.TryGetValue("Button Pop Sound", out bool buttonPopToggle))
            buttonPopSoundEnabled = buttonPopToggle;
        else
            SavedToggleStates["Button Pop Sound"] = buttonPopSoundEnabled;
        if (SavedToggleStates.TryGetValue("Notif Sound", out bool notifToggle))
            notifSoundEnabled = notifToggle;
        else
            SavedToggleStates["Notif Sound"] = notifSoundEnabled;
        if (!SavedToggleStates.ContainsKey("Nametag Distance Fade")) SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
        if (!SavedToggleStates.ContainsKey("Distance Fade")) SavedToggleStates["Distance Fade"] = SavedToggleStates["Nametag Distance Fade"];
        if (!SavedToggleStates.ContainsKey("Auto Scan")) SavedToggleStates["Auto Scan"] = autoScanEnabled;
        if (!SavedToggleStates.ContainsKey("Nametags")) SavedToggleStates["Nametags"] = nameTagsEnabled;
        if (!SavedToggleStates.ContainsKey("Aim Select"))
            SavedToggleStates["Aim Select"] = GetSavedToggle("Select User", checkerEnabled);
        if (!SavedToggleStates.ContainsKey("Select User")) SavedToggleStates["Select User"] = checkerEnabled;
        nameTagDistanceFadeEnabled = GetSavedToggle("Distance Fade", GetSavedToggle("Nametag Distance Fade", nameTagDistanceFadeEnabled));
        SavedToggleStates["Distance Fade"] = nameTagDistanceFadeEnabled;
        SavedToggleStates["Nametag Distance Fade"] = nameTagDistanceFadeEnabled;
        autoScanEnabled = SavedToggleStates["Auto Scan"];
        nameTagsEnabled = SavedToggleStates["Nametags"];
        checkerEnabled = GetSavedToggle("Aim Select", GetSavedToggle("Select User", checkerEnabled));
        SavedToggleStates["Aim Select"] = checkerEnabled;
        SavedToggleStates["Select User"] = checkerEnabled;
        EnsureVrToggleDefaults();
        targetNameTagScale = nameTagScaleSteps[Mathf.Clamp(nameTagScaleIndex, 0, nameTagScaleSteps.Length - 1)];
        if (clickSounds.Count > 0)
            currentClickIndex = Mathf.Clamp(currentClickIndex, 0, clickSounds.Count - 1);
        else
            currentClickIndex = 0;
    }
    
    
    private static string FormatDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;
        
        return char.ToUpper(name[0]) + name.Substring(1).ToLower();
    }
    

    public static List<ClickSoundEntry> clickSounds = new List<ClickSoundEntry>();
    public static int currentClickIndex = 0;

    private static bool startupSoundEnabled = true;
    private static bool menuOpenSoundEnabled = true;
    private static bool buttonPopSoundEnabled = true;
    private static bool notifSoundEnabled = true;
    private static bool clickSoundEnabled = true;

    public static void RebuildClickSounds()
    {
        clickSounds.Clear();
        if (Main.Instance == null)
            return;

        void Add(string name, AudioClip clip)
        {
            if (clip != null)
                clickSounds.Add(new ClickSoundEntry(name, clip));
        }

        Add("watch", Main.Instance.watchSound);
        Add("creamy", Main.Instance.creamySound);
        Add("destiny", Main.Instance.destinySound);
        Add("minecraft", Main.Instance.minecraftSound);
        Add("Wii", Main.Instance.wiiSound);
        Add("untitled", Main.Instance.untitledClickSound);

        if (clickSounds.Count > 0)
            currentClickIndex = Mathf.Clamp(currentClickIndex, 0, clickSounds.Count - 1);
    }

    public static bool IsStartupSoundEnabled() => startupSoundEnabled;
    public static bool IsMenuOpenSoundEnabled() => menuOpenSoundEnabled;
    public static bool IsButtonPopSoundEnabled() => buttonPopSoundEnabled;
    public static bool IsNotifSoundEnabled() => notifSoundEnabled;
    public static bool IsClickSoundEnabled() => clickSoundEnabled;

    public static string GetClickSoundLabel()
    {
        if (!clickSoundEnabled)
            return "Off";
        if (clickSounds.Count == 0)
            return "-";
        currentClickIndex = Mathf.Clamp(currentClickIndex, 0, clickSounds.Count - 1);
        return FormatDisplayName(clickSounds[currentClickIndex].Name);
    }

    public static string GetStartupSoundLabel() => startupSoundEnabled ? "On" : "Off";
    public static string GetMenuOpenSoundLabel() => menuOpenSoundEnabled ? "On" : "Off";
    public static string GetButtonPopSoundLabel() => buttonPopSoundEnabled ? "On" : "Off";
    public static string GetNotifSoundLabel() => notifSoundEnabled ? "On" : "Off";

    private static void ToggleClickSound()
    {
        if (clickSounds.Count == 0)
        {
            RebuildClickSounds();
            if (clickSounds.Count == 0) return;
        }

        if (!clickSoundEnabled)
        {
            clickSoundEnabled = true;
            currentClickIndex = 0;
        }
        else
        {
            currentClickIndex++;
            if (currentClickIndex >= clickSounds.Count)
            {
                clickSoundEnabled = false;
                currentClickIndex = 0;
                SaveButtonStates();
                if (Main.clickSoundText != null)
                    Main.clickSoundText.text = "Click Sound  :  Off";
                return;
            }
        }

        var entry = clickSounds[currentClickIndex];
        if (Main.Instance != null)
            Main.Instance.currentClickSound = entry.Clip;

        SaveButtonStates();

        if (Main.clickSoundText != null)
            Main.clickSoundText.text = "Click Sound  :  " + FormatDisplayName(entry.Name);
    }

    private static void ToggleStartupSound()
    {
        startupSoundEnabled = GetSavedToggle("Startup Sound", startupSoundEnabled);
        SavedToggleStates["Startup Sound"] = startupSoundEnabled;
        SaveButtonStates();
        UpdateSettingsLabels();
    }

    private static void ToggleMenuOpenSound()
    {
        menuOpenSoundEnabled = GetSavedToggle("Menu Open Sound", menuOpenSoundEnabled);
        SavedToggleStates["Menu Open Sound"] = menuOpenSoundEnabled;
        SaveButtonStates();
        UpdateSettingsLabels();
    }

    private static void ToggleButtonPopSound()
    {
        buttonPopSoundEnabled = GetSavedToggle("Button Pop Sound", buttonPopSoundEnabled);
        SavedToggleStates["Button Pop Sound"] = buttonPopSoundEnabled;
        SaveButtonStates();
        UpdateSettingsLabels();
    }

    private static void ToggleNotifSound()
    {
        notifSoundEnabled = GetSavedToggle("Notif Sound", notifSoundEnabled);
        SavedToggleStates["Notif Sound"] = notifSoundEnabled;
        SaveButtonStates();
        UpdateSettingsLabels();
    }


    private static bool smoothMenuEnabled = true;

    private static readonly string[] smoothingStrengthNames = { "Soft", "Normal", "Sharp" };
    private static readonly float[] smoothingStrengthValues = { 10f, 30f, 60f };
    private static int smoothingStrengthIndex = 1;

    private static readonly string[] menuScaleNames = { "Compact", "Normal", "Large" };
    private static readonly float[] menuScaleValues = { 0.32f, 0.375f, 0.43f };
    private static int menuScaleIndex = 1;

    private static readonly string[] headDistanceNames = { "Close", "Normal", "Far" };
    private static readonly float[] headDistanceValues = { 0.45f, 0.6f, 0.8f };
    private static int headDistanceIndex = 1;

    private static bool doubleClickOpenEnabled;
    private static string menuOpenBindCode = Main.DefaultMenuOpenBindCode;
    private static Coroutine openBindCaptureCoroutine;

    private static readonly string[] themeNames =
    {
        "Default", "Sakura", "Ocean", "Ember", "Mint", "Midnight", "Amber", "Crimson",
        "Violet", "Ice", "Toxic", "Rose", "Solar", "Slate", "Neon", "Grove", "Candy", "Copper"
    };
    private static readonly MenuThemePalette[] themePalettes =
    {
        MenuTheme.Themes.Default,
        MenuTheme.Themes.Sakura,
        MenuTheme.Themes.Ocean,
        MenuTheme.Themes.Ember,
        MenuTheme.Themes.Mint,
        MenuTheme.Themes.Midnight,
        MenuTheme.Themes.Amber,
        MenuTheme.Themes.Crimson,
        MenuTheme.Themes.Violet,
        MenuTheme.Themes.Ice,
        MenuTheme.Themes.Toxic,
        MenuTheme.Themes.Rose,
        MenuTheme.Themes.Solar,
        MenuTheme.Themes.Slate,
        MenuTheme.Themes.Neon,
        MenuTheme.Themes.Grove,
        MenuTheme.Themes.Candy,
        MenuTheme.Themes.Copper
    };
    private static int themeIndex = 1;


    private static void ToggleDoubleOpen()
    {
        doubleClickOpenEnabled = GetSavedToggle("Double Open", doubleClickOpenEnabled);
        ApplySavedSettings();
        SaveButtonStates();
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
        smoothMenuEnabled = GetSavedToggle("Menu Smoothing", smoothMenuEnabled);
        ApplySavedSettings();
        SaveButtonStates();
    }

    private static void CycleMenuSmoothingStrength()
    {
        smoothingStrengthIndex = (smoothingStrengthIndex + 1) % smoothingStrengthNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
    }

    private static void CycleMenuScale()
    {
        menuScaleIndex = (menuScaleIndex + 1) % menuScaleNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
    }

    private static void CycleHeadDistance()
    {
        headDistanceIndex = (headDistanceIndex + 1) % headDistanceNames.Length;
        ApplySavedSettings();
        SaveButtonStates();
    }

    private static void CycleTheme()
    {
        themeIndex = (themeIndex + 1) % themePalettes.Length;
        ApplyTheme();
                SaveButtonStates();
        UpdateSettingsLabels();
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

        RebuildClickSounds();
        if (clickSounds.Count > 0)
        {
            currentClickIndex = Mathf.Clamp(currentClickIndex, 0, clickSounds.Count - 1);
            Main.Instance.currentClickSound = clickSounds[currentClickIndex].Clip;
        }
        else
        {
            Main.Instance.currentClickSound = null;
        }

        Main.Instance.SetMenuSmoothing(smoothMenuEnabled, smoothingStrengthValues[smoothingStrengthIndex]);
        Main.Instance.SetMenuScale(menuScaleValues[menuScaleIndex]);
        Main.Instance.SetHeadDistance(headDistanceValues[headDistanceIndex]);
        Main.Instance.SetMenuOpenOptions(doubleClickOpenEnabled, menuOpenBindCode);
        ApplyTheme();
        ApplyVrSettings();
        UpdateRegionLabel();
        UpdateSettingsLabels();
    }

    public static string GetSmoothingStrengthLabel() => smoothingStrengthNames[smoothingStrengthIndex];
    public static string GetMenuScaleLabel() => menuScaleNames[menuScaleIndex];
    public static string GetHeadDistanceLabel() => headDistanceNames[headDistanceIndex];
    public static bool IsSmoothMenuEnabled() => smoothMenuEnabled;
    public static bool IsDoubleClickOpenEnabled() => doubleClickOpenEnabled;
    public static string GetMenuOpenBindCode() => menuOpenBindCode;

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

        if (Main.clickSoundText != null)
            Main.clickSoundText.text = "Click Sound  :  " + GetClickSoundLabel();

        if (Main.startupSoundText != null)
            Main.startupSoundText.text = "Startup Sound  :  " + GetStartupSoundLabel();

        if (Main.menuOpenSoundText != null)
            Main.menuOpenSoundText.text = "Menu Open Sound  :  " + GetMenuOpenSoundLabel();

        if (Main.buttonPopSoundText != null)
            Main.buttonPopSoundText.text = "Button Pop Sound  :  " + GetButtonPopSoundLabel();

        if (Main.notifSoundText != null)
            Main.notifSoundText.text = "Notif Sound  :  " + GetNotifSoundLabel();

        RefreshVrSettingsLabels();
        UpdateRegionLabel();
    }
    
}
