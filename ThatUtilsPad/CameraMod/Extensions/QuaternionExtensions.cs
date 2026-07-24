

using UnityEngine;

namespace CameraMod {
    public static class QuaternionExtensions {
        public static Quaternion Lerped(this Quaternion quat, Quaternion to, float a) {
            return Quaternion.Lerp(quat, to, a);
        }
    }
}
