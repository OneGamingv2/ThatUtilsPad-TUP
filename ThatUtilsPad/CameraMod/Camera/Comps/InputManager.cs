using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace CameraMod.Camera.Comps {
    public class InputManager : MonoBehaviour {
        public static InputManager instance;
        public bool LeftGrip;
        public bool RightGrip;
        public bool RightPrimaryButton;
        public bool LeftPrimaryButton;

        public bool LeftSecondaryButton;
        public bool RightSecondaryButton;

        public Vector2 GPLeftStick;
        public Vector2 GPRightStick;
        private XRNode lHandNode = XRNode.LeftHand;
        private XRNode rHandNode = XRNode.RightHand;

        private void Start() {
            instance = this;
        }

        private void Update() {
            var poller = ControllerInputPoller.instance;
            LeftGrip = poller.leftGrab;
            RightGrip = poller.rightGrab;
            RightPrimaryButton = poller.rightControllerPrimaryButton;
            LeftPrimaryButton = poller.leftControllerPrimaryButton;

            LeftSecondaryButton = poller.leftControllerSecondaryButton;
            RightSecondaryButton = poller.rightControllerSecondaryButton;

            if (Gamepad.current != null) {
                GPLeftStick = Gamepad.current.leftStick.ReadValue();
                GPRightStick = Gamepad.current.rightStick.ReadValue();
            }
        }
    }
}