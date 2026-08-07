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
    private const float AutoScanTraversalInterval = 1.25f;
    private const float PropertyScanCacheSeconds = 2f;
    private static float nextAutoScanTraversalTime;
    private static float nextAutoScanFullRefreshTime;
    private const float AutoScanFullRefreshInterval = 20f;

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
        public int UnknownCount;
    }

    public struct PlayerPropScanResult
    {
        public List<string> LegalMods;
        public List<string> IllegalMods;
        public List<string> UntrustedMods;
        public List<string> UnknownProps;

        public int TotalCount =>
            (LegalMods != null ? LegalMods.Count : 0) +
            (IllegalMods != null ? IllegalMods.Count : 0) +
            (UntrustedMods != null ? UntrustedMods.Count : 0) +
            (UnknownProps != null ? UnknownProps.Count : 0);
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
                if (!autoScannedActorNumbers.Add(player.ActorNumber))
                    continue;

                FindPropertySignatureHits(player);
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
        PlayerPropScanResult scan = ScanPlayerCustomProperties(player);
        int modCount = scan.LegalMods != null ? scan.LegalMods.Count : 0;
        int cheatCount = (scan.IllegalMods != null ? scan.IllegalMods.Count : 0) +
                         (scan.UntrustedMods != null ? scan.UntrustedMods.Count : 0);
        int unknownCount = scan.UnknownProps != null ? scan.UnknownProps.Count : 0;

        if (modCount == 0 && cheatCount == 0 && unknownCount == 0)
            return false;

        ScanLobbyMode mode = (ScanLobbyMode)Mathf.Clamp(scanModeIndex, 0, scanModeNames.Length - 1);
        string message = BuildScanNotificationMessage(player, modCount, cheatCount, unknownCount, mode);
        if (string.IsNullOrEmpty(message))
            return false;

        ShowNotification(message);
        return true;
    }

    private static string BuildScanNotificationMessage(
        Player player,
        int modCount,
        int cheatCount,
        int unknownCount,
        ScanLobbyMode mode)
    {
        string playerName = string.IsNullOrWhiteSpace(player.NickName) ? "Player " + player.ActorNumber : player.NickName;

        switch (mode)
        {
            case ScanLobbyMode.CheatsOnly:
                return cheatCount > 0 ? $"{playerName} has {cheatCount} Cheats installed" : "";
            case ScanLobbyMode.ModsOnly:
                if (modCount > 0 && unknownCount > 0)
                    return $"{playerName} has {modCount} Mods and {unknownCount} unknown props";
                if (modCount > 0)
                    return $"{playerName} has {modCount} Mods installed";
                if (unknownCount > 0)
                    return $"{playerName} has {unknownCount} unknown props";
                return "";
            default:
                List<string> parts = new List<string>(3);
                if (modCount > 0)
                    parts.Add(modCount + " Mods");
                if (cheatCount > 0)
                    parts.Add(cheatCount + " Cheats");
                if (unknownCount > 0)
                    parts.Add(unknownCount + " unknown props");
                if (parts.Count == 0)
                    return "";
                return playerName + " has " + string.Join(" and ", parts);
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
                if (IsPlatformFalsePositiveSignature(signature.Name))
                    continue;
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
        GetPropertySignatureCounts(rig, player, out legalCount, out illegalCount, out _);
    }

    public static void GetPropertySignatureCounts(
        VRRig rig,
        Player player,
        out int legalCount,
        out int illegalCount,
        out int unknownCount)
    {
        legalCount = 0;
        illegalCount = 0;
        unknownCount = 0;
        if (player == null)
            return;

        LoadPropertyListIfNeeded();
        string cacheKey = GetPropertyScanCacheKey(player);
        if (propertyCountCache.TryGetValue(cacheKey, out PropertyCountCacheEntry cached) &&
            Time.time < cached.ExpiresAt)
        {
            legalCount = cached.LegalCount;
            illegalCount = cached.IllegalCount;
            unknownCount = cached.UnknownCount;
            return;
        }

        PlayerPropScanResult scan = ScanPlayerCustomProperties(player);
        legalCount = scan.LegalMods.Count;
        illegalCount = scan.IllegalMods.Count + (scan.UntrustedMods != null ? scan.UntrustedMods.Count : 0);
        unknownCount = scan.UnknownProps.Count;

        propertyCountCache[cacheKey] = new PropertyCountCacheEntry
        {
            ExpiresAt = Time.time + PropertyScanCacheSeconds,
            LegalCount = legalCount,
            IllegalCount = illegalCount,
            UnknownCount = unknownCount
        };
    }

    public static PlayerPropScanResult ScanPlayerCustomProperties(Player player)
    {
        PlayerPropScanResult result = new PlayerPropScanResult
        {
            LegalMods = new List<string>(),
            IllegalMods = new List<string>(),
            UntrustedMods = new List<string>(),
            UnknownProps = new List<string>()
        };

        if (player == null)
            return result;

        try
        {
            LoadPropertyListIfNeeded();
            Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);
            HashSet<string> explainedPropKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectKittyAndBlossomHits(player, result, explainedPropKeys);

            foreach (KeyValuePair<string, List<string>> hit in hits)
            {
                if (!propertySignatures.TryGetValue(hit.Key, out PropertySignature signature))
                    continue;

                string display = FormatDetectedModName(signature.Name);
                if (string.IsNullOrWhiteSpace(display) || display == "Unknown")
                    display = signature.Name;

                AddClassifiedModDisplay(result, display, signature.IsLegal);

                if (hit.Value == null)
                    continue;

                for (int i = 0; i < hit.Value.Count; i++)
                    MarkExplainedPropertyKeys(explainedPropKeys, hit.Value[i]);
            }

            CollectBlossomStylePropertyHits(player, result, explainedPropKeys);

            result.LegalMods.Sort(StringComparer.OrdinalIgnoreCase);
            result.IllegalMods.Sort(StringComparer.OrdinalIgnoreCase);
            result.UntrustedMods.Sort(StringComparer.OrdinalIgnoreCase);
            CollectUnknownCustomProperties(player, explainedPropKeys, result.UnknownProps);
            result.UnknownProps.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP PROP SCAN] ScanPlayerCustomProperties failed: " + ex.Message);
        }

        return result;
    }

    private static void AddClassifiedModDisplay(PlayerPropScanResult result, string display, bool signatureLegal)
    {
        if (string.IsNullOrWhiteSpace(display))
            return;

        // Steam's "Platform" property can look like "Platform Steam" -> compact "platformsteam"
        // which falsely matched "platforms". Never show platform-store signals as mods.
        if (ShouldHideModListEntry(display) || IsPlatformFalsePositiveSignature(display))
            return;

        if (IsUntrustedModDisplay(display))
        {
            if (!result.UntrustedMods.Contains(display) &&
                !result.IllegalMods.Contains(display) &&
                !result.LegalMods.Contains(display))
                result.UntrustedMods.Add(display);
            return;
        }

        if (IsHardIllegalModDisplay(display))
        {
            if (!result.IllegalMods.Contains(display) && !result.LegalMods.Contains(display))
                result.IllegalMods.Add(display);
            return;
        }

        if (signatureLegal)
        {
            if (!result.LegalMods.Contains(display) &&
                !result.IllegalMods.Contains(display) &&
                !result.UntrustedMods.Contains(display))
                result.LegalMods.Add(display);
            return;
        }

        if (!result.IllegalMods.Contains(display) && !result.LegalMods.Contains(display))
            result.IllegalMods.Add(display);
    }

    private static bool IsUntrustedModDisplay(string display)
    {
        string n = SquishPropertyText(display);
        return n.Contains("fpsnametag") || n.Contains("zlothy") || n.Contains("kigui") ||
               n.Contains("kittyinfo") || n.Contains("untrusted") || n.Contains("notag") ||
               n.Contains("toomuchinfo") || n.Contains("monkeclick") || n.Contains("roomutils") ||
               n.Contains("propspoof") || n.Contains("propspam") || n.Contains("custommodspoof");
    }

    private static bool IsHardIllegalModDisplay(string display)
    {
        string n = SquishPropertyText(display);
        return n.Contains("bananaos") || n.Contains("polkadotted") || n.Contains("recroomrig") ||
               n.Contains("hansolo") || n.Contains("walksimulator") || n.Contains("genesis") ||
               n.Contains("dtasloi") || n.Contains("elixir") || n.Contains("malachi") ||
               n.Contains("yulookininhereweirdo") || n.Contains("seralyth") || n.Contains("voidmenu") ||
               n.Contains("cosmetx") || n.Contains("adminconsole") || n.Contains("devconsole") ||
               n.Contains("iimenu") || n.Contains("sentinel");
    }

    private static void CollectBlossomStylePropertyHits(
        Player player,
        PlayerPropScanResult result,
        HashSet<string> explainedPropKeys)
    {
        if (player?.CustomProperties == null)
            return;

        foreach (DictionaryEntry entry in player.CustomProperties)
        {
            string key = entry.Key != null ? entry.Key.ToString() : "";
            string valueText = FormatPhotonCustomPropertyValue(entry.Value);
            TryClassifyRawPropertyText(key, result, explainedPropKeys);
            TryClassifyRawPropertyText(valueText, result, explainedPropKeys);
            if (!string.IsNullOrEmpty(key))
                MarkExplainedPropertyKeys(explainedPropKeys, "PropKey:" + key);
        }
    }

    private static void TryClassifyRawPropertyText(
        string text,
        PlayerPropScanResult result,
        HashSet<string> explainedPropKeys)
    {
        if (string.IsNullOrWhiteSpace(text) || ShouldHideModListEntry(text) || TextLooksLikeNormalNetworkValue(text))
            return;

        string display = FormatDetectedModName(text);
        if (string.IsNullOrWhiteSpace(display) || display == "Unknown")
            display = text.Trim();

        bool known =
            IsUntrustedModDisplay(display) ||
            IsHardIllegalModDisplay(display) ||
            IsKnownLegalAdvertiseDisplay(display);

        if (!known)
            return;

        bool legal = IsKnownLegalAdvertiseDisplay(display);
        AddClassifiedModDisplay(result, display, legal);
        if (!string.IsNullOrEmpty(text))
            explainedPropKeys.Add(text);
    }

    private static bool IsKnownLegalAdvertiseDisplay(string display)
    {
        string n = SquishPropertyText(display);
        return n.Contains("thatutilspad") || n.Contains("thatutilspadfree") ||
               n.Contains("blossomchecker") || n == "bark" || n.Contains("barkenabled") ||
               n.Contains("barkversion") || n.Contains("barkplayersize") ||
               n == "grate" || n.Contains("grateversion") || n.Contains("gorillashirts") ||
               n.Contains("infowatch") || n.Contains("monkephone") || n.Contains("utilla") ||
               n.Contains("sakuraa") || n.Contains("openbodytracking") || n.Contains("gorillabody") ||
               n.Contains("monkster") || n.Contains("monkeye") || n.Contains("bigusnametag") ||
               n.Contains("holdablepad") || n.Contains("gorillainfo") || n.Contains("gorillawatch") ||
               n.Contains("bananaphone") || n.Contains("whoisthatmonke") || n.Contains("bigusnametag") ||
               n.Contains("simpleboards") || n.Contains("gfaces") || n.Contains("gpronouns") ||
               n.Contains("gorillashirts") || n.Contains("shirtversion") || n.Contains("shirtproperties");
    }

    private static bool ShouldHideModListEntry(string text)
    {
        string n = SquishPropertyText(text);
        return n == "didtutorial" || n == "didnotutorial" || n == "didnottutorial" ||
               n == "platform" || n == "platforms" || n == "plat" ||
               n == "steam" || n == "steamvr" || n == "quest" || n == "meta" ||
               n == "oculus" || n == "oculuspc" || n == "pc" || n == "pcvr" ||
               n.Contains("platform");
    }

    private static bool TextLooksLikeNormalNetworkValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string trimmed = text.Trim();
        string normalized = SquishPropertyText(text);
        if (bool.TryParse(trimmed, out _) ||
            int.TryParse(trimmed, out _) ||
            float.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _) ||
            normalized.Length <= 2)
            return true;

        if (normalized.Contains("photon") || normalized.Contains("actor") || normalized.Contains("room") ||
            normalized.Contains("queue") || normalized.Contains("gamemode") || normalized.Contains("version") ||
            normalized.Contains("cosmetic") || normalized.Contains("firstlogin") || normalized.Contains("account") ||
            normalized.Contains("login") || normalized.Contains("playerid") || normalized.Contains("session") ||
            normalized.Contains("device") || normalized.Contains("region") || normalized.Contains("hash") ||
            normalized.Contains("auth") || normalized.Contains("voice") || normalized.Contains("didtutorial"))
            return true;

        switch (normalized)
        {
            case "platform":
            case "platforms":
            case "playerplatform":
            case "currentplatform":
            case "plat":
            case "store":
            case "storename":
            case "fps":
            case "hz":
            case "color":
            case "playercolor":
            case "colour":
            case "playercolour":
            case "creationdate":
            case "created":
            case "userid":
            case "user":
            case "id":
            case "nickname":
            case "name":
            case "username":
            case "quest":
            case "meta":
            case "oculus":
            case "oculuspc":
            case "steam":
            case "steamvr":
            case "pc":
            case "pcvr":
            case "desktop":
            case "computer":
            case "standalone":
            case "android":
            case "rift":
            case "psvr":
            case "pico":
            case "unknown":
                return true;
            default:
                return normalized.Contains("platform") || normalized.Contains("steam64") || normalized.Contains("steamid");
        }
    }

    private static void MarkExplainedPropertyKeys(HashSet<string> explained, string sourceKey)
    {
        if (explained == null || string.IsNullOrEmpty(sourceKey))
            return;

        const string PropPrefix = "Prop:";
        const string PropKeyPrefix = "PropKey:";
        const string PropValPrefix = "PropVal:";

        string path = null;
        if (sourceKey.StartsWith(PropPrefix, StringComparison.OrdinalIgnoreCase))
            path = sourceKey.Substring(PropPrefix.Length);
        else if (sourceKey.StartsWith(PropKeyPrefix, StringComparison.OrdinalIgnoreCase))
            path = sourceKey.Substring(PropKeyPrefix.Length);
        else if (sourceKey.StartsWith(PropValPrefix, StringComparison.OrdinalIgnoreCase))
            path = sourceKey.Substring(PropValPrefix.Length);

        if (string.IsNullOrEmpty(path))
            return;

        explained.Add(path);
        int dot = path.IndexOf('.');
        if (dot > 0)
            explained.Add(path.Substring(0, dot));
        int bracket = path.IndexOf('[');
        if (bracket > 0)
            explained.Add(path.Substring(0, bracket));
    }

    private static void CollectUnknownCustomProperties(
        Player player,
        HashSet<string> explainedPropKeys,
        List<string> unknownOut)
    {
        if (player?.CustomProperties == null || unknownOut == null)
            return;

        explainedPropKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int maxUnknown = 24;

        foreach (DictionaryEntry entry in player.CustomProperties)
        {
            if (unknownOut.Count >= maxUnknown)
                break;

            string key = entry.Key != null ? entry.Key.ToString() : "";
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (IsIgnoredVanillaPropertyKey(key) || TextLooksLikeNormalNetworkValue(key))
                continue;
            if (explainedPropKeys.Contains(key))
                continue;

            if (!LooksLikeSuspiciousUnknownProp(key, entry.Value))
                continue;

            string label = FormatUnknownPropertyLabel(key, entry.Value);
            if (!string.IsNullOrEmpty(label) && !unknownOut.Contains(label))
                unknownOut.Add(label);
        }
    }

    private static bool LooksLikeSuspiciousUnknownProp(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (IsIgnoredVanillaPropertyKey(key) || TextLooksLikeNormalNetworkValue(key) || ShouldHideModListEntry(key))
            return false;

        string valueText = FormatPhotonCustomPropertyValue(value);
        if (TextLooksLikeNormalNetworkValue(valueText) && !LooksLikeModAdvertiseToken(key))
            return false;

        if ((value is ExitGames.Client.Photon.Hashtable || value is IDictionary) &&
            !LooksLikeModAdvertiseToken(key))
            return false;

        return LooksLikeModAdvertiseToken(key) || LooksLikeModAdvertiseToken(valueText);
    }

    private static bool LooksLikeModAdvertiseToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (trimmed.Length < 4 || trimmed.Length > 64)
            return false;
        if (TextLooksLikeNormalNetworkValue(trimmed) || ShouldHideModListEntry(trimmed))
            return false;

        string n = SquishPropertyText(trimmed);
        if (n.Length < 4)
            return false;

        if (LooksLikeGuidOrHash(trimmed))
            return false;

        int letters = 0;
        for (int i = 0; i < n.Length; i++)
            if (char.IsLetter(n[i])) letters++;
        if (letters < 3)
            return false;

        if (n.Contains("menu") || n.Contains("modmenu") || n.Contains("modchecker") ||
            n.Contains("client") || (n.Contains("pad") && n.Length >= 6) ||
            n.Contains("checker") || (n.Contains("version") && n.Length >= 10) ||
            (n.Contains("enabled") && n.Length >= 10) ||
            (n.Contains("using") && n.Length >= 8) ||
            (n.IndexOf('.') >= 0 && n.Length >= 8))
            return true;

        bool hasDigit = false;
        for (int i = 0; i < n.Length; i++)
            if (char.IsDigit(n[i])) { hasDigit = true; break; }
        return hasDigit && n.Length >= 8;
    }

    private static bool LooksLikeGuidOrHash(string text)
    {
        string t = text.Trim();
        if (t.Length == 32 || t.Length == 40 || t.Length == 64)
        {
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        if (Guid.TryParse(t, out _))
            return true;

        return false;
    }

    private static string FormatUnknownPropertyLabel(string key, object value)
    {
        string valueText = FormatPhotonCustomPropertyValue(value);
        if (string.IsNullOrEmpty(valueText) ||
            valueText.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            TextLooksLikeNormalNetworkValue(valueText) ||
            value is ExitGames.Client.Photon.Hashtable ||
            value is IDictionary)
            return key;

        if (valueText.Length > 36)
            valueText = valueText.Substring(0, 33) + "...";

        return key + " = " + valueText;
    }

    private static bool IsIgnoredVanillaPropertyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        if (ShouldHideModListEntry(key) || TextLooksLikeNormalNetworkValue(key))
            return true;

        string n = SquishPropertyText(key);
        switch (n)
        {
            case "actornumber":
            case "userid":
            case "nickname":
            case "ismasterclient":
            case "isinactive":
            case "didtutorial":
            case "didnotutorial":
            case "didnottutorial":
            case "size":
            case "scale":
            case "matcolor":
            case "setmatcolor":
            case "nativepreferred":
            case "returningplayer":
            case "playercolor":
            case "playercolour":
            case "color":
            case "colour":
            case "platform":
            case "platforms":
            case "playerplatform":
            case "currentplatform":
            case "plat":
            case "store":
            case "storename":
            case "fps":
            case "hz":
            case "ping":
            case "voice":
            case "muted":
            case "mute":
            case "zone":
            case "currentzone":
            case "queue":
            case "gamemode":
            case "mode":
            case "room":
            case "roomtype":
            case "cosmetics":
            case "cosmetic":
            case "tryon":
            case "tryonpack":
            case "activecosmetics":
            case "concatstring":
            case "nametags":
            case "debug":
            case "lck":
            case "pun":
            case "photon":
            case "thatutilspadfree":
            case "thatutilspadfreegui":
            case "thatutilspad":
            case "steam":
            case "steamvr":
            case "steam64":
            case "steamid":
            case "quest":
            case "meta":
            case "oculus":
            case "oculuspc":
            case "pc":
            case "pcvr":
                return true;
            default:
                if (n.Contains("platform") || n.Contains("steam") || n.Contains("oculus"))
                    return true;
                if (n.StartsWith("lma") || n.StartsWith("lme") || n.StartsWith("lmh") ||
                    n.StartsWith("lmb") || n.StartsWith("lmf") || n.StartsWith("lmk"))
                    return true;
                return false;
        }
    }

    private static NormalizedScanSource[] BuildNormalizedPropertyScanSources(Player player, VRRig rig)
    {
        List<NormalizedScanSource> sources = new List<NormalizedScanSource>(32);
        sources.Add(new NormalizedScanSource(
            "PhotonPlayer",
            $"{player.UserId} {player.ActorNumber}"));

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

                if (IsTooBroadPropertyToken(section, entry))
                    continue;

                if (IsPlatformFalsePositiveSignature(entry))
                    continue;

                bool isLegal = IsLegalPropertySignature(section, entry);
                if (section.Equals("Untrusted mod hints", StringComparison.OrdinalIgnoreCase))
                    isLegal = false;
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

    private static bool IsTooBroadPropertyToken(string section, string entry)
    {
        if (!section.Equals("Suspicious property tokens", StringComparison.OrdinalIgnoreCase) &&
            !section.Equals("Heuristic categories", StringComparison.OrdinalIgnoreCase))
            return false;

        string n = SquishPropertyText(entry);
        switch (n)
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
            case "esp":
            case "xray":
            case "hash":
            case "version":
            case "color":
            case "name":
            case "ui":
            case "gs":
            case "msp":
            case "gc":
            case "atlas":
            case "orbit":
            case "mango":
            case "destiny":
            case "crystal":
            case "violet":
            case "genesis":
            case "untitled":
            case "serverdata":
            case "isusing":
            case "confirmusing":
            case "nocone":
            case "vivid":
            case "platform":
            case "platforms":
            case "platformgravity":
            case "nonstickyplatforms":
            case "steam":
            case "quest":
            case "oculus":
            case "meta":
                return true;
            default:
                return n.Length <= 3 || n.Contains("platform");
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
            case "untrusted mod hints":
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

    private static bool IsPlatformFalsePositiveSignature(string text)
    {
        string n = SquishPropertyText(text).Replace(" ", "");
        return n == "platform" ||
               n == "platforms" ||
               n == "plat" ||
               n == "platformgravity" ||
               n == "nonstickyplatforms" ||
               n == "templateplatforms" ||
               n == "playerplatform" ||
               n == "currentplatform" ||
               (n.Contains("platform") &&
                (n.Contains("steam") || n.Contains("quest") || n.Contains("meta") ||
                 n.Contains("oculus") || n.Contains("store")));
    }

    private static bool NormalizedTextContainsSignature(NormalizedScanSource source, PropertySignature signature)
    {
        if (string.IsNullOrEmpty(source.Normalized) || string.IsNullOrEmpty(signature.Normalized))
            return false;

        if (IsPlatformFalsePositiveSignature(signature.Name) ||
            IsPlatformFalsePositiveSignature(signature.Normalized) ||
            IsPlatformFalsePositiveSignature(signature.Compact))
            return false;

        // "Platform Steam" / "platformsteam" must never count as "platforms".
        if (signature.Compact == "platforms" || signature.Normalized == "platforms")
            return ContainsWholeNormalizedToken(source.Normalized, "platforms") &&
                   !source.Compact.Contains("platformsteam") &&
                   !source.Compact.Contains("platformquest") &&
                   !source.Compact.Contains("platformmeta") &&
                   !source.Compact.Contains("platformoculus");

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

            onTranslated?.Invoke(creationDate);
        }, delegate
        {
            creationDates[userId] = "Error";
            onTranslated?.Invoke("Error");
        });
    }
    
}
