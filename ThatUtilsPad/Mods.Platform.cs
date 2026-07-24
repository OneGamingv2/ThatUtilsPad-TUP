using System;
using System.Collections.Generic;
using System.Text;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static readonly DateTime MetaRebrandCutoff = new DateTime(2022, 12, 15, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string> supportPlatformByUserId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> pendingSupportPlatformLookups =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> pendingPlayerProfileLookups =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] PcModHintTokens =
    {
        "bark", "grate", "blossom", "monkephone", "infowatch", "gorillashirts", "thatutilspad",
        "kamecolor", "juul", "bigusnametags", "gorillabody", "monkstermod", "monkeye", "ii", "void",
        "seralyth", "seralith", "haunted", "destiny", "crystal", "shiba", "mango", "nebula",
        "pulsar", "cosmos", "hydra", "spectre", "viper", "eclipse", "phantom", "walksim", "bananaos"
    };

    public static string GetPlatform(this VRRig rig)
    {
        Player player = null;
        try { player = rig != null ? rig.GetPhotonPlayer() : null; }
        catch { }

        return ResolvePlatform(rig, player);
    }

    public static string ResolvePlatform(VRRig rig, Player player)
    {
        if (player == null && rig != null)
        {
            try { player = rig.GetPhotonPlayer(); }
            catch { }
        }

        if (player != null && PhotonNetwork.LocalPlayer != null &&
            (player.IsLocal || player.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber))
        {
            string local = ResolveLocalSupportPlatform();
            if (IsSupportPlatform(local))
                return local;
        }

        string userId = player != null ? player.UserId : null;

        string fast = ResolveFastPlatform(rig, player);
        if (fast == "Steam")
        {
            if (!string.IsNullOrEmpty(userId))
                supportPlatformByUserId[userId] = "Steam";
            return "Steam";
        }

        if (!string.IsNullOrEmpty(userId) &&
            supportPlatformByUserId.TryGetValue(userId, out string cached) &&
            IsSupportPlatform(cached))
        {
            if (cached != "Steam")
                RequestSupportPlatformFromPlayFab(userId);
            return cached;
        }

        if (IsSupportPlatform(fast))
        {
            if (!string.IsNullOrEmpty(userId))
            {
                supportPlatformByUserId[userId] = fast;
                RequestSupportPlatformFromPlayFab(userId);
            }

            return fast;
        }

        if (!string.IsNullOrEmpty(userId))
        {
            RequestSupportPlatformFromPlayFab(userId);
            if (supportPlatformByUserId.TryGetValue(userId, out string resolved) && IsSupportPlatform(resolved))
                return resolved;
            return "Loading...";
        }

        return "Unknown";
    }

    private static string ResolveLocalSupportPlatform()
    {
        try
        {
            PlayFabAuthenticator auth = PlayFabAuthenticator.instance;
            if (auth == null || auth.platform == null)
                return "Unknown";

            string raw = auth.platform.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return "Unknown";

            return NormalizeSupportPlatformLabel(raw);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Support platform (local) failed: " + ex.Message);
            return "Unknown";
        }
    }

    private static string NormalizeSupportPlatformLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown";

        string upper = raw.Trim().ToUpperInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ');
        while (upper.Contains("  "))
            upper = upper.Replace("  ", " ");

        if (upper == "PC" || upper == "OCULUS PC" || upper == "OCULUSPC" || upper == "OCULUS" ||
            upper == "RIFT" || upper.Contains("OCULUS PC") || upper.Contains("PLATFORM OCULUS"))
            return "Oculus PC";
        if (upper == "STEAM" || upper.Contains("STEAM") || upper.Contains("PLATFORM STEAM"))
            return "Steam";
        if (upper == "QUEST" || upper == "META" || upper == "STANDALONE" || upper == "STANDALONEVR" ||
            upper == "ANDROID" || upper.Contains("STANDALONE") || upper.Contains("QUEST") ||
            upper.Contains("META") || upper.Contains("HORIZON") || upper.Contains("PLATFORM QUEST"))
            return "Quest";
        if (upper == "PSVR" || upper == "PS5" || upper == "PLAYSTATION" || upper == "PSN" ||
            upper.Contains("PSVR") || upper.Contains("PLAYSTATION") || upper.Contains("PLATFORM PSVR"))
            return "PSVR";
        if (upper == "PICO" || upper.Contains("PICO"))
            return "Pico";

        return "Unknown";
    }

    private static bool IsSupportPlatform(string platform)
    {
        return platform == "Steam" ||
               platform == "Quest" ||
               platform == "Oculus PC" ||
               platform == "PSVR" ||
               platform == "Pico";
    }

    private static string ResolveFastPlatform(VRRig rig, Player player)
    {
        if (player != null && LooksLikeSteam64(player.UserId))
            return "Steam";

        if (player != null && TryFindSteam64InProperties(player))
            return "Steam";

        string fromPhoton = ResolvePlatformFromPhotonProperties(player);
        if (fromPhoton == "Steam")
            return "Steam";

        string scored = ResolvePlatformSeralythStyle(rig, player);
        if (IsSupportPlatform(scored))
            return scored;

        if (IsSupportPlatform(fromPhoton))
            return fromPhoton;

        string fromCreation = ResolvePlatformFromCachedCreationDate(player != null ? player.UserId : null);
        if (fromCreation == "Oculus PC")
            return fromCreation;

        return "Unknown";
    }

    private static string ResolvePlatformSeralythStyle(VRRig rig, Player player)
    {
        int suspiciouslySteam = 0;
        int suspiciouslyPC = 0;
        int suspiciouslyQuest = 0;

        string cosmetics = "";
        try
        {
            if (rig != null)
                cosmetics = rig.Cosmetics() ?? "";
        }
        catch { }

        string cosmeticsCompact = cosmetics.Replace(" ", "").Replace(".", "").Replace("_", "").Replace("-", "");

        bool hasSteamFirstLogin =
            cosmetics.IndexOf("S. FIRST LOGIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
            cosmeticsCompact.IndexOf("SFIRSTLOGIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
            cosmeticsCompact.IndexOf("STEAMFIRSTLOGIN", StringComparison.OrdinalIgnoreCase) >= 0;

        bool hasOculusFirstLogin =
            !hasSteamFirstLogin &&
            (cosmetics.IndexOf("FIRST LOGIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
             cosmetics.IndexOf("GAME-PURCHASE", StringComparison.OrdinalIgnoreCase) >= 0 ||
             cosmeticsCompact.IndexOf("FIRSTLOGIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
             cosmeticsCompact.IndexOf("GAMEPURCHASE", StringComparison.OrdinalIgnoreCase) >= 0);

        if (hasSteamFirstLogin)
            suspiciouslySteam += 3;

        if (hasOculusFirstLogin)
            suspiciouslyPC += 2;

        int pcTier = 0;
        int questTier = 0;
        try
        {
            if (rig != null)
            {
                pcTier = RigBits.GetPCTier(rig);
                questTier = RigBits.GetQuestTier(rig);
            }
        }
        catch { }

        if (pcTier > 0)
            suspiciouslySteam += 2;
        else if (questTier > 0)
            suspiciouslyQuest += 2;

        if (player != null && LooksLikeSteam64(player.UserId))
            suspiciouslySteam += 3;
        if (player != null && TryFindSteam64InProperties(player))
            suspiciouslySteam += 3;

        if (player != null && PlayerHasPcModSignature(player))
            suspiciouslySteam += 2;

        if (player?.CustomProperties != null && player.CustomProperties.Count >= 2 &&
            suspiciouslySteam == 0 && suspiciouslyQuest == 0 && suspiciouslyPC == 0)
        {
            suspiciouslySteam += 1;
        }

        if (suspiciouslySteam > suspiciouslyPC && suspiciouslySteam > suspiciouslyQuest)
            return "Steam";
        if (suspiciouslyPC > suspiciouslySteam && suspiciouslyPC > suspiciouslyQuest)
            return "Oculus PC";
        if (suspiciouslyQuest > suspiciouslySteam && suspiciouslyQuest > suspiciouslyPC)
            return "Quest";

        if (suspiciouslySteam > 0)
            return "Steam";
        if (suspiciouslyPC > 0)
            return "Oculus PC";
        if (suspiciouslyQuest > 0)
            return "Quest";

        return "Unknown";
    }

    private static string ResolvePlatformFromPhotonProperties(Player player)
    {
        if (player?.CustomProperties == null || player.CustomProperties.Count == 0)
            return "Unknown";

        foreach (object key in player.CustomProperties.Keys)
        {
            string keyText = key != null ? key.ToString() : "";
            object value = player.CustomProperties[key];
            string valueText = value != null ? value.ToString() : "";

            string keyUpper = keyText.Trim().ToUpperInvariant();
            if (keyUpper == "PLATFORM" || keyUpper == "PLAT" || keyUpper.Contains("PLATFORM"))
            {
                string normalized = NormalizeSupportPlatformLabel(valueText);
                if (IsSupportPlatform(normalized))
                    return normalized;
            }

            string combined = SquishPropertyText(keyText + " " + valueText);
            if (combined.Contains("steam64") || combined.Contains("steamid") ||
                (combined.Contains("steam") && !combined.Contains("steamdeck")))
                return "Steam";
        }

        return "Unknown";
    }

    private static bool PlayerHasPcModSignature(Player player)
    {
        if (player?.CustomProperties == null || player.CustomProperties.Count == 0)
            return false;

        foreach (object key in player.CustomProperties.Keys)
        {
            string keyText = SquishPropertyText(key != null ? key.ToString() : "");
            object value = player.CustomProperties[key];
            string valueText = SquishPropertyText(value != null ? value.ToString() : "");

            for (int i = 0; i < PcModHintTokens.Length; i++)
            {
                string token = PcModHintTokens[i];
                if (keyText.Contains(token) || valueText.Contains(token))
                    return true;
            }
        }

        return false;
    }

    private static bool TryFindSteam64InProperties(Player player)
    {
        if (player?.CustomProperties == null)
            return false;

        foreach (object key in player.CustomProperties.Keys)
        {
            if (LooksLikeSteam64(key != null ? key.ToString() : null))
                return true;
            object value = player.CustomProperties[key];
            if (LooksLikeSteam64(value != null ? value.ToString() : null))
                return true;
        }

        return false;
    }

    private static string ResolvePlatformFromCachedCreationDate(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return "Unknown";

        if (!creationDates.TryGetValue(userId, out string dateText) ||
            string.IsNullOrWhiteSpace(dateText) ||
            dateText == "Loading..." ||
            dateText == "Error")
        {
            return "Unknown";
        }

        if (!DateTime.TryParse(dateText, out DateTime created))
            return "Unknown";

        DateTime utc = created.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
            : created.ToUniversalTime();

        return utc < MetaRebrandCutoff ? "Oculus PC" : "Unknown";
    }

    private static void RequestSupportPlatformFromPlayFab(string userId)
    {
        if (string.IsNullOrEmpty(userId) || pendingSupportPlatformLookups.Contains(userId))
            return;

        if (supportPlatformByUserId.TryGetValue(userId, out string existing) && existing == "Steam")
            return;

        if (creationDates.TryGetValue(userId, out string existingDate) &&
            !string.IsNullOrWhiteSpace(existingDate) &&
            existingDate != "Loading...")
        {
            FinishSupportPlatformFromCreationDate(userId);
            return;
        }

        pendingSupportPlatformLookups.Add(userId);
        GetCreationDate(userId, _ =>
        {
            pendingSupportPlatformLookups.Remove(userId);
            FinishSupportPlatformFromCreationDate(userId);
        });
    }

    private static void FinishSupportPlatformFromCreationDate(string userId)
    {
        string fromDate = ResolvePlatformFromCachedCreationDate(userId);
        if (fromDate == "Oculus PC" &&
            (!supportPlatformByUserId.TryGetValue(userId, out string existing) || !IsSupportPlatform(existing)))
        {
            supportPlatformByUserId[userId] = fromDate;
        }

        if (!supportPlatformByUserId.TryGetValue(userId, out string platform) || !IsSupportPlatform(platform) || platform == "Quest")
            RequestPlayerProfilePlatform(userId);
    }

    private static void RequestPlayerProfilePlatform(string userId)
    {
        if (string.IsNullOrEmpty(userId) || pendingPlayerProfileLookups.Contains(userId))
            return;

        pendingPlayerProfileLookups.Add(userId);
        try
        {
            PlayFabClientAPI.GetPlayerProfile(new GetPlayerProfileRequest
            {
                PlayFabId = userId,
                ProfileConstraints = new PlayerProfileViewConstraints
                {
                    ShowLinkedAccounts = true,
                    ShowOrigination = true,
                    ShowCreated = true
                }
            },
            result =>
            {
                pendingPlayerProfileLookups.Remove(userId);
                string platform = GetPlatformFromPlayerProfile(result != null ? result.PlayerProfile : null);
                if (IsSupportPlatform(platform))
                {
                    if (!supportPlatformByUserId.TryGetValue(userId, out string existing) ||
                        !IsSupportPlatform(existing) ||
                        platform == "Steam" ||
                        (existing == "Quest" && platform != "Quest"))
                    {
                        supportPlatformByUserId[userId] = platform;
                    }
                }

            },
            error =>
            {
                pendingPlayerProfileLookups.Remove(userId);
                ApplyPlatformFallbackWhenPlayFabMissing(userId);
            });
        }
        catch (Exception ex)
        {
            pendingPlayerProfileLookups.Remove(userId);
            ApplyPlatformFallbackWhenPlayFabMissing(userId);
            Debug.LogWarning("[TUP] GetPlayerProfile platform exception: " + ex.Message);
        }
    }

    private static string GetPlatformFromPlayFabAccountInfo(UserAccountInfo accountInfo)
    {
        if (accountInfo == null)
            return "Unknown";

        if (accountInfo.SteamInfo != null)
            return "Steam";

        if (accountInfo.PsnInfo != null)
            return "PSVR";

        if (accountInfo.AndroidDeviceInfo != null)
            return "Quest";

        string fromOpenId = ResolvePlatformFromOpenIdInfo(accountInfo.OpenIdInfo);
        if (IsSupportPlatform(fromOpenId))
            return fromOpenId;

        string fromOrigination = ResolvePlatformFromUserOrigination(
            accountInfo.TitleInfo != null ? accountInfo.TitleInfo.Origination : null,
            accountInfo.Created);
        if (IsSupportPlatform(fromOrigination))
            return fromOrigination;

        string fromCustomId = ResolvePlatformFromCustomId(
            accountInfo.CustomIdInfo != null ? accountInfo.CustomIdInfo.CustomId : null,
            accountInfo.Created);
        if (IsSupportPlatform(fromCustomId))
            return fromCustomId;

        if (LooksLikeSteam64(accountInfo.CustomIdInfo != null ? accountInfo.CustomIdInfo.CustomId : null))
            return "Steam";

        if (accountInfo.Created.ToUniversalTime() < MetaRebrandCutoff)
            return "Oculus PC";

        return "Unknown";
    }

    private static void ApplyPlatformFallbackWhenPlayFabMissing(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        if (LooksLikeSteam64(userId))
        {
            supportPlatformByUserId[userId] = "Steam";
            return;
        }

        string fromDate = ResolvePlatformFromCachedCreationDate(userId);
        if (fromDate == "Oculus PC")
        {
            supportPlatformByUserId[userId] = "Oculus PC";
            return;
        }

        if (!supportPlatformByUserId.TryGetValue(userId, out string existing) || !IsSupportPlatform(existing))
            supportPlatformByUserId[userId] = "Unknown";
    }

    private static string GetPlatformFromPlayerProfile(PlayerProfileModel profile)
    {
        if (profile == null)
            return "Unknown";

        if (profile.LinkedAccounts != null)
        {
            for (int i = 0; i < profile.LinkedAccounts.Count; i++)
            {
                LinkedPlatformAccountModel link = profile.LinkedAccounts[i];
                if (link?.Platform == LoginIdentityProvider.Steam)
                    return "Steam";

                if (LooksLikeSteam64(link?.PlatformUserId) || LooksLikeSteam64(link?.Username))
                    return "Steam";
            }

            for (int i = 0; i < profile.LinkedAccounts.Count; i++)
            {
                LinkedPlatformAccountModel link = profile.LinkedAccounts[i];
                if (link?.Platform == LoginIdentityProvider.PSN)
                    return "PSVR";
            }

            for (int i = 0; i < profile.LinkedAccounts.Count; i++)
            {
                LinkedPlatformAccountModel link = profile.LinkedAccounts[i];
                if (link == null || link.Platform == null)
                    continue;

                if (link.Platform == LoginIdentityProvider.AndroidDevice)
                    return "Quest";

                if (link.Platform == LoginIdentityProvider.OpenIdConnect)
                {
                    string openIdGuess = ResolvePlatformFromOpenIdText(link.PlatformUserId, link.Username, link.Email);
                    if (IsSupportPlatform(openIdGuess))
                        return openIdGuess;
                }

                if (link.Platform == LoginIdentityProvider.Custom)
                {
                    DateTime created = profile.Created.HasValue ? profile.Created.Value.ToUniversalTime() : DateTime.MinValue;
                    string customGuess = ResolvePlatformFromCustomId(link.PlatformUserId, created);
                    if (IsSupportPlatform(customGuess))
                        return customGuess;
                }
            }
        }

        if (profile.Origination.HasValue)
        {
            DateTime created = profile.Created.HasValue ? profile.Created.Value.ToUniversalTime() : DateTime.MinValue;
            string fromOrigination = ResolvePlatformFromLoginIdentity(profile.Origination.Value, created);
            if (IsSupportPlatform(fromOrigination))
                return fromOrigination;
        }

        if (profile.Created.HasValue && profile.Created.Value.ToUniversalTime() < MetaRebrandCutoff)
            return "Oculus PC";

        if (profile.Created.HasValue &&
            (profile.LinkedAccounts == null || profile.LinkedAccounts.Count == 0) &&
            profile.Created.Value.ToUniversalTime() < MetaRebrandCutoff)
            return "Oculus PC";

        return "Unknown";
    }

    private static string ResolvePlatformFromOpenIdInfo(List<UserOpenIdInfo> openIdInfos)
    {
        if (openIdInfos == null || openIdInfos.Count == 0)
            return "Unknown";

        for (int i = 0; i < openIdInfos.Count; i++)
        {
            UserOpenIdInfo info = openIdInfos[i];
            if (info == null)
                continue;

            string resolved = ResolvePlatformFromOpenIdText(info.ConnectionId, info.Issuer, info.Subject);
            if (IsSupportPlatform(resolved))
                return resolved;
        }

        return "Unknown";
    }

    private static string ResolvePlatformFromOpenIdText(params string[] parts)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
                continue;
            builder.Append(' ').Append(parts[i]);
        }

        string text = builder.ToString().ToLowerInvariant();
        if (text.Length == 0)
            return "Unknown";

        if (text.Contains("pico"))
            return "Pico";
        if (text.Contains("psn") || text.Contains("playstation") || text.Contains("psvr"))
            return "PSVR";
        if (text.Contains("steam"))
            return "Steam";
        if (text.Contains("meta") || text.Contains("quest") || text.Contains("horizon"))
            return "Quest";
        if (text.Contains("oculus") || text.Contains("rift"))
            return "Oculus PC";

        return "Unknown";
    }

    private static string ResolvePlatformFromCustomId(string customId, DateTime created)
    {
        if (string.IsNullOrWhiteSpace(customId))
            return "Unknown";

        string lower = customId.Trim().ToLowerInvariant();
        if (lower.Contains("steam") || LooksLikeSteam64(customId))
            return "Steam";
        if (lower.Contains("pico"))
            return "Pico";
        if (lower.Contains("psn") || lower.Contains("psvr"))
            return "PSVR";
        if (lower.Contains("quest") || lower.Contains("meta") || lower.Contains("android"))
            return "Quest";
        if (lower.Contains("oculus") || lower.Contains("rift"))
            return "Oculus PC";

        bool mostlyDigits = true;
        int digitCount = 0;
        for (int i = 0; i < customId.Length; i++)
        {
            char c = customId[i];
            if (char.IsDigit(c))
                digitCount++;
            else if (c != '-' && c != '_')
            {
                mostlyDigits = false;
                break;
            }
        }

        if (mostlyDigits && digitCount >= 10)
        {
            DateTime utc = created.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
                : created.ToUniversalTime();
            return utc < MetaRebrandCutoff ? "Oculus PC" : "Unknown";
        }

        return "Unknown";
    }

    private static string ResolvePlatformFromUserOrigination(UserOrigination? origination, DateTime created)
    {
        if (!origination.HasValue)
            return "Unknown";

        switch (origination.Value)
        {
            case UserOrigination.Steam:
                return "Steam";
            case UserOrigination.PSN:
                return "PSVR";
            case UserOrigination.Android:
                return "Quest";
            case UserOrigination.OpenIdConnect:
                return "Quest";
            case UserOrigination.CustomId:
            {
                DateTime utc = created.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
                    : created.ToUniversalTime();
                return utc < MetaRebrandCutoff ? "Oculus PC" : "Unknown";
            }
            default:
                return "Unknown";
        }
    }

    private static string ResolvePlatformFromLoginIdentity(LoginIdentityProvider provider, DateTime created)
    {
        switch (provider)
        {
            case LoginIdentityProvider.Steam:
                return "Steam";
            case LoginIdentityProvider.PSN:
                return "PSVR";
            case LoginIdentityProvider.AndroidDevice:
                return "Quest";
            case LoginIdentityProvider.OpenIdConnect:
                return "Quest";
            case LoginIdentityProvider.Custom:
            {
                DateTime utc = created.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
                    : created.ToUniversalTime();
                return utc < MetaRebrandCutoff ? "Oculus PC" : "Unknown";
            }
            default:
                return "Unknown";
        }
    }

    private static bool LooksLikeSteam64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        StringBuilder digits = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsDigit(value[i]))
                digits.Append(value[i]);
        }

        string text = digits.ToString();
        return text.Length == 17 && text.StartsWith("765611", StringComparison.Ordinal);
    }

    public static string FormatDetectedModName(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            return "Unknown";

        string normalized = SquishPropertyText(modName);
        if (normalized.Contains("seralyth") || normalized.Contains("seralith") ||
            normalized.Contains("org.seralyth") || normalized.Contains("menuseralyth") ||
            normalized.Contains("confirmusing") || normalized == "isusing")
            return "SERALYTH MOD MENU";
        if (normalized.Contains("goldentrophy") && normalized.Contains("console"))
            return "Seralyth Console";
        if (normalized.Contains("utilla") || normalized.Contains("org.legoandmars"))
            return "Utilla";
        if (normalized.Contains("iimenu") || normalized.Contains("iisstupid") || normalized.Contains("org.iidk") ||
            (normalized.Contains("ii") && normalized.Contains("menu")))
            return "II's Stupid Menu";
        if (normalized.Contains("void") && (normalized.Contains("menu") || normalized.Contains("client") || normalized.Contains("open")))
            return "Void";
        if (normalized.Contains("haunted"))
            return "Haunted Mod Menu";
        if (normalized.Contains("monkemodmenu") || normalized.Contains("monkemod"))
            return "Monke Mod Menu";
        if (normalized.Contains("destiny"))
            return "Destiny Menu";
        if (normalized.Contains("crystal"))
            return "Crystal Menu";
        if (normalized.Contains("shiba") || normalized == "dark")
            return "Shiba GT Dark";
        if (normalized.Contains("mango"))
            return "Mango";
        if (normalized.Contains("nebula"))
            return "Nebula";
        if (normalized.Contains("pulsar"))
            return "Pulsar";
        if (normalized.Contains("cosmos"))
            return "Cosmos";
        if (normalized.Contains("hydra"))
            return "Hydra";
        if (normalized.Contains("spectre"))
            return "Spectre";
        if (normalized.Contains("viper"))
            return "Viper";
        if (normalized.Contains("eclipse"))
            return "Eclipse";
        if (normalized.Contains("phantom"))
            return "Phantom";
        if (normalized.Contains("violetpaid"))
            return "Violet Paid";
        if (normalized.Contains("violetfree") || normalized.Contains("violetontop") || normalized == "violet")
            return "Violet";
        if (normalized.Contains("obsidian"))
            return "Obsidian";
        if (normalized.Contains("oblivion"))
            return "Oblivion";
        if (normalized.Contains("asteroid"))
            return "Asteroid Lite";
        if (normalized.Contains("elux"))
            return "Elux";
        if (normalized.Contains("atlas"))
            return "Atlas";
        if (normalized.Contains("fusioned"))
            return "Fusioned";
        if (normalized.Contains("mediapad"))
            return "Media Pad";
        if (normalized.Contains("gorillashop"))
            return "GorillaShop";
        if (normalized.Contains("emotewheel"))
            return "Fortnite Emote Wheel";
        if (normalized.Contains("untitled"))
            return "Untitled";
        if (normalized.Contains("orbit") || normalized.Contains("øƦɓƖƬ"))
            return "Orbit";
        if (normalized.Contains("blossom") || normalized.Contains("monkster") || normalized.Contains("monkeye"))
            return "BlossomChecker";
        if (normalized.Contains("kigui"))
            return "Kitty Info";
        if (normalized.Contains("cosmeticx") || normalized.Contains("cosmeitcx") || normalized.Contains("cokecosmetics"))
            return "CosmeticX";
        if (normalized.Contains("fpsnametag") || normalized.Contains("zlothy"))
            return "FPS Nametags / Untrusted";
        if (normalized.Contains("yulookininhereweirdo") || normalized.Contains("1yulookininhereweirdo"))
            return "Malachi Menu Reborn";
        if (normalized.Contains("dtasloi") || normalized == "dtaoi")
            return "Pepsi Dee Mod Checker";
        if (normalized.Contains("walksimulator") || normalized.Contains("walksim"))
            return "WalkSimulator";
        if (normalized.Contains("bananaos"))
            return "Banana OS";
        if (normalized.Contains("elixir"))
            return "Elixir";
        if (normalized.Contains("genesis"))
            return "Genesis";
        if (normalized.Contains("recroomrig") || normalized.Contains("ilikecheese"))
            return "Rec Room Rig";
        if (normalized.Contains("grateversion") || normalized == "grate")
            return "Grate";
        if (normalized.Contains("bark") || normalized.Contains("kame"))
            return "Bark";
        if (normalized.Contains("za5fih") || normalized.Contains("lbjubzwl9") || normalized.Contains("jsqls2jev") ||
            normalized.Contains("vjx0hkty") || normalized.Contains("lfae3j") || normalized.Contains("va7blsu") ||
            normalized.Contains("wuc97wz67") || normalized.Contains("gw0ybcndar") || normalized.Contains("juul"))
            return "Sakuraa";

        return modName;
    }

    private static void CachePlatformFromPlayFab(string userId, UserAccountInfo accountInfo)
    {
        if (string.IsNullOrEmpty(userId) || accountInfo == null)
            return;

        string platform = GetPlatformFromPlayFabAccountInfo(accountInfo);
        if (IsSupportPlatform(platform))
        {
            if (!supportPlatformByUserId.TryGetValue(userId, out string existing) ||
                !IsSupportPlatform(existing) ||
                platform == "Steam" ||
                (existing == "Quest" && platform != "Quest"))
            {
                supportPlatformByUserId[userId] = platform;
            }

            pendingSupportPlatformLookups.Remove(userId);
            if (platform != "Steam")
                RequestPlayerProfilePlatform(userId);
            return;
        }

        RequestPlayerProfilePlatform(userId);
    }
}
