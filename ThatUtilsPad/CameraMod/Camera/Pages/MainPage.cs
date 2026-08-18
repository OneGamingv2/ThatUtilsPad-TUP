using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CameraMod.Camera.Pages
{
    public class MainPage
    {
        public GameObject GO;
        public TMP_Text FOVText;
        public TMP_Text SmoothText;
        public TMP_Text NearClipText;
        public TMP_Text ModeHintText;
        public TMP_Text ZoomLabel;
        public TMP_Text SmoothLabel;
        public TMP_Text NearLabel;
        public TMP_Text SettingTitle;
        public Transform RecDot;
        public RawImage PreviewImage;

        public MainPage(Transform rootT)
        {
            GO = rootT != null ? rootT.gameObject : null;
            if (rootT == null)
                return;

            // Procedural / Pokruk paths
            FOVText = FindTmp(rootT, "Canvas/FovValueText", "FovValueText");
            SmoothText = FindTmp(rootT, "Canvas/SmoothingValueText", "SmoothingValueText");
            NearClipText = FindTmp(rootT, "Canvas/NearClipValueText", "NearClipValueText");
            ModeHintText = FindTmp(rootT, "Canvas/ModeHint", "ModeHint");
            ZoomLabel = FindTmp(rootT, "Canvas/FovText", "FovText");
            SmoothLabel = FindTmp(rootT, "Canvas/SmoothingText", "SmoothingText");
            NearLabel = FindTmp(rootT, "Canvas/NearClipText", "NearClipText");
            RecDot = rootT.Find("RecDot");

            // Bundle tablet (CameraMod): ExtSettings/Value shows the active setting read-out
            if (FOVText == null)
            {
                Transform ext = CameraTabletAssets.FindDeep(rootT, "ExtSettings") ?? rootT;
                FOVText = FindTmp(ext, "Value");
                SettingTitle = FindTmp(ext, "Title");
                SmoothText = FOVText;
                NearClipText = FOVText;
            }

            Transform preview = rootT.Find("CameraScreen/Preview")
                                ?? rootT.Find("CameraScreen")
                                ?? CameraTabletAssets.FindDeep(rootT, "Preview")
                                ?? CameraTabletAssets.FindDeep(rootT, "MainView");
            if (preview != null)
            {
                PreviewImage = preview.GetComponent<RawImage>()
                               ?? preview.GetComponentInChildren<RawImage>(true);
            }

            if (ZoomLabel != null) ZoomLabel.text = "ZOOM";
            if (SmoothLabel != null) SmoothLabel.text = "SMOOTH";
            if (NearLabel != null) NearLabel.text = "NEAR";
        }

        private static TMP_Text FindTmp(Transform root, params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Transform t = root.Find(paths[i]);
                if (t == null)
                    continue;
                TMP_Text tmp = t.GetComponent<TMP_Text>() ?? t.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null)
                    return tmp;
            }

            string last = paths[paths.Length - 1];
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Equals(last, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null)
                    return tmp;
            }

            return null;
        }
    }
}
