using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static bool vrDiagnosticsHudEnabled;
    private static bool vrFpsCounterEnabled = true;
    private static bool vrPingDisplayEnabled = true;
    private static bool vrNetworkStatsEnabled = true;
    private static bool vrFpsAverageEnabled;
    private static bool vrFpsSlowEnabled;
    private static bool vrFrametimeEnabled;

    private static bool controllerHapticsEnabled = true;
    private static bool comfortReducedMotionEnabled;

    private static readonly float[] resolutionScaleValues = { 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };
    private static readonly string[] resolutionScaleNames = { "80%", "90%", "100%", "110%", "120%" };
    private static int resolutionScaleIndex = 2;

    private static readonly int[] msaaValues = { 0, 2, 4, 8 };
    private static readonly string[] msaaNames = { "Off", "2x", "4x", "8x" };
    private static int msaaIndex = 2;

    private static readonly ShadowQuality[] shadowValues =
    {
        ShadowQuality.Disable,
        ShadowQuality.HardOnly,
        ShadowQuality.All
    };
    private static readonly string[] shadowNames = { "Off", "Hard", "All" };
    private static int shadowIndex = 1;

    private static readonly float[] renderDistanceValues = { 300f, 500f, 800f, 1200f };
    private static readonly string[] renderDistanceNames = { "Near", "Normal", "Far", "Ultra" };
    private static int renderDistanceIndex = 1;

    private static readonly float[] masterVolumeValues = { 0.00f, 0.20f, 0.40f, 0.60f, 0.80f, 1.00f };
    private static readonly string[] masterVolumeNames = { "0%", "20%", "40%", "60%", "80%", "100%" };
    private static int masterVolumeIndex = 5;

    private static int graphicsQualityIndex = -1;

    public const string DefaultHudMoveBindCode = "Right:JoystickButton";
    public const string DefaultAimSelectBindCode = "Right:TriggerButton";

    private static string hudMoveBindCode = DefaultHudMoveBindCode;
    private static string aimSelectBindCode = DefaultAimSelectBindCode;
    private static Vector3 vrHudLocalPosition = Vector3.zero;
    private static Coroutine? hudMoveBindCaptureCoroutine;
    private static Coroutine? aimSelectBindCaptureCoroutine;

    public static bool IsControllerHapticsEnabled() => controllerHapticsEnabled;

    public static bool IsVrDiagnosticsHudEnabled() => vrDiagnosticsHudEnabled;
    public static bool IsVrFpsCounterEnabled() => vrFpsCounterEnabled;
    public static bool IsVrPingDisplayEnabled() => vrPingDisplayEnabled;
    public static bool IsVrNetworkStatsEnabled() => vrNetworkStatsEnabled;
    public static bool IsVrFpsAverageEnabled() => vrFpsAverageEnabled;
    public static bool IsVrFpsSlowEnabled() => vrFpsSlowEnabled;
    public static bool IsVrFrametimeEnabled() => vrFrametimeEnabled;

    public static string GetHudMoveBindCode() => string.IsNullOrWhiteSpace(hudMoveBindCode) ? DefaultHudMoveBindCode : hudMoveBindCode;
    public static string GetAimSelectBindCode() => string.IsNullOrWhiteSpace(aimSelectBindCode) ? DefaultAimSelectBindCode : aimSelectBindCode;

    public static Vector3 GetVrHudLocalPosition(Vector3 fallback)
    {
        return vrHudLocalPosition;
    }

    public static void SetVrHudLocalPosition(Vector3 position, bool save = true)
    {
        vrHudLocalPosition = position;
        if (save)
            SaveButtonStates();
    }

    public static void PersistVrHudLocalPosition() => SaveButtonStates();

    public static string GetVrDiagnosticsHudLabel() => vrDiagnosticsHudEnabled ? "On" : "Off";
    public static string GetVrFpsCounterLabel() => vrFpsCounterEnabled ? "On" : "Off";
    public static string GetVrPingDisplayLabel() => vrPingDisplayEnabled ? "On" : "Off";
    public static string GetVrNetworkStatsLabel() => vrNetworkStatsEnabled ? "On" : "Off";
    public static string GetVrFpsAverageLabel() => vrFpsAverageEnabled ? "On" : "Off";
    public static string GetVrFpsSlowLabel() => vrFpsSlowEnabled ? "On" : "Off";
    public static string GetVrFrametimeLabel() => vrFrametimeEnabled ? "On" : "Off";
    public static string GetHudMoveBindLabel() => Main.GetMenuOpenBindDisplayName(GetHudMoveBindCode());
    public static string GetAimSelectBindLabel() => Main.GetMenuOpenBindDisplayName(GetAimSelectBindCode());

    public static string GetGraphicsQualityLabel()
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0)
            return "Default";
        graphicsQualityIndex = Mathf.Clamp(graphicsQualityIndex, 0, names.Length - 1);
        return names[graphicsQualityIndex];
    }
    public static string GetResolutionScaleLabel() => resolutionScaleNames[Mathf.Clamp(resolutionScaleIndex, 0, resolutionScaleNames.Length - 1)];
    public static string GetMsaaLabel() => msaaNames[Mathf.Clamp(msaaIndex, 0, msaaNames.Length - 1)];
    public static string GetShadowQualityLabel() => shadowNames[Mathf.Clamp(shadowIndex, 0, shadowNames.Length - 1)];
    public static string GetRenderDistanceLabel() => renderDistanceNames[Mathf.Clamp(renderDistanceIndex, 0, renderDistanceNames.Length - 1)];
    public static string GetControllerHapticsLabel() => controllerHapticsEnabled ? "On" : "Off";
    public static string GetComfortModeLabel() => comfortReducedMotionEnabled ? "Reduced Motion" : "Standard";
    public static string GetMasterVolumeLabel() => masterVolumeNames[Mathf.Clamp(masterVolumeIndex, 0, masterVolumeNames.Length - 1)];

    private static void ToggleVrDiagnosticsHud()
    {
        vrDiagnosticsHudEnabled = GetSavedToggle("Diagnostics HUD", vrDiagnosticsHudEnabled);
        SavedToggleStates["Diagnostics HUD"] = vrDiagnosticsHudEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrFpsCounter()
    {
        vrFpsCounterEnabled = GetSavedToggle("FPS Counter", vrFpsCounterEnabled);
        SavedToggleStates["FPS Counter"] = vrFpsCounterEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrPingDisplay()
    {
        vrPingDisplayEnabled = GetSavedToggle("Ping Display", vrPingDisplayEnabled);
        SavedToggleStates["Ping Display"] = vrPingDisplayEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrNetworkStats()
    {
        vrNetworkStatsEnabled = GetSavedToggle("Network Stats", vrNetworkStatsEnabled);
        SavedToggleStates["Network Stats"] = vrNetworkStatsEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrFpsAverage()
    {
        vrFpsAverageEnabled = GetSavedToggle("Average FPS", vrFpsAverageEnabled);
        SavedToggleStates["Average FPS"] = vrFpsAverageEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrFpsSlow()
    {
        vrFpsSlowEnabled = GetSavedToggle("Slow FPS", vrFpsSlowEnabled);
        SavedToggleStates["Slow FPS"] = vrFpsSlowEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleVrFrametime()
    {
        vrFrametimeEnabled = GetSavedToggle("Frametime", vrFrametimeEnabled);
        SavedToggleStates["Frametime"] = vrFrametimeEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void StartHudMoveBindCapture()
    {
        if (Main.Instance == null)
            return;

        if (hudMoveBindCaptureCoroutine != null)
            Main.Instance.StopCoroutine(hudMoveBindCaptureCoroutine);

        hudMoveBindCaptureCoroutine = Main.Instance.StartCoroutine(CaptureHudMoveBind());
    }

    private static void StartAimSelectBindCapture()
    {
        if (Main.Instance == null)
            return;

        if (aimSelectBindCaptureCoroutine != null)
            Main.Instance.StopCoroutine(aimSelectBindCaptureCoroutine);

        aimSelectBindCaptureCoroutine = Main.Instance.StartCoroutine(CaptureAimSelectBind());
    }

    private static IEnumerator CaptureHudMoveBind()
    {
        HashSet<string> ignored = Main.GetPressedMenuOpenBinds();
        Main.Instance?.RefreshNamedButtonTitle("HUD Move Bind", "HUD Move Bind  :  Listening...");

        while (true)
        {
            if (Main.TryGetPressedMenuOpenBind(ignored, out string bindCode, out _))
            {
                hudMoveBindCode = bindCode;
                SaveButtonStates();
                UpdateSettingsLabels();
                break;
            }

            HashSet<string> currentlyPressed = Main.GetPressedMenuOpenBinds();
            ignored.RemoveWhere(code => !currentlyPressed.Contains(code));
            yield return null;
        }

        hudMoveBindCaptureCoroutine = null;
    }

    private static IEnumerator CaptureAimSelectBind()
    {
        HashSet<string> ignored = Main.GetPressedMenuOpenBinds();
        Main.Instance?.RefreshNamedButtonTitle("Aim Select Bind", "Aim Select Bind  :  Listening...");

        while (true)
        {
            if (Main.TryGetPressedMenuOpenBind(ignored, out string bindCode, out _))
            {
                aimSelectBindCode = bindCode;
                SaveButtonStates();
                UpdateSettingsLabels();
                break;
            }

            HashSet<string> currentlyPressed = Main.GetPressedMenuOpenBinds();
            ignored.RemoveWhere(code => !currentlyPressed.Contains(code));
            yield return null;
        }

        aimSelectBindCaptureCoroutine = null;
    }

    private static void ResetVrHudPosition()
    {
        VrDiagnosticsMonitor.ResetHudPosition();
        ShowNotification("HUD position reset", 1.5f);
        UpdateSettingsLabels();
    }

    private static void CycleGraphicsQuality()
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0)
            return;

        graphicsQualityIndex = (graphicsQualityIndex + 1) % names.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void CycleResolutionScale()
    {
        resolutionScaleIndex = (resolutionScaleIndex + 1) % resolutionScaleValues.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void CycleMsaa()
    {
        msaaIndex = (msaaIndex + 1) % msaaValues.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void CycleShadowQuality()
    {
        shadowIndex = (shadowIndex + 1) % shadowValues.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void CycleRenderDistance()
    {
        renderDistanceIndex = (renderDistanceIndex + 1) % renderDistanceValues.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleControllerHaptics()
    {
        controllerHapticsEnabled = GetSavedToggle("Controller Haptics", controllerHapticsEnabled);
        SavedToggleStates["Controller Haptics"] = controllerHapticsEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void ToggleComfortMode()
    {
        comfortReducedMotionEnabled = GetSavedToggle("Comfort Mode", comfortReducedMotionEnabled);
        SavedToggleStates["Comfort Mode"] = comfortReducedMotionEnabled;
        SaveButtonStates();
        ApplyVrSettings();
    }

    private static void CycleMasterVolume()
    {
        masterVolumeIndex = (masterVolumeIndex + 1) % masterVolumeValues.Length;
        SaveButtonStates();
        ApplyVrSettings();
    }

    internal static void ApplyVrSettings()
    {
        vrDiagnosticsHudEnabled = GetSavedToggle("Diagnostics HUD", vrDiagnosticsHudEnabled);
        vrFpsCounterEnabled = GetSavedToggle("FPS Counter", vrFpsCounterEnabled);
        vrPingDisplayEnabled = GetSavedToggle("Ping Display", vrPingDisplayEnabled);
        vrNetworkStatsEnabled = GetSavedToggle("Network Stats", vrNetworkStatsEnabled);
        vrFpsAverageEnabled = GetSavedToggle("Average FPS", vrFpsAverageEnabled);
        vrFpsSlowEnabled = GetSavedToggle("Slow FPS", vrFpsSlowEnabled);
        vrFrametimeEnabled = GetSavedToggle("Frametime", vrFrametimeEnabled);
        controllerHapticsEnabled = GetSavedToggle("Controller Haptics", controllerHapticsEnabled);
        comfortReducedMotionEnabled = GetSavedToggle("Comfort Mode", comfortReducedMotionEnabled);

        int qualityCount = QualitySettings.names != null ? QualitySettings.names.Length : 0;
        if (qualityCount > 0 && graphicsQualityIndex >= 0)
        {
            graphicsQualityIndex = Mathf.Clamp(graphicsQualityIndex, 0, qualityCount - 1);
            if (QualitySettings.GetQualityLevel() != graphicsQualityIndex)
                QualitySettings.SetQualityLevel(graphicsQualityIndex, true);
        }
        else if (qualityCount > 0)
        {
            graphicsQualityIndex = QualitySettings.GetQualityLevel();
        }

        resolutionScaleIndex = Mathf.Clamp(resolutionScaleIndex, 0, resolutionScaleValues.Length - 1);
        try
        {
            float desired = resolutionScaleValues[resolutionScaleIndex];
            if (Mathf.Abs(XRSettings.eyeTextureResolutionScale - desired) > 0.001f)
                XRSettings.eyeTextureResolutionScale = desired;
        }
        catch
        {
        }

        msaaIndex = Mathf.Clamp(msaaIndex, 0, msaaValues.Length - 1);
        if (QualitySettings.antiAliasing != msaaValues[msaaIndex])
            QualitySettings.antiAliasing = msaaValues[msaaIndex];

        shadowIndex = Mathf.Clamp(shadowIndex, 0, shadowValues.Length - 1);
        if (QualitySettings.shadows != shadowValues[shadowIndex])
            QualitySettings.shadows = shadowValues[shadowIndex];

        renderDistanceIndex = Mathf.Clamp(renderDistanceIndex, 0, renderDistanceValues.Length - 1);
        ApplyFarClip(renderDistanceValues[renderDistanceIndex]);

        masterVolumeIndex = Mathf.Clamp(masterVolumeIndex, 0, masterVolumeValues.Length - 1);
        AudioListener.volume = masterVolumeValues[masterVolumeIndex];

        if (Main.Instance != null)
            Main.Instance.SetReducedMotionEnabled(comfortReducedMotionEnabled);

        if (vrDiagnosticsHudEnabled)
            VrDiagnosticsMonitor.Ensure();
        VrDiagnosticsMonitor.SyncFromSettings();
        UpdateSettingsLabels();
    }

    private static void ApplyFarClip(float farClip)
    {
        if (Main.FirstPersonCamera != null)
            Main.FirstPersonCamera.farClipPlane = farClip;
        if (Main.ThirdPersonCamera != null && Main.ThirdPersonCamera != Main.FirstPersonCamera)
            Main.ThirdPersonCamera.farClipPlane = farClip;
    }

    internal static void AppendVrSettingsToSave(List<string> lines)
    {
        lines.Add("vrDiagnosticsHudEnabled=" + vrDiagnosticsHudEnabled);
        lines.Add("vrFpsCounterEnabled=" + vrFpsCounterEnabled);
        lines.Add("vrPingDisplayEnabled=" + vrPingDisplayEnabled);
        lines.Add("vrNetworkStatsEnabled=" + vrNetworkStatsEnabled);
        lines.Add("vrFpsAverageEnabled=" + vrFpsAverageEnabled);
        lines.Add("vrFpsSlowEnabled=" + vrFpsSlowEnabled);
        lines.Add("vrFrametimeEnabled=" + vrFrametimeEnabled);
        lines.Add("controllerHapticsEnabled=" + controllerHapticsEnabled);
        lines.Add("comfortReducedMotionEnabled=" + comfortReducedMotionEnabled);
        lines.Add("graphicsQualityIndex=" + graphicsQualityIndex);
        lines.Add("resolutionScaleIndex=" + resolutionScaleIndex);
        lines.Add("msaaIndex=" + msaaIndex);
        lines.Add("shadowIndex=" + shadowIndex);
        lines.Add("renderDistanceIndex=" + renderDistanceIndex);
        lines.Add("masterVolumeIndex=" + masterVolumeIndex);
        lines.Add("hudMoveBind=" + GetHudMoveBindCode());
        lines.Add("aimSelectBind=" + GetAimSelectBindCode());
        lines.Add("vrHudPosX=" + vrHudLocalPosition.x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        lines.Add("vrHudPosY=" + vrHudLocalPosition.y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        lines.Add("vrHudPosZ=" + vrHudLocalPosition.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static void ParseVrSetting(string line)
    {
        const string VrDiagnosticsHudPrefix = "vrDiagnosticsHudEnabled=";
        const string VrFpsPrefix = "vrFpsCounterEnabled=";
        const string VrPingPrefix = "vrPingDisplayEnabled=";
        const string VrNetworkPrefix = "vrNetworkStatsEnabled=";
        const string VrFpsAvgPrefix = "vrFpsAverageEnabled=";
        const string VrFpsSlowPrefix = "vrFpsSlowEnabled=";
        const string VrFtPrefix = "vrFrametimeEnabled=";
        const string HapticsPrefix = "controllerHapticsEnabled=";
        const string ComfortPrefix = "comfortReducedMotionEnabled=";
        const string QualityPrefix = "graphicsQualityIndex=";
        const string ResolutionPrefix = "resolutionScaleIndex=";
        const string MsaaPrefix = "msaaIndex=";
        const string ShadowPrefix = "shadowIndex=";
        const string RenderDistancePrefix = "renderDistanceIndex=";
        const string VolumePrefix = "masterVolumeIndex=";
        const string HudMoveBindPrefix = "hudMoveBind=";
        const string AimSelectBindPrefix = "aimSelectBind=";
        const string HudXPrefix = "vrHudPosX=";
        const string HudYPrefix = "vrHudPosY=";
        const string HudZPrefix = "vrHudPosZ=";

        if (line.StartsWith(VrDiagnosticsHudPrefix))
            bool.TryParse(line.Substring(VrDiagnosticsHudPrefix.Length), out vrDiagnosticsHudEnabled);
        else if (line.StartsWith(VrFpsPrefix))
            bool.TryParse(line.Substring(VrFpsPrefix.Length), out vrFpsCounterEnabled);
        else if (line.StartsWith(VrPingPrefix))
            bool.TryParse(line.Substring(VrPingPrefix.Length), out vrPingDisplayEnabled);
        else if (line.StartsWith(VrNetworkPrefix))
            bool.TryParse(line.Substring(VrNetworkPrefix.Length), out vrNetworkStatsEnabled);
        else if (line.StartsWith(VrFpsAvgPrefix))
            bool.TryParse(line.Substring(VrFpsAvgPrefix.Length), out vrFpsAverageEnabled);
        else if (line.StartsWith(VrFpsSlowPrefix))
            bool.TryParse(line.Substring(VrFpsSlowPrefix.Length), out vrFpsSlowEnabled);
        else if (line.StartsWith(VrFtPrefix))
            bool.TryParse(line.Substring(VrFtPrefix.Length), out vrFrametimeEnabled);
        else if (line.StartsWith(HapticsPrefix))
            bool.TryParse(line.Substring(HapticsPrefix.Length), out controllerHapticsEnabled);
        else if (line.StartsWith(ComfortPrefix))
            bool.TryParse(line.Substring(ComfortPrefix.Length), out comfortReducedMotionEnabled);
        else if (line.StartsWith(QualityPrefix))
            int.TryParse(line.Substring(QualityPrefix.Length), out graphicsQualityIndex);
        else if (line.StartsWith(ResolutionPrefix))
            int.TryParse(line.Substring(ResolutionPrefix.Length), out resolutionScaleIndex);
        else if (line.StartsWith(MsaaPrefix))
            int.TryParse(line.Substring(MsaaPrefix.Length), out msaaIndex);
        else if (line.StartsWith(ShadowPrefix))
            int.TryParse(line.Substring(ShadowPrefix.Length), out shadowIndex);
        else if (line.StartsWith(RenderDistancePrefix))
            int.TryParse(line.Substring(RenderDistancePrefix.Length), out renderDistanceIndex);
        else if (line.StartsWith(VolumePrefix))
            int.TryParse(line.Substring(VolumePrefix.Length), out masterVolumeIndex);
        else if (line.StartsWith(HudMoveBindPrefix))
        {
            string bind = line.Substring(HudMoveBindPrefix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(bind))
                hudMoveBindCode = bind;
        }
        else if (line.StartsWith(AimSelectBindPrefix))
        {
            string bind = line.Substring(AimSelectBindPrefix.Length).Trim();
            if (!string.IsNullOrWhiteSpace(bind))
                aimSelectBindCode = bind;
        }
        else if (line.StartsWith(HudXPrefix) && float.TryParse(line.Substring(HudXPrefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x))
            vrHudLocalPosition.x = x;
        else if (line.StartsWith(HudYPrefix) && float.TryParse(line.Substring(HudYPrefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
            vrHudLocalPosition.y = y;
        else if (line.StartsWith(HudZPrefix) && float.TryParse(line.Substring(HudZPrefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            vrHudLocalPosition.z = z;
    }

    internal static void EnsureVrToggleDefaults()
    {
        if (!SavedToggleStates.ContainsKey("Diagnostics HUD")) SavedToggleStates["Diagnostics HUD"] = vrDiagnosticsHudEnabled;
        if (!SavedToggleStates.ContainsKey("FPS Counter")) SavedToggleStates["FPS Counter"] = vrFpsCounterEnabled;
        if (!SavedToggleStates.ContainsKey("Ping Display")) SavedToggleStates["Ping Display"] = vrPingDisplayEnabled;
        if (!SavedToggleStates.ContainsKey("Network Stats")) SavedToggleStates["Network Stats"] = vrNetworkStatsEnabled;
        if (!SavedToggleStates.ContainsKey("Average FPS")) SavedToggleStates["Average FPS"] = vrFpsAverageEnabled;
        if (!SavedToggleStates.ContainsKey("Slow FPS")) SavedToggleStates["Slow FPS"] = vrFpsSlowEnabled;
        if (!SavedToggleStates.ContainsKey("Frametime")) SavedToggleStates["Frametime"] = vrFrametimeEnabled;
        if (!SavedToggleStates.ContainsKey("Controller Haptics")) SavedToggleStates["Controller Haptics"] = controllerHapticsEnabled;
        if (!SavedToggleStates.ContainsKey("Comfort Mode")) SavedToggleStates["Comfort Mode"] = comfortReducedMotionEnabled;
    }

    internal static void RefreshVrSettingsLabels()
    {
        if (Main.Instance == null)
            return;

        Main.Instance.RefreshNamedButtonTitle("Diagnostics HUD", "Diagnostics HUD  :  " + GetVrDiagnosticsHudLabel());
        Main.Instance.RefreshNamedButtonTitle("FPS Counter", "FPS Counter  :  " + GetVrFpsCounterLabel());
        Main.Instance.RefreshNamedButtonTitle("Ping Display", "Ping Display  :  " + GetVrPingDisplayLabel());
        Main.Instance.RefreshNamedButtonTitle("Network Stats", "Network Stats  :  " + GetVrNetworkStatsLabel());
        Main.Instance.RefreshNamedButtonTitle("Average FPS", "Average FPS  :  " + GetVrFpsAverageLabel());
        Main.Instance.RefreshNamedButtonTitle("Slow FPS", "Slow FPS  :  " + GetVrFpsSlowLabel());
        Main.Instance.RefreshNamedButtonTitle("Frametime", "Frametime  :  " + GetVrFrametimeLabel());
        Main.Instance.RefreshNamedButtonTitle("HUD Move Bind", "HUD Move Bind  :  " + GetHudMoveBindLabel());
        Main.Instance.RefreshNamedButtonTitle("Aim Select Bind", "Aim Select Bind  :  " + GetAimSelectBindLabel());
        Main.Instance.RefreshNamedButtonTitle("Reset HUD Pos", "Reset HUD Pos");
        Main.Instance.RefreshNamedButtonTitle("Graphics Quality", "Graphics Quality  :  " + GetGraphicsQualityLabel());
        Main.Instance.RefreshNamedButtonTitle("Resolution Scale", "Resolution Scale  :  " + GetResolutionScaleLabel());
        Main.Instance.RefreshNamedButtonTitle("MSAA", "MSAA  :  " + GetMsaaLabel());
        Main.Instance.RefreshNamedButtonTitle("Shadow Quality", "Shadow Quality  :  " + GetShadowQualityLabel());
        Main.Instance.RefreshNamedButtonTitle("Render Distance", "Render Distance  :  " + GetRenderDistanceLabel());
        Main.Instance.RefreshNamedButtonTitle("Controller Haptics", "Controller Haptics  :  " + GetControllerHapticsLabel());
        Main.Instance.RefreshNamedButtonTitle("Comfort Mode", "Comfort Mode  :  " + GetComfortModeLabel());
        Main.Instance.RefreshNamedButtonTitle("Master Volume", "Master Volume  :  " + GetMasterVolumeLabel());
    }
}
