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
    private int playerModelBuildVersion;
    private bool playerModelPreviewBuildInProgress;
    private Vector3 playerModelPreviewMeshOriginOffset;
    private const int PlayerModelPreviewRendererLimit = 28;

    private static readonly Vector3 PlayerModelPreviewLocalPosition = new Vector3(0f, -0.01f, -0.03f);
    private static readonly Vector3 PlayerModelPreviewLocalEuler = new Vector3(0f, 180f, 0f);
    private static readonly Vector3 PlayerModelPreviewLocalScale = new Vector3(0.09f, 0.09f, 0.09f);

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

            string name = child.name ?? "";
            if (name.IndexOf("MonkeBase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Box", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Border", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Silhouette", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                child.gameObject.SetActive(false);
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
        if (playerModelPreviewObj == null || playerModelPreviewBindings.Count == 0)
            return;

        if (playerModelPreviewSourceRig == null || !playerModelPreviewSourceRig.gameObject.activeInHierarchy)
        {
            DestroyPlayerModelPreview();
            return;
        }

        UpdatePlayerModelPreviewBindings(true);
        KeepPreviewPoseAnchored();
    }

    private void KeepPreviewPoseAnchored()
    {
        if (playerModelPreviewObj == null)
            return;

        Transform t = playerModelPreviewObj.transform;
        t.localPosition = PlayerModelPreviewLocalPosition;

        t.localRotation = Quaternion.Euler(PlayerModelPreviewLocalEuler);
        t.localScale = PlayerModelPreviewLocalScale;
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
            KeepPreviewPoseAnchored();
            SetCheckerMonkeSilhouetteVisible(false);
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

        Vector3 feetCenter = new Vector3(combined.Value.center.x, combined.Value.min.y, combined.Value.center.z);
        playerModelPreviewMeshOriginOffset = playerModelPreviewObj.transform.InverseTransformPoint(feetCenter);

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
