using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.NVIDIA;

namespace ThatUtilsPad.MenuComponents;

public class ButtonTrigger : GorillaPressableButton
{
    public string BtnIdentifier;

    public static void PcPress(ButtonTrigger button)
    {
        if (button == null) return;
        
        Debug.Log("ButtonActivationPC");

        if (Mods.Actions.TryGetValue(button.BtnIdentifier, out Action? action))
            action.Invoke();
        else
            Debug.LogWarning($"[TUP: WARNING] No mod found for button: {button.BtnIdentifier}");
    }
    
    public override void ButtonActivationWithHand(bool isLeftHand)
    {
        base.ButtonActivationWithHand(isLeftHand);

        if (isLeftHand)
            return;

        Debug.Log("ButtonActivationWithHand");
        
        if (Mods.Actions.TryGetValue(BtnIdentifier, out Action? action))
            action.Invoke();
        else
            Debug.LogWarning($"[TUP: WARNING] No mod found for button: {BtnIdentifier}");
    }
}

public static class MenuEffects
{
    public static IEnumerator SpawnHitCircle(Vector3 position, Transform hit)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.02f;
        
        GameObject.Destroy(sphere.GetComponent<Collider>());
        GameObject.Destroy(sphere.GetComponent<Rigidbody>());

        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        Color startColor = new Color32(255, 255, 255, 200);
        renderer.material.color = startColor;

        float duration = 0.4f;
        float time = 0f;

        Vector3 startScale = Vector3.one * 0.02f;
        Vector3 endScale = Vector3.one * 0.055f;

        while (time < duration)
        {
            float t = time / duration;
            float easeT = 1f - Mathf.Pow(1f - t, 3);
            
            sphere.transform.localScale = Vector3.Lerp(startScale, endScale, easeT);
            
            Color c = startColor;
            c.a = Mathf.Lerp(1f, 0f, easeT);
            renderer.material.color = c;

            time += Time.deltaTime;
            yield return null;
        }

        GameObject.Destroy(sphere);
    }
}

public class FollowMenu : MonoBehaviour
{
    public Transform  Target;
    public Vector3    Position;
    public Quaternion Rotation;

    private void LateUpdate()
    {
        if (Target == null) return;

        transform.position = Target.TransformPoint(Position);
        transform.rotation = Target.rotation * Rotation;
    }
}

public static class MenuTheme
{
    private static void ApplyColor(Transform parent, string name, Color32 color)
    {
        Transform t = parent.Find(name);

        if (t == null) return;

        Renderer? rend = t.GetComponent<Renderer>();

        if (rend == null) return;

        rend.material.shader = ShaderCache.UberShader;
        rend.material.color  = color;
    }

    public static void Assign(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 accentColor)
    {
        Transform root = menuObj.transform;
        ApplyColor(root, "Main",       mainColor);
        ApplyColor(root, "MainBorder", borderColor);
        ApplyColor(root, "GripPipe",   mainColor);
        ApplyColor(root, "GripAccent", accentColor);
    }

    public static void AssignSakura(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 accentColor)
    {
        // Base colors
        Assign(menuObj, mainColor, borderColor, accentColor);

        Transform root = menuObj.transform;

        // Extra sakura parts
        ApplyColor(root, "Pole1",        mainColor);
        ApplyColor(root, "Pole2",        mainColor);
        ApplyColor(root, "PipeTop1",     borderColor);
        ApplyColor(root, "PipeTop2",     borderColor);
        ApplyColor(root, "UnderBar",     borderColor);
        ApplyColor(root, "BarConnector", mainColor);
        ApplyColor(root, "TopBarUnder",  mainColor);
        ApplyColor(root, "TopBar",       accentColor);
    }
}