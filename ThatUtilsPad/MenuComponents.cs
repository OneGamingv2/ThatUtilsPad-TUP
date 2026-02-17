using System;
using UnityEngine;

namespace ThatUtilsPad.MenuComponents;

public class ButtonTrigger : GorillaPressableButton
{
    public string BtnIdentifier;

    public static void PcPress(ButtonTrigger button)
    {
        if (button == null) return;

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

        if (Mods.Actions.TryGetValue(BtnIdentifier, out Action? action))
            action.Invoke();
        else
            Debug.LogWarning($"[TUP: WARNING] No mod found for button: {BtnIdentifier}");
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