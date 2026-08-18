using CameraMod.Camera;
using UnityEngine;

namespace CameraMod.Button.Buttons
{
    public class HoldableButton : BaseButton
    {
        private float lastClicked;
        private float lastHoldTick;
        private float holdTickInterval = 0.16f;
        public float clickMinInterval = 0.22f;
        public float holdDurationThreshold = 0.35f;
        private float entered;
        private int collidersCount;

        private bool isHolding => collidersCount > 0 && Time.time - entered > holdDurationThreshold;

        private void OnTriggerEnter(Collider col)
        {
            CameraController controller = CameraController.Instance;
            if (controller == null || !controller.IsReady || controller.ButtonsTimeouted)
                return;
            if (!isHand(col))
                return;

            collidersCount++;
            entered = Time.time;
            float cooldown = controller != null
                ? Mathf.Max(clickMinInterval, controller.ButtonCooldownSeconds)
                : clickMinInterval;
            if (Time.time - lastClicked < cooldown)
                return;

            Vibration(isLeft(col));
            onClick?.Invoke();
            lastClicked = Time.time;
            controller.NotifyButtonPressed();
        }

        private void OnTriggerStay(Collider col)
        {
            CameraController controller = CameraController.Instance;
            if (controller == null || !controller.IsReady || controller.ButtonsTimeouted)
                return;
            if (!isHand(col) || !isHolding)
                return;
            if (Time.time - lastHoldTick <= holdTickInterval)
                return;

            onClick?.Invoke();
            Vibration(isLeft(col));
            lastHoldTick = Time.time;
            controller.NotifyButtonPressed();
        }

        private void OnTriggerExit(Collider col)
        {
            if (!isHand(col))
                return;
            collidersCount = Mathf.Max(0, collidersCount - 1);
        }
    }
}
