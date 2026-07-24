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
                cap.radius = 0.5f;
                cap.height = 2.2f;
                cap.direction = 1;
            }
            else
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerStay(Collider col)
        {
            if (controller == null || tabletT == null || InputManager.instance == null || leftHandT == null || col == null)
                return;

            if (!col.name.Contains("Left"))
                return;

            if (InputManager.instance.LeftGrip && controller.cameraMode != CameraMode.FirstPersonView)
            {
                tabletT.parent = leftHandT;
                if (controller.cameraMode == CameraMode.FollowPlayer)
                    controller.cameraMode = CameraMode.None;
            }

            if (!InputManager.instance.LeftGrip && tabletT.parent == leftHandT)
                tabletT.parent = null;
        }

        private void Update()
        {
            if (controller == null || tabletT == null || InputManager.instance == null || leftHandT == null)
                return;
            if (controller.cameraMode == CameraMode.FirstPersonView)
                return;

            float dist = Vector3.Distance(leftHandT.position, transform.position);
            if (InputManager.instance.LeftGrip && dist < 0.18f)
            {
                tabletT.parent = leftHandT;
                if (controller.cameraMode == CameraMode.FollowPlayer)
                    controller.cameraMode = CameraMode.None;
            }
            else if (!InputManager.instance.LeftGrip && tabletT.parent == leftHandT)
            {
                tabletT.parent = null;
            }
        }
    }
}
