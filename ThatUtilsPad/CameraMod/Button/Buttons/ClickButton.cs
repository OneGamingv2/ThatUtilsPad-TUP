using System;
using CameraMod.Camera;
using UnityEngine;

namespace CameraMod.Button.Buttons
{
    public class ClickButton : BaseButton
    {
        private float lastClicked;
        public float clickMinInterval = 0.1f;

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
            onClick?.Invoke();
            lastClicked = Time.time;
        }
    }
}
