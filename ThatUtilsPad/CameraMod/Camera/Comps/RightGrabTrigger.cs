using UnityEngine;

#pragma warning disable CS0618
namespace CameraMod.Camera.Comps
{
    internal class RightGrabTrigger : MonoBehaviour
    {
        public static RightGrabTrigger instance;

        public Transform rightHandT => GorillaTagger.Instance != null ? GorillaTagger.Instance.rightHandTransform : null;
        private CameraController controller => CameraController.Instance;
        private Transform tabletT => controller != null ? controller.cameraTabletT : null;
        private bool wasHolding;

        private void Start()
        {
            instance = this;
            gameObject.layer = 18;
            EnsureTrigger();
        }

        private void EnsureTrigger()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = true;
                cap.radius = 0.55f;
                cap.height = 2.15f;
                cap.direction = 1;
            }
            else
            {
                col.isTrigger = true;
            }
        }

        private void Update()
        {
            if (controller == null || tabletT == null || InputManager.instance == null || rightHandT == null)
                return;
            if (controller.cameraMode == CameraMode.FirstPersonView)
                return;

            float dist = Vector3.Distance(rightHandT.position, transform.position);
            bool gripping = InputManager.instance.RightGrip && dist < 0.22f;

            if (gripping)
            {
                if (!wasHolding)
                    controller.BeginHandHold();
                tabletT.parent = rightHandT;
                wasHolding = true;
            }
            else if (wasHolding || tabletT.parent == rightHandT)
            {
                if (tabletT.parent == rightHandT)
                    tabletT.parent = null;
                if (wasHolding)
                    controller.EndHandHold();
                wasHolding = false;
            }
        }
    }
}
