using System;
using CameraMod.Camera.Comps;
using UnityEngine;

namespace CameraMod.Button {
    public abstract class BaseButton : MonoBehaviour {
        public bool isHand(Collider col) => col.name == "RightHandTriggerCollider" || col.name == "LeftHandTriggerCollider";
        public bool isLeft(Collider col) => col.name == "LeftHandTriggerCollider";
        
        public Action onClick;
        public BaseButton OnClick(Action onClickDelegate) {
            onClick = onClickDelegate;
            return this;
        }
        
        public void Vibration(bool isLeftHand) {
            GorillaTagger.Instance.StartVibration(isLeftHand, GorillaTagger.Instance.tagHapticStrength / 2f, GorillaTagger.Instance.tagHapticDuration / 4f);
        }
        
        protected virtual void Start() {
            gameObject.layer = 18;
        }
    }
}