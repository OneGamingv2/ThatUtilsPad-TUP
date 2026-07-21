using UnityEngine;

namespace ThatUtilsPad;

public static class ComponentExtensions
{
    public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null)
            component = obj.AddComponent<T>();
        return component;
    }
}
