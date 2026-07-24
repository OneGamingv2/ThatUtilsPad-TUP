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
            if (controller == null || tabletT == null || InputManager.instance == null || rightHandT == null || col == null)
                return;

            if (!col.name.Contains("Right"))
                return;

            if (InputManager.instance.RightGrip && controller.cameraMode != CameraMode.FirstPersonView)
            {
                tabletT.parent = rightHandT;
                if (controller.cameraMode == CameraMode.FollowPlayer)
                    controller.cameraMode = CameraMode.None;
            }

            if (!InputManager.instance.RightGrip && tabletT.parent == rightHandT)
                tabletT.parent = null;
        }

        private void Update()
        {
            if (controller == null || tabletT == null || InputManager.instance == null || rightHandT == null)
                return;
            if (controller.cameraMode == CameraMode.FirstPersonView)
                return;

            float dist = Vector3.Distance(rightHandT.position, transform.position);
            if (InputManager.instance.RightGrip && dist < 0.18f)
            {
                tabletT.parent = rightHandT;
                if (controller.cameraMode == CameraMode.FollowPlayer)
                    controller.cameraMode = CameraMode.None;
            }
            else if (!InputManager.instance.RightGrip && tabletT.parent == rightHandT)
            {
                tabletT.parent = null;
            }
        }
    }
}
