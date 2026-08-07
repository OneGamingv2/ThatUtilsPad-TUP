using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Realtime;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static readonly string[] BlossomLegalMods =
    {
        "BlossomChecker", "Blossom Mod Checker", "Monkster Mod Checker", "Monksters Mod Checker",
        "MonkEye Mod Checker", "Open Body Tracking By Ivy ~ (2.1.0)", "GorillaBody 4.0.0",
        "MonkePhone", "Bark", "Grate", "InfoWatch", "bigusnametags++", "GorillaShirts",
        "GorillaShirtsFeb26", "Za5fih", "LBjubZwL9", "JSqLs2JEvXZJ6ei", "VJx0HKTY", "lfae3j",
        "va7bLsuhWVsqNkH", "WuC97WZ67", "Gw0ybcNDar", "KameColor", "BarkEnabledModules",
        "KameState", "BarkVersion", "BarkPlayerSize", "Juul", "ThatUtilsPad", "ThatUtilsPad Free"
    };

    private static readonly string[] BlossomIllegalMods =
    {
        "BananaOS", "PolkaDottedPlayer", "RecRoomRig", "HanSolo1000Falcon",
        "1y u lookin in here weirdo", "WalkSimulator", "genesis", "DTASLOI", "Elixir"
    };

    private static readonly string[] BlossomUntrustedMods =
    {
        "fps-nametags for zlothy", "FPS-Nametages for Zlothy", "ki.gui", "Zlothy"
    };

    private static readonly Dictionary<string, string> KittySpecialMods =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "GFaces", "gFACES" },
            { "drowsiiiGorillaInfoBoard", "gInfo Board" },
            { "MonkeCosmetics::Material", "Monke Cosmetics" },
            { "DeeTags", "DEE TAGS" },
            { "GorillaNametags", "GORILLA NAMETAGS" },
            { "Boy Do I Love Information", "BDIL-INFO" },
            { "NametagsPlusPlus", "NAMETAGS++" },
            { "kingbingus.oculusreportmenu", "OCULUS REPORT MENU" },
            { "github.com/maroon-shadow/SimpleBoards", "SIMPLEBOARDS" },
            { "cody likes burritos", "ShutUpMonkeys" },
            { "ObsidianMC", "OBSIDIAN" },
            { "GTrials", "gTRIALS" },
            { "usinggphys", "gPHYS" },
            { "github.com/ZlothY29IQ/GorillaMediaDisplay", "gMEDIA DISPLAY" },
            { "github.com/ZlothY29IQ/TooMuchInfo", "TOOMUCHINFO" },
            { "github.com/ZlothY29IQ/RoomUtils-IW", "ROOMUTILS-IW" },
            { "github.com/ZlothY29IQ/MonkeClick", "MONKECLICK" },
            { "github.com/ZlothY29IQ/MonkeClick-CI", "MONKECLICK-CI" },
            { "github.com/ZlothY29IQ/MonkeRealism", "MONKEREALISM" },
            { "WalkSimulator", "WalkSimulator" },
            { "FPS-Nametags for Zlothy", "FPS Nametags / Untrusted" },
            { "Dingus", "DINGUS" },
            { "MediaPad", "Media Pad" },
            { "GorillaCinema", "gCINEMA" },
            { "ChainedTogetherActive", "CHAINEDTOGETHER" },
            { "GPronouns", "gPRONOUNS" },
            { "CSVersion", "CustomSkin" },
            { "github.com/ZlothY29IQ/Zloth-RecRoomRig", "ZLOTHYBodyEst" },
            { "ShirtProperties", "SHIRTS-OLD" },
            { "GorillaShirts", "GorillaShirts" },
            { "genesis", "Genesis" },
            { "EmoteWheel", "Emote Wheel" },
            { "MistUser", "MIST" },
            { "ElixirMenu", "Elixir" },
            { "Elixir", "Elixir" },
            { "seralyth", "SERALYTH MOD MENU" },
            { "seralyth menu", "SERALYTH MOD MENU" },
            { "seralyth software", "SERALYTH MOD MENU" },
            { "privateuiroom", "SERALYTH MOD MENU" },
            { "cameratablet", "SERALYTH MOD MENU" },
            { "menuusertracers", "SERALYTH MOD MENU" },
            { "promptvideoplayer", "SERALYTH MOD MENU" },
            { "dynamicanimations", "SERALYTH MOD MENU" },
            { "com.goldentrophy.gorillatag.console", "Seralyth Console" },
            { "goldentrophy", "Seralyth Console" },
            { "safelua", "Seralyth Console" },
            { "org.legoandmars.gorillatag.utilla", "Utilla" },
            { "moddedgamemode", "Utilla" },
            { "utilla.events", "Utilla" },
            { "legoandmars", "Utilla" },
            { "iiadmin", "II Admin Console" },
            { "gorillatag.console", "Admin Console" },
            { "devconsole", "Dev Console" },
            { "debugconsole", "Dev Console" },
            { "exec-safe", "Admin Console" },
            { "VioletFreeUser", "Violet Free" },
            { "Hidden Menu", "Hidden Menu" },
            { "HP_Left", "Holdable Pad" },
            { "GrateVersion", "Grate" },
            { "void_menu_open", "Void" },
            { "BananaOS", "Banana OS" },
            { "CarName", "Vehicles" },
            { "6XpyykmrCthKhFeUfkYGxv7xnXpoe2", "CCMV2" },
            { "cronos", "Cronos" },
            { "ORBIT", "Orbit" },
            { "Violet On Top", "Violet" },
            { "VioletPaidUser", "Violet Paid" },
            { "MonkePhone", "MonkePhone" },
            { "Body Tracking", "Body Tracking" },
            { "Body Estimation", "Body Estimation" },
            { "GorillaTorsoEstimator", "Torso Est" },
            { "com.duv14.gorillatag.gorillainfo", "GorillaInfo" },
            { "gorillainfo", "GorillaInfo" },
            { "GorillaWatch", "GorillaWatch" },
            { "InfoWatch", "InfoWatch" },
            { "BananaPhone", "BananaPhone" },
            { "CustomMaterial", "Custom Cosmetics" },
            { "cheese is gouda", "WhoIsThatMonke" },
            { "WhoIsThatMonke Version", "WhoIsThatMonke" },
            { "I like cheese", "Rec Room Rig" },
            { "pmversion", "Player Models" },
            { "gorillastats", "GorillaStats" },
            { "using gorilladrift", "GorillaDrift" },
            { "monkehavocversion", "MonkeHavoc" },
            { "spectapeversion", "SpecTape" },
            { "cokecosmetics", "CosmeticX" },
            { "shirtversion", "GorillaShirts" },
            { "OculusReportMenu", "Report Menu" },
            { "ThareonsChecker", "ThareonsChecker" },
            { "FemBoyChecker", "Sentinel Menu" },
            { "BlossomChecker", "BlossomChecker" },
            { "ThatUtilsPad", "ThatUtilsPad Free" },
            { "ThatUtilsPad Free", "ThatUtilsPad Free" },
            { "ki.gui", "Kitty Info" },
            { "Bark", "Bark" },
            { "BarkEnabledModules", "Bark" },
            { "BarkVersion", "Bark" },
            { "BarkPlayerSize", "Bark" },
            { "KameColor", "Bark" },
            { "KameState", "Bark" },
            { "PolkaDottedPlayer", "PolkaDottedPlayer" },
            { "HanSolo1000Falcon", "HanSolo1000Falcon" },
            { "1y u lookin in here weirdo", "Malachi Menu Reborn" },
            { "DTASLOI", "Pepsi Dee Mod Checker" },
            { "RecRoomRig", "Rec Room Rig" }
        };

    private static readonly string[] SeralythMenuTokens =
    {
        "seralyth", "privateuiroom", "cameratablet", "menuusertracers",
        "promptvideoplayer", "dynamicanimations"
    };

    private static readonly string[] SeralythConsoleTokens =
    {
        "goldentrophy", "safelua", "com.goldentrophy.gorillatag.console"
    };

    private static readonly string[] UtillaTokens =
    {
        "utilla", "org.legoandmars", "moddedgamemode", "utilla.events", "legoandmars"
    };

    private static string NormalizeBlossomToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        char[] buffer = new char[value.Length];
        int n = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == ' ' || c == '_' || c == '-' || c == '\'' || c == '.')
                continue;
            buffer[n++] = char.ToLowerInvariant(c);
        }

        return n == 0 ? "" : new string(buffer, 0, n);
    }

    private static bool BlossomTokenContainsListed(string haystackNorm, string listedRaw)
    {
        string needle = NormalizeBlossomToken(listedRaw);
        if (needle.Length == 0 || haystackNorm.Length == 0)
            return false;

        if (needle.Length <= 8)
            return haystackNorm == needle;

        return haystackNorm == needle || haystackNorm.Contains(needle);
    }

    private static bool IsBlossomLegalToken(string text)
    {
        string n = NormalizeBlossomToken(text);
        if (n.Length == 0)
            return false;
        for (int i = 0; i < BlossomLegalMods.Length; i++)
        {
            if (BlossomTokenContainsListed(n, BlossomLegalMods[i]))
                return true;
        }
        return false;
    }

    private static bool IsBlossomIllegalToken(string text)
    {
        string n = NormalizeBlossomToken(text);
        if (n.Length == 0)
            return false;
        for (int i = 0; i < BlossomIllegalMods.Length; i++)
        {
            if (BlossomTokenContainsListed(n, BlossomIllegalMods[i]))
                return true;
        }
        return false;
    }

    private static bool IsBlossomUntrustedToken(string text)
    {
        string n = NormalizeBlossomToken(text);
        if (n.Length == 0)
            return false;
        for (int i = 0; i < BlossomUntrustedMods.Length; i++)
        {
            if (BlossomTokenContainsListed(n, BlossomUntrustedMods[i]))
                return true;
        }
        return false;
    }

    private static bool PlayerHasKnownModSignature(Player player)
    {
        if (player?.CustomProperties == null || player.CustomProperties.Count == 0)
            return false;

        foreach (DictionaryEntry entry in player.CustomProperties)
        {
            string key = entry.Key != null ? entry.Key.ToString() : "";
            string value = entry.Value != null ? entry.Value.ToString() : "";

            if (KittySpecialMods.ContainsKey(key))
                return true;
            if (IsBlossomIllegalToken(key) || IsBlossomIllegalToken(value) ||
                IsBlossomUntrustedToken(key) || IsBlossomUntrustedToken(value) ||
                IsBlossomLegalToken(key) || IsBlossomLegalToken(value))
                return true;
        }

        return false;
    }

    private static void CollectKittyAndBlossomHits(
        Player player,
        PlayerPropScanResult result,
        HashSet<string> explainedPropKeys)
    {
        if (player?.CustomProperties == null)
            return;

        HashSet<string> propTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int trueFlagCount = 0;
        bool didTutorialFalse = false;

        foreach (DictionaryEntry entry in player.CustomProperties)
        {
            string key = entry.Key != null ? entry.Key.ToString() : "";
            string valueText = FormatPhotonCustomPropertyValue(entry.Value);
            if (!string.IsNullOrEmpty(key))
                propTokens.Add(NormalizeBlossomToken(key));
            if (!string.IsNullOrEmpty(valueText))
                propTokens.Add(NormalizeBlossomToken(valueText));

            if (string.Equals(key, "didTutorial", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(valueText, "false", StringComparison.OrdinalIgnoreCase))
                didTutorialFalse = true;

            if (string.Equals(valueText, "true", StringComparison.OrdinalIgnoreCase))
                trueFlagCount++;

            if (!string.IsNullOrEmpty(key) && KittySpecialMods.TryGetValue(key, out string kittyDisplay))
            {
                AddClassifiedModDisplay(result, kittyDisplay, IsKnownLegalAdvertiseDisplay(kittyDisplay) || IsBlossomLegalToken(kittyDisplay));
                MarkExplainedPropertyKeys(explainedPropKeys, "PropKey:" + key);
            }

            if (!string.IsNullOrEmpty(valueText))
            {
                string valueTrim = valueText.Trim();
                if (valueTrim.Length >= 6 &&
                    KittySpecialMods.TryGetValue(valueTrim, out string kittyFromValue))
                {
                    AddClassifiedModDisplay(result, kittyFromValue, IsKnownLegalAdvertiseDisplay(kittyFromValue) || IsBlossomLegalToken(kittyFromValue));
                    MarkExplainedPropertyKeys(explainedPropKeys, "PropKey:" + key);
                }
            }

            ClassifyBlossomText(key, result, explainedPropKeys);
            if (!string.IsNullOrEmpty(valueText) && valueText.Trim().Length >= 5)
                ClassifyBlossomText(valueText, result, explainedPropKeys);
            if (!string.IsNullOrEmpty(key))
                MarkExplainedPropertyKeys(explainedPropKeys, "PropKey:" + key);
        }

        if (CountTokenHits(propTokens, SeralythMenuTokens) >= 2)
            AddClassifiedModDisplay(result, "SERALYTH MOD MENU", false);
        if (CountTokenHits(propTokens, SeralythConsoleTokens) >= 2)
            AddClassifiedModDisplay(result, "Seralyth Console", false);
        if (CountTokenHits(propTokens, UtillaTokens) >= 2)
            AddClassifiedModDisplay(result, "Utilla", true);

        int propCount = player.CustomProperties.Count;
        int advertiseHits = CountAdvertiseTokens(player);
        if (propCount >= 70 && advertiseHits >= 4)
            AddClassifiedModDisplay(result, "Prop Spoofer", false);
        else if (propCount >= 45 && advertiseHits >= 3)
            AddClassifiedModDisplay(result, "Prop Spam", false);

        if (didTutorialFalse && propCount >= 10 && advertiseHits >= 2)
            AddClassifiedModDisplay(result, "No Tag On Join", false);

        if (trueFlagCount >= 18 && propCount >= 24 && advertiseHits >= 3)
            AddClassifiedModDisplay(result, "Custom Mod Spoofer", false);
    }

    private static int CountAdvertiseTokens(Player player)
    {
        if (player?.CustomProperties == null)
            return 0;

        int count = 0;
        foreach (DictionaryEntry entry in player.CustomProperties)
        {
            string key = entry.Key != null ? entry.Key.ToString() : "";
            if (IsIgnoredVanillaPropertyKey(key))
                continue;
            if (LooksLikeModAdvertiseToken(key))
                count++;
        }

        return count;
    }

    private static int CountTokenHits(HashSet<string> propTokens, string[] needles)
    {
        if (propTokens == null || needles == null)
            return 0;

        int hits = 0;
        for (int i = 0; i < needles.Length; i++)
        {
            string needle = NormalizeBlossomToken(needles[i]);
            if (needle.Length == 0)
                continue;

            foreach (string token in propTokens)
            {
                if (token == needle || (needle.Length >= 8 && token.Contains(needle)))
                {
                    hits++;
                    break;
                }
            }
        }

        return hits;
    }

    private static void ClassifyBlossomText(
        string text,
        PlayerPropScanResult result,
        HashSet<string> explainedPropKeys)
    {
        if (string.IsNullOrWhiteSpace(text) || ShouldHideModListEntry(text) || TextLooksLikeNormalNetworkValue(text))
            return;

        string display = FormatDetectedModName(text);
        if (string.IsNullOrWhiteSpace(display) || display == "Unknown")
            display = text.Trim();

        if (IsBlossomIllegalToken(text) || IsHardIllegalModDisplay(display))
        {
            AddClassifiedModDisplay(result, FormatDetectedModName(display), false);
            explainedPropKeys.Add(text);
            return;
        }

        if (IsBlossomUntrustedToken(text) || IsUntrustedModDisplay(display))
        {
            AddClassifiedModDisplay(result, FormatDetectedModName(display), false);
            explainedPropKeys.Add(text);
            return;
        }

        if (IsBlossomLegalToken(text) || IsKnownLegalAdvertiseDisplay(display))
        {
            if (LooksLikeSakuraaAdvertiseKey(text))
                display = "Sakuraa";
            else
                display = FormatDetectedModName(display);

            AddClassifiedModDisplay(result, display, true);
            explainedPropKeys.Add(text);
        }
    }

    private static bool LooksLikeSakuraaAdvertiseKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 5)
            return false;
        if (!IsBlossomLegalToken(text))
            return false;

        bool hasLetter = false;
        bool hasDigit = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetter(text[i])) hasLetter = true;
            else if (char.IsDigit(text[i])) hasDigit = true;
        }

        return hasLetter && hasDigit;
    }
}
