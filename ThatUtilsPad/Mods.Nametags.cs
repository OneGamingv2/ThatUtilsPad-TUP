using System;
using System.Collections.Generic;
using GorillaLocomotion;
using GorillaNetworking;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

public static partial class Mods
{
    private sealed class NameTagLabelBinding
    {
        public TMP_Text? Tmp;
        public Text? UiText;

        public void Set(string value)
        {
            if (Tmp != null)
                Tmp.text = value;
            else if (UiText != null)
                UiText.text = value;
        }
    }

    private sealed class NameTagColorBinding
    {
        public readonly List<Graphic> Graphics = new List<Graphic>();
        public readonly List<SpriteRenderer> Sprites = new List<SpriteRenderer>();
        public readonly List<Renderer> Renderers = new List<Renderer>();
    }

    private sealed class NameTagView
    {
        public GameObject Root = null!;
        public Transform RootTransform = null!;
        public Transform Anchor = null!;
        public NameTagLabelBinding Name = null!;
        public NameTagLabelBinding Fps = null!;
        public NameTagLabelBinding Ping = null!;
        public NameTagLabelBinding Mods = null!;
        public NameTagColorBinding FpsColors = null!;
        public NameTagColorBinding PingColors = null!;
        public NameTagColorBinding ModsColors = null!;
        public GameObject? IconMeta;
        public GameObject? IconPc;
        public GameObject? IconUnknown;
        public GameObject? IconSteam;
        public string? LastName;
        public int LastFps = int.MinValue;
        public int LastPing = int.MinValue;
        public NameTagModStatus LastModStatus;
        public bool HasModStatus;
        public string? LastPlatform;
        public Color LastFpsColor;
        public Color LastPingColor;
        public Color LastModsColor;
        public bool HasFpsColor;
        public bool HasPingColor;
        public bool HasModsColor;
    }

    private static bool nameTagsEnabled;
    private static readonly Dictionary<VRRig, NameTagView> nametags = new Dictionary<VRRig, NameTagView>();
    private static readonly List<VRRig> nameTagRigScratch = new List<VRRig>();
    private static readonly HashSet<VRRig> activeNameTagRigs = new HashSet<VRRig>();
    private static readonly MaterialPropertyBlock nameTagPropertyBlock = new MaterialPropertyBlock();
    private static readonly int NameTagColorProperty = Shader.PropertyToID("_Color");
    public static AssetBundle? nameBundle;
    private static GameObject? nameTagPrefab;
    private static bool nameTagPrefabLookupAttempted;
    private static bool loggedMissingNameTagPrefab;
    private static readonly Dictionary<VRRig, NameTagModStatus> tagScanPocket = new Dictionary<VRRig, NameTagModStatus>();
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
        Camera? camera = Main.GetActiveCamera();
        Transform? cameraTransform = camera != null ? camera.transform : null;

        activeNameTagRigs.Clear();
        foreach (VRRig rig in VRRigCache.ActiveRigs)
        {
            if (rig != null && !rig.isLocal)
                activeNameTagRigs.Add(rig);
        }

        nameTagRigScratch.Clear();
        foreach (KeyValuePair<VRRig, NameTagView> pair in nametags)
        {
            if (pair.Key == null || !activeNameTagRigs.Contains(pair.Key))
                nameTagRigScratch.Add(pair.Key!);
        }

        for (int i = 0; i < nameTagRigScratch.Count; i++)
            RemoveNameTag(nameTagRigScratch[i]);
        nameTagRigScratch.Clear();

        foreach (VRRig rig in activeNameTagRigs)
        {
            if (!nametags.TryGetValue(rig, out NameTagView view))
            {
                GameObject? prefab = GetNameTagPrefab();
                if (prefab == null)
                    return;

                view = CreateNameTagView(rig, prefab);
                nametags.Add(rig, view);
            }

            if (view.Root == null)
            {
                RemoveNameTag(rig);
                continue;
            }

            Transform anchor = view.Anchor != null ? view.Anchor : rig.transform;
            Vector3 anchorPosition = anchor.position;
            view.RootTransform.position = anchorPosition + Vector3.up * 0.45f;

            if (cameraTransform != null)
            {
                view.RootTransform.LookAt(cameraTransform.position);
                view.RootTransform.Rotate(0f, 450f, 0f);
            }

            float visibleScale = targetNameTagScale;
            if (nameTagDistanceFadeEnabled && cameraTransform != null)
            {
                float fadeDistance = nameTagFadeDistanceValues[nameTagFadeDistanceIndex];
                float distanceSquared = (cameraTransform.position - anchorPosition).sqrMagnitude;
                visibleScale = distanceSquared <= fadeDistance * fadeDistance ? targetNameTagScale : 0f;
            }

            view.RootTransform.localScale = Vector3.Lerp(
                view.RootTransform.localScale,
                Vector3.one * visibleScale,
                Time.deltaTime * 10f);

            if (!view.Root.activeSelf)
                view.Root.SetActive(true);

            if (visibleScale <= 0.0001f)
                continue;

            if (nextNameTagInfoUpdateTimes.TryGetValue(rig, out float nextInfoUpdate) && Time.time < nextInfoUpdate)
                continue;

            nextNameTagInfoUpdateTimes[rig] =
                Time.time + NameTagInfoRefreshSeconds + (Mathf.Abs(rig.GetInstanceID() % 8) * 0.02f);
            RefreshNameTag(rig, view);
        }
    }

    private static NameTagView CreateNameTagView(VRRig rig, GameObject prefab)
    {
        GameObject root = Object.Instantiate(prefab, rig.transform, true);
        root.transform.localScale = Vector3.one * targetNameTagScale;
        root.SetActive(true);

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        SpriteRenderer[] sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        Transform? name = FindCachedTransform(transforms, "Name");
        Transform? fps = FindCachedTransform(transforms, "FPS");
        Transform? ping = FindCachedTransform(transforms, "Ping");
        Transform? mods = FindCachedTransform(transforms, "Mods");

        return new NameTagView
        {
            Root = root,
            RootTransform = root.transform,
            Anchor = rig.headMesh != null ? rig.headMesh.transform : rig.transform,
            Name = CreateLabelBinding(name),
            Fps = CreateLabelBinding(fps),
            Ping = CreateLabelBinding(ping),
            Mods = CreateLabelBinding(mods),
            FpsColors = CreateColorBinding(transforms, graphics, sprites, renderers, fps, "FPS"),
            PingColors = CreateColorBinding(transforms, graphics, sprites, renderers, ping, "Ping"),
            ModsColors = CreateColorBinding(transforms, graphics, sprites, renderers, mods, "Mods"),
            IconMeta = FindPlatformIcon(transforms, "IconMeta", "Meta", "Quest", "Standalone"),
            IconPc = FindPlatformIcon(transforms, "IconPC", "IconPc", "PC", "Oculus"),
            IconUnknown = FindPlatformIcon(transforms, "IconUnknown", "Unknown"),
            IconSteam = FindPlatformIcon(transforms, "IconSteam", "Steam", "Icon_Steam", "SteamIcon")
        };
    }

    private static GameObject? FindPlatformIcon(Transform[] transforms, params string[] names)
    {
        for (int n = 0; n < names.Length; n++)
        {
            Transform? exact = FindCachedTransform(transforms, names[n]);
            if (exact != null)
                return exact.gameObject;
        }

        for (int n = 0; n < names.Length; n++)
        {
            string needle = names[n];
            for (int i = 0; i < transforms.Length; i++)
            {
                string name = transforms[i].name ?? "";
                if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return transforms[i].gameObject;
            }
        }

        return null;
    }

    private static Transform? FindCachedTransform(Transform[] transforms, string name)
    {
        for (int i = 0; i < transforms.Length; i++)
        {
            if (string.Equals(transforms[i].name, name, StringComparison.Ordinal))
                return transforms[i];
        }
        return null;
    }

    private static GameObject? GetCachedGameObject(Transform[] transforms, string name)
    {
        Transform? transform = FindCachedTransform(transforms, name);
        return transform != null ? transform.gameObject : null;
    }

    private static NameTagLabelBinding CreateLabelBinding(Transform? target)
    {
        NameTagLabelBinding binding = new NameTagLabelBinding();
        if (target != null)
        {
            target.TryGetComponent(out binding.Tmp);
            if (binding.Tmp == null)
                target.TryGetComponent(out binding.UiText);
        }
        return binding;
    }

    private static NameTagColorBinding CreateColorBinding(
        Transform[] transforms,
        Graphic[] graphics,
        SpriteRenderer[] sprites,
        Renderer[] renderers,
        Transform? metricRoot,
        string metricName)
    {
        NameTagColorBinding binding = new NameTagColorBinding();

        for (int i = 0; i < graphics.Length; i++)
        {
            if (IsMetricColorTarget(graphics[i].transform, transforms, metricRoot, metricName))
                binding.Graphics.Add(graphics[i]);
        }

        for (int i = 0; i < sprites.Length; i++)
        {
            if (IsMetricColorTarget(sprites[i].transform, transforms, metricRoot, metricName))
                binding.Sprites.Add(sprites[i]);
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (IsMetricColorTarget(renderers[i].transform, transforms, metricRoot, metricName))
                binding.Renderers.Add(renderers[i]);
        }

        return binding;
    }

    private static bool IsMetricColorTarget(
        Transform target,
        Transform[] transforms,
        Transform? metricRoot,
        string metricName)
    {
        if (metricRoot != null && (target == metricRoot || target.IsChildOf(metricRoot)))
            return true;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            string name = candidate.name ?? string.Empty;
            bool metricMatch = name.IndexOf(metricName, StringComparison.OrdinalIgnoreCase) >= 0;
            bool iconMatch = name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0;
            if (metricMatch && iconMatch && (target == candidate || target.IsChildOf(candidate)))
                return true;
        }

        return false;
    }

    private static void RefreshNameTag(VRRig rig, NameTagView view)
    {
        NetPlayer netPlayer = GetPlayerFromVRRig(rig);
        string displayName = netPlayer != null ? netPlayer.NickName : rig.playerNameVisible;
        if (!string.Equals(view.LastName, displayName, StringComparison.Ordinal))
        {
            view.LastName = displayName;
            view.Name.Set(displayName);
        }

        int fps = RigBits.GetFPS(rig);
        if (view.LastFps != fps)
        {
            view.LastFps = fps;
            view.Fps.Set(fps + "Hz");
        }
        SetMetricColorIfChanged(view.FpsColors, GetFpsStatusColor(fps), ref view.LastFpsColor, ref view.HasFpsColor);

        int ping = GetPingThrottled(rig);
        if (view.LastPing != ping)
        {
            view.LastPing = ping;
            view.Ping.Set(ping + "Ms");
        }
        SetMetricColorIfChanged(view.PingColors, GetPingStatusColor(ping), ref view.LastPingColor, ref view.HasPingColor);

        NameTagModStatus modStatus = GetDetectedModStatus(rig);
        if (!view.HasModStatus ||
            view.LastModStatus.LegalCount != modStatus.LegalCount ||
            view.LastModStatus.IllegalCount != modStatus.IllegalCount ||
            !string.Equals(view.LastModStatus.PrimaryLabel, modStatus.PrimaryLabel, StringComparison.Ordinal))
        {
            view.HasModStatus = true;
            view.LastModStatus = modStatus;
            view.Mods.Set(FormatNameTagModStatus(modStatus));
        }
        SetMetricColorIfChanged(view.ModsColors, GetModStatusColor(modStatus), ref view.LastModsColor, ref view.HasModsColor);

        string platform;
        try { platform = GetPlatform(rig); }
        catch { platform = "Unknown"; }

        if (!string.Equals(view.LastPlatform, platform, StringComparison.Ordinal))
        {
            view.LastPlatform = platform;
            SetPlatformIcons(view, platform);
        }
    }

    private static void SetMetricColorIfChanged(
        NameTagColorBinding binding,
        Color color,
        ref Color lastColor,
        ref bool hasColor)
    {
        if (hasColor && lastColor == color)
            return;

        hasColor = true;
        lastColor = color;

        for (int i = 0; i < binding.Graphics.Count; i++)
        {
            Graphic graphic = binding.Graphics[i];
            if (graphic != null)
                graphic.color = color;
        }

        for (int i = 0; i < binding.Sprites.Count; i++)
        {
            SpriteRenderer sprite = binding.Sprites[i];
            if (sprite != null)
                sprite.color = color;
        }

        for (int i = 0; i < binding.Renderers.Count; i++)
        {
            Renderer renderer = binding.Renderers[i];
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(nameTagPropertyBlock);
            nameTagPropertyBlock.SetColor(NameTagColorProperty, color);
            renderer.SetPropertyBlock(nameTagPropertyBlock);
            nameTagPropertyBlock.Clear();
        }
    }

    private static void SetPlatformIcons(NameTagView view, string platform)
    {

        if (string.Equals(platform, "Loading...", StringComparison.Ordinal))
        {
            SetActiveIfChanged(view.IconUnknown, false);
            SetActiveIfChanged(view.IconMeta, false);
            SetActiveIfChanged(view.IconPc, false);
            SetActiveIfChanged(view.IconSteam, false);
            return;
        }

        SetPlatformIconActive(view.IconMeta, false);
        SetPlatformIconActive(view.IconPc, false);
        SetPlatformIconActive(view.IconUnknown, false);
        SetPlatformIconActive(view.IconSteam, false);

        switch (platform)
        {
            case "Steam":
                if (!SetPlatformIconActive(view.IconSteam, true) && view.IconPc != null)
                    SetPlatformIconActive(view.IconPc, true);
                else if (view.IconSteam == null)
                    SetPlatformIconActive(view.IconUnknown, true);
                break;
            case "PC":
            case "Oculus PC":
            case "Oculus":
                if (!SetPlatformIconActive(view.IconPc, true))
                    SetPlatformIconActive(view.IconMeta, true);
                break;
            case "Standalone":
            case "StandaloneVR":
            case "Meta":
            case "Quest":
            case "PSVR":
            case "Pico":
                SetPlatformIconActive(view.IconMeta, true);
                break;
            default:
                SetPlatformIconActive(view.IconUnknown, true);
                break;
        }
    }

    private static bool SetPlatformIconActive(GameObject? target, bool active)
    {
        if (target == null)
            return false;

        if (active)
        {
            Transform walk = target.transform;
            while (walk != null)
            {
                if (!walk.gameObject.activeSelf)
                    walk.gameObject.SetActive(true);
                if (walk.parent == null || walk.parent == walk)
                    break;
                walk = walk.parent;
            }
        }

        if (target.activeSelf != active)
            target.SetActive(active);

        if (active)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }

            Graphic[] graphics = target.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                    graphics[i].enabled = true;
            }
        }

        return active && target.activeInHierarchy;
    }

    private static void SetActiveIfChanged(GameObject? target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    public static GameObject? PeekNameTagPrefab() => GetNameTagPrefab();

    private static GameObject? GetNameTagPrefab()
    {
        if (nameTagPrefab != null)
            return nameTagPrefab;
        if (nameBundle == null || nameTagPrefabLookupAttempted)
            return null;

        nameTagPrefabLookupAttempted = true;
        string[] assetNames = nameBundle.GetAllAssetNames();
        string? prefabPath = null;
        for (int i = 0; i < assetNames.Length; i++)
        {
            string assetName = assetNames[i];
            if (assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                assetName.IndexOf("nametag", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefabPath = assetName;
                break;
            }
        }

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

    private static NameTagModStatus GetDetectedModStatus(VRRig rig)
    {
        if (rig == null)
            return default;

        bool hasCached = tagScanPocket.TryGetValue(rig, out NameTagModStatus cached);
        if (!nextNameTagModScanTimes.TryGetValue(rig, out float nextScan))
        {
            nextNameTagModScanTimes[rig] =
                Time.time + (Mathf.Abs(rig.GetInstanceID() % 100) * 0.03f);
            return default;
        }
        if (Time.time < nextScan)
            return hasCached ? cached : default;

        NameTagModStatus status = default;
        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null)
            {
                Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);
                List<string> illegalNames = new List<string>();
                List<string> legalNames = new List<string>();

                foreach (string key in hits.Keys)
                {
                    if (!propertySignatures.TryGetValue(key, out PropertySignature signature))
                        continue;

                    string display = FormatDetectedModName(signature.Name);
                    if (string.IsNullOrWhiteSpace(display) || display == "Unknown")
                        display = signature.Name;

                    if (signature.IsLegal)
                    {
                        status.LegalCount++;
                        if (!legalNames.Contains(display))
                            legalNames.Add(display);
                    }
                    else
                    {
                        status.IllegalCount++;
                        if (!illegalNames.Contains(display))
                            illegalNames.Add(display);
                    }
                }

                if (illegalNames.Count > 0)
                    status.PrimaryLabel = PreferCustomMenuLabel(illegalNames);
                else if (legalNames.Count > 0)
                    status.PrimaryLabel = PreferCustomMenuLabel(legalNames);
            }
        }
        catch
        {
            status = default;
        }

        tagScanPocket[rig] = status;
        nextNameTagModScanTimes[rig] =
            Time.time + NameTagModScanSeconds + (Mathf.Abs(rig.GetInstanceID() % 10) * 0.1f);
        return status;
    }

    private static string PreferCustomMenuLabel(List<string> names)
    {
        if (names == null || names.Count == 0)
            return null;

        string[] priority =
        {
            "SERALYTH MOD MENU", "Seralyth Console", "II's Stupid Menu", "II Menu", "Void",
            "Haunted Mod Menu", "Monke Mod Menu", "Destiny Menu", "Crystal Menu", "Shiba GT Dark",
            "Mango", "Nebula", "Pulsar", "Cosmos", "Hydra", "Spectre", "Viper", "Eclipse",
            "Phantom", "Violet Paid", "Violet", "Obsidian", "Oblivion", "Asteroid Lite", "Elux",
            "Atlas", "Fusioned", "Genesis", "Grate", "Orbit", "Untitled", "Bark", "BlossomChecker",
            "Sakuraa", "WalkSimulator", "Banana OS", "Media Pad", "Utilla"
        };

        for (int i = 0; i < priority.Length; i++)
        {
            for (int j = 0; j < names.Count; j++)
            {
                if (string.Equals(names[j], priority[i], StringComparison.OrdinalIgnoreCase) ||
                    names[j].IndexOf(priority[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return priority[i];
            }
        }

        return names[0];
    }

    private static string FormatNameTagModStatus(NameTagModStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.PrimaryLabel))
        {
            int total = status.LegalCount + status.IllegalCount;
            if (total > 1)
                return status.PrimaryLabel + " +" + (total - 1);
            return status.PrimaryLabel;
        }

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

    private static void RemoveNameTag(VRRig rig)
    {
        if (nametags.TryGetValue(rig, out NameTagView view))
        {
            if (view.Root != null)
                Object.Destroy(view.Root);
            nametags.Remove(rig);
        }

        nextNameTagInfoUpdateTimes.Remove(rig);
        tagScanPocket.Remove(rig);
        nextNameTagModScanTimes.Remove(rig);
        RemovePingCache(rig);
    }

    private static void DestroyAllNameTags()
    {
        foreach (NameTagView view in nametags.Values)
        {
            if (view.Root != null)
                Object.Destroy(view.Root);
        }

        nametags.Clear();
        nameTagRigScratch.Clear();
        activeNameTagRigs.Clear();
        nextNameTagInfoUpdateTimes.Clear();
        tagScanPocket.Clear();
        nextNameTagModScanTimes.Clear();
    }

    private struct NameTagModStatus
    {
        public int LegalCount;
        public int IllegalCount;
        public string PrimaryLabel;
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
        nameTagDistanceFadeEnabled = GetSavedToggle("Distance Fade", GetSavedToggle("Nametag Distance Fade", nameTagDistanceFadeEnabled));
        SavedToggleStates["Distance Fade"] = nameTagDistanceFadeEnabled;
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
        nameTagsEnabled = GetSavedToggle("Nametags", nameTagsEnabled);
        SavedToggleStates["Nametags"] = nameTagsEnabled;
        if (!nameTagsEnabled)
            DisableNameTags();
        SaveButtonStates();
    }

    public static void DisableNameTags()
    {
        DestroyAllNameTags();
        ClearPingCaches();
    }

    public static void NameTagsLoop()
    {
        if (nameTagsEnabled)
            UpdateNameTags();
    }

    public static void Shutdown()
    {
        nameTagsEnabled = false;
        DestroyAllNameTags();
        ClearPingCaches();
        ClearPropertyScanCaches();
        voiceComponentsByRig.Clear();
        ClearAllBoneHighlights();
        nameTagPrefab = null;
        nameTagPrefabLookupAttempted = false;
        loggedMissingNameTagPrefab = false;

        if (regionStatsCoroutine != null && CoroutineHandler.Instance != null)
        {
            CoroutineHandler.Instance.StopCoroutine(regionStatsCoroutine);
            regionStatsCoroutine = null;
        }

        if (checkerCoroutine != null && CoroutineHandler.Instance != null)
        {
            CoroutineHandler.Instance.StopCoroutine(checkerCoroutine);
            checkerCoroutine = null;
        }

        CleanupCheckerVisuals(true);
    }
}
