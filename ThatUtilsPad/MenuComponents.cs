using UnityEngine;

namespace ThatUtilsPad
{
    public class ButtonTrigger : GorillaPressableButton
    {
        public string btnIdentifier;

        public override void ButtonActivationWithHand(bool isLeftHand)
        {
            base.ButtonActivationWithHand(isLeftHand);

            if (!isLeftHand)
            {
                if (Mods.Actions.TryGetValue(btnIdentifier, out var action))
                {
                    action.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[TUP: WARNING] No mod found for button: {btnIdentifier}");
                }
            }
        }
    }

    public class FollowMenu : MonoBehaviour
    {
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;

        void LateUpdate()
        {
            if (target == null) return;

            transform.position = target.TransformPoint(position);
            transform.rotation = target.rotation * rotation;
        }
    }

    public static class MenuTheme
    {
        static void ApplyColor(Transform parent, string name, Color32 color)
        {
            Transform t = parent.Find(name);
            if (t == null) return;

            var rend = t.GetComponent<Renderer>();
            if (rend == null) return;

            rend.material.shader = Shader.Find("GorillaTag/UberShader");
            rend.material.color = color;
        }

        public static void Assign(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 accentColor)
        {
            Transform root = menuObj.transform;
            ApplyColor(root, "Main", mainColor);
            ApplyColor(root, "MainBorder", borderColor);
            ApplyColor(root, "GripPipe", mainColor);
            ApplyColor(root, "GripAccent", accentColor);
        }

        public static void AssignSakura(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 accentColor)
        {
            // Base colors
            Assign(menuObj, mainColor, borderColor, accentColor);

            Transform root = menuObj.transform;

            // Extra sakura parts
            ApplyColor(root, "Pole1", mainColor);
            ApplyColor(root, "Pole2", mainColor);
            ApplyColor(root, "PipeTop1", borderColor);
            ApplyColor(root, "PipeTop2", borderColor);
            ApplyColor(root, "UnderBar", borderColor);
            ApplyColor(root, "BarConnector", mainColor);
            ApplyColor(root, "TopBarUnder", mainColor);
            ApplyColor(root, "TopBar", accentColor);
        }
    }
}