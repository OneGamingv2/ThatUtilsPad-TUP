using System;
using System.Collections;
using GorillaLocomotion;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
    private const float AutoScanTraversalInterval = 0.5f;
    private const float PropertyScanCacheSeconds = 2f;
    private static float nextAutoScanTraversalTime;
    private static float nextAutoScanFullRefreshTime;
    private const float AutoScanFullRefreshInterval = 8f;

    private readonly struct NormalizedScanSource
    {
        public readonly string Key;
        public readonly string Normalized;
        public readonly string Compact;

        public NormalizedScanSource(string key, string value)
        {
            Key = key;
            Normalized = SquishPropertyText(value);
            Compact = Normalized.Replace(" ", "");
        }
    }

    private sealed class PropertyHitCacheEntry
    {
        public float ExpiresAt;
        public Dictionary<string, List<string>> Hits;
    }

    private struct PropertyCountCacheEntry
    {
        public float ExpiresAt;
        public int LegalCount;
        public int IllegalCount;
    }

    private static readonly Dictionary<string, PropertyHitCacheEntry> propertyHitCache =
        new Dictionary<string, PropertyHitCacheEntry>(StringComparer.Ordinal);
    private static readonly Dictionary<string, PropertyCountCacheEntry> propertyCountCache =
        new Dictionary<string, PropertyCountCacheEntry>(StringComparer.Ordinal);
    private static string propertyCacheRoomKey = "";

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
        autoScanEnabled = GetSavedToggle("Auto Scan", autoScanEnabled);
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
        try
        {
            AutoScanLoopInternal();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] AutoScanLoop: " + ex.Message);
        }
    }

    private static void AutoScanLoopInternal()
    {
        if (!autoScanEnabled)
            return;

        if (Time.time < nextAutoScanTraversalTime)
            return;
        nextAutoScanTraversalTime = Time.time + AutoScanTraversalInterval;

        if (!PhotonNetwork.InRoom)
        {
            autoScanRoomKey = "";
            autoScannedActorNumbers.Clear();
            ClearPropertyScanCaches();
            return;
        }

        string roomKey = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "room";
        if (!roomKey.Equals(autoScanRoomKey, StringComparison.Ordinal))
        {
            autoScanRoomKey = roomKey;
            autoScannedActorNumbers.Clear();
            ClearPropertyScanCaches();
        }

        if (Time.time >= nextAutoScanFullRefreshTime)
        {
            nextAutoScanFullRefreshTime = Time.time + AutoScanFullRefreshInterval;
            autoScannedActorNumbers.Clear();
            ClearPropertyScanCaches();
        }

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player == PhotonNetwork.LocalPlayer)
                continue;

            try
            {

                FindPropertySignatureHits(player);

                if (!autoScannedActorNumbers.Add(player.ActorNumber))
                    continue;

                ScanPhotonPlayerAndNotify(player, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP] AutoScan player failed: " + ex.Message);
            }
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
        try
        {
            return FindPropertySignatureHitsUnsafe(player);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP PROP SCAN] FindPropertySignatureHits failed: " + ex.Message);
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, List<string>> FindPropertySignatureHitsUnsafe(Player player)
    {
        LoadPropertyListIfNeeded();

        string cacheKey = GetPropertyScanCacheKey(player);
        if (propertyHitCache.TryGetValue(cacheKey, out PropertyHitCacheEntry cached) &&
            Time.time < cached.ExpiresAt)
            return cached.Hits;

        VRRig rig = GetRigForPhotonPlayer(player);
        NormalizedScanSource[] sources = BuildNormalizedPropertyScanSources(player, rig);
        Dictionary<string, List<string>> hits = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            NormalizedScanSource source = sources[sourceIndex];
            foreach (PropertySignature signature in propertySignatures.Values)
            {
                if (!NormalizedTextContainsSignature(source, signature))
                    continue;

                if (!hits.TryGetValue(signature.Name, out List<string> sourceNames))
                {
                    sourceNames = new List<string>();
                    hits[signature.Name] = sourceNames;
                }

                if (!sourceNames.Contains(source.Key))
                    sourceNames.Add(source.Key);
            }
        }

        propertyHitCache[cacheKey] = new PropertyHitCacheEntry
        {
            ExpiresAt = Time.time + PropertyScanCacheSeconds,
            Hits = hits
        };
        return hits;
    }

    public static void GetPropertySignatureCounts(VRRig rig, Player player, out int legalCount, out int illegalCount)
    {
        legalCount = 0;
        illegalCount = 0;
        if (player == null)
            return;

        LoadPropertyListIfNeeded();
        string cacheKey = GetPropertyScanCacheKey(player);
        if (propertyCountCache.TryGetValue(cacheKey, out PropertyCountCacheEntry cached) &&
            Time.time < cached.ExpiresAt)
        {
            legalCount = cached.LegalCount;
            illegalCount = cached.IllegalCount;
            return;
        }

        NormalizedScanSource[] sources = BuildNormalizedPropertyScanSources(player, rig);
        foreach (PropertySignature signature in propertySignatures.Values)
        {
            bool matched = false;
            for (int i = 0; i < sources.Length; i++)
            {
                if (!NormalizedTextContainsSignature(sources[i], signature))
                    continue;
                matched = true;
                break;
            }

            if (!matched)
                continue;
            if (signature.IsLegal)
                legalCount++;
            else
                illegalCount++;
        }

        propertyCountCache[cacheKey] = new PropertyCountCacheEntry
        {
            ExpiresAt = Time.time + PropertyScanCacheSeconds,
            LegalCount = legalCount,
            IllegalCount = illegalCount
        };
    }

    private static NormalizedScanSource[] BuildNormalizedPropertyScanSources(Player player, VRRig rig)
    {
        List<NormalizedScanSource> sources = new List<NormalizedScanSource>(32);
        sources.Add(new NormalizedScanSource(
            "PhotonPlayer",
            $"{player.NickName} {player.UserId} {player.ActorNumber}"));

        StringBuilder allProperties = new StringBuilder();
        if (player.CustomProperties != null)
        {
            foreach (object keyObj in player.CustomProperties.Keys)
            {
                string key = keyObj != null ? keyObj.ToString() : "";
                object rawValue = player.CustomProperties[keyObj];
                string valueText = FormatPhotonCustomPropertyValue(rawValue);
                string pairText = key + " " + valueText;

                if (allProperties.Length > 0)
                    allProperties.Append(' ');
                allProperties.Append(pairText);

                string safeKey = string.IsNullOrEmpty(key) ? "unnamed" : key;
                sources.Add(new NormalizedScanSource("Prop:" + safeKey, pairText));
                if (!string.IsNullOrEmpty(key))
                    sources.Add(new NormalizedScanSource("PropKey:" + safeKey, key));
                if (!string.IsNullOrEmpty(valueText) &&
                    !string.Equals(valueText, "null", StringComparison.OrdinalIgnoreCase))
                {
                    sources.Add(new NormalizedScanSource("PropVal:" + safeKey, valueText));
                }

                AppendNestedPropertyScanSources(sources, safeKey, rawValue, 0);
            }
        }

        sources.Add(new NormalizedScanSource("CustomProperties", allProperties.ToString()));

        if (rig != null)
        {
            try { sources.Add(new NormalizedScanSource("Cosmetics", rig.Cosmetics())); } catch { }
            try { sources.Add(new NormalizedScanSource("Platform", rig.GetPlatform())); } catch { }
            try { sources.Add(new NormalizedScanSource("FPS", RigBits.GetFPS(rig).ToString())); } catch { }
        }

        return sources.ToArray();
    }

    private static void AppendNestedPropertyScanSources(
        List<NormalizedScanSource> sources,
        string parentKey,
        object value,
        int depth)
    {
        if (sources == null || value == null || depth > 4)
            return;

        if (value is ExitGames.Client.Photon.Hashtable table)
        {
            foreach (object nestedKeyObj in table.Keys)
            {
                string nestedKey = nestedKeyObj != null ? nestedKeyObj.ToString() : "";
                object nestedValue = table[nestedKeyObj];
                string path = parentKey + "." + (string.IsNullOrEmpty(nestedKey) ? "item" : nestedKey);
                string nestedText = nestedKey + " " + FormatPhotonCustomPropertyValue(nestedValue);
                sources.Add(new NormalizedScanSource("Prop:" + path, nestedText));
                AppendNestedPropertyScanSources(sources, path, nestedValue, depth + 1);
            }
            return;
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                string nestedKey = entry.Key != null ? entry.Key.ToString() : "";
                string path = parentKey + "." + (string.IsNullOrEmpty(nestedKey) ? "item" : nestedKey);
                sources.Add(new NormalizedScanSource(
                    "Prop:" + path,
                    nestedKey + " " + FormatPhotonCustomPropertyValue(entry.Value)));
                AppendNestedPropertyScanSources(sources, path, entry.Value, depth + 1);
            }
            return;
        }

        if (value is string || value is byte[] || value.GetType().IsPrimitive)
            return;

        if (value is System.Collections.IEnumerable enumerable)
        {
            int index = 0;
            foreach (object item in enumerable)
            {
                if (item == null || item is string || item.GetType().IsPrimitive)
                {
                    index++;
                    continue;
                }

                string path = parentKey + "[" + index + "]";
                sources.Add(new NormalizedScanSource("Prop:" + path, FormatPhotonCustomPropertyValue(item)));
                AppendNestedPropertyScanSources(sources, path, item, depth + 1);
                index++;
                if (index > 64)
                    break;
            }
        }
    }

    public static void InvalidatePropertyScanCache(Player player = null)
    {
        if (player == null)
        {
            ClearPropertyScanCaches();
            return;
        }

        string key = GetPropertyScanCacheKey(player);
        propertyHitCache.Remove(key);
        propertyCountCache.Remove(key);
    }

    private static string GetPropertyScanCacheKey(Player player)
    {
        string room = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : "";
        if (!string.Equals(propertyCacheRoomKey, room, StringComparison.Ordinal))
        {
            ClearPropertyScanCaches();
            propertyCacheRoomKey = room;
        }
        return !string.IsNullOrEmpty(player.UserId)
            ? room + "|u:" + player.UserId
            : room + "|a:" + player.ActorNumber;
    }

    private static void ClearPropertyScanCaches()
    {
        propertyHitCache.Clear();
        propertyCountCache.Clear();
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
                string normalized = SquishPropertyText(entry);
                propertySignatures[entry] = new PropertySignature(
                    entry,
                    isLegal,
                    section,
                    normalized,
                    normalized.Replace(" ", ""));
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
            "resurgence", "elixir", "orbit", "rexon", "cosmetx", "bananaos", "walksimulator",
            "recroomrig", "hansolo", "genesis", "dtasloi", "polkadotted", "zlothy", "kigui"
        };

        for (int i = 0; i < illegalTokens.Length; i++)
            if (normalized.Contains(illegalTokens[i]))
                return false;
        return true;
    }

    private static bool NormalizedTextContainsSignature(NormalizedScanSource source, PropertySignature signature)
    {
        if (string.IsNullOrEmpty(source.Normalized) || string.IsNullOrEmpty(signature.Normalized))
            return false;

        if (signature.RequiresWholeToken)
            return ContainsWholeNormalizedToken(source.Normalized, signature.Normalized);
        if (source.Normalized.Contains(signature.Normalized))
            return true;
        return signature.Compact.Length > 3 && source.Compact.Contains(signature.Compact);
    }

    private static bool ContainsWholeNormalizedToken(string text, string token)
    {
        int start = 0;
        while ((start = text.IndexOf(token, start, StringComparison.Ordinal)) >= 0)
        {
            int end = start + token.Length;
            if ((start == 0 || text[start - 1] == ' ') && (end == text.Length || text[end] == ' '))
                return true;
            start = end;
        }
        return false;
    }

    private static string SquishPropertyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        StringBuilder result = new StringBuilder(text.Length);
        bool pendingSpace = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToLowerInvariant(text[i]);
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && result.Length > 0)
                    result.Append(' ');
                result.Append(c);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return result.ToString();
    }

    private static string FormatPhotonCustomPropertyValue(object value)
    {
        if (value == null) return "null";
        if (value is string) return value.ToString();
        if (value is byte[] bytes)
            return BitConverter.ToString(bytes);
        if (value is ExitGames.Client.Photon.Hashtable table)
        {
            StringBuilder tableResult = new StringBuilder("{");
            bool first = true;
            foreach (object tableKey in table.Keys)
            {
                if (!first) tableResult.Append(' ');
                first = false;
                tableResult.Append(tableKey).Append('=')
                    .Append(FormatPhotonCustomPropertyValue(table[tableKey]));
            }
            return tableResult.Append('}').ToString();
        }
        if (value is System.Collections.IDictionary dictionary)
        {
            StringBuilder dictResult = new StringBuilder("{");
            bool first = true;
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (!first) dictResult.Append(' ');
                first = false;
                dictResult.Append(entry.Key).Append('=')
                    .Append(FormatPhotonCustomPropertyValue(entry.Value));
            }
            return dictResult.Append('}').ToString();
        }
        if (value is System.Collections.IEnumerable values)
        {
            StringBuilder result = new StringBuilder("[");
            bool first = true;
            foreach (object item in values)
            {
                if (!first)
                    result.Append(", ");
                result.Append(FormatPhotonCustomPropertyValue(item));
                first = false;
            }
            return result.Append(']').ToString();
        }

        return value.ToString();
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
            CachePlatformFromPlayFab(userId, result.AccountInfo);

            onTranslated?.Invoke(creationDate);
        }, delegate
        {
            creationDates[userId] = "Error";

            pendingSupportPlatformLookups.Remove(userId);
            onTranslated?.Invoke("Error");
        });
    }
    
}
