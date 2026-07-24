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
using Vector2 = UnityEngine.Vector2;
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
    public UIImage[] KeyboardImages;
    public Coroutine KeyboardPressRoutine;
    public Coroutine SwitchRoutine;
    public Transform ButtonRoot;
    public bool IsToggle = false;
    public bool IsOn = false;
    public float Cooldown = 0f;
    public bool IsOnCooldown = false;
    public bool IsKeyboardKey = false;
    public bool SkipSwitchAnimation = false;
    public Transform HoverBar;
    public Transform HoverTitle;
    public Coroutine HoverRoutine;
    public bool IsHovered;
    private static readonly Dictionary<string, float> CooldownEnds = new Dictionary<string, float>();

    public static void PcPress(ButtonTrigger button)
    {
        if (button == null) return;
        if (button.IsOnCooldown || GetRemainingCooldown(button.BtnIdentifier) > 0f) return;
        if (Mods.IsRenaming && !button.IsKeyboardKey) return;
        Main.Instance.PlayBtnCickSound();
        if (!button.IsKeyboardKey)
            StartButtonUiPulse(button);

        if (button.SkipSwitchAnimation && button.CustomAction != null)
        {
            button.CustomAction.Invoke();
            return;
        }

        if (button.IsKeyboardKey)
        {
            if (button.KeyboardPressRoutine != null)
            {
                MenuEffects.ResetKeyboardButtonPress(button);
                Tools.SafeStopCoroutine(ref button.KeyboardPressRoutine);
            }

            bool suppressHighlight = string.Equals(button.BtnIdentifier, "Delete", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(button.BtnIdentifier, "Enter", StringComparison.OrdinalIgnoreCase);
            if (!suppressHighlight)
            {
                button.KeyboardPressRoutine = Tools.SafeStartCoroutine(
                    MenuEffects.KeyboardButtonPress(
                        button,
                        button.BodyRenderer,
                        button.KeyboardOutlineRenderers != null && button.KeyboardOutlineRenderers.Length > 0
                            ? button.KeyboardOutlineRenderers
                            : button.OutlineRenderer != null
                                ? new[] { button.OutlineRenderer }
                                : Array.Empty<Renderer>(),
                        button.KeyboardImages)
                );
            }

            button.CustomAction?.Invoke();
            return;
        }

        if (button.IsToggle)
        {
            if (button.SwitchRoutine != null)
                Tools.SafeStopCoroutine(ref button.SwitchRoutine);

            button.IsOn = !button.IsOn;
            Mods.SaveToggleState(button.BtnIdentifier, button.IsOn);
            if (button.Knob != null && button.Slider != null)
            {
                button.SwitchRoutine = Tools.SafeStartCoroutine(button.IsOn
                    ? MenuEffects.ActivateSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer)
                    : MenuEffects.DeactivateSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer));
            }
        }
        else
        {
            if (button.SwitchRoutine != null)
                Tools.SafeStopCoroutine(ref button.SwitchRoutine);

            if (button.Knob != null && button.Slider != null)
                button.SwitchRoutine = Tools.SafeStartCoroutine(MenuEffects.ToggleSwitch(button.Knob, button.Slider, button.BodyRenderer, button.OutlineRenderer));
        }

        if (button.Cooldown > 0f)
        {
            CooldownEnds[button.BtnIdentifier] = Time.time + button.Cooldown;
            Tools.SafeStartCoroutine(RunCooldown(button));
        }

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

    private static void StartButtonUiPulse(ButtonTrigger button)
    {
        if (button == null || button.ButtonRoot == null)
            return;

        if (button.KeyboardPressRoutine != null)
        {
            Tools.SafeStopCoroutine(ref button.KeyboardPressRoutine);
            RestoreButtonUiPulse(button);
        }

        UnityEngine.UI.Graphic body = button.ButtonRoot.GetComponent<UnityEngine.UI.Graphic>();
        Transform outlineTransform = button.ButtonRoot.Find("Outline");
        UnityEngine.UI.Graphic outline = outlineTransform != null ? outlineTransform.GetComponent<UnityEngine.UI.Graphic>() : null;
        if (body == null && outline == null)
            return;

        button.KeyboardPressRoutine = Tools.SafeStartCoroutine(ButtonUiPulse(button, body, outline));
    }

    private static void RestoreButtonUiPulse(ButtonTrigger button)
    {
        if (button == null || button.ButtonRoot == null)
            return;

        UnityEngine.UI.Graphic body = button.ButtonRoot.GetComponent<UnityEngine.UI.Graphic>();
        Transform outlineTransform = button.ButtonRoot.Find("Outline");
        UnityEngine.UI.Graphic outline = outlineTransform != null ? outlineTransform.GetComponent<UnityEngine.UI.Graphic>() : null;
        if (body != null && ButtonPulseOriginals.TryGetValue(body, out Color bodyColor))
        {
            body.color = bodyColor;
            ButtonPulseOriginals.Remove(body);
        }
        if (outline != null && ButtonPulseOriginals.TryGetValue(outline, out Color outlineColor))
        {
            outline.color = outlineColor;
            ButtonPulseOriginals.Remove(outline);
        }
    }

    private static readonly Dictionary<UnityEngine.UI.Graphic, Color> ButtonPulseOriginals = new Dictionary<UnityEngine.UI.Graphic, Color>();
    private static readonly Color ButtonPulseColor = new Color32(26, 26, 36, 255);
    private static readonly Color ButtonOutlinePulseColor = new Color32(34, 34, 52, 255);

    private static IEnumerator ButtonUiPulse(ButtonTrigger button, UnityEngine.UI.Graphic body, UnityEngine.UI.Graphic outline)
    {
        Color bodyStart = body != null ? GetStoredPulseColor(body) : Color.white;
        Color outlineStart = outline != null ? GetStoredPulseColor(outline) : Color.white;
        Color bodyTarget = WithAlpha(ButtonPulseColor, bodyStart.a);
        Color outlineTarget = WithAlpha(ButtonOutlinePulseColor, outlineStart.a);

        yield return LerpButtonPulse(body, outline, bodyStart, outlineStart, bodyTarget, outlineTarget, 0.25f, true);
        yield return LerpButtonPulse(body, outline, bodyTarget, outlineTarget, bodyStart, outlineStart, 0.25f, false);

        if (body != null)
        {
            body.color = bodyStart;
            ButtonPulseOriginals.Remove(body);
        }
        if (outline != null)
        {
            outline.color = outlineStart;
            ButtonPulseOriginals.Remove(outline);
        }
        if (button != null)
            button.KeyboardPressRoutine = null;
    }

    private static Color GetStoredPulseColor(UnityEngine.UI.Graphic image)
    {
        if (image == null)
            return Color.white;
        if (!ButtonPulseOriginals.TryGetValue(image, out Color color))
        {
            color = image.color;
            ButtonPulseOriginals[image] = color;
        }
        return color;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static IEnumerator LerpButtonPulse(UnityEngine.UI.Graphic body, UnityEngine.UI.Graphic outline, Color bodyFrom, Color outlineFrom, Color bodyTo, Color outlineTo, float duration, bool easeIn)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = easeIn ? t * t : 1f - (1f - t) * (1f - t);
            if (body != null)
                body.color = Color.Lerp(bodyFrom, bodyTo, t);
            if (outline != null)
                outline.color = Color.Lerp(outlineFrom, outlineTo, t);
            yield return null;
        }

        if (body != null)
            body.color = bodyTo;
        if (outline != null)
            outline.color = outlineTo;
    }
    public static void RestoreCooldown(ButtonTrigger button)
    {
        if (button == null || button.Cooldown <= 0f) return;
        if (GetRemainingCooldown(button.BtnIdentifier) > 0f)
            Tools.SafeStartCoroutine(RunCooldown(button));
    }

    private static float GetRemainingCooldown(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return 0f;
        if (!CooldownEnds.TryGetValue(identifier, out float endTime)) return 0f;
        float remaining = endTime - Time.time;
        if (remaining > 0f) return remaining;
        CooldownEnds.Remove(identifier);
        return 0f;
    }

    private static bool IsAlive(UnityEngine.Object obj) => obj != null;

    private static IEnumerator RunCooldown(ButtonTrigger button)
    {
        if (!IsAlive(button))
            yield break;

        string identifier = button.BtnIdentifier;
        button.IsOnCooldown = true;
        float remainingCooldown = GetRemainingCooldown(identifier);

        Transform root = button.ButtonRoot;
        if (!IsAlive(root))
        {
            if (IsAlive(button)) button.IsOnCooldown = false;
            yield break;
        }

        Transform main = root.Find("Main") ?? root;
        if (!IsAlive(main))
        {
            if (IsAlive(button)) button.IsOnCooldown = false;
            yield break;
        }

        Transform overlayT = main.Find("Overlay") ?? root.Find("Overlay");
        if (!IsAlive(overlayT))
        {
            if (IsAlive(button)) button.IsOnCooldown = false;
            yield break;
        }

        Transform delayT = overlayT.Find("Delay") ?? main.Find("Delay") ?? root.Find("Delay");
        Vector3 delayStartScale = IsAlive(delayT) ? delayT.localScale : Vector3.one;

        RawImage overlayRI = overlayT.GetComponent<RawImage>();
        RawImage delayRI   = IsAlive(delayT) ? delayT.GetComponent<RawImage>() : null;
        UIImage overlayI   = overlayT.GetComponent<UIImage>();
        UIImage delayI     = IsAlive(delayT) ? delayT.GetComponent<UIImage>() : null;

        if (main != root) main.gameObject.SetActive(true);
        overlayT.gameObject.SetActive(true);
        if (IsAlive(delayT)) delayT.gameObject.SetActive(true);

        SetGraphicAlpha(overlayRI, overlayI, 0f);
        SetGraphicAlpha(delayRI, delayI, 0f);

        const float fadeDuration = 0.4f;

        for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
        {
            if (!IsAlive(button) || !IsAlive(overlayT))
                yield break;
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / fadeDuration), 2f);
            SetGraphicAlpha(overlayRI, overlayI, Mathf.Lerp(0f, 0.2f, p));
            SetGraphicAlpha(delayRI, delayI, Mathf.Lerp(0f, 1f, p));
            yield return null;
        }

        if (!IsAlive(button) || !IsAlive(overlayT))
            yield break;

        SetGraphicAlpha(overlayRI, overlayI, 0.2f);
        SetGraphicAlpha(delayRI, delayI, 1f);

        float startRemaining = Mathf.Max(0.001f, remainingCooldown > 0f ? remainingCooldown : button.Cooldown);
        while (GetRemainingCooldown(identifier) > 0f)
        {
            if (!IsAlive(button))
                yield break;
            float remaining = GetRemainingCooldown(identifier);
            if (IsAlive(delayT))
                delayT.localScale = new Vector3(delayStartScale.x * Mathf.Pow(Mathf.Clamp01(remaining / startRemaining), 2f), delayStartScale.y, delayStartScale.z);
            yield return null;
        }

        if (!IsAlive(button) || !IsAlive(overlayT))
            yield break;

        if (IsAlive(delayT)) delayT.localScale = new Vector3(0f, delayStartScale.y, delayStartScale.z);

        for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
        {
            if (!IsAlive(button) || !IsAlive(overlayT))
                yield break;
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / fadeDuration), 2f);
            SetGraphicAlpha(overlayRI, overlayI, Mathf.Lerp(0.2f, 0f, p));
            SetGraphicAlpha(delayRI, delayI, Mathf.Lerp(1f, 0f, p));
            yield return null;
        }

        if (!IsAlive(button) || !IsAlive(overlayT))
            yield break;

        SetGraphicAlpha(overlayRI, overlayI, 0f);
        SetGraphicAlpha(delayRI, delayI, 0f);
        overlayT.gameObject.SetActive(false);
        if (IsAlive(delayT))
        {
            delayT.localScale = delayStartScale;
            delayT.gameObject.SetActive(false);
        }
        if (IsAlive(main) && main != root) main.gameObject.SetActive(false);
        CooldownEnds.Remove(identifier);
        if (IsAlive(button)) button.IsOnCooldown = false;
    }

    private static void SetGraphicAlpha(RawImage raw, UIImage image, float alpha)
    {
        if (raw != null)
            raw.color = new Color(raw.color.r, raw.color.g, raw.color.b, alpha);
        if (image != null)
            image.color = new Color(image.color.r, image.color.g, image.color.b, alpha);
    }

        
        
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
        if (gameObject.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        bool namedCollider =
            gameObject.name.Equals("Collider", StringComparison.OrdinalIgnoreCase) ||
            gameObject.name.EndsWith("Collider", StringComparison.OrdinalIgnoreCase);
        if (!namedCollider && trigger == null)
            return;

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

public class VolumeSliderCollider : MonoBehaviour
{
    public RectTransform SliderRect;
    public RectTransform FillRect;
    public Action<float> OnValueChanged;
    public float MinFillWidth = 15f;
    public float MaxFillWidth = 524.19f;
    public float FillSmoothDuration = 0.5f;

    private float lastSetTime;
    private float currentValue;
    private float targetValue;
    private float fillVelocity;
    private bool fillKnowsItsSize;
    private bool fillAnimating;
    private BoxCollider hitBox;
    private const float FillSettleEpsilon = 0.0001f;
    private void Awake()
    {
        gameObject.layer = 2;
        Collider col = GetComponent<Collider>();
        if (col == null)
            col = gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        hitBox = col as BoxCollider;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Update()
    {
        if (!fillAnimating || FillRect == null || !fillKnowsItsSize)
            return;

        currentValue = Mathf.SmoothDamp(currentValue, targetValue, ref fillVelocity, FillSmoothDuration, Mathf.Infinity, Time.deltaTime);
        if (Mathf.Abs(currentValue - targetValue) <= FillSettleEpsilon &&
            Mathf.Abs(fillVelocity) <= FillSettleEpsilon)
        {
            currentValue = targetValue;
            fillVelocity = 0f;
            fillAnimating = false;
        }
        ApplyFill(currentValue);
    }

    private void OnTriggerEnter(Collider other) => TrySetFromPresser(other);
    private void OnTriggerStay(Collider other) => TrySetFromPresser(other);

    private void TrySetFromPresser(Collider other)
    {
        if (gameObject.name.IndexOf("HoverCollider", StringComparison.OrdinalIgnoreCase) >= 0 ||
            !(gameObject.name.Equals("Collider", StringComparison.OrdinalIgnoreCase) || gameObject.name.EndsWith("Collider", StringComparison.OrdinalIgnoreCase)))
            return;

        if (other.GetComponent<ButtonPresser>() == null)
            return;

        SetFromWorldPoint(other.transform.position);
    }

    public void SetFromWorldPoint(Vector3 worldPoint)
    {
        if (hitBox == null)
            hitBox = GetComponent<BoxCollider>();

        if (hitBox != null && hitBox.size.x > 0.0001f)
        {
            Vector3 hitLocal = transform.InverseTransformPoint(worldPoint);
            float min = hitBox.center.x - hitBox.size.x * 0.5f;
            float max = hitBox.center.x + hitBox.size.x * 0.5f;
            float sliderValue = Mathf.InverseLerp(min, max, hitLocal.x);
            SetValue01(sliderValue, true);
            return;
        }

        if (SliderRect == null)
            return;

        Vector3 local = SliderRect.InverseTransformPoint(worldPoint);
        Rect rect = SliderRect.rect;
        float value = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
        SetValue01(value, true);
    }

    public void SetValue01(float value, bool notify)
    {
        value = Mathf.Clamp01(value);
        bool targetChanged = Mathf.Abs(targetValue - value) > FillSettleEpsilon;
        targetValue = value;
        if (!fillKnowsItsSize)
        {
            fillKnowsItsSize = true;
            currentValue = value;
            fillVelocity = 0f;
            fillAnimating = false;
            ApplyFill(value);
        }
        else if (targetChanged || Mathf.Abs(currentValue - targetValue) > FillSettleEpsilon)
        {
            fillAnimating = true;
        }

        if (!notify || Time.time - lastSetTime < 0.025f)
            return;

        lastSetTime = Time.time;
        OnValueChanged?.Invoke(value);
    }

    private void ApplyFill(float value)
    {
        if (FillRect == null)
            return;

        Vector2 size = FillRect.sizeDelta;
        size.x = Mathf.Lerp(MinFillWidth, MaxFillWidth, Mathf.Clamp01(value));
        FillRect.sizeDelta = size;
    }
}
public class ButtonHoverCollider : MonoBehaviour
{
    public ButtonTrigger trigger;

    private void Awake()
    {
        gameObject.layer = 2;
        if (trigger == null)
            trigger = GetComponentInParent<ButtonTrigger>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<ButtonPresser>() == null || trigger == null || trigger.IsHovered)
            return;

        trigger.IsHovered = true;
        if (trigger.HoverRoutine != null)
            Tools.SafeStopCoroutine(ref trigger.HoverRoutine);
        trigger.HoverRoutine = Tools.SafeStartCoroutine(MenuEffects.ButtonHover(trigger, true));
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<ButtonPresser>() == null || trigger == null || !trigger.IsHovered)
            return;

        trigger.IsHovered = false;
        if (trigger.HoverRoutine != null)
            Tools.SafeStopCoroutine(ref trigger.HoverRoutine);
        trigger.HoverRoutine = Tools.SafeStartCoroutine(MenuEffects.ButtonHover(trigger, false));
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
        SafeStopCoroutine(ref routine);
    }

    public static Coroutine SafeStartCoroutine(IEnumerator routine)
    {
        if (routine == null || CoroutineHandler.Instance == null)
            return null;

        try
        {
            return CoroutineHandler.Instance.StartCoroutine(routine);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Coroutine start blocked: " + ex.Message);
            return null;
        }
    }

    public static void SafeStopCoroutine(ref Coroutine routine)
    {
        if (routine == null)
            return;

        try
        {
            if (CoroutineHandler.Instance != null)
                CoroutineHandler.Instance.StopCoroutine(routine);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Coroutine stop blocked: " + ex.Message);
        }
        finally
        {
            routine = null;
        }
    }
    
    private static readonly Dictionary<Transform, Vector3> originalScales =
        new Dictionary<Transform, Vector3>();

    public static void RememberPanelSizes(Transform menuRoot)
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
            "FPS/Ping",
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
            "ModsTitle",
            "AddToSaved",
            "SavedPlayers",
            "Saved Players"
        };

        foreach (string part in checkerParts)
        {
            Transform t = FindCheckerPart(sideHolder, part);
            if (t == null)
                continue;
            
            if (!originalScales.ContainsKey(t))
                originalScales[t] = t.localScale;
        }
    }

    public static Transform FindCheckerPart(Transform root, string name)
    {
        if (root == null)
            return null;

        Transform direct = root.Find(name);
        if (direct != null)
            return direct;

        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child != null && child.name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    public static Vector3 GetRememberedSize(Transform t)
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

    private static void SafeSetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        try
        {
            foreach (Material mat in renderer.materials)
                if (mat != null)
                    mat.color = color;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Renderer color guard: " + ex.Message);
        }
    }

    private static void SafeSetImageColor(UIImage image, Color color)
    {
        if (image == null)
            return;

        try
        {
            image.color = color;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] UI color guard: " + ex.Message);
        }
    }

    private static Color SafeGetImageColor(UIImage image, Color fallback)
    {
        if (image == null)
            return fallback;

        try
        {
            return image.color;
        }
        catch
        {
            return fallback;
        }
    }

    public static void ResetKeyboardButtonPress(ButtonTrigger button)
    {
        if (button == null)
            return;

        SafeSetRendererColor(button.BodyRenderer, MenuTheme.Current.Main);
        Renderer[] outlines = button.KeyboardOutlineRenderers != null && button.KeyboardOutlineRenderers.Length > 0
            ? button.KeyboardOutlineRenderers
            : button.OutlineRenderer != null
                ? new[] { button.OutlineRenderer }
                : Array.Empty<Renderer>();
        foreach (Renderer outline in outlines)
            SafeSetRendererColor(outline, MenuTheme.Current.Button);
        if (button.KeyboardImages != null)
            foreach (UIImage image in button.KeyboardImages)
                SafeSetImageColor(image, MenuTheme.Current.Main);
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
            "FPS/Ping",
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
            "AddToSaved",
            "SavedPlayers",
            "Saved Players"
        };

        const float delayBetween = 0.06f;
        Vector3 hiddenScale = Vector3.zero;

        void StartPop(Transform t)
        {
            if (t == null) return;

            t.localScale = Vector3.zero;

            CoroutineHandler.Instance.StartCoroutine(
                PopTransform(t, Tools.GetRememberedSize(t))
            );
        }
        
        StartPop(Tools.FindCheckerPart(sideHolder, "MonkeBase"));
        StartPop(Tools.FindCheckerPart(sideHolder, "MonkeColor"));
        
        yield return new WaitForSeconds(delayBetween);
        
        foreach (string path in paths)
        {
            Transform t = Tools.FindCheckerPart(sideHolder, path);
            if (t == null)
                continue;

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

    public static IEnumerator ButtonHover(ButtonTrigger button, bool enter)
    {
        if (button == null)
            yield break;

        Transform bar = button.HoverBar;
        Transform title = button.HoverTitle;
        const float duration = 0.18f;
        Vector3 titleIn = new Vector3(0.0085f, 0f, 0f);
        Vector3 titleOut = new Vector3(0.00943f, 0f, 0f);

        Vector3 barFrom = bar != null ? bar.localScale : Vector3.zero;
        Vector3 barTo = enter ? Vector3.one : Vector3.zero;
        Vector3 titleFrom = title != null ? title.localPosition : titleOut;
        Vector3 titleTo = enter ? new Vector3(titleIn.x, titleFrom.y, titleFrom.z) : new Vector3(titleOut.x, titleFrom.y, titleFrom.z);

        if (bar != null)
            bar.gameObject.SetActive(true);

        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOut(Mathf.Clamp01(time / duration));
            if (bar != null)
                bar.localScale = Vector3.LerpUnclamped(barFrom, barTo, p);
            if (title != null)
                title.localPosition = Vector3.LerpUnclamped(titleFrom, titleTo, p);
            yield return null;
        }

        if (bar != null)
        {
            bar.localScale = barTo;
            bar.gameObject.SetActive(enter);
        }
        if (title != null)
            title.localPosition = titleTo;

        button.HoverRoutine = null;
    }
    public static void SnapActivated(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
    {
        if (knob != null) knob.localPosition = new Vector3(0.00235f, 0f, 0f);
        SpriteRenderer sr = slider?.GetComponent<SpriteRenderer>();
        UIImage sliderImage = slider?.GetComponent<UIImage>();
        if (sr != null) sr.color = MenuTheme.Current.Accent;
        SafeSetImageColor(sliderImage, MenuTheme.Current.Accent);
        SafeSetRendererColor(bodyRenderer, MenuTheme.Current.Button);
        SafeSetRendererColor(outlineRenderer, MenuTheme.Current.ButtonLight);
    }

    public static void SnapDeactivated(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
    {
        if (knob != null) knob.localPosition = Vector3.zero;
        SpriteRenderer sr = slider?.GetComponent<SpriteRenderer>();
        UIImage sliderImage = slider?.GetComponent<UIImage>();
        if (sr != null) sr.color = MenuTheme.Current.ButtonBase;
        SafeSetImageColor(sliderImage, MenuTheme.Current.ButtonBase);
        SafeSetRendererColor(bodyRenderer, MenuTheme.Current.Main);
        SafeSetRendererColor(outlineRenderer, MenuTheme.Current.Button);
    }

    public static IEnumerator ActivateSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
        => SliderEnumActivate(knob, slider, bodyRenderer, outlineRenderer);

    public static IEnumerator DeactivateSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
        => SliderEnumDeActivate(knob, slider, bodyRenderer, outlineRenderer);

    public static IEnumerator ToggleSwitch(Transform knob, Transform slider, Renderer bodyRenderer = null, Renderer outlineRenderer = null)
    {
        yield return SliderEnumActivate(knob, slider, bodyRenderer, outlineRenderer);
        yield return SliderEnumDeActivate(knob, slider, bodyRenderer, outlineRenderer);
    }

    public static IEnumerator KeyboardButtonPress(ButtonTrigger button, Renderer bodyRenderer, Renderer[] outlineRenderers, UIImage[] uiImages = null)
    {
        bool hasRenderers = bodyRenderer != null || (outlineRenderers != null && outlineRenderers.Any(outline => outline != null));
        bool hasImages = uiImages != null && uiImages.Any(image => image != null);
        if (!hasRenderers && !hasImages)
            yield break;

        Color bodyStart = MenuTheme.Current.Main;
        Color outlineStart = MenuTheme.Current.Button;
        Color bodyPressed = MenuTheme.Current.Button;
        Color outlinePressed = MenuTheme.Current.ButtonLight;
        Color imagePressed = MenuTheme.Current.ButtonLight;
        Color[] imageStartColors = uiImages != null
            ? uiImages.Select(image => SafeGetImageColor(image, MenuTheme.Current.Main)).ToArray()
            : Array.Empty<Color>();

        SafeSetRendererColor(bodyRenderer, bodyStart);
        if (outlineRenderers != null)
            foreach (Renderer outline in outlineRenderers)
                SafeSetRendererColor(outline, outlineStart);

        const float duration = 0.12f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = EaseOut(Mathf.Clamp01(t / duration));
            SafeSetRendererColor(bodyRenderer, Color.Lerp(bodyStart, bodyPressed, p));
            if (outlineRenderers != null)
                foreach (Renderer outline in outlineRenderers)
                    SafeSetRendererColor(outline, Color.Lerp(outlineStart, outlinePressed, p));
            if (uiImages != null)
                for (int i = 0; i < uiImages.Length; i++)
                    SafeSetImageColor(uiImages[i], Color.Lerp(imageStartColors[i], imagePressed, p));
            yield return null;
        }

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float p = EaseOut(Mathf.Clamp01(t / duration));
            SafeSetRendererColor(bodyRenderer, Color.Lerp(bodyPressed, bodyStart, p));
            if (outlineRenderers != null)
                foreach (Renderer outline in outlineRenderers)
                    SafeSetRendererColor(outline, Color.Lerp(outlinePressed, outlineStart, p));
            if (uiImages != null)
                for (int i = 0; i < uiImages.Length; i++)
                    SafeSetImageColor(uiImages[i], Color.Lerp(imagePressed, imageStartColors[i], p));
            yield return null;
        }

        SafeSetRendererColor(bodyRenderer, bodyStart);
        if (outlineRenderers != null)
            foreach (Renderer outline in outlineRenderers)
                SafeSetRendererColor(outline, outlineStart);
        if (uiImages != null)
            for (int i = 0; i < uiImages.Length; i++)
                SafeSetImageColor(uiImages[i], imageStartColors[i]);

        if (button != null)
            button.KeyboardPressRoutine = null;
    }

    private static IEnumerator SliderEnumActivate(Transform knob, Transform slider, Renderer bodyRenderer, Renderer outlineRenderer)
    {
        if (knob == null || slider == null)
            yield break;

        float duration = 0.225f;
        float time = 0f;

        Vector3 startPosition = new Vector3(-0.00235f, 0f, 0f);
        Vector3 endPosition   = new Vector3( 0.00235f, 0f, 0f);

        SpriteRenderer sr = slider.GetComponent<SpriteRenderer>();
        UIImage sliderImage = slider.GetComponent<UIImage>();

        Color32 sliderStart   = MenuTheme.Current.ButtonBase;
        Color32 sliderTarget  = MenuTheme.Current.Accent;
        Color32 bodyStart     = MenuTheme.Current.Main;
        Color32 bodyTarget    = MenuTheme.Current.Button;
        Color32 outlineStart  = MenuTheme.Current.Button;
        Color32 outlineTarget = MenuTheme.Current.ButtonLight;

        knob.localPosition = startPosition;

        while (time < duration)
        {

            if (knob == null || slider == null)
                yield break;

            float easeT = EaseOut(time / duration);

            knob.localPosition = Vector3.Lerp(startPosition, endPosition, easeT);
            if (sr != null) sr.color = Color.Lerp(sliderStart, sliderTarget, easeT);
            SafeSetImageColor(sliderImage, Color.Lerp(sliderStart, sliderTarget, easeT));
            SafeSetRendererColor(bodyRenderer, Color.Lerp(bodyStart, bodyTarget, easeT));
            SafeSetRendererColor(outlineRenderer, Color.Lerp(outlineStart, outlineTarget, easeT));

            time += Time.deltaTime;
            yield return null;
        }

        if (knob == null || slider == null)
            yield break;

        knob.localPosition = endPosition;
        if (sr != null) sr.color = sliderTarget;
        SafeSetImageColor(sliderImage, sliderTarget);
        SafeSetRendererColor(bodyRenderer, bodyTarget);
        SafeSetRendererColor(outlineRenderer, outlineTarget);
    }

    private static IEnumerator SliderEnumDeActivate(Transform knob, Transform slider, Renderer bodyRenderer, Renderer outlineRenderer)
    {
        if (knob == null || slider == null)
            yield break;

        float duration = 0.225f;
        float time = 0f;

        Vector3 startPosition = new Vector3( 0.00235f, 0f, 0f);
        Vector3 endPosition   = new Vector3(-0.00235f, 0f, 0f);

        SpriteRenderer sr = slider.GetComponent<SpriteRenderer>();
        UIImage sliderImage = slider.GetComponent<UIImage>();

        Color32 sliderStart   = MenuTheme.Current.Accent;
        Color32 sliderTarget  = MenuTheme.Current.ButtonBase;
        Color32 bodyStart     = MenuTheme.Current.Button;
        Color32 bodyTarget    = MenuTheme.Current.Main;
        Color32 outlineStart  = MenuTheme.Current.ButtonLight;
        Color32 outlineTarget = MenuTheme.Current.Button;

        knob.localPosition = startPosition;

        while (time < duration)
        {

            if (knob == null || slider == null)
                yield break;

            float easeT = EaseOut(time / duration);

            knob.localPosition = Vector3.Lerp(startPosition, endPosition, easeT);
            if (sr != null) sr.color = Color.Lerp(sliderStart, sliderTarget, easeT);
            SafeSetImageColor(sliderImage, Color.Lerp(sliderStart, sliderTarget, easeT));
            SafeSetRendererColor(bodyRenderer, Color.Lerp(bodyStart, bodyTarget, easeT));
            SafeSetRendererColor(outlineRenderer, Color.Lerp(outlineStart, outlineTarget, easeT));

            time += Time.deltaTime;
            yield return null;
        }

        if (knob == null || slider == null)
            yield break;

        knob.localPosition = endPosition;
        if (sr != null) sr.color = sliderTarget;
        SafeSetImageColor(sliderImage, sliderTarget);
        SafeSetRendererColor(bodyRenderer, bodyTarget);
        SafeSetRendererColor(outlineRenderer, outlineTarget);
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
        
        if (btnObj != null)
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
    public bool       YawOnly;
    public bool       UseYawDeadzone;
    public float      YawActivationDegrees = 30f;
    private float     anchoredYaw;
    private bool      hasYawAnchor;

    private void OnEnable()
    {
        if (Target != null)
            Snap();
    }

    public void Snap()
    {
        if (Target == null)
            return;

        Quaternion targetRotation = GetTargetRotation();
        transform.position = Target.position + targetRotation * LocalPosition;
        transform.rotation = targetRotation * LocalRotation;
    }

    public void ResetDeadzoneAnchor()
    {
        hasYawAnchor = false;
    }

    private Quaternion GetTargetRotation()
    {
        if (!YawOnly)
            return Target.rotation;

        float targetYaw = Target.eulerAngles.y;
        if (!UseYawDeadzone)
        {
            anchoredYaw = targetYaw;
            hasYawAnchor = true;
            return Quaternion.Euler(0f, targetYaw, 0f);
        }

        if (!hasYawAnchor)
        {
            anchoredYaw = targetYaw;
            hasYawAnchor = true;
        }
        else if (Mathf.Abs(Mathf.DeltaAngle(anchoredYaw, targetYaw)) >= YawActivationDegrees)
        {
            anchoredYaw = targetYaw;
        }

        return Quaternion.Euler(0f, anchoredYaw, 0f);
    }

    public bool Frozen;

    private void LateUpdate()
    {
        if (Target == null || Frozen) return;

        Quaternion targetRotation = GetTargetRotation();
        float t = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, Target.position + targetRotation * LocalPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation * LocalRotation, t);
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
        public static readonly MenuThemePalette Default = new()
        {
            SakuraParts = true
        };

        public static readonly MenuThemePalette Sakura = new()
        {
            Main = new Color32(18, 10, 16, 255),
            Border = new Color32(12, 6, 10, 255),
            Button = new Color32(28, 16, 24, 255),
            ButtonLight = new Color32(56, 32, 48, 255),
            ButtonBase = new Color32(16, 8, 14, 255),
            Accent = new Color32(255, 143, 186, 255),
            Mid = new Color32(14, 8, 12, 255),
            MidDark = new Color32(10, 6, 9, 255),
            DarkAccent = new Color32(84, 36, 58, 255),
            LightMain = new Color32(32, 18, 28, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Ocean = new()
        {
            Main = new Color32(8, 14, 20, 255),
            Border = new Color32(5, 10, 15, 255),
            Button = new Color32(14, 24, 34, 255),
            ButtonLight = new Color32(30, 52, 72, 255),
            ButtonBase = new Color32(8, 16, 24, 255),
            Accent = new Color32(94, 196, 232, 255),
            Mid = new Color32(7, 12, 18, 255),
            MidDark = new Color32(5, 9, 14, 255),
            DarkAccent = new Color32(28, 70, 92, 255),
            LightMain = new Color32(18, 30, 42, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Ember = new()
        {
            Main = new Color32(18, 10, 8, 255),
            Border = new Color32(12, 6, 5, 255),
            Button = new Color32(28, 16, 12, 255),
            ButtonLight = new Color32(58, 34, 24, 255),
            ButtonBase = new Color32(16, 10, 8, 255),
            Accent = new Color32(255, 122, 64, 255),
            Mid = new Color32(14, 8, 6, 255),
            MidDark = new Color32(10, 6, 5, 255),
            DarkAccent = new Color32(92, 42, 24, 255),
            LightMain = new Color32(32, 18, 14, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Mint = new()
        {
            Main = new Color32(8, 16, 14, 255),
            Border = new Color32(5, 11, 10, 255),
            Button = new Color32(14, 26, 22, 255),
            ButtonLight = new Color32(32, 58, 48, 255),
            ButtonBase = new Color32(8, 18, 15, 255),
            Accent = new Color32(110, 231, 183, 255),
            Mid = new Color32(7, 14, 12, 255),
            MidDark = new Color32(5, 10, 9, 255),
            DarkAccent = new Color32(28, 78, 60, 255),
            LightMain = new Color32(18, 34, 28, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Midnight = new()
        {
            Main = new Color32(8, 10, 22, 255),
            Border = new Color32(5, 6, 14, 255),
            Button = new Color32(14, 16, 34, 255),
            ButtonLight = new Color32(32, 36, 72, 255),
            ButtonBase = new Color32(8, 10, 24, 255),
            Accent = new Color32(122, 148, 255, 255),
            Mid = new Color32(7, 8, 18, 255),
            MidDark = new Color32(5, 6, 14, 255),
            DarkAccent = new Color32(36, 42, 96, 255),
            LightMain = new Color32(18, 20, 42, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Amber = new()
        {
            Main = new Color32(16, 12, 6, 255),
            Border = new Color32(11, 8, 4, 255),
            Button = new Color32(26, 20, 10, 255),
            ButtonLight = new Color32(54, 42, 20, 255),
            ButtonBase = new Color32(14, 11, 6, 255),
            Accent = new Color32(245, 186, 72, 255),
            Mid = new Color32(13, 10, 5, 255),
            MidDark = new Color32(9, 7, 4, 255),
            DarkAccent = new Color32(92, 64, 22, 255),
            LightMain = new Color32(30, 24, 12, 255),
            SakuraParts = true
        };

        public static readonly MenuThemePalette Crimson = new()
        {
            Main = new Color32(16, 8, 10, 255),
            Border = new Color32(11, 5, 7, 255),
            Button = new Color32(28, 12, 16, 255),
            ButtonLight = new Color32(58, 24, 32, 255),
            ButtonBase = new Color32(14, 8, 10, 255),
            Accent = new Color32(239, 71, 111, 255),
            Mid = new Color32(13, 6, 8, 255),
            MidDark = new Color32(9, 4, 6, 255),
            DarkAccent = new Color32(96, 28, 42, 255),
            LightMain = new Color32(30, 14, 18, 255),
            SakuraParts = true
        };
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
        ApplyKeyboardSafe(keyboard, "Main", Current.Border);
        ApplyKeyboardSafe(keyboard, "Border", Current.Border);
        ApplyKeyboardDirectSafe(keyboard, "Outline", Current.Button);
    }

    public static void ApplyKeyboardKey(Transform key, out Renderer bodyRenderer, out Renderer outlineRenderer)
    {
        ApplyKeyboardKey(key, out bodyRenderer, out Renderer[] outlineRenderers);
        outlineRenderer = outlineRenderers.Length > 0 ? outlineRenderers[0] : null;
    }

    public static void ApplyKeyboardKey(Transform key, out Renderer bodyRenderer, out Renderer[] outlineRenderers)
    {
        bodyRenderer = null;
        outlineRenderers = Array.Empty<Renderer>();
        if (IsProtectedKeyboardVisual(key))
            return;

        bodyRenderer = ApplyRenderer(key, Current.Main);
        UIImage image = key.GetComponent<UIImage>() ?? key.GetComponentInChildren<UIImage>(true);
        if (image != null) image.color = Current.Main;

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

    private static void ApplyKeyboardSafe(Transform root, string name, Color32 color)
    {
        if (root == null) return;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase) && !IsProtectedKeyboardVisual(child))
                ApplyRenderer(child, color);
        }
    }

    private static void ApplyKeyboardDirectSafe(Transform root, string name, Color32 color)
    {
        if (root == null) return;

        foreach (Transform child in root)
        {
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase) && !IsProtectedKeyboardVisual(child))
                ApplyRenderer(child, color);
        }
    }

    private static bool IsProtectedKeyboardVisual(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name.Equals("Base", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("Enter", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        TMP_Text text = transform != null ? transform.GetComponent<TMP_Text>() ?? transform.GetComponentInChildren<TMP_Text>(true) : null;
        if (text == null)
            return false;

        string value = text.text?.Trim();
        return value != null && (value.Equals("Enter", StringComparison.OrdinalIgnoreCase)
                                 || value.Equals("Delete", StringComparison.OrdinalIgnoreCase));
    }
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

        UIImage image = target.GetComponent<UIImage>();
        if (image != null)
            image.color = color;

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

