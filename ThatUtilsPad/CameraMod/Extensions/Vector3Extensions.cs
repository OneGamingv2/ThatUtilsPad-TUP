using UnityEngine;

namespace CameraMod {
    public static class Vector3Extensions {
        public static Vector3 Scaled(this Vector3 vec1, Vector3 vec2) {
            return Vector3.Scale(vec1, vec2);
        }

        public static Quaternion ToQuaternion(this Vector3 vec) {
            return Quaternion.Euler(vec);
        }
    }
}
