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

        LoadPropertyListIfNeeded();
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
        LoadPropertyListIfNeeded();
        return propertyLegality;
    }

    private static void PrintDetectedPropertySignatures(Player player, string playerLabel)
    {
        Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);

        if (hits.Count == 0)
        {
            return;
        }

        foreach (var hit in hits)
        {
            PropertySignature signature = propertySignatures[hit.Key];
            string verdict = signature.IsLegal ? "LEGAL" : "ILLEGAL";
        }
    }

    private static Dictionary<string, List<string>> FindPropertySignatureHits(Player player)
    {
        LoadPropertyListIfNeeded();

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
            try { sources["FPS"] = RigBits.GetFPS(rig).ToString(); } catch { }
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

    private static void LoadPropertyListIfNeeded()
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

        string normalized = SquishPropertyText(entry);
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

        string normalizedText = SquishPropertyText(text);
        string normalizedSignature = SquishPropertyText(signature);
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

    private static string SquishPropertyText(string text)
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

        if (RigBits.GetPCTier(rig) > 0)
            suspiciouslySteam++;
        else if (RigBits.GetQuestTier(rig) > 0)
            suspiciouslyQuest++;


        if (suspiciouslySteam > suspiciouslyPC && suspiciouslySteam > suspiciouslyQuest) return "Steam";
        if (suspiciouslyPC > suspiciouslySteam && suspiciouslyPC > suspiciouslyQuest) return "PC";
        if (suspiciouslyQuest > suspiciouslySteam && suspiciouslyQuest > suspiciouslyPC) return "Standalone";

        return "Standalone";
    }
    
    public static readonly Dictionary<string, float> waitingForCreationDate = new Dictionary<string, float>();
    public static readonly Dictionary<string, string> creationDates = new Dictionary<string, string>();
    public static string GetCreationDate(string input, Action<string> onTranslated = null, string format = "MM/dd/yyyy")
    {
        if (creationDates.TryGetValue(input, out string date))
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
        if (creationDates.TryGetValue(userId, out string date))
        {
            onTranslated?.Invoke(date);
            return;
        }

        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest { PlayFabId = userId }, delegate (GetAccountInfoResult result)
        {
            string creationDate = result.AccountInfo.Created.ToString(format);
            creationDates[userId] = creationDate;

            onTranslated?.Invoke(creationDate);
        }, delegate { creationDates[userId] = "Error"; onTranslated?.Invoke("Error"); });
    }
    
}
