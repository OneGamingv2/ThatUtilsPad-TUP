using TMPro;
using UnityEngine;

namespace CameraMod.Camera.Pages
{
    public class PovPage
    {
        public GameObject GO;
        public TextMeshPro ZoomText;
        public TextMeshPro SmoothText;
        public TextMeshPro NearText;
        public TextMeshPro HeightText;
        public TextMeshPro DistText;
        public TextMeshPro SideText;
        public TextMeshPro StableText;

        public PovPage(Transform t)
        {
            GO = t != null ? t.gameObject : null;
            if (t == null)
                return;

            ZoomText = FindTmp(t, "Canvas/ZoomValueText", "ZoomValueText");
            SmoothText = FindTmp(t, "Canvas/PovSmoothValueText", "PovSmoothValueText");
            NearText = FindTmp(t, "Canvas/PovNearValueText", "PovNearValueText");
            HeightText = FindTmp(t, "Canvas/HeightValueText", "HeightValueText");
            DistText = FindTmp(t, "Canvas/DistValueText", "DistValueText");
            SideText = FindTmp(t, "Canvas/SideValueText", "SideValueText");
            StableText = FindTmp(t, "Canvas/StableValueText", "StableValueText");
        }

        private static TextMeshPro FindTmp(Transform root, params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Transform found = root.Find(paths[i]);
                if (found == null)
                    continue;
                TextMeshPro tmp = found.GetComponent<TextMeshPro>() ?? found.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null)
                    return tmp;
            }

            string last = paths[paths.Length - 1];
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.Equals(last, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                TextMeshPro tmp = child.GetComponent<TextMeshPro>();
                if (tmp != null)
                    return tmp;
            }

            return null;
        }
    }
}
