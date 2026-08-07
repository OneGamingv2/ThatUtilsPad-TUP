using System.Collections.Generic;
using UnityEngine;

namespace CameraMod.Camera.AppearanceFeatures
{
    public class AppearancesLoader
    {
        private static readonly Dictionary<string, GameObject> appearancePrefabs = new Dictionary<string, GameObject>();
        private static bool isLoaded;

        private static void Load()
        {
            if (isLoaded) return;
            appearancePrefabs["Default"] = ProceduralGui.BuildAppearance("Default", new Color(0.035f, 0.035f, 0.055f));
            appearancePrefabs["Purple"] = ProceduralGui.BuildAppearance("Purple", new Color(0.22f, 0.14f, 0.32f));
            foreach (GameObject prefab in appearancePrefabs.Values)
                prefab.SetActive(false);
            isLoaded = true;
        }

        public static bool IsValidAppearanceName(string name)
        {
            if (!isLoaded) Load();
            return appearancePrefabs.ContainsKey(name);
        }

        public static Appearance InstantiateAppearance(string name)
        {
            if (!isLoaded) Load();
            if (!appearancePrefabs.TryGetValue(name, out GameObject prefab))
                prefab = appearancePrefabs["Default"];

            GameObject instance = Object.Instantiate(prefab);
            instance.SetActive(true);
            instance.transform.localScale = Vector3.one;
            string resolved = appearancePrefabs.ContainsKey(name) ? name : "Default";
            return new Appearance(instance, resolved);
        }
    }
}
