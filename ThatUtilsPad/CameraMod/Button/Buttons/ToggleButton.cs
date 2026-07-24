using System;
using CameraMod.Camera;
using UnityEngine;

namespace CameraMod.Button.Buttons
{
    public class ToggleButton : BaseButton
    {
        private Vector3 upPosition;
        private Vector3 downPosition;
        private Func<bool> getter;
        private Action<bool> setter;
        private bool savable;
        private string saveKey;
        private Material material;
        private float lastClicked;
        public float clickMinInterval = 0.1f;

        public Color enabledColor = new Color(1f, 0.9f, 0.9f);
        public Color disabledColor = new Color(0.9f, 1f, 0.9f);

        public ToggleButton InitToggleButton(Action<bool> setter, Func<bool> getter, bool savable = true, string overrideSaveName = null)
        {
            Renderer rend = GetComponent<Renderer>();
            if (rend != null)
                material = rend.material;

            upPosition = transform.localPosition;
            downPosition = transform.localPosition + new Vector3(0.02f, 0f, 0f);

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

        private void UpdateAppearance(bool isDown)
        {
            if (material != null)
                material.color = isDown ? enabledColor : disabledColor;
            transform.localPosition = isDown ? downPosition : upPosition;
        }

        private void OnTriggerEnter(Collider col)
        {
            CameraController controller = CameraController.Instance;
            if (controller == null || !controller.IsReady || controller.ButtonsTimeouted)
                return;
            if (!isHand(col))
                return;
            if (Time.time - lastClicked <= clickMinInterval)
                return;

            Vibration(isLeft(col));
            IsDown = !IsDown;
            onClick?.Invoke();
            lastClicked = Time.time;
        }
    }
}
