using System;
using ThatUtilsPad.MenuComponents;
using UnityEngine;

namespace CameraMod.Button
{
    public abstract class BaseButton : MonoBehaviour
    {
        public Action onClick;

        public BaseButton OnClick(Action onClickDelegate)
        {
            onClick = onClickDelegate;
            return this;
        }

        public bool isHand(Collider col)
        {
            if (col == null)
                return false;

            if (col.GetComponent<ButtonPresser>() != null || col.GetComponentInParent<ButtonPresser>() != null)
                return true;

            string n = col.name ?? "";
            if (n == "RightHandTriggerCollider" || n == "LeftHandTriggerCollider")
                return true;

            bool hasHand = n.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasTrigger = n.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Finger", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasHand && hasTrigger;
        }

        public bool isLeft(Collider col)
        {
            if (col == null)
                return false;

            ButtonPresser presser = col.GetComponent<ButtonPresser>() ?? col.GetComponentInParent<ButtonPresser>();
            if (presser != null)
                return presser.isLeft;

            string n = col.name ?? "";
            return n.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Vibration(bool isLeftHand)
        {
            if (GorillaTagger.Instance == null)
                return;
            GorillaTagger.Instance.StartVibration(
                isLeftHand,
                GorillaTagger.Instance.tagHapticStrength / 2f,
                GorillaTagger.Instance.tagHapticDuration / 4f);
        }

        protected virtual void Start()
        {
            gameObject.layer = 18;
        }
    }
}
