using UnityEngine;

#pragma warning disable CS0618
namespace CameraMod.Camera.Comps
{
    internal class LeftGrabTrigger : MonoBehaviour
    {
        public static LeftGrabTrigger instance;

        public Transform leftHandT => GorillaTagger.Instance != null ? GorillaTagger.Instance.leftHandTransform : null;
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
            if (controller == null || tabletT == null || InputManager.instance == null || leftHandT == null)
                return;
            if (controller.cameraMode == CameraMode.FirstPersonView)
                return;

            float dist = Vector3.Distance(leftHandT.position, transform.position);
            bool gripping = InputManager.instance.LeftGrip && dist < 0.22f;

            if (gripping)
            {
                if (!wasHolding)
                    controller.BeginHandHold();
                tabletT.parent = leftHandT;
                wasHolding = true;
            }
            else if (wasHolding || tabletT.parent == leftHandT)
            {
                if (tabletT.parent == leftHandT)
                    tabletT.parent = null;
                if (wasHolding)
                    controller.EndHandHold();
                wasHolding = false;
            }
        }
    }
}
