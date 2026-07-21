using GorillaLocomotion;
using UnityEngine;

namespace ThatUtilsPad;

public class NotificationFollower : MonoBehaviour
{
    private static readonly Quaternion NotificationRotation = Quaternion.Euler(0f, -90f, 0f);
    public Vector3 LocalPosition;

    private void LateUpdate()
    {
        Transform head = GTPlayer.Instance?.headCollider?.transform;
        if (head == null)
            return;

        Transform body = GTPlayer.Instance?.bodyCollider?.transform;
        float yaw = body != null ? body.eulerAngles.y : head.eulerAngles.y;
        Quaternion anchorRotation = Quaternion.Euler(0f, yaw, 0f);

        transform.position = head.position + (anchorRotation * LocalPosition);
        transform.rotation = anchorRotation * NotificationRotation;
    }
}
