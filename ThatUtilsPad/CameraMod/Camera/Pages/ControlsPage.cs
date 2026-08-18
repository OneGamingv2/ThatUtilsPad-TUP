using TMPro;
using UnityEngine;

namespace CameraMod.Camera.Pages
{
    public class ControlsPage
    {
        public GameObject GO;
        public TextMeshPro BindValueText;
        public TextMeshPro KeepGuiStatusText;
        public TextMeshPro SmoothStatusText;

        public ControlsPage(Transform t)
        {
            GO = t != null ? t.gameObject : null;
            if (t == null)
                return;

            BindValueText = FindTmp(t, "Canvas/BindValueText", "BindValueText");
            KeepGuiStatusText = FindTmp(t, "Canvas/KeepGuiStatusText", "KeepGuiStatusText");
            SmoothStatusText = FindTmp(t, "Canvas/SmoothStatusText", "SmoothStatusText");
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
