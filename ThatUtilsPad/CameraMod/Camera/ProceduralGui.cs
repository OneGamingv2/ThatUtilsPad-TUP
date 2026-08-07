using ThatUtilsPad;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace CameraMod.Camera
{
    public static class ProceduralGui
    {
        private static Material baseMat;
        private static TMP_FontAsset cachedFont;

        private static Color Body => ToColor(MenuTheme.Current.Main);
        private static Color Bezel => ToColor(MenuTheme.Current.LightMain);
        private static Color Accent => ToColor(MenuTheme.Current.Accent);
        private static Color Row => ToColor(MenuTheme.Current.Button);
        private static Color Btn => ToColor(MenuTheme.Current.ButtonLight);
        private static Color Danger => new Color(0.72f, 0.28f, 0.38f);
        private static Color Plus => new Color(0.32f, 0.62f, 0.42f);
        private static Color Minus => new Color(0.68f, 0.30f, 0.36f);
        private static Color Muted => ToColor(MenuTheme.Current.DarkAccent);
        private static readonly Color Label = new Color(0.86f, 0.84f, 0.92f);
        private static readonly Color Value = Color.white;

        private const float BodyW = 0.36f;
        private const float BodyH = 0.50f;
        private const float BodyD = 0.026f;
        private const float FaceZ = -0.015f;

        public static GameObject BuildCameraTablet()
        {
            EnsureMaterials();

            GameObject root = new GameObject("CameraTablet");

            CreateBox(root.transform, "FakeCamera",
                Vector3.zero, new Vector3(BodyW, BodyH, BodyD), Body, false);

            CreateBox(root.transform, "FacePlate",
                new Vector3(0f, 0f, -(BodyD * 0.52f)),
                new Vector3(BodyW * 0.94f, BodyH * 0.94f, 0.0035f),
                Bezel, false);

            CreateBox(root.transform, "AccentRail",
                new Vector3(0f, BodyH * 0.455f, -(BodyD * 0.58f)),
                new Vector3(BodyW * 0.72f, 0.003f, 0.002f),
                Accent, false);

            GameObject camGo = new GameObject("Camera");
            camGo.transform.SetParent(root.transform, false);
            // Sit just behind the faceplate, looking out the tablet front (-Z).
            camGo.transform.localPosition = new Vector3(0f, 0.09f, -0.06f);
            camGo.transform.localRotation = Quaternion.identity;
            UnityEngine.Camera cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.depth = -50f;
            cam.enabled = true;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            cam.useOcclusionCulling = false;

            RenderTexture rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                name = "TUP_CameraPreview"
            };
            rt.Create();
            cam.targetTexture = rt;

            CreateHandle(root.transform, "LeftGrabCol", new Vector3(-(BodyW * 0.5f + 0.052f), 0f, 0f));
            CreateHandle(root.transform, "RightGrabCol", new Vector3(BodyW * 0.5f + 0.052f, 0f, 0f));

            Transform mainPage = CreatePage(root.transform, "MainPage", true);
            Transform miscPage = CreatePage(root.transform, "MiscPage", false);
            BuildMainPage(mainPage, rt);
            BuildMiscPage(miscPage);

            return root;
        }

        public static GameObject BuildColorScreen()
        {
            EnsureMaterials();
            GameObject root = new GameObject("ColorScreen");

            for (int i = 1; i <= 3; i++)
            {
                CreateBox(root.transform, "Screen" + i,
                    new Vector3((i - 2) * 1.15f, 1.35f, 0f),
                    new Vector3(1.05f, 2.1f, 0.04f),
                    Color.green, false);
            }

            Transform stuff = new GameObject("Stuff").transform;
            stuff.SetParent(root.transform, false);
            stuff.localPosition = new Vector3(0f, 0.15f, -0.08f);

            CreateFaceButton(stuff, "RedButton", new Vector3(-0.32f, 0f, 0f), new Vector2(0.24f, 0.10f), "RED", new Color(0.85f, 0.2f, 0.2f));
            CreateFaceButton(stuff, "GreenButton", new Vector3(0f, 0f, 0f), new Vector2(0.24f, 0.10f), "GREEN", new Color(0.2f, 0.75f, 0.3f));
            CreateFaceButton(stuff, "BlueButton", new Vector3(0.32f, 0f, 0f), new Vector2(0.24f, 0.10f), "BLUE", new Color(0.25f, 0.45f, 0.9f));
            return root;
        }

        public static GameObject BuildAppearance(string name, Color bodyColor)
        {
            EnsureMaterials();
            GameObject root = new GameObject(name);
            Transform offset = new GameObject("Offset").transform;
            offset.SetParent(root.transform, false);

            CreateBox(offset, "Tablet",
                new Vector3(0f, 0f, 0.016f),
                new Vector3(BodyW + 0.014f, BodyH + 0.014f, BodyD + 0.008f),
                bodyColor, false);

            CreateHandle(offset, "Handle", new Vector3(-(BodyW * 0.5f + 0.052f), 0f, 0f), bodyColor * 0.65f, keepCollider: false);
            CreateHandle(offset, "Handle2", new Vector3(BodyW * 0.5f + 0.052f, 0f, 0f), bodyColor * 0.65f, keepCollider: false);

            Transform owner = new GameObject("IsOwner").transform;
            owner.SetParent(offset, false);
            owner.localPosition = new Vector3(0f, BodyH * 0.52f, -0.02f);
            owner.gameObject.SetActive(false);
            CreateBox(owner, "AdminLabelBg", Vector3.zero, new Vector3(0.12f, 0.03f, 0.006f), Accent, false);
            CreateFlatLabel(owner, "Text", "OWNER", Vector3.zero, 0.022f, Color.black, TextAlignmentOptions.Center);

            return root;
        }

        private static void BuildMainPage(Transform page, RenderTexture preview)
        {
            Transform canvas = CreatePage(page, "Canvas", true);

            CreateBox(page, "ViewfinderFrame",
                new Vector3(0f, 0.155f, FaceZ + 0.001f),
                new Vector3(0.300f, 0.168f, 0.002f),
                new Color(0.04f, 0.04f, 0.06f), false);

            CreatePreviewScreen(page, preview);

            CreateBox(page, "RecDot",
                new Vector3(-0.122f, 0.214f, FaceZ - 0.001f),
                new Vector3(0.010f, 0.010f, 0.002f),
                new Color(0.92f, 0.22f, 0.28f), false);

            CreateFlatLabel(canvas, "Title", "CAMERA", new Vector3(0f, 0.238f, FaceZ - 0.002f), 0.026f, Color.white, TextAlignmentOptions.Center);
            CreateFlatLabel(canvas, "ModeHint", "Grip sides  ·  A to recall", new Vector3(0f, 0.055f, FaceZ - 0.002f), 0.013f, Label, TextAlignmentOptions.Center);

            BuildValueRow(page, canvas, "Fov", "FOV", "90", 0.018f);
            BuildValueRow(page, canvas, "Smoothing", "SMOOTH", "0.07", -0.022f);
            BuildValueRow(page, canvas, "NearClip", "NEAR", "0.05", -0.062f);

            RenameChild(canvas, "FovValue", "FovValueText");
            RenameChild(canvas, "SmoothingValue", "SmoothingValueText");
            RenameChild(canvas, "NearClipValue", "NearClipValueText");
            RenameChild(canvas, "FovLabel", "FovText");
            RenameChild(canvas, "SmoothingLabel", "SmoothingText");
            RenameChild(canvas, "NearClipLabel", "NearClipText");

            CreateFaceButton(page, "FPVButton", new Vector3(-0.098f, -0.112f, FaceZ), new Vector2(0.092f, 0.038f), "FPV", Btn);
            CreateFaceButton(page, "TPVButton", new Vector3(0f, -0.112f, FaceZ), new Vector2(0.092f, 0.038f), "3RD", Btn);
            CreateFaceButton(page, "FPButton", new Vector3(0.098f, -0.112f, FaceZ), new Vector2(0.092f, 0.038f), "FOLLOW", Btn);

            CreateFaceButton(page, "FlipCamButton", new Vector3(-0.098f, -0.160f, FaceZ), new Vector2(0.092f, 0.036f), "FLIP", Danger);
            CreateFaceButton(page, "RollLock", new Vector3(0f, -0.160f, FaceZ), new Vector2(0.092f, 0.036f), "ROLL", Muted);
            CreateFaceButton(page, "HideHeadCosmetics", new Vector3(0.098f, -0.160f, FaceZ), new Vector2(0.092f, 0.036f), "HIDE", Muted);

            CreateFaceButton(page, "MiscButton", new Vector3(0f, -0.210f, FaceZ), new Vector2(0.220f, 0.036f), "SETTINGS", Accent * 0.55f + Btn * 0.45f);
        }

        private static void BuildValueRow(Transform page, Transform canvas, string id, string label, string value, float y)
        {
            CreateBox(page, id + "RowBg",
                new Vector3(0f, y, FaceZ + 0.0015f),
                new Vector3(0.300f, 0.034f, 0.0015f),
                Row, false);

            CreateFlatLabel(canvas, id + "Label", label, new Vector3(-0.122f, y, FaceZ - 0.002f), 0.014f, Label, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, id + "Value", value, new Vector3(-0.018f, y, FaceZ - 0.002f), 0.015f, Value, TextAlignmentOptions.Left);

            string upName = id == "Fov" ? "FovUP"
                : id == "Smoothing" ? "SmoothingUpButton"
                : "NearClipUp";
            string downName = id == "Fov" ? "FovDown"
                : id == "Smoothing" ? "SmoothingDownButton"
                : "NearClipDown";

            CreateFaceButton(page, downName, new Vector3(0.072f, y, FaceZ), new Vector2(0.040f, 0.028f), "-", Minus);
            CreateFaceButton(page, upName, new Vector3(0.120f, y, FaceZ), new Vector2(0.040f, 0.028f), "+", Plus);
        }

        private static void BuildMiscPage(Transform page)
        {
            Transform canvas = CreatePage(page, "Canvas", true);

            CreateFlatLabel(canvas, "FPSettingsText", "FOLLOW", new Vector3(0f, 0.215f, FaceZ - 0.002f), 0.022f, Color.white, TextAlignmentOptions.Center);

            CreateBox(page, "FollowRowBg",
                new Vector3(0f, 0.145f, FaceZ + 0.0015f),
                new Vector3(0.300f, 0.078f, 0.0015f),
                Row, false);

            CreateFlatLabel(canvas, "MinDistText", "MIN DIST", new Vector3(-0.122f, 0.165f, FaceZ - 0.002f), 0.013f, Label, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "MinDistValueText", "2", new Vector3(-0.010f, 0.165f, FaceZ - 0.002f), 0.015f, Value, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "SpeedText", "SPEED", new Vector3(-0.122f, 0.125f, FaceZ - 0.002f), 0.013f, Label, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "SpeedValueText", "0.01", new Vector3(-0.010f, 0.125f, FaceZ - 0.002f), 0.015f, Value, TextAlignmentOptions.Left);

            CreateFaceButton(page, "MinDistDownButton", new Vector3(0.072f, 0.165f, FaceZ), new Vector2(0.040f, 0.028f), "-", Minus);
            CreateFaceButton(page, "MinDistUpButton", new Vector3(0.120f, 0.165f, FaceZ), new Vector2(0.040f, 0.028f), "+", Plus);
            CreateFaceButton(page, "SpeedDownButton", new Vector3(0.072f, 0.125f, FaceZ), new Vector2(0.040f, 0.028f), "-", Minus);
            CreateFaceButton(page, "SpeedUpButton", new Vector3(0.120f, 0.125f, FaceZ), new Vector2(0.040f, 0.028f), "+", Plus);

            CreateFlatLabel(canvas, "TPSettingsText", "THIRD PERSON", new Vector3(0f, 0.055f, FaceZ - 0.002f), 0.020f, Color.white, TextAlignmentOptions.Center);

            CreateBox(page, "TpRowBg",
                new Vector3(0f, -0.010f, FaceZ + 0.0015f),
                new Vector3(0.300f, 0.078f, 0.0015f),
                Row, false);

            CreateFlatLabel(canvas, "TPposition", "SIDE", new Vector3(-0.122f, 0.010f, FaceZ - 0.002f), 0.013f, Label, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "TPText", "Back", new Vector3(-0.010f, 0.010f, FaceZ - 0.002f), 0.015f, Value, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "TPRot", "HEAD ROT", new Vector3(-0.122f, -0.030f, FaceZ - 0.002f), 0.013f, Label, TextAlignmentOptions.Left);
            CreateFlatLabel(canvas, "TPRotText", "TRUE", new Vector3(0.010f, -0.030f, FaceZ - 0.002f), 0.015f, Value, TextAlignmentOptions.Left);

            CreateFaceButton(page, "TPModeDownButton", new Vector3(0.072f, 0.010f, FaceZ), new Vector2(0.040f, 0.028f), "-", Minus);
            CreateFaceButton(page, "TPModeUpButton", new Vector3(0.120f, 0.010f, FaceZ), new Vector2(0.040f, 0.028f), "+", Plus);
            CreateFaceButton(page, "TPRotButton", new Vector3(0.096f, -0.030f, FaceZ), new Vector2(0.072f, 0.028f), "TOGGLE", Muted);

            CreateFaceButton(page, "GreenScreenButton", new Vector3(-0.078f, -0.120f, FaceZ), new Vector2(0.128f, 0.038f), "GREEN SCREEN", Plus);
            CreateFaceButton(page, "BackButton", new Vector3(0.078f, -0.120f, FaceZ), new Vector2(0.128f, 0.038f), "BACK", Danger);

            Transform skins = new GameObject("Skins").transform;
            skins.SetParent(page, false);
            CreateFaceButton(skins, "Default", new Vector3(-0.078f, -0.175f, FaceZ), new Vector2(0.128f, 0.034f), "DARK", Btn);
            CreateFaceButton(skins, "Purple", new Vector3(0.078f, -0.175f, FaceZ), new Vector2(0.128f, 0.034f), "ACCENT", Accent * 0.65f + Btn * 0.35f);
        }

        private static void CreatePreviewScreen(Transform page, RenderTexture preview)
        {
            GameObject canvasGo = new GameObject("CameraScreen");
            canvasGo.transform.SetParent(page, false);
            // Face the player on the tablet front (-Z).
            canvasGo.transform.localPosition = new Vector3(0f, 0.155f, FaceZ - 0.001f);
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            canvasGo.transform.localScale = Vector3.one;

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 80;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(284f, 152f);
            // Negative Y corrects the common URP RenderTexture upside-down preview.
            canvasRect.localScale = new Vector3(0.001f, -0.001f, 0.001f);

            GameObject imageGo = new GameObject("Preview");
            imageGo.transform.SetParent(canvasGo.transform, false);
            RawImage raw = imageGo.AddComponent<RawImage>();
            raw.texture = preview;
            raw.color = Color.white;
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 1f, 1f);

            RectTransform imageRect = imageGo.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
        }

        private static void RenameChild(Transform parent, string from, string to)
        {
            Transform child = parent.Find(from);
            if (child != null)
                child.name = to;
        }

        private static Transform CreatePage(Transform parent, string name, bool active)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.SetActive(active);
            return go.transform;
        }

        private static void CreateHandle(Transform parent, string name, Vector3 pos, Color? color = null, bool keepCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            go.transform.localScale = new Vector3(0.052f, 0.090f, 0.052f);

            Collider[] cols = go.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                Object.DestroyImmediate(cols[i]);

            if (keepCollider)
            {
                CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
                cap.isTrigger = true;
                cap.direction = 1;
                cap.height = 2.5f;
                cap.radius = 0.7f;
                cap.center = Vector3.zero;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            go.layer = 18;

            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
                rend.sharedMaterial = CreateUniqueMat(color ?? (Accent * 0.55f + Btn * 0.45f));
        }

        private static GameObject CreateFaceButton(Transform parent, string name, Vector3 pos, Vector2 size, string label, Color color)
        {
            GameObject go = CreateBox(parent, name, pos, new Vector3(size.x, size.y, 0.016f), color, true);
            go.layer = 18;

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.4f, 1.5f, 5.0f);
            box.center = new Vector3(0f, 0f, -1.4f);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (!string.IsNullOrEmpty(label))
            {
                TextMeshPro tmp = CreateFlatLabel(
                    go.transform,
                    "Text",
                    label,
                    new Vector3(0f, 0f, -0.65f),
                    1f,
                    Color.white,
                    TextAlignmentOptions.Center);

                float sx = Mathf.Max(0.001f, size.x);
                float sy = Mathf.Max(0.001f, size.y);
                float target = label.Length >= 10 ? 0.0135f : 0.0165f;
                tmp.transform.localScale = new Vector3(target / sx, target / sy, target / 0.016f);
                tmp.fontSize = 10f;
                tmp.rectTransform.sizeDelta = new Vector2(20f, 3f);
            }

            return go;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;

            Collider[] existing = go.GetComponents<Collider>();
            for (int i = 0; i < existing.Length; i++)
                Object.DestroyImmediate(existing[i]);

            if (keepCollider)
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one;
            }

            go.layer = 18;

            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
                rend.sharedMaterial = CreateUniqueMat(color);
            return go;
        }

        private static TextMeshPro CreateFlatLabel(Transform parent, string name, string text, Vector3 pos, float worldSize, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Mathf.Max(0.008f, worldSize);

            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 7.5f;
            tmp.alignment = align;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(16f, 2.2f);
            tmp.sortingOrder = 40;
            ApplyFigtree(tmp);
            return tmp;
        }

        private static void ApplyFigtree(TMP_Text text)
        {
            try
            {
                if (cachedFont == null && FontCache.figtreeBundle != null)
                    cachedFont = FontCache.figtreeBundle.LoadAsset<TMP_FontAsset>("assets/fonts/figtree.asset");
                if (cachedFont != null)
                    text.font = cachedFont;
            }
            catch
            {
            }
        }

        private static void EnsureMaterials()
        {
            if (baseMat != null)
                return;

            Shader shader = ShaderCache.UberShader
                            ?? Shader.Find("GorillaTag/UberShader")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Diffuse");
            baseMat = new Material(shader);
        }

        private static Material CreateUniqueMat(Color color)
        {
            EnsureMaterials();
            Material mat = new Material(baseMat);
            if (mat.HasProperty("_Color"))
                mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            return mat;
        }

        private static Color ToColor(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }
}
