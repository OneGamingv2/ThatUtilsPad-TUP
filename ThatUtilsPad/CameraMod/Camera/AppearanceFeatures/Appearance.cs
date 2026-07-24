using UnityEngine;

namespace CameraMod.Camera.AppearanceFeatures
{
    public class Appearance
    {
        public GameObject go;
        public Transform transform => go != null ? go.transform : null;

        private GameObject adminLabel;
        public readonly string name;

        public bool isOwner
        {
            get => adminLabel != null && adminLabel.activeSelf;
            set
            {
                if (adminLabel != null)
                    adminLabel.SetActive(value);
            }
        }

        public Appearance(GameObject go, string name)
        {
            this.go = go;
            this.name = name;

            Transform offset = go != null ? go.transform.Find("Offset") : null;
            Transform owner = offset != null ? offset.Find("IsOwner") : null;
            adminLabel = owner != null ? owner.gameObject : null;
        }
    }
}
