using System;
using System.Collections;
using System.IO;
using System.Numerics;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.NVIDIA;
using UnityEngine.UIElements;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace ThatUtilsPad.MenuComponents;

public class ButtonTrigger : MonoBehaviour
{
    public string BtnIdentifier;
    public Action? CustomAction;

    public static void PcPress(ButtonTrigger button)
    {
        if (button == null) return;
        Debug.Log("Pressed: " + button.BtnIdentifier);
        
        Debug.Log("ButtonActivationPC");
        Main.Instance.PlayBtnCickSound();
        
        if (button.CustomAction != null)
        {
            button.CustomAction.Invoke();
            return;
        }

        if (Mods.TryGetAction(button.BtnIdentifier, out var action))
            action?.Invoke();
        else
            Debug.LogWarning($"[TUP: WARNING] No mod found for button: {button.BtnIdentifier}");
    }
    
    //public override void ButtonActivationWithHand(bool isLeftHand)
    //{
        //base.ButtonActivationWithHand(isLeftHand);

        //if (isLeftHand)
        //    return;

        //Debug.Log("ButtonActivationWithHand");
        
        //if (CustomAction != null)
        //{
        //    CustomAction.Invoke();
        //     return;
        //}
        
        //if (Mods.TryGetAction(BtnIdentifier, out var action))
        //    action?.Invoke();
        //else
        //    Debug.LogWarning($"[TUP: WARNING] No mod found for button: {BtnIdentifier}");
    //}
}

public class ButtonPresser : MonoBehaviour
{
    public bool isLeft = false;
}

public class ButtonCollider : MonoBehaviour
{
    private static float lastGlobalTime;
    private float lastLocalTime;

    public ButtonTrigger trigger; // reference to your existing script

    private void Awake()
    {
        gameObject.layer = 2; // you already did this 👍

        if (trigger == null)
            trigger = GetComponent<ButtonTrigger>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // cooldown (prevents spam)
        if (Time.time - lastGlobalTime < 0.1f || Time.time - lastLocalTime < 0.2f)
            return;

        ButtonPresser presser = other.GetComponent<ButtonPresser>();
        if (presser == null)
            return;

        if (trigger != null)
        {
            ButtonTrigger.PcPress(trigger);
            GorillaTagger.Instance.StartVibration(presser.isLeft, 0.1f, 0.1f);
        }

        lastGlobalTime = Time.time;
        lastLocalTime = Time.time;
    }
}


public static class Tools
{
    public static Texture2D LoadEmbeddedImage(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ThatUtilsPad.Assets.Icons." + name);

        if (stream == null) return null;
        byte[] imageData = new byte[stream.Length];
        stream.Read(imageData, 0, imageData.Length);
        Texture2D texture = new(2, 2);
        texture.LoadImage(imageData);

        return texture;
    }
    
    public static void StopCoroutine(ref Coroutine routine)
    {
        if (routine != null)
        {
            CoroutineHandler.Instance.StopCoroutine(routine);
            routine = null;
        }
    }
}

public static class MenuEffects
{
    public static IEnumerator PopMenu(GameObject menuObj, Vector3 targetScale, Vector3 startScale, bool useEaseOut)
    {
        float duration = 0.25f;
        float time = 0f;
        
        while (time < duration)
        {
            float t = time / duration;
            float easeT;
            
            if (useEaseOut)
                easeT = 1f - Mathf.Pow(1f - t, 3);   
            else
                easeT = Mathf.Pow(t, 3);
            
            menuObj.transform.localScale = Vector3.Lerp(startScale, targetScale, easeT);

            time += Time.deltaTime;
            yield return null;
        }
        
        menuObj.transform.localScale = targetScale;
        Debug.Log("Menu Size: " + menuObj.transform.localScale);
    }

    public static IEnumerator PopButton(GameObject btnObj, GameObject btnOutline, Vector3 targetScale, Vector3 startScale, bool useEaseOut)
    {
        float duration = 0.5f;
        float time = 0f;
        
        while (time < duration)
        {
            if (btnObj == null || btnOutline == null)
                yield break;
            
            float t = time / duration;
            float easeT;
            
            if (useEaseOut)
                easeT = 1f - Mathf.Pow(1f - t, 3);   
            else
                easeT = Mathf.Pow(t, 3);
            
            btnObj.transform.localScale = Vector3.Lerp(startScale, targetScale, easeT);
            btnOutline.transform.localScale = Vector3.Lerp(startScale, targetScale * 1.001f, easeT);

            time += Time.deltaTime;
            yield return null;
        }
        
        btnObj.transform.localScale = targetScale;
        btnOutline.transform.localScale = targetScale * 1.001f;
    }

    public static IEnumerator SpawnHitCircle(Vector3 position, Transform hit)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.01f;
        
        GameObject.Destroy(sphere.GetComponent<Collider>());
        GameObject.Destroy(sphere.GetComponent<Rigidbody>());

        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        Color startColor = new Color32(255, 255, 255, 200);
        renderer.material.color = startColor;

        float duration = 0.4f;
        float time = 0f;

        Vector3 startScale = Vector3.one * 0.01f;
        Vector3 endScale = Vector3.one * 0.02f;

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

    public static void Assign(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 buttonColor, Color32 accentColor)
    {
        Transform root = menuObj.transform;
        Transform side = menuObj.transform.Find("SideHolder");
        
        ApplyColor(root, "Main",       mainColor);
        ApplyColor(root, "MainBorder", borderColor);
        ApplyColor(root, "GripPipe",   mainColor);
        ApplyColor(root, "GripAccent", accentColor);
        ApplyColor(side, "SideMain", mainColor);
        ApplyColor(side, "SideBorder", borderColor);
        
        ApplyColor(root, "SelectorBorder", borderColor);
        ApplyColor(root, "SelectorMain", mainColor);
        
        ApplyColor(root, "SelectorMainSide", mainColor);
        ApplyColor(root, "SelectorBorderSide", borderColor);
        
        ApplyColor(root, "SelectorBtn1", buttonColor);
        ApplyColor(root, "SelectorBtn2", buttonColor);
        ApplyColor(root, "SelectorBtn3", buttonColor);
        ApplyColor(root, "SelectorBtn4", buttonColor);
        ApplyColor(root, "SelectorBtn5", buttonColor);
        ApplyColor(root, "SelectorBtn6", buttonColor);
        ApplyColor(root, "SelectorBtn7", buttonColor);
        ApplyColor(root, "SelectorBtn8", buttonColor);
        ApplyColor(root, "SelectorBtn9", buttonColor);
        ApplyColor(root, "SelectorBtn10", buttonColor);
        ApplyColor(root, "SelectorBtn11", buttonColor);
        ApplyColor(root, "SelectorBtn12", buttonColor);
        ApplyColor(root, "SelectorBtn13", buttonColor);
        ApplyColor(root, "SelectorBtn14", buttonColor);

        ApplyColor(side, "ReportHateSpeech", buttonColor);
        ApplyColor(side, "ReportCheating", buttonColor);
        ApplyColor(side, "ReportToxicity", buttonColor);
        
        ApplyColor(side, "VolumeUp", buttonColor);
        ApplyColor(side, "VolumeDown", buttonColor);
        ApplyColor(side, "Mute", buttonColor);
        ApplyColor(side, "MuteElse", buttonColor);
        
        ApplyColor(root, "BackPage", buttonColor);
        ApplyColor(root, "NextPage", buttonColor);
        ApplyColor(root, "HomeBtn", buttonColor);
        
        ApplyColor(side, "Mods", buttonColor);
        ApplyColor(side, "Cheats", buttonColor);
        ApplyColor(side, "User", buttonColor);
    }

    public static void AssignSakura(GameObject menuObj, Color32 mainColor, Color32 borderColor, Color32 accentColor)
    {
        Color32 buttonColor = new(30, 30, 46,  255);
        
        // Base colors
        Assign(menuObj, mainColor, borderColor, buttonColor, accentColor);

        Transform root = menuObj.transform;
        Transform side = menuObj.transform.Find("SideHolder");
        
        // Extra sakura parts
        ApplyColor(root, "Pole1",        mainColor);
        ApplyColor(root, "Pole2",        mainColor);
        ApplyColor(root, "PipeTop1",     borderColor);
        ApplyColor(root, "PipeTop2",     borderColor);
        ApplyColor(root, "UnderBar",     borderColor);
        ApplyColor(root, "BarConnector", mainColor);
        ApplyColor(root, "TopBarUnder",  mainColor);
        ApplyColor(root, "TopBar",       accentColor);
        
        ApplyColor(side, "PoleSide1",        mainColor);
        ApplyColor(side, "PoleSide2",        mainColor);
        ApplyColor(side, "PipeTopSide1",     borderColor);
        ApplyColor(side, "PipeTopSide2",     borderColor);
        ApplyColor(side, "UnderBarSide",     borderColor);
        ApplyColor(side, "BarConnectorSide", mainColor);
        ApplyColor(side, "TopBarUnderSide",  mainColor);
        ApplyColor(side, "TopBarSide",       accentColor);
    }
}