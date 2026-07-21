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
    
    private static bool nameTagsEnabled = false;
    private static readonly Dictionary<VRRig, GameObject> nametags = new();
    public static AssetBundle nameBundle;
    private static GameObject nameTagPrefab;
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
        var copy = nametags.ToList();
        foreach (var pair in copy)
        {
            if (pair.Key == null || !VRRigCache.ActiveRigs.Contains(pair.Key))
            {
                Object.Destroy(pair.Value);
                nametags.Remove(pair.Key);
                nextNameTagInfoUpdateTimes.Remove(pair.Key);
                tagScanPocket.Remove(pair.Key);
                nextNameTagModScanTimes.Remove(pair.Key);
            }
        }
        
        foreach (var rig in VRRigCache.ActiveRigs)
        {
            if (rig == null) continue;
            if (rig.isLocal) continue;
            
            if (!nametags.ContainsKey(rig))
            {
                GameObject prefab = GetNameTagPrefab();
                if (prefab == null) return;

                GameObject tag = Object.Instantiate(prefab, rig.transform, true);
                tag.transform.localScale = Vector3.one * targetNameTagScale;
                tag.SetActive(true);
                nametags.Add(rig, tag);
            }

            GameObject nametag = nametags[rig];
            
            if (nametag == null)
            {
                nametags.Remove(rig);
                nextNameTagInfoUpdateTimes.Remove(rig);
                tagScanPocket.Remove(rig);
                nextNameTagModScanTimes.Remove(rig);
                continue;
            }
            
            Transform anchor = rig.headMesh != null ? rig.headMesh.transform : rig.transform;
            nametag.transform.position = anchor.position + Vector3.up * 0.87f;
            
            Camera camera = Main.GetActiveCamera();
            if (camera != null)
            {
                nametag.transform.LookAt(camera.transform.position);
                nametag.transform.Rotate(0f, 450f, 0f);
            }
            float visibleScale = targetNameTagScale;
            if (nameTagDistanceFadeEnabled && camera != null)
            {
                float distance = Vector3.Distance(camera.transform.position, anchor.position);
                visibleScale = distance <= nameTagFadeDistanceValues[nameTagFadeDistanceIndex] ? targetNameTagScale : 0f;
            }
            nametag.transform.localScale = Vector3.Lerp(nametag.transform.localScale, Vector3.one * visibleScale, Time.deltaTime * 10f);
            if (!nametag.activeSelf)
                nametag.SetActive(true);

            if (visibleScale <= 0.0001f)
                continue;

            float nextInfoUpdate;
            if (nextNameTagInfoUpdateTimes.TryGetValue(rig, out nextInfoUpdate) && Time.time < nextInfoUpdate)
                continue;
            nextNameTagInfoUpdateTimes[rig] = Time.time + NameTagInfoRefreshSeconds + (Mathf.Abs(rig.GetInstanceID() % 8) * 0.02f);

            NetPlayer netPlayer = GetPlayerFromVRRig(rig);
            SetTagText(nametag.transform.Find("Name"), netPlayer != null ? netPlayer.NickName : rig.playerNameVisible);

            int fps = RigBits.GetFPS(rig);
            int ping = GetPingThrottled(rig);
            NameTagModStatus modStatus = GetDetectedModStatus(rig);

            SetTagText(nametag.transform.Find("FPS"), fps + "Hz");
            SetTagMetricColor(nametag.transform, "FPS", GetFpsStatusColor(fps));

            SetTagText(nametag.transform.Find("Ping"), ping + "Ms");
            SetTagMetricColor(nametag.transform, "Ping", GetPingStatusColor(ping));

            SetTagText(nametag.transform.Find("Mods"), FormatNameTagModStatus(modStatus));
            SetTagMetricColor(nametag.transform, "Mods", GetModStatusColor(modStatus));

            var iconsLeft = nametag.transform.Find("IconsLeft");

            string platform;
            try { platform = GetPlatform(rig); }
            catch { platform = "Unknown"; }

            void SetPlatformIcons(Transform parent, string plat)
            {
                if (parent == null) return;
                
                var meta = parent.Find("IconMeta");
                var pc = parent.Find("IconPC");
                var unknown = parent.Find("IconUnknown");
                var steam = parent.Find("IconSteam");

                if (meta != null) meta.gameObject.SetActive(false);
                if (pc != null) pc.gameObject.SetActive(false);
                if (unknown != null) unknown.gameObject.SetActive(false);
                if (steam != null) steam.gameObject.SetActive(false);
                
                switch (plat)
                {
                    case "Standalone":
                        if (meta != null) meta.gameObject.SetActive(true);
                        break;

                    case "PC":
                        if (pc != null) pc.gameObject.SetActive(true);
                        break;

                    case "Steam":
                        if (steam != null) steam.gameObject.SetActive(true);
                        break;

                    default:
                        if (unknown != null) unknown.gameObject.SetActive(true);
                        break;
                }
            }
            
            SetPlatformIcons(iconsLeft, platform);
        }
    }
    

    private static GameObject GetNameTagPrefab()
    {
        if (nameTagPrefab != null)
            return nameTagPrefab;

        if (nameBundle == null)
            return null;

        string[] assetNames = nameBundle.GetAllAssetNames();
        string prefabPath = assetNames.FirstOrDefault(name =>
            name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
            name.IndexOf("nametag", StringComparison.OrdinalIgnoreCase) >= 0);

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

    private static void SetTagText(Transform textTransform, string value)
    {
        if (textTransform == null) return;

        TMP_Text tmp = textTransform.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = value;
            return;
        }

        UnityEngine.UI.Text uiText = textTransform.GetComponent<UnityEngine.UI.Text>();
        if (uiText != null)
            uiText.text = value;
    }

    private static NameTagModStatus GetDetectedModStatus(VRRig rig)
    {
        if (rig == null) return default;

        if (tagScanPocket.TryGetValue(rig, out NameTagModStatus cached) &&
            nextNameTagModScanTimes.TryGetValue(rig, out float nextScan) &&
            Time.time < nextScan)
            return cached;

        if (!tagScanPocket.ContainsKey(rig) && !nextNameTagModScanTimes.ContainsKey(rig))
        {
            nextNameTagModScanTimes[rig] = Time.time + (Mathf.Abs(rig.GetInstanceID() % 100) * 0.03f);
            return default;
        }

        NameTagModStatus status = default;
        try
        {
            Player player = rig.GetPhotonPlayer();
            if (player != null)
            {
                Dictionary<string, List<string>> hits = FindPropertySignatureHits(player);
                foreach (string key in hits.Keys)
                {
                    if (!propertySignatures.TryGetValue(key, out PropertySignature signature))
                        continue;

                    if (signature.IsLegal)
                        status.LegalCount++;
                    else
                        status.IllegalCount++;
                }
            }
        }
        catch
        {
            status = default;
        }

        tagScanPocket[rig] = status;
        nextNameTagModScanTimes[rig] = Time.time + NameTagModScanSeconds + (Mathf.Abs(rig.GetInstanceID() % 10) * 0.1f);
        return status;
    }

    private static string FormatNameTagModStatus(NameTagModStatus status)
    {
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

    private static void SetTagMetricColor(Transform root, string metricName, Color color)
    {
        if (root == null) return;

        SetTagColor(root.Find(metricName), color);

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string name = child.name ?? "";
            bool metricMatch = name.IndexOf(metricName, StringComparison.OrdinalIgnoreCase) >= 0;
            bool iconMatch = name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0;

            if (metricMatch && iconMatch)
                SetTagColor(child, color);
        }
    }

    private static void SetTagColor(Transform target, Color color)
    {
        if (target == null) return;

        TMP_Text tmp = target.GetComponent<TMP_Text>();
        if (tmp != null) tmp.color = color;

        UnityEngine.UI.Text uiText = target.GetComponent<UnityEngine.UI.Text>();
        if (uiText != null) uiText.color = color;

        foreach (Image image in target.GetComponentsInChildren<Image>(true))
            image.color = color;

        foreach (RawImage rawImage in target.GetComponentsInChildren<RawImage>(true))
            rawImage.color = color;

        SpriteRenderer spriteRenderer = target.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.color = color;

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            foreach (Material material in renderer.materials)
                material.color = color;
        }
    }

    private struct NameTagModStatus
    {
        public int LegalCount;
        public int IllegalCount;
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
        nameTagDistanceFadeEnabled = !nameTagDistanceFadeEnabled;
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
        nameTagsEnabled = !nameTagsEnabled;
        
        if (!nameTagsEnabled)
        {
            DisableNameTags();
        }
    }
    
    public static void DisableNameTags()
    {
        foreach (var tag in nametags.Values)
        {
            if (tag != null)
                tag.SetActive(false);
        }
        nextNameTagInfoUpdateTimes.Clear();
        tagScanPocket.Clear();
        nextNameTagModScanTimes.Clear();
    }
    
    public static void NameTagsLoop()
    {
        if (!nameTagsEnabled)
            return;

        UpdateNameTags();
    }

    
}
