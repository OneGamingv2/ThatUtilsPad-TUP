using ThatUtilsPad;
using TMPro;
using UnityEngine;

namespace CameraMod.Camera
{
    public static class ProceduralGui
    {
        private static Material baseMat;
        private static TMP_FontAsset cachedFont;

        private static readonly Color Body = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color Bezel = new Color(0.14f, 0.15f, 0.18f);
        private static readonly Color Accent = new Color(0.25f, 0.78f, 0.82f);
        private static readonly Color ModeBtn = new Color(0.17f, 0.22f, 0.30f);
        private static readonly Color Plus = new Color(0.20f, 0.58f, 0.38f);
        private static readonly Color Minus = new Color(0.62f, 0.26f, 0.28f);
        private static readonly Color Danger = new Color(0.58f, 0.28f, 0.30f);
        private static readonly Color Mute = new Color(0.26f, 0.27f, 0.32f);
        private static readonly Color Label = new Color(0.78f, 0.82f, 0.88f);
        private static readonly Color Value = new Color(1f, 1f, 1f);

        private const float BodyW = 0.34f;
        private const float BodyH = 0.48f;
        private const float BodyD = 0.024f;

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
                new Vector3(0f, BodyH * 0.445f, -(BodyD * 0.56f)),
                new Vector3(BodyW * 0.78f, 0.0035f, 0.002f),
                Accent, false);

            GameObject camGo = new GameObject("Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.08f, -0.02f);
            camGo.transform.localRotation = Quaternion.identity;
            UnityEngine.Camera cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.depth = 25f;
            cam.enabled = true;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            RenderTexture rt = new RenderTexture(960, 720, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();
            cam.targetTexture = rt;

            CreateHandle(root.transform, "LeftGrabCol", new Vector3(-(BodyW * 0.5f + 0.055f), 0f, 0f));
            CreateHandle(root.transform, "RightGrabCol", new Vector3(BodyW * 0.5f + 0.055f, 0f, 0f));

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

            CreateHandle(offset, "Handle", new Vector3(-(BodyW * 0.5f + 0.055f), 0f, 0f), bodyColor * 0.65f, keepCollider: false);
            CreateHandle(offset, "Handle2", new Vector3(BodyW * 0.5f + 0.055f, 0f, 0f), bodyColor * 0.65f, keepCollider: false);

            Transform owner = new GameObject("IsOwner").transform;
            owner.SetParent(offset, false);
            owner.localPosition = new Vector3(0f, BodyH * 0.52f, -0.02f);
            owner.gameObject.SetActive(false);
            CreateBox(owner, "AdminLabelBg", Vector3.zero, new Vector3(0.12f, 0.03f, 0.006f), new Color(0.9f, 0.75f, 0.2f), false);
            CreateWorldLabel(owner, "Text", "OWNER", Vector3.zero, 0.028f, Color.black, TextAlignmentOptions.Center);

            return root;
        }

        private static void BuildMainPage(Transform page, RenderTexture preview)
        {
            Transform canvas = CreatePage(page, "Canvas", true);

            CreateBox(page, "ViewfinderFrame",
                new Vector3(0f, 0.145f, -0.0155f),
                new Vector3(0.292f, 0.178f, 0.002f),
                new Color(0.04f, 0.04f, 0.05f), false);

            GameObject screen = CreateBox(page, "CameraScreen",
                new Vector3(0f, 0.145f, -0.0165f),
                new Vector3(0.274f, 0.162f, 0.0015f),
                Color.white, false);
            ApplyPreviewMaterial(screen, preview);

            CreateBox(page, "RecDot",
                new Vector3(-0.118f, 0.210f, -0.017f),
                new Vector3(0.012f, 0.012f, 0.002f),
                new Color(0.92f, 0.18f, 0.20f), false);

            CreateWorldLabel(canvas, "Title", "CAMERA", new Vector3(0f, 0.232f, -0.018f), 0.032f, Color.white, TextAlignmentOptions.Center);
            CreateWorldLabel(canvas, "ModeHint", "Hold side grips  ·  A to summon", new Vector3(0f, 0.040f, -0.018f), 0.016f, Label, TextAlignmentOptions.Center);

            BuildValueRow(page, canvas, "Fov", "FOV", "90", -0.005f);
            BuildValueRow(page, canvas, "Smoothing", "SMOOTH", "0.07", -0.048f);
            BuildValueRow(page, canvas, "NearClip", "NEAR CLIP", "0.05", -0.091f);

            RenameChild(canvas, "FovValue", "FovValueText");
            RenameChild(canvas, "SmoothingValue", "SmoothingValueText");
            RenameChild(canvas, "NearClipValue", "NearClipValueText");
            RenameChild(canvas, "FovLabel", "FovText");
            RenameChild(canvas, "SmoothingLabel", "SmoothingText");
            RenameChild(canvas, "NearClipLabel", "NearClipText");

            CreateFaceButton(page, "FPVButton", new Vector3(-0.100f, -0.140f, -0.017f), new Vector2(0.096f, 0.042f), "FPV", ModeBtn);
            CreateFaceButton(page, "TPVButton", new Vector3(0f, -0.140f, -0.017f), new Vector2(0.096f, 0.042f), "3RD", ModeBtn);
            CreateFaceButton(page, "FPButton", new Vector3(0.100f, -0.140f, -0.017f), new Vector2(0.096f, 0.042f), "FOLLOW", ModeBtn);

            CreateFaceButton(page, "FlipCamButton", new Vector3(-0.100f, -0.188f, -0.017f), new Vector2(0.096f, 0.038f), "FLIP", Danger);
            CreateFaceButton(page, "RollLock", new Vector3(0f, -0.188f, -0.017f), new Vector2(0.096f, 0.038f), "ROLL", new Color(0.42f, 0.42f, 0.28f));
            CreateFaceButton(page, "HideHeadCosmetics", new Vector3(0.100f, -0.188f, -0.017f), new Vector2(0.096f, 0.038f), "HIDE HAT", new Color(0.44f, 0.30f, 0.54f));

            CreateFaceButton(page, "MiscButton", new Vector3(0f, -0.232f, -0.017f), new Vector2(0.220f, 0.036f), "SETTINGS", Mute);
        }

        private static void BuildValueRow(Transform page, Transform canvas, string id, string label, string value, float y)
        {
            CreateWorldLabel(canvas, id + "Label", label, new Vector3(-0.132f, y, -0.018f), 0.018f, Label, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, id + "Value", value, new Vector3(-0.018f, y, -0.018f), 0.020f, Value, TextAlignmentOptions.Left);

            string upName = id == "Fov" ? "FovUP"
                : id == "Smoothing" ? "SmoothingUpButton"
                : "NearClipUp";
            string downName = id == "Fov" ? "FovDown"
                : id == "Smoothing" ? "SmoothingDownButton"
                : "NearClipDown";

            CreateFaceButton(page, upName, new Vector3(0.078f, y, -0.017f), new Vector2(0.048f, 0.032f), "+", Plus);
            CreateFaceButton(page, downName, new Vector3(0.132f, y, -0.017f), new Vector2(0.048f, 0.032f), "-", Minus);
        }

        private static void BuildMiscPage(Transform page)
        {
            Transform canvas = CreatePage(page, "Canvas", true);

            CreateWorldLabel(canvas, "FPSettingsText", "FOLLOW SETTINGS", new Vector3(0f, 0.210f, -0.018f), 0.026f, Color.white, TextAlignmentOptions.Center);
            CreateWorldLabel(canvas, "MinDistText", "MIN DIST", new Vector3(-0.132f, 0.145f, -0.018f), 0.017f, Label, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "MinDistValueText", "2", new Vector3(-0.010f, 0.145f, -0.018f), 0.020f, Value, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "SpeedText", "SPEED", new Vector3(-0.132f, 0.095f, -0.018f), 0.017f, Label, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "SpeedValueText", "0.01", new Vector3(-0.010f, 0.095f, -0.018f), 0.020f, Value, TextAlignmentOptions.Left);

            CreateWorldLabel(canvas, "TPSettingsText", "THIRD PERSON", new Vector3(0f, 0.035f, -0.018f), 0.024f, Color.white, TextAlignmentOptions.Center);
            CreateWorldLabel(canvas, "TPposition", "SIDE", new Vector3(-0.132f, -0.020f, -0.018f), 0.017f, Label, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "TPText", "Back", new Vector3(-0.010f, -0.020f, -0.018f), 0.020f, Value, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "TPRot", "HEAD ROT", new Vector3(-0.132f, -0.070f, -0.018f), 0.017f, Label, TextAlignmentOptions.Left);
            CreateWorldLabel(canvas, "TPRotText", "TRUE", new Vector3(0.020f, -0.070f, -0.018f), 0.020f, Value, TextAlignmentOptions.Left);

            CreateFaceButton(page, "MinDistUpButton", new Vector3(0.078f, 0.145f, -0.017f), new Vector2(0.048f, 0.032f), "+", Plus);
            CreateFaceButton(page, "MinDistDownButton", new Vector3(0.132f, 0.145f, -0.017f), new Vector2(0.048f, 0.032f), "-", Minus);
            CreateFaceButton(page, "SpeedUpButton", new Vector3(0.078f, 0.095f, -0.017f), new Vector2(0.048f, 0.032f), "+", Plus);
            CreateFaceButton(page, "SpeedDownButton", new Vector3(0.132f, 0.095f, -0.017f), new Vector2(0.048f, 0.032f), "-", Minus);
            CreateFaceButton(page, "TPModeUpButton", new Vector3(0.078f, -0.020f, -0.017f), new Vector2(0.048f, 0.032f), "+", Plus);
            CreateFaceButton(page, "TPModeDownButton", new Vector3(0.132f, -0.020f, -0.017f), new Vector2(0.048f, 0.032f), "-", Minus);
            CreateFaceButton(page, "TPRotButton", new Vector3(0.105f, -0.070f, -0.017f), new Vector2(0.078f, 0.032f), "TOGGLE", new Color(0.42f, 0.42f, 0.28f));

            CreateFaceButton(page, "GreenScreenButton", new Vector3(-0.078f, -0.145f, -0.017f), new Vector2(0.128f, 0.040f), "GREEN SCREEN", new Color(0.16f, 0.55f, 0.28f));
            CreateFaceButton(page, "BackButton", new Vector3(0.078f, -0.145f, -0.017f), new Vector2(0.128f, 0.040f), "BACK", Danger);

            Transform skins = new GameObject("Skins").transform;
            skins.SetParent(page, false);
            CreateFaceButton(skins, "Default", new Vector3(-0.078f, -0.200f, -0.017f), new Vector2(0.128f, 0.038f), "DARK", Mute);
            CreateFaceButton(skins, "Purple", new Vector3(0.078f, -0.200f, -0.017f), new Vector2(0.128f, 0.038f), "TEAL", Accent);
        }

        private static void ApplyPreviewMaterial(GameObject screen, RenderTexture preview)
        {
            Material screenMat = CreateUniqueMat(Color.white);
            Shader texShader = Shader.Find("Unlit/Texture")
                               ?? Shader.Find("Universal Render Pipeline/Unlit")
                               ?? Shader.Find("GUI/Text Shader")
                               ?? (baseMat != null ? baseMat.shader : Shader.Find("Sprites/Default"));
            screenMat.shader = texShader;
            if (screenMat.HasProperty("_MainTex"))
                screenMat.mainTexture = preview;
            if (screenMat.HasProperty("_BaseMap"))
                screenMat.SetTexture("_BaseMap", preview);
            MeshRenderer rend = screen.GetComponent<MeshRenderer>();
            if (rend != null)
                rend.sharedMaterial = screenMat;
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
            go.transform.localScale = new Vector3(0.060f, 0.095f, 0.060f);

            Collider[] cols = go.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                Object.DestroyImmediate(cols[i]);

            if (keepCollider)
            {
                CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
                cap.isTrigger = true;
                cap.direction = 1;
                cap.height = 2.6f;
                cap.radius = 0.75f;
                cap.center = Vector3.zero;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            go.layer = 18;

            MeshRenderer rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
                rend.sharedMaterial = CreateUniqueMat(color ?? new Color(0.22f, 0.55f, 0.58f, 0.95f));
        }

        private static GameObject CreateFaceButton(Transform parent, string name, Vector3 pos, Vector2 size, string label, Color color)
        {
            GameObject go = CreateBox(parent, name, pos, new Vector3(size.x, size.y, 0.018f), color, true);
            go.layer = 18;

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.35f, 1.45f, 4.5f);
            box.center = new Vector3(0f, 0f, -1.2f);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (!string.IsNullOrEmpty(label))
            {
                float worldText = label.Length > 8 ? 0.016f : 0.020f;
                TextMeshPro tmp = CreateWorldLabel(go.transform, "Text", label, new Vector3(0f, 0f, -0.62f), worldText, Color.white, TextAlignmentOptions.Center);
                tmp.transform.localScale = new Vector3(
                    worldText / Mathf.Max(0.001f, size.x),
                    worldText / Mathf.Max(0.001f, size.y),
                    worldText / Mathf.Max(0.001f, 0.018f));
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

        private static TextMeshPro CreateWorldLabel(Transform parent, string name, string text, Vector3 pos, float worldSize, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, worldSize);

            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 8f;
            tmp.alignment = align;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(18f, 2.4f);
            tmp.sortingOrder = 20;
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
    }
}
