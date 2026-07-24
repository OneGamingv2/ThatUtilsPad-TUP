using TMPro;
using UnityEngine;

namespace CameraMod.Camera.Pages
{
    public class MainPage
    {
        public GameObject GO;
        public TextMeshPro FOVText;
        public TextMeshPro SmoothText;
        public TextMeshPro NearClipText;

        public MainPage(Transform rootT)
        {
            GO = rootT != null ? rootT.gameObject : null;
            if (rootT == null)
                return;

            FOVText = FindTmp(rootT, "Canvas/FovValueText", "FovValueText", "FovValue");
            SmoothText = FindTmp(rootT, "Canvas/SmoothingValueText", "SmoothingValueText", "SmoothingValue");
            NearClipText = FindTmp(rootT, "Canvas/NearClipValueText", "NearClipValueText", "NearClipValue");
        }

        private static TextMeshPro FindTmp(Transform root, params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Transform t = root.Find(paths[i]);
                if (t == null)
                    continue;
                TextMeshPro tmp = t.GetComponent<TextMeshPro>() ?? t.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null)
                    return tmp;
            }

            string last = paths[paths.Length - 1];
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Equals(last, System.StringComparison.OrdinalIgnoreCase) ||
                    t.name.IndexOf(last, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TextMeshPro tmp = t.GetComponent<TextMeshPro>();
                    if (tmp != null)
                        return tmp;
                }
            }

            return null;
        }
    }
}
