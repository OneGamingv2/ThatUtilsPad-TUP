using System;
using CameraMod.Camera;
using ThatUtilsPad.MenuComponents;
using UnityEngine;

namespace CameraMod.Button.Buttons
{
    public class ToggleButton : BaseButton
    {
        private Func<bool> getter;
        private Action<bool> setter;
        private bool savable;
        private string saveKey;
        private Material material;
        private Color baseColor = Color.white;
        private float lastClicked;
        public float clickMinInterval = 0.28f;

        public ToggleButton InitToggleButton(Action<bool> setter, Func<bool> getter, bool savable = true, string overrideSaveName = null)
        {
            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
            {
                material = rend.material;
                baseColor = material.color;
            }

            this.getter = getter;
            this.setter = setter;
            this.savable = savable;
            saveKey = overrideSaveName ?? name;

            if (savable)
            {
                int save = PlayerPrefs.GetInt(saveKey, -1);
                if (save != -1)
                    this.setter?.Invoke(save == 1);
            }

            UpdateAppearance(this.getter != null && this.getter());
            return this;
        }

        public bool IsDown
        {
            get => getter != null && getter();
            set
            {
                setter?.Invoke(value);
                UpdateAppearance(value);
                if (savable)
                    PlayerPrefs.SetInt(saveKey, value ? 1 : 0);
            }
        }

        public void RefreshAppearance()
        {
            UpdateAppearance(getter != null && getter());
        }

        private void UpdateAppearance(bool isDown)
        {
            if (material == null)
                return;

            Color accent = MenuTheme.Current.Accent;
            material.color = isDown ? Color.Lerp(baseColor, accent, 0.82f) : baseColor;
        }

        private void OnTriggerEnter(Collider col)
        {
            CameraController controller = CameraController.Instance;
            if (controller == null || !controller.IsReady || controller.ButtonsTimeouted)
                return;
            if (!isHand(col))
                return;
            float cooldown = controller != null
                ? Mathf.Max(clickMinInterval, controller.ButtonCooldownSeconds)
                : clickMinInterval;
            if (Time.time - lastClicked <= cooldown)
                return;

            Vibration(isLeft(col));
            IsDown = !IsDown;
            onClick?.Invoke();
            lastClicked = Time.time;
            controller.NotifyButtonPressed();
        }
    }
}
