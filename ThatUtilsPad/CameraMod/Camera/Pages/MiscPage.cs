using TMPro;
using UnityEngine;

namespace CameraMod.Camera.Pages
{
    public class MiscPage
    {
        public GameObject GO;
        public TextMeshPro MinDistText;
        public TextMeshPro SpeedText;
        public TextMeshPro TpText;
        public TextMeshPro TpRotText;

        public MiscPage(Transform t)
        {
            GO = t != null ? t.gameObject : null;
            if (t == null)
                return;

            MinDistText = FindTmp(t, "Canvas/MinDistValueText", "MinDistValueText");
            SpeedText = FindTmp(t, "Canvas/SpeedValueText", "SpeedValueText");
            TpText = FindTmp(t, "Canvas/TPText", "TPText");
            TpRotText = FindTmp(t, "Canvas/TPRotText", "TPRotText");
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
