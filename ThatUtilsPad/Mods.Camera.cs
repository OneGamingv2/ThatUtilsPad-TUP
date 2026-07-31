using CameraMod.Camera;
using UnityEngine;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static bool EnsureCameraReady(out CameraController controller)
    {
        CameraMod.Camera.Patches.StartPatch.EnsureStarted();
        controller = CameraController.Instance;
        if (controller == null)
        {
            ShowNotification("Camera not ready yet", NotificationDefaultDuration);
            return false;
        }

        if (!controller.IsReady)
        {
            controller.Init();
            ShowNotification("Camera still loading...", NotificationDefaultDuration);
            return false;
        }

        return true;
    }

    private static void ToggleCameraTablet()
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;

        Transform tablet = controller.cameraTabletT;
        if (tablet == null)
        {
            ShowNotification("Camera tablet missing", NotificationDefaultDuration);
            return;
        }

        bool next = !tablet.gameObject.activeSelf || controller.cameraMode != CameraMode.None;
        if (next)
        {
            tablet.gameObject.SetActive(true);
            controller.BringTabletToPlayer();
            ShowNotification("Camera tablet ready", NotificationDefaultDuration);
        }
        else
        {
            tablet.gameObject.SetActive(false);
            ShowNotification("Camera hidden", NotificationDefaultDuration);
        }
    }

    private static void CameraEnableFpv()
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        controller.EnableFPV();
        ShowNotification("First person camera", NotificationDefaultDuration);
    }

    private static void CameraEnableTpv()
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        controller.EnableTPV();
        ShowNotification("Third person camera", NotificationDefaultDuration);
    }

    private static void CameraToggleFollow()
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        bool wasFollow = controller.cameraMode == CameraMode.FollowPlayer;
        controller.EnableFollow();
        ShowNotification(wasFollow ? "Follow off" : "Follow player", NotificationDefaultDuration);
    }

    private static void CameraFlip()
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        controller.Flip();
        ShowNotification("Camera flipped", NotificationDefaultDuration);
    }

    private static void CameraChangeFov(float delta)
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        controller.ChangeFov(delta);
    }

    private static void CameraChangeSmoothing(float delta)
    {
        if (!EnsureCameraReady(out CameraController controller))
            return;
        controller.ChangeSmoothing(delta);
    }
}
