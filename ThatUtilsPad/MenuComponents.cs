using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.NVIDIA;
using UnityEngine.UIElements;
using UIImage  = UnityEngine.UI.Image;
using RawImage = UnityEngine.UI.RawImage;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace ThatUtilsPad.MenuComponents;

public class ButtonTrigger : MonoBehaviour
{
    public string BtnIdentifier;
    public Action? CustomAction;
    public Transform Slider;
    public Transform Knob;
    public Renderer BodyRenderer;
    public Renderer OutlineRenderer;
    public Renderer[] KeyboardOutlineRenderers;
    public Coroutine KeyboardPressRoutine;
    public Coroutine SwitchRoutine;
    public Transform ButtonRoot;
    public bool IsToggle = false;
    public bool IsOn = false;
    public float Cooldown = 0f;
    public bool IsOnCooldown = false;
    public bool IsKeyboardKey = false;

    public static void PcPress(ButtonTrigger button)
    {
        if (button == null) return;
        if (button.IsOnCooldown) return;
        if (Mods.IsRenaming && !button.IsKeyboardKey) return;

        Debug.Log("Pressed: " + button.BtnIdentifier);
        Main.Instance.PlayBtnCickSound();

        if (button.IsKeyboardKey)
        {
            if (button.KeyboardPressRoutine != null)
                CoroutineHandler.Instance.StopCoroutine(button.KeyboardPressRoutine);

            button.KeyboardPressRoutine = CoroutineHandler.Instance.StartCoroutine(
                MenuEffects.KeyboardButtonPress(
                    button,
                    button.BodyRenderer,
                    button.KeyboardOutlineRenderers != null && button.KeyboardOutlineRenderers.Length > 0
                        ? button.KeyboardOutlineRenderers
                        : button.OutlineRenderer != null
                            ? new[] { button.OutlineRenderer }
                            : Array.Empty<Renderer>())
            );
            button.CustomAction?.Invoke();
            return;
        }

        if (button.IsToggle)
        {
            if (button.SwitchRoutine != null)
                CoroutineHandler.Instance.StopCoroutine(button.SwitchRoutine);

            button.IsOn = !button.IsOn;
            Mods.SaveToggleState(button.BtnIdentifier, button.IsOn);
            button.SwitchRoutine = CoroutineHandler.Instance.StartCoroutine(button.IsOn
                ? MenuEffects.ActivateSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer)
                : MenuEffects.DeactivateSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer));
        }
        else
        {
            if (button.SwitchRoutine != null)
                CoroutineHandler.Instance.StopCoroutine(button.SwitchRoutine);

            button.SwitchRoutine = CoroutineHandler.Instance.StartCoroutine(MenuEffects.ToggleSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer));
        }

        if (button.Cooldown > 0f)
            CoroutineHandler.Instance.StartCoroutine(RunCooldown(button));

        if (button.CustomAction != null)
        {
            button.CustomAction.Invoke();
            return;
        }

        if (Mods.TryGetAction(button.BtnIdentifier, out var modAction))
            modAction?.Action?.Invoke();
        else
            Debug.LogWarning($"[TUP: WARNING] No mod found for button: {button.BtnIdentifier}");
    }

    private static IEnumerator RunCooldown(ButtonTrigger button)
    {
        button.IsOnCooldown = true;

        Transform main = button.ButtonRoot != null ? button.ButtonRoot.Find("Main") : null;
        if (main == null) { button.IsOnCooldown = false; yield break; }

        Transform overlayT = main.Find("Overlay");
        if (overlayT == null) { button.IsOnCooldown = false; yield break; }

        Transform delayT = overlayT.Find("Delay") ?? main.Find("Delay");

        Vector3 delayStartScale = delayT != null ? delayT.localScale : Vector3.one;

        RawImage overlayRI = overlayT.GetComponent<RawImage>();
        RawImage delayRI   = delayT?.GetComponent<RawImage>();

        // Activate the Canvas and ensure Overlay is visible
        main.gameObject.SetActive(true);
        overlayT.gameObject.SetActive(true);

        if (overlayRI != null) overlayRI.color = new Color(overlayRI.color.r, overlayRI.color.g, overlayRI.color.b, 0f);
        if (delayRI   != null) delayRI.color   = new Color(delayRI.color.r,   delayRI.color.g,   delayRI.color.b,   0f);

        const float fadeDuration = 0.4f;

        // Fade in
        for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
        {
            float p = t / fadeDuration;
            if (overlayRI != null) overlayRI.color = new Color(overlayRI.color.r, overlayRI.color.g, overlayRI.color.b, Mathf.Lerp(0f, 0.2f, p));
            if (delayRI   != null) delayRI.color   = new Color(delayRI.color.r,   delayRI.color.g,   delayRI.color.b,   Mathf.Lerp(0f, 1f,   p));
            yield return null;
        }
        if (overlayRI != null) overlayRI.color = new Color(overlayRI.color.r, overlayRI.color.g, overlayRI.color.b, 0.2f);
        if (delayRI   != null) delayRI.color   = new Color(delayRI.color.r,   delayRI.color.g,   delayRI.color.b,   1f);

        // Scale bar X to 0 over cooldown duration
        for (float t = 0f; t < button.Cooldown; t += Time.deltaTime)
        {
            if (delayT != null)
                delayT.localScale = new Vector3(Mathf.Lerp(delayStartScale.x, 0f, t / button.Cooldown), delayStartScale.y, delayStartScale.z);
            yield return null;
        }
        if (delayT != null) delayT.localScale = new Vector3(0f, delayStartScale.y, delayStartScale.z);

        // Fade out
        for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
        {
            float p = t / fadeDuration;
            if (overlayRI != null) overlayRI.color = new Color(overlayRI.color.r, overlayRI.color.g, overlayRI.color.b, Mathf.Lerp(0.2f, 0f, p));
            if (delayRI   != null) delayRI.color   = new Color(delayRI.color.r,   delayRI.color.g,   delayRI.color.b,   Mathf.Lerp(1f,   0f, p));
            yield return null;
        }

        main.gameObject.SetActive(false);
        if (delayT != null) delayT.localScale = delayStartScale;
        button.IsOnCooldown = false;
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

    public ButtonTrigger trigger;

    private void Awake()
    {
        gameObject.layer = 2;

        if (trigger == null)
            trigger = GetComponent<ButtonTrigger>();
    }

    private void OnTriggerEnter(Collider other)
    {
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
    
    private static readonly Dictionary<Transform, Vector3> originalScales =
        new Dictionary<Transform, Vector3>();

    /// <summary>
    /// Stores the current scale of all checker components.
    /// Call this once before setting their scales to Vector3.zero.
    /// </summary>
    public static void CacheCheckerScales(Transform menuRoot)
    {
        if (menuRoot == null)
            return;

        Transform sideHolder = menuRoot.Find("SideHolder");
        if (sideHolder == null)
            return;

        string[] checkerParts =
        {
            "MonkeBase",
            "MonkeColor",
            "Name",
            "FPS",
            "Platform",
            "Ping",
            "Date",
            "VolumeUp",
            "VolumeDown",
            "Volume",
            "VolumePercent",
            "MuteElse",
            "Mute",
            "AddToSaved"
        };

        foreach (string part in checkerParts)
        {
            Transform t = sideHolder.Find(part);
            if (t == null)
                continue;
            
            if (!originalScales.ContainsKey(t))
                originalScales[t] = t.localScale;
        }
    }

    public static Vector3 GetCachedScale(Transform t)
    {
        if (t != null && originalScales.TryGetValue(t, out Vector3 scale))
            return scale;

        return Vector3.one;
    }
}

public static class MenuEffects
{
    private static float EaseOut(float t)
    {
        return 1f - Mathf.Pow(1f - t, 5f);
    }

    public static IEnumerator CheckerCompEnum(Transform menuObj)
    {
        if (menuObj == null)
            yield break;

        Transform sideHolder = menuObj.transform.Find("SideHolder");
        if (sideHolder == null)
            yield break;


        string[] paths =
        {
            "Name",
            "FPS",
            "Platform",
            "Ping",
            "Date",
            "VolumeUp",
            "VolumeDown",
            "Volume",
            "VolumePercent",
            "MuteElse",
            "Mute",
            "AddToSaved"
        };


        const float delayBetween = 0.06f;
        Vector3 hiddenScale = Vector3.zero;


        void StartPop(Transform t)
        {
            if (t == null) return;

            t.localScale = Vector3.zero;

            CoroutineHandler.Instance.StartCoroutine(
                PopTransform(t, Tools.GetCachedScale(t))
            );
        }
        
        StartPop(sideHolder.Find("MonkeBase"));
        StartPop(sideHolder.Find("MonkeColor"));
        
        yield return new WaitForSeconds(delayBetween);
        
        foreach (string path in paths)
        {
            Transform t = sideHolder.Find(path);
            StartPop(t);

            yield return new WaitForSeconds(delayBetween);
        }
    }
    

    private static IEnumerator PopTransform(Transform target, Vector3 targetScale)
    {
        if (target == null)
            yield break;

        Vector3 startScale = Vector3.zero;
        float duration = 0.5f;
        float time = 0f;

        while (time < duration)
        {
            if (target == null)
                yield break;

            float t = time / duration;
            float eased = EaseOut(t);

            target.localScale = Vector3.LerpUnclamped(startScale, targetScale, eased);

            time += Time.deltaTime;
            yield return null;
        }

        if (target != null)
            target.localScale = targetScale;
    }
    
    
    public static void SnapActivated(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
    {
        if (knob   != null) knob.localPosition = new Vector3(-1.53f, 0f, 0f);
        SpriteRenderer sr = slider?.GetComponent<SpriteRenderer>();
        if (sr             != null) sr.color = MenuTheme.Current.Accent;
        if (bodyRenderer   != null) bodyRenderer.material.color    = MenuTheme.Current.Button;
        if (outlineRenderer!= null) outlineRenderer.material.color = MenuTheme.Current.ButtonLight;
    }

    public static IEnumerator ActivateSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
        => SliderEnumActivate(knob, slider, bodyRenderer, outlineRenderer);

    public static IEnumerator DeactivateSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
        => SliderEnumDeActivate(knob, slider, bodyRenderer, outlineRenderer);

    public static IEnumerator ToggleSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
    {
        yield return CoroutineHandler.Instance.StartCoroutine(SliderEnumActivate(knob, slider, bodyRenderer, outlineRenderer));
        yield return CoroutineHandler.Instance.StartCoroutine(SliderEnumDeActivate(knob, slider, bodyRenderer, outlineRenderer));
    }

    public static IEnumerator KeyboardButtonPress(ButtonTrigger button, Renderer bodyRenderer, Renderer[] outlineRenderers)
    {
        if (bodyRenderer == null && (outlineRenderers == null || outlineRenderers.Length == 0))
            yield break;

        Color bodyStart = MenuTheme.Current.Main;
        Color outlineStart = MenuTheme.Current.Button;
        Color bodyPressed = MenuTheme.Current.Button;
        Color outlinePressed = MenuTheme.Current.ButtonLight;

        if (bodyRenderer != null) bodyRenderer.material.color = bodyStart;
        if (outlineRenderers != null)
            foreach (Renderer outline in outlineRenderers)
                if (outline != null) outline.material.color = outlineStart;

        const float duration = 0.225f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = EaseOut(t / duration);
            if (bodyRenderer != null) bodyRenderer.material.color = Color.Lerp(bodyStart, bodyPressed, p);
            if (outlineRenderers != null)
                foreach (Renderer outline in outlineRenderers)
                    if (outline != null) outline.material.color = Color.Lerp(outlineStart, outlinePressed, p);
            yield return null;
        }

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = EaseOut(t / duration);
            if (bodyRenderer != null) bodyRenderer.material.color = Color.Lerp(bodyPressed, bodyStart, p);
            if (outlineRenderers != null)
                foreach (Renderer outline in outlineRenderers)
                    if (outline != null) outline.material.color = Color.Lerp(outlinePressed, outlineStart, p);
            yield return null;
        }

        if (bodyRenderer != null) bodyRenderer.material.color = bodyStart;
        if (outlineRenderers != null)
            foreach (Renderer outline in outlineRenderers)
                if (outline != null) outline.material.color = outlineStart;

        if (button != null)
            button.KeyboardPressRoutine = null;
    }

    private static IEnumerator SliderEnumActivate(Transform knob, Transform slider, Renderer bodyRenderer, Renderer outlineRenderer)
    {
        if (slider == null || knob == null)
        {
            Debug.Log("Not found knob or slider");
            yield break;
        }

        float duration = 0.225f;
        float time = 0f;

        Vector3 startPosition = new Vector3(1.53f, 0f, 0f);
        Vector3 endPosition   = new Vector3(-1.53f, 0f, 0f);

        SpriteRenderer sr = slider.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogError("No SpriteRenderer");
            yield break;
        }

        Color32 sliderStart   = MenuTheme.Current.ButtonBase;
        Color32 sliderTarget  = MenuTheme.Current.Accent;
        Color32 bodyStart     = MenuTheme.Current.Main;
        Color32 bodyTarget    = MenuTheme.Current.Button;
        Color32 outlineStart  = MenuTheme.Current.Button;
        Color32 outlineTarget = MenuTheme.Current.ButtonLight;

        knob.localPosition = startPosition;

        while (time < duration)
        {
            float easeT = EaseOut(time / duration);

            knob.localPosition = Vector3.Lerp(startPosition, endPosition, easeT);
            sr.color = Color.Lerp(sliderStart, sliderTarget, easeT);
            if (bodyRenderer    != null) bodyRenderer.material.color    = Color.Lerp(bodyStart,    bodyTarget,    easeT);
            if (outlineRenderer != null) outlineRenderer.material.color = Color.Lerp(outlineStart, outlineTarget, easeT);

            time += Time.deltaTime;
            yield return null;
        }

        knob.localPosition = endPosition;
        sr.color = sliderTarget;
        if (bodyRenderer    != null) bodyRenderer.material.color    = bodyTarget;
        if (outlineRenderer != null) outlineRenderer.material.color = outlineTarget;
    }

    private static IEnumerator SliderEnumDeActivate(Transform knob, Transform slider, Renderer bodyRenderer, Renderer outlineRenderer)
    {
        if (slider == null || knob == null)
        {
            Debug.Log("Not found knob or slider");
            yield break;
        }

        float duration = 0.225f;
        float time = 0f;

        Vector3 startPosition = new Vector3(-1.53f, 0f, 0f);
        Vector3 endPosition   = new Vector3( 1.53f, 0f, 0f);

        SpriteRenderer sr = slider.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogError("No SpriteRenderer");
            yield break;
        }

        Color32 sliderStart   = MenuTheme.Current.Accent;
        Color32 sliderTarget  = MenuTheme.Current.ButtonBase;
        Color32 bodyStart     = MenuTheme.Current.Button;
        Color32 bodyTarget    = MenuTheme.Current.Main;
        Color32 outlineStart  = MenuTheme.Current.ButtonLight;
        Color32 outlineTarget = MenuTheme.Current.Button;

        knob.localPosition = startPosition;

        while (time < duration)
        {
            float easeT = EaseOut(time / duration);

            knob.localPosition = Vector3.Lerp(startPosition, endPosition, easeT);
            sr.color = Color.Lerp(sliderStart, sliderTarget, easeT);
            if (bodyRenderer    != null) bodyRenderer.material.color    = Color.Lerp(bodyStart,    bodyTarget,    easeT);
            if (outlineRenderer != null) outlineRenderer.material.color = Color.Lerp(outlineStart, outlineTarget, easeT);

            time += Time.deltaTime;
            yield return null;
        }

        knob.localPosition = endPosition;
        sr.color = sliderTarget;
        if (bodyRenderer    != null) bodyRenderer.material.color    = bodyTarget;
        if (outlineRenderer != null) outlineRenderer.material.color = outlineTarget;
    }
    
    public static IEnumerator TapCircleEnum(Transform circle)
    {
        float duration = 0.5f;
        float time = 0f;

        Vector3 startScale = new Vector3(0.0257443171f, 0.205954537f, 0);
        Vector3 targetScale = new Vector3(0.338474751f, 2.707798f, 0f); 

        SpriteRenderer sr = circle.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogError("No SpriteRenderer");
            yield break;
        }

        Color startColor = sr.color;
        float startAlpha = 0.55f;
        float targetAlpha = 0f;
        
        circle.localScale = startScale;
        sr.color = new Color(startColor.r, startColor.g, startColor.b, startAlpha);
        
        while (time < duration)
        {
            float t = time / duration;
            float easeT = EaseOut(t);
            
            circle.localScale = Vector3.Lerp(startScale, targetScale, easeT);
            
            float newAlpha = Mathf.Lerp(startAlpha, targetAlpha, easeT);
            sr.color = new Color(startColor.r, startColor.g, startColor.b, newAlpha);

            
            time += Time.deltaTime;
            yield return null;
        }
        
        circle.localScale = targetScale;
        sr.color = new Color(startColor.r, startColor.g, startColor.b, targetAlpha);
    }
    
    public static IEnumerator PopMenu(GameObject menuObj, Vector3 targetScale, Vector3 startScale, bool useEaseOut)
    {
        if (menuObj == null)
            yield break;

        float duration = 0.25f;
        float time = 0f;
        
        while (time < duration)
        {
            if (menuObj == null)
                yield break;

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
        
        if (menuObj != null)
            menuObj.transform.localScale = targetScale;
    }

    public static IEnumerator PopButton(GameObject btnObj, Vector3 targetScale, Vector3 startScale, bool useEaseOut)
    {
        float duration = 0.5f;
        float time = 0f;
        
        while (time < duration)
        {
            if (btnObj == null)
                yield break;
            
            float t = time / duration;
            float easeT;
            
            if (useEaseOut)
                easeT = 1f - Mathf.Pow(1f - t, 3);   
            else
                easeT = Mathf.Pow(t, 3);
            
            btnObj.transform.localScale = Vector3.Lerp(startScale, targetScale, easeT);

            time += Time.deltaTime;
            yield return null;
        }
        
        btnObj.transform.localScale = targetScale;
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

public class SmoothFollowMenu : MonoBehaviour
{
    public Transform  Target;
    public Vector3    LocalPosition;
    public Quaternion LocalRotation = Quaternion.identity;
    public float      Smoothing     = 6f;

    private void OnEnable()
    {
        if (Target != null)
            Snap();
    }

    public void Snap()
    {
        transform.position = Target.TransformPoint(LocalPosition);
        transform.rotation = Target.rotation * LocalRotation;
    }

    public bool Frozen;

    private void LateUpdate()
    {
        if (Target == null || Frozen) return;

        transform.position = Vector3.Lerp(
            transform.position,
            Target.TransformPoint(LocalPosition),
            Smoothing * Time.deltaTime
        );
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Target.rotation * LocalRotation,
            Smoothing * Time.deltaTime
        );
    }
}

public sealed class MenuThemePalette
{
    public Color32 Main         { get; set; } = new(9, 9, 14, 255);
    public Color32 Border       { get; set; } = new(6, 6, 11, 255);
    public Color32 Button       { get; set; } = new(15, 15, 23, 255);
    public Color32 ButtonLight  { get; set; } = new(36, 36, 52, 255);
    public Color32 ButtonBase   { get; set; } = new(9, 9, 18, 255);
    public Color32 Accent       { get; set; } = new(203, 166, 247, 255);
    public Color32 Mid          { get; set; } = new(8, 8, 13, 255);
    public Color32 MidDark      { get; set; } = new(7, 7, 12, 255);
    public Color32 DarkAccent   { get; set; } = new(57, 44, 72, 255);
    public Color32 LightMain    { get; set; } = new(18, 18, 26, 255);
    public bool    SakuraParts  { get; set; }
}

public static class MenuTheme
{
    public static class Themes
    {
        public static readonly MenuThemePalette Default = new();
        public static readonly MenuThemePalette Sakura = new() { SakuraParts = true };
    }

    public static MenuThemePalette Current { get; private set; } = Themes.Default;

    private static readonly string[] MainParts =
    {
        "Main", "GripMain", "SideMain", "SelectorMain", "SelectorMainSide",
        "SelectorBtn1", "SelectorBtn2", "SelectorBtn3", "SelectorBtn4", "SelectorBtn5",
        "SelectorBtn6", "SelectorBtn7", "SelectorBtn8", "SelectorBtn9",
        "PageNext", "PageBack", "VolumeUp", "VolumeDown", "Mute", "MuteElse", "AddToSaved"
    };

    private static readonly string[] BorderParts =
    {
        "MainBorder", "GripConnect", "SideBorder", "SelectorBorder", "SelectorBorderSide", "Discord"
    };

    private static readonly string[] MidParts =
    {
        "TopHolder", "BottomHolderLight", "InfoMain", "CheckerTitle"
    };

    private static readonly string[] MidDarkParts =
    {
        "BottomHolderDark", "ModsRating", "NameMain", "KeyboardTitle"
    };

    private static readonly string[] LightMainParts =
    {
        "Seperator", "Separator", "Seperator1", "Seperator2", "Seperator3", "Seperator4", "Seperator7"
    };

    private static readonly string[] DarkAccentParts =
    {
        "PageVis1", "PageVis3"
    };

    private static readonly string[] AccentParts =
    {
        "GripAccent", "PageVis2"
    };

    private static readonly string[] SakuraMainParts =
    {
        "Pole1", "Pole2", "BarConnector", "TopBarUnder", "GripPipe",
        "PoleSide1", "PoleSide2", "BarConnectorSide", "TopBarUnderSide"
    };

    private static readonly string[] SakuraBorderParts =
    {
        "PipeTop1", "PipeTop2", "UnderBar", "PipeTopSide1", "PipeTopSide2", "UnderBarSide"
    };

    private static readonly string[] SakuraAccentParts =
    {
        "TopBar", "TopBarSide"
    };

    public static void Use(MenuThemePalette theme)
    {
        Current = theme;
    }

    public static void Assign(GameObject menuObj, MenuThemePalette theme)
    {
        Use(theme);

        Transform root = menuObj.transform;
        Apply(root, MainParts, theme.Main);
        Apply(root, BorderParts, theme.Border);
        Apply(root, MidParts, theme.Mid);
        Apply(root, MidDarkParts, theme.MidDark);
        Apply(root, LightMainParts, theme.LightMain);
        Apply(root, DarkAccentParts, theme.DarkAccent);
        Apply(root, AccentParts, theme.Accent);

        ApplyOutlines(root, "SelectorBtn", 9, theme.Button);
        ApplyOutlines(root, "PageNext", theme.Button);
        ApplyOutlines(root, "PageBack", theme.Button);
        ApplyOutlines(root, "VolumeUp", theme.Button);
        ApplyOutlines(root, "VolumeDown", theme.Button);
        ApplyOutlines(root, "Mute", theme.Button);
        ApplyOutlines(root, "MuteElse", theme.Button);
        ApplyOutlines(root, "AddToSaved", theme.Button);

        if (!theme.SakuraParts) return;

        Apply(root, SakuraMainParts, theme.Main);
        Apply(root, SakuraBorderParts, theme.Border);
        Apply(root, SakuraAccentParts, theme.Accent);
    }

    public static void ApplySelectorButton(Transform selectorButton)
    {
        ApplyRenderer(selectorButton, Current.Main);
        ApplyChild(selectorButton, "Outline", Current.Button);
    }

    public static void ApplyNavButton(Transform navButton)
    {
        ApplyRenderer(navButton, Current.Main);
        ApplyChild(navButton, "Outline", Current.Button);
    }

    public static void ApplyMenuButton(Transform button, out Renderer bodyRenderer, out Renderer outlineRenderer)
    {
        bodyRenderer = ApplyRenderer(button, Current.Main);
        outlineRenderer = ApplyChild(button, "Outline", Current.Button);
    }

    public static void ApplyKeyboard(Transform keyboard)
    {
        Apply(keyboard, "Main", Current.Border);
        Apply(keyboard, "Border", Current.Border);
        ApplyDirectChildren(keyboard, "Outline", Current.Button);
    }

    public static void ApplyKeyboardKey(Transform key, out Renderer bodyRenderer, out Renderer outlineRenderer)
    {
        ApplyKeyboardKey(key, out bodyRenderer, out Renderer[] outlineRenderers);
        outlineRenderer = outlineRenderers.Length > 0 ? outlineRenderers[0] : null;
    }

    public static void ApplyKeyboardKey(Transform key, out Renderer bodyRenderer, out Renderer[] outlineRenderers)
    {
        bodyRenderer = ApplyRenderer(key, Current.Main);

        Renderer mainRenderer = ApplyChild(key, "Main", Current.Main);
        if (mainRenderer != null)
            bodyRenderer = mainRenderer;

        List<Renderer> renderers = ApplyChildren(key, "Outline", Current.Button);
        if (renderers.Count == 0)
            renderers = ApplyChildren(key, "Border", Current.Button);

        outlineRenderers = renderers.ToArray();
    }

    public static Renderer ApplyKeyboardOutline(Transform outline)
        => ApplyRenderer(outline, Current.Button);

    private static void Apply(Transform root, IEnumerable<string> names, Color32 color)
    {
        foreach (string name in names)
            Apply(root, name, color);
    }

    private static void Apply(Transform root, string name, Color32 color)
    {
        if (root == null) return;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ApplyRenderer(child, color);
        }
    }

    private static void ApplyDirectChildren(Transform root, string name, Color32 color)
    {
        if (root == null) return;

        foreach (Transform child in root)
        {
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ApplyRenderer(child, color);
        }
    }

    private static void ApplyOutlines(Transform root, string prefix, int count, Color32 color)
    {
        for (int i = 1; i <= count; i++)
            ApplyOutlines(root, prefix + i, color);
    }

    private static void ApplyOutlines(Transform root, string parentName, Color32 color)
    {
        if (root == null) return;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(parentName, StringComparison.OrdinalIgnoreCase))
                ApplyChild(child, "Outline", color);
        }
    }

    private static Renderer ApplyChild(Transform parent, string childName, Color32 color)
    {
        Transform child = FindDirectOrDeepChild(parent, childName);
        return child != null ? ApplyRenderer(child, color) : null;
    }

    private static List<Renderer> ApplyChildren(Transform parent, string childName, Color32 color)
    {
        List<Renderer> renderers = new List<Renderer>();
        if (parent == null) return renderers;

        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child == parent || !child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                continue;

            Renderer renderer = ApplyRenderer(child, color);
            if (renderer != null)
                renderers.Add(renderer);
        }

        return renderers;
    }

    private static Transform FindDirectOrDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;

        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child != parent && child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private static Renderer ApplyRenderer(Transform target, Color32 color)
    {
        if (target == null) return null;

        Renderer renderer = target.GetComponent<Renderer>() ?? target.GetComponentInChildren<Renderer>(true);
        if (renderer == null) return null;

        foreach (Material mat in renderer.materials)
        {
            mat.shader = ShaderCache.UberShader;
            mat.color = color;
        }

        return renderer;
    }
}
