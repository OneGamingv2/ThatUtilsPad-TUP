using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ThatUtilsPad;

public partial class Main
{
    private sealed class PreviewMeshBinding
    {
        public Transform SourceRoot;
        public Transform SourceTransform;
        public Transform PreviewTransform;
        public SkinnedMeshRenderer SkinnedSource;
        public Mesh BakedMesh;
    }

    private readonly List<PreviewMeshBinding> playerModelPreviewBindings = new List<PreviewMeshBinding>();
    private GameObject playerModelPreviewObj;
    private Transform playerModelPreviewRoot;
    private VRRig playerModelPreviewSourceRig;
    private Coroutine playerModelPreviewBuildRoutine;
    private Coroutine outfitPreviewRefreshRoutine;
    private int playerModelBuildVersion;
    private bool playerModelPreviewBuildInProgress;
    private bool outfitPreviewMode;
    private Vector3 playerModelPreviewMeshOriginOffset;
    private const int PlayerModelPreviewRendererLimit = 28;
    private Transform volumeControlsRoot;

    private static readonly Vector3 PlayerModelPreviewLocalPosition = new Vector3(0f, -0.01f, -0.03f);
    private static readonly Vector3 PlayerModelPreviewLocalEuler = new Vector3(0f, 180f, 0f);
    private static readonly Vector3 PlayerModelPreviewLocalScale = new Vector3(0.09f, 0.09f, 0.09f);
    private const float PlayerModelPreviewTargetSize = 0.17f;
    private const float OutfitOverviewTargetSize = 0.22f;
    private float playerModelPreviewCachedScale = 0.09f;
    private bool playerModelPreviewScaleReady;

    private void CachePlayerModelPreviewRoot()
    {
        if (menuObj == null)
        {
            playerModelPreviewRoot = null;
            return;
        }

        Transform side = menuObj.transform.Find("SideHolder");
        if (side == null)
        {
            playerModelPreviewRoot = menuObj.transform;
            return;
        }

        Transform checkerMonke =
            side.Find("CheckerMonke") ??
            FindChildByName(side, "CheckerMonke");
        playerModelPreviewRoot = checkerMonke != null ? checkerMonke : side;
    }

    private Vector3 GetCheckerMonkeSlotCenterLocal()
    {
        if (playerModelPreviewRoot == null)
            return Vector3.zero;

        Transform monkeBase =
            playerModelPreviewRoot.Find("MonkeBase") ??
            FindChildByName(playerModelPreviewRoot, "MonkeBase");
        if (monkeBase != null)
            return monkeBase.localPosition;

        Transform monkeColor =
            playerModelPreviewRoot.Find("MonkeColor") ??
            FindChildByName(playerModelPreviewRoot, "MonkeColor");
        if (monkeColor != null)
            return monkeColor.localPosition;

        return Vector3.zero;
    }

    private void KeepPreviewPoseAnchored()
    {
        if (playerModelPreviewObj == null)
            return;

        CachePlayerModelPreviewRoot();
        Transform parent = playerModelPreviewRoot != null ? playerModelPreviewRoot : menuObj != null ? menuObj.transform : null;
        Transform t = playerModelPreviewObj.transform;
        if (parent != null && t.parent != parent)
            t.SetParent(parent, false);

        t.localPosition = PlayerModelPreviewLocalPosition;
        t.localRotation = Quaternion.Euler(PlayerModelPreviewLocalEuler);
        float fitted = playerModelPreviewScaleReady
            ? playerModelPreviewCachedScale
            : FitPreviewScaleToSlot();
        playerModelPreviewCachedScale = fitted;
        playerModelPreviewScaleReady = true;
        t.localScale = new Vector3(fitted, fitted, fitted);
    }

    private float FitPreviewScaleToSlot()
    {
        if (playerModelPreviewObj == null)
            return 0.08f;

        bool outfitPage = outfitPreviewMode || string.Equals(currentCategory, "Cosmetics", StringComparison.OrdinalIgnoreCase);
        float targetSize = outfitPage ? OutfitOverviewTargetSize : PlayerModelPreviewTargetSize;
        float minScale = outfitPage ? 0.08f : 0.06f;
        float maxScale = outfitPage ? 0.32f : 0.28f;

        Renderer[] renderers = playerModelPreviewObj.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return outfitPage ? 0.12f : 0.08f;

        Bounds? combined = null;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (combined == null)
                combined = r.bounds;
            else
            {
                Bounds b = combined.Value;
                b.Encapsulate(r.bounds);
                combined = b;
            }
        }

        if (combined == null)
            return outfitPage ? 0.12f : 0.08f;

        Transform parent = playerModelPreviewObj.transform.parent;
        float parentLossy = parent != null
            ? Mathf.Max(Mathf.Abs(parent.lossyScale.x), 0.0001f)
            : 1f;
        float worldMax = Mathf.Max(combined.Value.size.x, combined.Value.size.y, combined.Value.size.z);
        float localMaxAtUnitScale = worldMax / parentLossy;
        if (localMaxAtUnitScale < 0.0001f)
            return outfitPage ? 0.12f : 0.08f;

        return Mathf.Clamp(targetSize / localMaxAtUnitScale, minScale, maxScale);
    }

    public void StartPlayerModelPreview(VRRig rig)
    {
        if (rig == null || !rig.gameObject.activeInHierarchy)
        {
            DestroyPlayerModelPreview();
            return;
        }

        CachePlayerModelPreviewRoot();
        playerModelBuildVersion++;
        playerModelPreviewBuildInProgress = true;

        if (playerModelPreviewBuildRoutine != null)
            StopCoroutine(playerModelPreviewBuildRoutine);

        playerModelPreviewBuildRoutine = StartCoroutine(BuildPlayerModelPreviewOverFrames(rig, playerModelBuildVersion));
    }

    public void EnterOutfitPreviewMode()
    {
        outfitPreviewMode = true;
        Mods.BrowseToOutfit(Mods.GetCurrentOutfitIndex());
        ApplyOutfitCarouselSideUi();
        RefreshOutfitPreviewDelayed();
        if (outfitTitleEnforceRoutine != null)
            StopCoroutine(outfitTitleEnforceRoutine);
        outfitTitleEnforceRoutine = StartCoroutine(EnforceOutfitTitleForAMoment());
    }

    private Coroutine outfitTitleEnforceRoutine;

    private IEnumerator EnforceOutfitTitleForAMoment()
    {
        for (int i = 0; i < 8; i++)
        {
            if (!outfitPreviewMode || currentCategory != "Cosmetics")
                break;
            SetCheckerPanelTitle("Outfit");
            ApplyOutfitCarouselSideUi();
            yield return null;
        }
        outfitTitleEnforceRoutine = null;
    }

    public void ExitOutfitPreviewMode()
    {
        if (!outfitPreviewMode && currentCategory != "Cosmetics")
        {
            if (checkerPanelTitleText != null &&
                string.Equals(checkerPanelTitleText.text, "Outfit", StringComparison.OrdinalIgnoreCase))
                RestorePlayerCheckerSideUiFromOutfitMode();
            return;
        }

        outfitPreviewMode = false;
        if (outfitPreviewRefreshRoutine != null)
        {
            StopCoroutine(outfitPreviewRefreshRoutine);
            outfitPreviewRefreshRoutine = null;
        }
        if (outfitTitleEnforceRoutine != null)
        {
            StopCoroutine(outfitTitleEnforceRoutine);
            outfitTitleEnforceRoutine = null;
        }

        RestorePlayerCheckerSideUiFromOutfitMode();
        if (currentCategory != "SelectUser" || !Mods.HasSelectedPlayer())
            DestroyPlayerModelPreview();
    }

    public void ForceCloseOutfitPreviewMode()
    {
        outfitPreviewMode = false;
        if (outfitPreviewRefreshRoutine != null)
        {
            StopCoroutine(outfitPreviewRefreshRoutine);
            outfitPreviewRefreshRoutine = null;
        }
        if (outfitTitleEnforceRoutine != null)
        {
            StopCoroutine(outfitTitleEnforceRoutine);
            outfitTitleEnforceRoutine = null;
        }
        RestorePlayerCheckerSideUiFromOutfitMode();
        DestroyPlayerModelPreview();
    }

    public void RefreshOutfitPreviewDelayed()
    {
        if (!isMenuOpened || currentCategory != "Cosmetics")
            return;

        if (outfitPreviewRefreshRoutine != null)
            StopCoroutine(outfitPreviewRefreshRoutine);
        outfitPreviewRefreshRoutine = StartCoroutine(RefreshOutfitPreviewAfterCosmeticsApply());
    }

    private IEnumerator RefreshOutfitPreviewAfterCosmeticsApply()
    {
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.12f);

        if (!isMenuOpened || currentCategory != "Cosmetics")
        {
            outfitPreviewRefreshRoutine = null;
            yield break;
        }

        VRRig localRig = Mods.GetLocalVRRig();
        if (localRig != null)
            StartPlayerModelPreview(localRig);
        ApplyOutfitCarouselSideUi();
        outfitPreviewRefreshRoutine = null;
    }

    public void ApplyOutfitCarouselSideUi()
    {
        if (!outfitPreviewMode && currentCategory != "Cosmetics")
            return;

        SetCheckerPanelTitle("Outfit");
        if (nameTextComp != null)
            nameTextComp.text = Mods.GetBrowsingOutfitLabel();
        if (fpsPingTextComp != null)
        {
            MakeCheckerTextShow(fpsPingTextComp);
            fpsPingTextComp.text = Mods.GetOutfitDotsLabel();
        }
        if (platformTextComp != null)
            platformTextComp.text = Mods.IsBrowsingOutfitEquipped() ? "Equipped" : "Preview";
        if (dateTextComp != null)
            dateTextComp.text = Mods.IsBrowsingOutfitEquipped()
                ? "This look is on"
                : "Equip to wear this look";
        UpdateCheckerPlatformIcon("Unknown");
        SetVolumeControlsVisible(false);
    }

    private void ApplyOutfitPreviewSideUi()
    {
        ApplyOutfitCarouselSideUi();
    }

    private void RestorePlayerCheckerSideUiFromOutfitMode()
    {
        SetCheckerPanelTitle(string.IsNullOrEmpty(checkerPanelTitleDefault) ? "Player Checker" : checkerPanelTitleDefault);
        SetVolumeControlsVisible(true);
        if (currentCategory == "SelectUser" && Mods.HasSelectedPlayer())
            return;
        ClearCheckerSelectionUiKeepPreview(false);
        SetMoreInfoVisible(false);
    }

    private void ClearCheckerSelectionUiKeepPreview(bool destroyPreview)
    {
        lastCheckerName = "-----";
        lastCheckerFpsPing = "--Hz \u2022 --Ms";
        lastCheckerPlatform = "Unknown";
        lastCheckerDate = "Date: --/--/----";
        lastCheckerColorStr = "--";
        lastCheckerLegalMods = "None";
        lastCheckerIllegalMods = "None";
        lastCheckerUnknownPropList = Array.Empty<string>();
        hasLastCheckerColor = false;
        moreInfoModList = Array.Empty<MoreInfoModEntry>();
        moreInfoModsPage = 0;
        moreInfoModsExpanded = false;
        if (nameTextComp != null) nameTextComp.text = lastCheckerName;
        if (fpsPingTextComp != null) { MakeCheckerTextShow(fpsPingTextComp); fpsPingTextComp.text = lastCheckerFpsPing; }
        if (platformTextComp != null) platformTextComp.text = "Platform";
        UpdateCheckerPlatformIcon("Unknown");
        if (dateTextComp != null) dateTextComp.text = lastCheckerDate;
        if (destroyPreview)
            DestroyPlayerModelPreview();
    }

    private void SetVolumeControlsVisible(bool visible)
    {
        if (volumeControlsRoot == null && menuObj != null)
        {
            Transform side = menuObj.transform.Find("SideHolder");
            if (side != null)
            {
                volumeControlsRoot =
                    side.Find("VolumeControls") ??
                    FindChildByName(side, "VolumeControls");
            }
        }

        if (volumeControlsRoot != null)
            volumeControlsRoot.gameObject.SetActive(visible);
    }

    public void DestroyPlayerModelPreview()
    {
        playerModelBuildVersion++;
        playerModelPreviewBuildInProgress = false;

        if (playerModelPreviewBuildRoutine != null)
        {
            StopCoroutine(playerModelPreviewBuildRoutine);
            playerModelPreviewBuildRoutine = null;
        }

        playerModelPreviewBindings.Clear();
        playerModelPreviewSourceRig = null;
        playerModelPreviewMeshOriginOffset = Vector3.zero;
        playerModelPreviewScaleReady = false;
        playerModelPreviewCachedScale = 0.1f;

        if (playerModelPreviewObj != null)
        {
            Destroy(playerModelPreviewObj);
            playerModelPreviewObj = null;
        }

        SetCheckerMonkeSilhouetteVisible(false);
    }

    private void HideCheckerMonkeBoxPermanently()
    {
        SetCheckerMonkeSilhouetteVisible(false);

        Transform side = menuObj != null ? menuObj.transform.Find("SideHolder") : null;
        if (side == null)
            return;

        Transform checkerMonke =
            side.Find("CheckerMonke") ??
            FindChildByName(side, "CheckerMonke");
        if (checkerMonke == null)
            return;

        for (int i = 0; i < checkerMonke.childCount; i++)
        {
            Transform child = checkerMonke.GetChild(i);
            if (child == null)
                continue;

            if (playerModelPreviewObj != null &&
                (child == playerModelPreviewObj.transform || child.IsChildOf(playerModelPreviewObj.transform)))
                continue;

            string name = child.name ?? "";
            if (name.StartsWith("TUP_", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.IndexOf("MonkeBase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("MonkeColor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Box", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Silhouette", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                child.gameObject.SetActive(false);
                continue;
            }

            Image[] images = child.GetComponentsInChildren<Image>(true);
            for (int img = 0; img < images.Length; img++)
            {
                if (images[img] != null)
                    images[img].enabled = false;
            }

            SpriteRenderer[] sprites = child.GetComponentsInChildren<SpriteRenderer>(true);
            for (int s = 0; s < sprites.Length; s++)
            {
                if (sprites[s] != null)
                    sprites[s].enabled = false;
            }
        }

        Transform monkeBase =
            checkerMonke.Find("MonkeBase") ??
            FindChildByName(checkerMonke, "MonkeBase");
        if (monkeBase != null)
            monkeBase.gameObject.SetActive(false);
    }

    private void UpdatePlayerModelPreviewLive()
    {
        if (playerModelPreviewObj == null)
            return;

        if (playerModelPreviewSourceRig == null || !playerModelPreviewSourceRig.gameObject.activeInHierarchy)
        {
            DestroyPlayerModelPreview();
            return;
        }

        KeepPreviewPoseAnchored();
    }

    private IEnumerator BuildPlayerModelPreviewOverFrames(VRRig rig, int version)
    {
        yield return null;

        if (version != playerModelBuildVersion || rig == null || menuObj == null)
        {
            FinishPlayerModelPreviewBuild(version);
            yield break;
        }

        DestroyPlayerModelPreviewVisualOnly();
        CachePlayerModelPreviewRoot();

        Transform parent = playerModelPreviewRoot != null ? playerModelPreviewRoot : menuObj.transform;
        if (parent == null)
        {
            FinishPlayerModelPreviewBuild(version);
            yield break;
        }

        playerModelPreviewObj = new GameObject("TUP_SelectedPlayerModelPreview");
        playerModelPreviewObj.transform.SetParent(parent, false);
        KeepPreviewPoseAnchored();
        playerModelPreviewObj.layer = LayerMask.NameToLayer("Ignore Raycast");
        playerModelPreviewSourceRig = rig;
        playerModelPreviewBindings.Clear();

        SetCheckerMonkeSilhouetteVisible(false);

        int copied = 0;
        int processed = 0;
        Renderer[] renderers = rig.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (version != playerModelBuildVersion || playerModelPreviewObj == null)
            {
                FinishPlayerModelPreviewBuild(version);
                yield break;
            }

            Renderer sourceRenderer = renderers[i];
            if (sourceRenderer == null || copied >= PlayerModelPreviewRendererLimit || !ShouldCopyPreviewStillRenderer(sourceRenderer))
                continue;

            if (CopyPreviewRenderer(rig, sourceRenderer))
            {
                copied++;
                if (sourceRenderer is SkinnedMeshRenderer)
                    yield return null;
            }

            processed++;
            if (processed % 3 == 0)
                yield return null;
        }

        if (copied == 0)
        {
            List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is SkinnedMeshRenderer skin &&
                    skin != null &&
                    skin.enabled &&
                    skin.sharedMesh != null &&
                    skin.gameObject.activeInHierarchy &&
                    !skin.forceRenderingOff)
                {
                    skins.Add(skin);
                }
            }

            skins.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
            int fallbackLimit = Mathf.Min(6, skins.Count);
            for (int i = 0; i < fallbackLimit; i++)
            {
                if (version != playerModelBuildVersion || playerModelPreviewObj == null)
                {
                    FinishPlayerModelPreviewBuild(version);
                    yield break;
                }

                if (CopyPreviewRenderer(rig, skins[i]))
                {
                    copied++;
                    yield return null;
                }
            }
        }

        if (copied > 0)
        {
            RecenterPreviewMeshes();
            playerModelPreviewScaleReady = false;
            KeepPreviewPoseAnchored();
            playerModelPreviewScaleReady = false;
            KeepPreviewPoseAnchored();
            SetCheckerMonkeSilhouetteVisible(false);
            HideCheckerMonkeBoxPermanently();
        }
        else
        {
            SetCheckerMonkeSilhouetteVisible(false);
        }

        FinishPlayerModelPreviewBuild(version);
    }

    private void RecenterPreviewMeshes()
    {
        if (playerModelPreviewObj == null || playerModelPreviewBindings.Count == 0)
        {
            playerModelPreviewMeshOriginOffset = Vector3.zero;
            return;
        }

        Bounds? combined = null;
        for (int i = 0; i < playerModelPreviewBindings.Count; i++)
        {
            PreviewMeshBinding binding = playerModelPreviewBindings[i];
            if (binding.PreviewTransform == null)
                continue;

            MeshFilter filter = binding.PreviewTransform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;

            Bounds b = filter.sharedMesh.bounds;
            Vector3 worldCenter = binding.PreviewTransform.TransformPoint(b.center);
            Vector3 lossy = binding.PreviewTransform.lossyScale;
            Vector3 worldSize = Vector3.Scale(b.size, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            Bounds worldBounds = new Bounds(worldCenter, worldSize);
            if (combined == null)
                combined = worldBounds;
            else
            {
                Bounds c = combined.Value;
                c.Encapsulate(worldBounds);
                combined = c;
            }
        }

        if (combined == null)
        {
            playerModelPreviewMeshOriginOffset = Vector3.zero;
            return;
        }

        Vector3 center = combined.Value.center;
        playerModelPreviewMeshOriginOffset = playerModelPreviewObj.transform.InverseTransformPoint(center);

        for (int i = 0; i < playerModelPreviewBindings.Count; i++)
        {
            PreviewMeshBinding binding = playerModelPreviewBindings[i];
            if (binding.PreviewTransform == null)
                continue;
            binding.PreviewTransform.localPosition -= playerModelPreviewMeshOriginOffset;
        }
    }

    private bool CopyPreviewRenderer(VRRig rig, Renderer sourceRenderer)
    {
        if (playerModelPreviewObj == null || sourceRenderer == null)
            return false;

        if (sourceRenderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
        {
            Mesh baked = new Mesh { name = "TUP_Bake_" + sourceRenderer.name };
            skinned.BakeMesh(baked);
            CreatePreviewMeshPiece(rig.transform, playerModelPreviewObj.transform, sourceRenderer.transform, baked, sourceRenderer.sharedMaterials, sourceRenderer.name, skinned);
            return true;
        }

        MeshFilter filter = sourceRenderer.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            CreatePreviewMeshPiece(rig.transform, playerModelPreviewObj.transform, sourceRenderer.transform, filter.sharedMesh, sourceRenderer.sharedMaterials, sourceRenderer.name, null);
            return true;
        }

        return false;
    }

    private void FinishPlayerModelPreviewBuild(int version)
    {
        if (version == playerModelBuildVersion)
        {
            playerModelPreviewBuildInProgress = false;
            playerModelPreviewBuildRoutine = null;
        }
    }

    private void DestroyPlayerModelPreviewVisualOnly()
    {
        playerModelPreviewBindings.Clear();
        playerModelPreviewMeshOriginOffset = Vector3.zero;
        playerModelPreviewScaleReady = false;
        playerModelPreviewCachedScale = 0.1f;
        if (playerModelPreviewObj != null)
        {
            Destroy(playerModelPreviewObj);
            playerModelPreviewObj = null;
        }
    }

    private void SetCheckerMonkeSilhouetteVisible(bool visible)
    {
        if (checkerMonkeColorImage != null)
            checkerMonkeColorImage.enabled = visible;
        if (checkerMonkeColorSprite != null)
            checkerMonkeColorSprite.enabled = visible;

        Transform monkeBase =
            menuObj != null
                ? menuObj.transform.Find("SideHolder/CheckerMonke/MonkeBase") ??
                  menuObj.transform.Find("SideHolder/MonkeBase") ??
                  FindChildByName(menuObj.transform.Find("SideHolder"), "MonkeBase")
                : null;
        if (monkeBase != null)
        {
            Renderer[] renderers = monkeBase.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }

            Image[] images = monkeBase.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null)
                    images[i].enabled = visible;
            }
        }

        Transform monkeColor =
            menuObj != null
                ? menuObj.transform.Find("SideHolder/CheckerMonke/MonkeColor") ??
                  menuObj.transform.Find("SideHolder/MonkeColor")
                : null;
        if (monkeColor != null && monkeColor != monkeBase)
            monkeColor.gameObject.SetActive(visible);
    }

    private void UpdatePlayerModelPreviewBindings(bool updateSkinnedMeshes)
    {
        for (int i = playerModelPreviewBindings.Count - 1; i >= 0; i--)
        {
            PreviewMeshBinding binding = playerModelPreviewBindings[i];
            if (binding.SourceRoot == null || binding.SourceTransform == null || binding.PreviewTransform == null)
            {
                playerModelPreviewBindings.RemoveAt(i);
                continue;
            }

            binding.PreviewTransform.localPosition =
                binding.SourceRoot.InverseTransformPoint(binding.SourceTransform.position) - playerModelPreviewMeshOriginOffset;
            binding.PreviewTransform.localRotation = Quaternion.Inverse(binding.SourceRoot.rotation) * binding.SourceTransform.rotation;
            binding.PreviewTransform.localScale = DivideVector3(binding.SourceTransform.lossyScale, binding.SourceRoot.lossyScale);

            if (updateSkinnedMeshes && binding.SkinnedSource != null && binding.BakedMesh != null)
                binding.SkinnedSource.BakeMesh(binding.BakedMesh);
        }
    }

    private void CreatePreviewMeshPiece(
        Transform rigRoot,
        Transform previewParent,
        Transform sourceTransform,
        Mesh mesh,
        Material[] materials,
        string sourceName,
        SkinnedMeshRenderer skinnedSource)
    {
        GameObject previewPiece = new GameObject("Preview " + sourceName);
        previewPiece.transform.SetParent(previewParent, false);
        previewPiece.transform.localPosition = rigRoot.InverseTransformPoint(sourceTransform.position);
        previewPiece.transform.localRotation = Quaternion.Inverse(rigRoot.rotation) * sourceTransform.rotation;
        previewPiece.transform.localScale = DivideVector3(sourceTransform.lossyScale, rigRoot.lossyScale);
        previewPiece.layer = LayerMask.NameToLayer("Ignore Raycast");

        MeshFilter meshFilter = previewPiece.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = previewPiece.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        meshRenderer.sortingOrder = 20;

        playerModelPreviewBindings.Add(new PreviewMeshBinding
        {
            SourceRoot = rigRoot,
            SourceTransform = sourceTransform,
            PreviewTransform = previewPiece.transform,
            SkinnedSource = skinnedSource,
            BakedMesh = skinnedSource != null ? mesh : null
        });
    }

    private static Vector3 DivideVector3(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            Mathf.Abs(divisor.x) > 0.0001f ? value.x / divisor.x : value.x,
            Mathf.Abs(divisor.y) > 0.0001f ? value.y / divisor.y : value.y,
            Mathf.Abs(divisor.z) > 0.0001f ? value.z / divisor.z : value.z);
    }

    private static bool ShouldCopyPreviewStillRenderer(Renderer sourceRenderer)
    {
        if (sourceRenderer == null || !sourceRenderer.enabled || sourceRenderer.forceRenderingOff)
            return false;
        if (!sourceRenderer.gameObject.activeInHierarchy)
            return false;

        string objectName = GetFullObjectPath(sourceRenderer.transform).ToLowerInvariant();
        string rendererName = sourceRenderer.name.ToLowerInvariant();

        if (RendererLooksLikeDefaultOrHelperRig(objectName, rendererName))
            return false;

        if (sourceRenderer is SkinnedMeshRenderer)
        {
            return !RendererLooksLikeUnwantedBlackMarker(sourceRenderer, objectName)
                && RendererLooksLikeGorillaOrCosmetic(objectName, rendererName)
                && !objectName.Contains("skeleton")
                && !objectName.Contains("debug")
                && !objectName.Contains("wire")
                && !objectName.Contains("shadow")
                && !objectName.Contains("outline")
                && !objectName.Contains("marker")
                && !objectName.Contains("nametag")
                && !objectName.Contains("name tag")
                && !objectName.Contains("canvas")
                && !objectName.Contains("text")
                && !objectName.Contains("board")
                && !objectName.Contains("badge")
                && !objectName.Contains("particle");
        }

        return !RendererLooksLikeUnwantedBlackMarker(sourceRenderer, objectName)
            && RendererLooksLikeGorillaOrCosmetic(objectName, rendererName)
            && !objectName.Contains("skeleton")
            && !objectName.Contains("armature")
            && !objectName.Contains("debug")
            && !objectName.Contains("wire")
            && !objectName.Contains("shadow")
            && !objectName.Contains("outline")
            && !objectName.Contains("marker")
            && !objectName.Contains("nametag")
            && !objectName.Contains("name tag")
            && !objectName.Contains("canvas")
            && !objectName.Contains("text")
            && !objectName.Contains("board")
            && !objectName.Contains("badge")
            && !objectName.Contains("particle");
    }

    private static bool RendererLooksLikeGorillaOrCosmetic(string objectName, string rendererName)
    {
        return rendererName.Contains("gorilla") || rendererName.Contains("monkey") || rendererName.Contains("monke") ||
               rendererName.Contains("body") || rendererName.Contains("head") || rendererName.Contains("chest") ||
               rendererName.Contains("hand") || rendererName.Contains("arm") || rendererName.Contains("fur") ||
               rendererName.Contains("skin") || rendererName.Contains("face") || rendererName.Contains("eye") ||
               rendererName.Contains("mouth") || rendererName.Contains("leg") || rendererName.Contains("foot") ||
               rendererName.Contains("cosmetic") ||
               objectName.Contains("gorilla") || objectName.Contains("monkey") || objectName.Contains("monke") ||
               objectName.Contains("body") || objectName.Contains("head") || objectName.Contains("chest") ||
               objectName.Contains("hand") || objectName.Contains("arm") || objectName.Contains("fur") ||
               objectName.Contains("skin") || objectName.Contains("face") || objectName.Contains("eye") ||
               objectName.Contains("mouth") || objectName.Contains("leg") || objectName.Contains("foot") ||
               objectName.Contains("cosmetic") || objectName.Contains("hat") || objectName.Contains("glasses") ||
               objectName.Contains("mask") || objectName.Contains("shirt") || objectName.Contains("holdable") ||
               objectName.Contains("item");
    }

    private static bool RendererLooksLikeDefaultOrHelperRig(string objectName, string rendererName)
    {
        return rendererName.Contains("default") || rendererName.Contains("prototype") || rendererName.Contains("temp") ||
               rendererName.Contains("test") || rendererName.Contains("dev") || rendererName.Contains("debug") ||
               rendererName.Contains("editor") || rendererName.Contains("tpose") ||
               objectName.Contains("/default") || objectName.Contains("default/") ||
               objectName.Contains("/prototype") || objectName.Contains("prototype/") ||
               objectName.Contains("/temp") || objectName.Contains("temp/") ||
               objectName.Contains("/test") || objectName.Contains("test/") ||
               objectName.Contains("/dev") || objectName.Contains("dev/") ||
               objectName.Contains("/editor") || objectName.Contains("editor/");
    }

    private static bool RendererLooksLikeUnwantedBlackMarker(Renderer sourceRenderer, string objectName)
    {
        if (objectName.Contains("face") || objectName.Contains("eye") || objectName.Contains("mouth"))
            return false;

        Vector3 size = sourceRenderer.bounds.size;
        bool isTiny = size.x < 0.12f && size.y < 0.12f && size.z < 0.12f;
        Material[] materials = sourceRenderer.sharedMaterials;
        if (!isTiny || materials == null || materials.Length == 0)
            return false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            Color color = material.HasProperty("_Color") ? material.color : Color.white;
            if (color.r > 0.08f || color.g > 0.08f || color.b > 0.08f)
                return false;
        }

        return true;
    }

    private static string GetFullObjectPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
