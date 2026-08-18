using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CameraMod.Camera
{
    /// <summary>
    /// Loads the custom ThatUtilsPad camera tablet AssetBundle (tup-cameramod).
    /// Prefab root: CameraMod. Falls back to null so callers can use ProceduralGui.
    /// </summary>
    public static class CameraTabletAssets
    {
        public const string ResourceName = "ThatUtilsPad.Assets.Camera.tup-cameramod";
        public const string PrefabName = "CameraMod";

        private static AssetBundle _bundle;
        private static GameObject _prefab;
        private static bool _tried;

        public static bool IsBundleTablet(Transform tablet)
        {
            return tablet != null && tablet.name.IndexOf("CameraMod", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static GameObject TryInstantiateTablet()
        {
            EnsureLoaded();
            if (_prefab == null)
                return null;

            GameObject go = UnityEngine.Object.Instantiate(_prefab);
            go.name = "CameraMod";
            return go;
        }

        /// <summary>
        /// Bundle has no Camera child — create one + RT preview under MainView.
        /// Fixes the opaque black SettingsExpand / MainView Image covering the feed.
        /// </summary>
        public static UnityEngine.Camera EnsurePreviewCamera(Transform tabletRoot)
        {
            if (tabletRoot == null)
                return null;

            Transform camT = tabletRoot.Find("Camera");
            UnityEngine.Camera cam;
            if (camT == null)
            {
                GameObject camGo = new GameObject("Camera");
                camGo.transform.SetParent(tabletRoot, false);
                // Sit just in front of the screen bezel, looking out into the world.
                camGo.transform.localPosition = new Vector3(0f, 0.02f, -0.08f);
                camGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                cam = camGo.AddComponent<UnityEngine.Camera>();
            }
            else
            {
                cam = camT.GetComponent<UnityEngine.Camera>() ?? camT.gameObject.AddComponent<UnityEngine.Camera>();
            }

            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.depth = -50f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.useOcclusionCulling = false;
            cam.cullingMask = ~(1 << 18);
            cam.enabled = true; // always render through-lens on this tablet

            RenderTexture rt = cam.targetTexture;
            if (rt == null)
            {
                rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                    name = "TUP_CameraPreview_Bundle"
                };
                rt.Create();
                cam.targetTexture = rt;
            }

            PrepBundleScreen(tabletRoot);
            BindPreviewTargets(tabletRoot, rt);
            EnsureGrabColliders(tabletRoot);
            return cam;
        }

        /// <summary>
        /// Clear opaque black overlays: MainView plate, SettingsExpand menu, stray Images.
        /// Also keeps ExtSettings collapsed so a full black settings page can't cover the feed.
        /// </summary>
        public static void PrepBundleScreen(Transform tabletRoot)
        {
            if (tabletRoot == null)
                return;

            // Kill any leftover procedural skin boxes parented under the asset tablet.
            for (int i = tabletRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = tabletRoot.GetChild(i);
                string n = child.name ?? "";
                if (n.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    || n.Equals("Purple", StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            Transform mainView = FindDeep(tabletRoot, "MainView");
            if (mainView != null)
            {
                var bg = mainView.GetComponent<Image>();
                if (bg != null)
                {
                    Color c = bg.color;
                    c.a = 0f;
                    bg.color = c;
                    bg.raycastTarget = false;
                }
            }

            // SettingsExpand was a full opaque black rectangle (the "black old GUI").
            Transform expand = FindDeep(tabletRoot, "SettingsExpand");
            if (expand != null)
            {
                var img = expand.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = 0f;
                    img.color = c;
                    img.enabled = false;
                }
                // Keep a collider for the expand poke if present; don't paint black.
                foreach (var g in expand.GetComponentsInChildren<Graphic>(true))
                {
                    if (g == null) continue;
                    if (g is TextMeshProUGUI) continue;
                    Color c = g.color;
                    if (c.a > 0.5f && c.r < 0.15f && c.g < 0.15f && c.b < 0.15f)
                    {
                        c.a = 0f;
                        g.color = c;
                    }
                }
            }

            // ExtSettings full page stays collapsed until user expands.
            Transform ext = FindDeep(tabletRoot, "ExtSettings");
            if (ext != null)
                ext.gameObject.SetActive(false);

            // Neutralize any other near-black opaque Images on Main that aren't the Dynamic Island.
            Transform main = tabletRoot.Find("Main") ?? FindDeep(tabletRoot, "Main");
            if (main != null)
            {
                foreach (var img in main.GetComponentsInChildren<Image>(true))
                {
                    if (img == null) continue;
                    string path = img.gameObject.name ?? "";
                    if (path.Equals("DynamicIsland", StringComparison.OrdinalIgnoreCase)
                        || (img.transform.parent != null
                            && img.transform.parent.name.Equals("DynamicIsland", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    // Button icons (NextArrow etc.) keep some opacity; kill flat black panels.
                    Color c = img.color;
                    bool nearBlack = c.r < 0.12f && c.g < 0.12f && c.b < 0.18f && c.a > 0.4f;
                    if (nearBlack && (img.sprite == null || path.IndexOf("Expand", StringComparison.OrdinalIgnoreCase) >= 0
                                      || path.Equals("MainView", StringComparison.OrdinalIgnoreCase)
                                      || path.Equals("SettingsExpand", StringComparison.OrdinalIgnoreCase)
                                      || path.Equals("Image", StringComparison.OrdinalIgnoreCase)))
                    {
                        c.a = 0f;
                        img.color = c;
                    }
                }
            }

            Transform mainScreen = tabletRoot.Find("MainScreen");
            if (mainScreen != null)
            {
                var mr = mainScreen.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.enabled = false;
            }
        }

        public static void BindPreviewTargets(Transform tabletRoot, Texture texture)
        {
            if (tabletRoot == null || texture == null)
                return;

            PrepBundleScreen(tabletRoot);

            Transform mainView = FindDeep(tabletRoot, "MainView");
            if (mainView == null)
                return;

            RawImage raw = null;
            Transform previewT = mainView.Find("Preview");
            if (previewT != null)
                raw = previewT.GetComponent<RawImage>();

            if (raw == null)
            {
                // Create viewfinder UNDER SettingsHolder / DynamicIsland (first sibling).
                GameObject preview = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                preview.layer = 18;
                RectTransform prt = preview.GetComponent<RectTransform>();
                prt.SetParent(mainView, false);
                prt.SetAsFirstSibling();
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.zero;
                prt.sizeDelta = Vector2.zero;
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;
                prt.localScale = Vector3.one;
                prt.localRotation = Quaternion.identity;
                raw = preview.GetComponent<RawImage>();
            }
            else
            {
                raw.rectTransform.SetAsFirstSibling();
                raw.rectTransform.anchorMin = Vector2.zero;
                raw.rectTransform.anchorMax = Vector2.one;
                raw.rectTransform.offsetMin = Vector2.zero;
                raw.rectTransform.offsetMax = Vector2.zero;
            }

            raw.texture = texture;
            raw.color = Color.white;
            raw.raycastTarget = false;
            // Prefers the RT under the button strip, not covering grips / HUD.
            if (raw.transform.parent == mainView)
                raw.transform.SetAsFirstSibling();
        }

        public static void EnsureGrabColliders(Transform tabletRoot)
        {
            WireGrip(tabletRoot, "GripMainL", left: true);
            WireGrip(tabletRoot, "GripMainR", left: false);
            WireGrip(tabletRoot, "GripAccentL", left: true);
            WireGrip(tabletRoot, "GripAccentR", left: false);
            WireGrip(tabletRoot, "GripConnectL", left: true);
            WireGrip(tabletRoot, "GripConnectR", left: false);
        }

        private static void WireGrip(Transform root, string name, bool left)
        {
            Transform grip = root.Find(name);
            if (grip == null)
                return;

            Collider col = grip.GetComponent<Collider>() ?? grip.GetComponentInChildren<Collider>(true);
            if (col == null)
            {
                BoxCollider box = grip.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.04f, 0.12f, 0.05f);
                col = box;
            }
            else
            {
                col.isTrigger = true;
            }

            grip.gameObject.layer = 18;

            if (left)
            {
                if (grip.GetComponent<Comps.LeftGrabTrigger>() == null)
                    grip.gameObject.AddComponent<Comps.LeftGrabTrigger>();
            }
            else
            {
                if (grip.GetComponent<Comps.RightGrabTrigger>() == null)
                    grip.gameObject.AddComponent<Comps.RightGrabTrigger>();
            }
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        /// <summary>Prefer the button's Collider child (this prefab's poke targets).</summary>
        public static Transform ResolveButtonHit(Transform tabletRoot, string relativePath)
        {
            if (tabletRoot == null || string.IsNullOrEmpty(relativePath))
                return null;

            Transform t = tabletRoot.Find(relativePath);
            if (t == null)
                t = FindDeep(tabletRoot, relativePath.Contains("/")
                    ? relativePath.Substring(relativePath.LastIndexOf('/') + 1)
                    : relativePath);
            if (t == null)
                return null;

            Transform hit = t.Find("Collider");
            if (hit == null)
            {
                Collider c = t.GetComponentInChildren<Collider>(true);
                hit = c != null ? c.transform : t;
            }

            hit.gameObject.layer = 18;
            Collider col = hit.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
            else
            {
                BoxCollider box = hit.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.08f, 0.04f, 0.02f);
            }

            return hit;
        }

        private static void EnsureLoaded()
        {
            if (_tried)
                return;
            _tried = true;

            try
            {
                Assembly asm = typeof(CameraTabletAssets).Assembly;
                using Stream stream = asm.GetManifestResourceStream(ResourceName);
                if (stream == null)
                {
                    Debug.LogWarning("[TUP Camera] Embedded bundle missing: " + ResourceName);
                    return;
                }

                byte[] buf = new byte[stream.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = stream.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                _bundle = AssetBundle.LoadFromMemory(buf);
                if (_bundle == null)
                {
                    Debug.LogWarning("[TUP Camera] AssetBundle.LoadFromMemory failed for tup-cameramod");
                    return;
                }

                _prefab = _bundle.LoadAsset<GameObject>(PrefabName);
                if (_prefab == null)
                {
                    // Some Unity versions need the full container path.
                    _prefab = _bundle.LoadAsset<GameObject>("assets/prefabs/cameramod.prefab");
                }
                if (_prefab == null)
                {
                    string[] names = _bundle.GetAllAssetNames();
                    Debug.LogWarning("[TUP Camera] CameraMod prefab not in bundle. Assets: " +
                                     (names != null ? string.Join(", ", names) : "(none)"));
                }
                else
                {
                    Debug.Log("[TUP Camera] Loaded tup-cameramod AssetBundle (CameraMod prefab).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TUP Camera] Bundle load failed: " + ex.Message);
            }
        }
    }
}
