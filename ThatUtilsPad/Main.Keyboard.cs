using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using GorillaLocomotion;
using GorillaNetworking;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PlayFab;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public partial class Main
{
    private void InitKeyboard()
    {
        Transform keyboard = menuObj.transform.Find("Keyboard");
        if (keyboard == null)
        {
            Debug.LogError("[TUP] Keyboard not found in menu prefab");
            return;
        }

        keyboard.gameObject.SetActive(false);
        MenuTheme.ApplyKeyboard(keyboard);

        HashSet<Transform> keyRoots = new HashSet<Transform>();
        List<Renderer> keyboardOutlines = GetKeyboardOutlines(keyboard);
        foreach (Transform key in keyboard.GetComponentsInChildren<Transform>(true))
        {
            if (key == keyboard || IsKeyboardNonKeyPath(key, keyboard))
                continue;

            string keyName = ReadKeyName(key);
            if (string.IsNullOrEmpty(keyName))
                continue;

            Transform keyRoot = GetKeyboardKeyRoot(key, keyboard);
            if (keyRoot == null || !keyRoots.Add(keyRoot))
                continue;

            bool protectedVisual = IsProtectedKeyboardVisualRoot(keyRoot, keyName);
            SetLayerRecursive(keyRoot, 2);

            Collider kc = keyRoot.GetComponent<Collider>();
            if (kc == null) kc = keyRoot.gameObject.AddComponent<BoxCollider>();
            FitKeyboardCollider(keyRoot, kc);
            kc.isTrigger = true;

            Rigidbody krb = keyRoot.GetComponent<Rigidbody>();
            if (krb != null) Destroy(krb);

            ButtonTrigger trigger = keyRoot.GetComponent<ButtonTrigger>() ?? keyRoot.gameObject.AddComponent<ButtonTrigger>();
            trigger.IsKeyboardKey = true;
            trigger.BtnIdentifier = keyName;
            trigger.ButtonRoot = keyRoot;
            Renderer bodyRenderer = null;
            Renderer[] outlineRenderers = Array.Empty<Renderer>();
            if (!protectedVisual)
                MenuTheme.ApplyKeyboardKey(keyRoot, out bodyRenderer, out outlineRenderers);
            if (!protectedVisual)
            {
                Renderer siblingOutline = FindNearestKeyboardOutline(keyRoot, keyboardOutlines);
                if (siblingOutline != null)
                    outlineRenderers = outlineRenderers.Concat(new[] { siblingOutline }).Distinct().ToArray();
            }
            trigger.BodyRenderer = bodyRenderer;
            trigger.OutlineRenderer = outlineRenderers.Length > 0 ? outlineRenderers[0] : null;
            trigger.KeyboardOutlineRenderers = outlineRenderers;
            trigger.KeyboardImages = protectedVisual
                ? Array.Empty<Image>()
                : keyRoot.GetComponentsInChildren<Image>(true).Where(image => image != null).Distinct().ToArray();
            string captured = keyName;
            trigger.CustomAction = () => HandleKeyInput(captured);

            ButtonCollider keyCollider = keyRoot.GetComponent<ButtonCollider>() ?? keyRoot.gameObject.AddComponent<ButtonCollider>();
            keyCollider.trigger = trigger;
        }
    }



    private static bool IsKeyboardNonKeyPath(Transform transform, Transform keyboard)
    {
        for (Transform current = transform; current != null && current != keyboard; current = current.parent)
        {
            if (current.name.Equals("Base", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("Preview", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("Top", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
    private static bool IsKeyboardBasePath(Transform transform, Transform keyboard)
    {
        for (Transform current = transform; current != null && current != keyboard; current = current.parent)
            if (current.name.Equals("Base", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private static bool IsProtectedKeyboardVisualRoot(Transform keyRoot, string keyName)
    {
        if (keyRoot == null)
            return true;

        if (keyRoot.name.Equals("Base", StringComparison.OrdinalIgnoreCase))
            return true;

        return keyName.Equals("Enter", StringComparison.OrdinalIgnoreCase)
               || keyName.Equals("Delete", StringComparison.OrdinalIgnoreCase);
    }
    private static void FitKeyboardCollider(Transform keyRoot, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (box == null)
            return;

        RectTransform rect = keyRoot.GetComponent<RectTransform>();
        if (rect == null)
            rect = keyRoot.GetComponentInChildren<RectTransform>(true);
        if (rect == null)
            return;

        Vector2 size = rect.rect.size;
        if (size.sqrMagnitude <= 0.001f)
            size = rect.sizeDelta;
        if (size.sqrMagnitude <= 0.001f)
            return;

        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(0.02f, size.x), Mathf.Max(0.02f, size.y), 0.02f);
    }
    private static List<Renderer> GetKeyboardOutlines(Transform keyboard)
    {
        List<Renderer> outlines = new List<Renderer>();
        foreach (Transform child in keyboard)
        {
            if (!child.name.Equals("Outline", StringComparison.OrdinalIgnoreCase))
                continue;

            Renderer renderer = MenuTheme.ApplyKeyboardOutline(child);
            if (renderer != null)
                outlines.Add(renderer);
        }

        return outlines;
    }

    private static Renderer FindNearestKeyboardOutline(Transform key, List<Renderer> outlines)
    {
        Renderer nearest = null;
        float nearestDistance = float.MaxValue;
        Vector3 keyPosition = key.localPosition;

        foreach (Renderer outline in outlines)
        {
            if (outline == null)
                continue;

            float distance = (outline.transform.localPosition - keyPosition).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = outline;
        }

        return nearest;
    }

    private static Transform GetKeyboardKeyRoot(Transform key, Transform keyboard)
    {
        if (key == null || keyboard == null)
            return null;

        if (key.GetComponent<TMP_Text>() != null && key.parent != null && key.parent != keyboard)
            return key.parent;

        return key.parent == keyboard || key.GetComponent<Collider>() != null || key.GetComponent<Renderer>() != null || key.GetComponent<Image>() != null || key.GetComponent<RawImage>() != null
            ? key
            : null;
    }

    private static string ReadKeyName(Transform key)
    {
        string keyName = CleanKeyName(key.name);
        if (!string.IsNullOrEmpty(keyName))
            return keyName;

        TMP_Text text = key.GetComponent<TMP_Text>();
        if (text != null)
            return CleanKeyName(text.text);

        foreach (Transform child in key)
        {
            if (!child.name.Equals("ButtonText", StringComparison.OrdinalIgnoreCase)
                && !child.name.Equals("Text", StringComparison.OrdinalIgnoreCase))
                continue;

            text = child.GetComponent<TMP_Text>();
            if (text != null)
                return CleanKeyName(text.text);
        }

        return "";
    }

    private static string CleanKeyName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string key = raw.Trim();
        key = key.Replace("\r", "").Replace("\n", "").Trim();
        if (key.Length == 0)
            return "";

        string upper = key.ToUpperInvariant();
        return upper switch
        {
            "ENTER" or "RETURN" => "Enter",
            "DELETE" or "DEL" or "BACKSPACE" => "Delete",
            "SPACE" or "SPACEBAR" => "Space",
            "CLOSE" or "ESC" or "ESCAPE" => "Close",
            _ when key.Length == 1 => key,
            _ => "",
        };
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;

        foreach (Transform child in root)
            SetLayerRecursive(child, layer);
    }

    public static void ShowKeyboard(GameObject keyboard)
    {
        if (keyboard == null)
            return;

        if (!KeyboardScales.TryGetValue(keyboard, out Vector3 targetScale))
        {
            targetScale = keyboard.transform.localScale * 1.35f;
            KeyboardScales[keyboard] = targetScale;
        }

        StopKeyboardScaleRoutine(keyboard);
        keyboard.SetActive(true);
        keyboard.transform.localScale = Vector3.zero;
        KeyboardScaleRoutines[keyboard] = Instance.StartCoroutine(ScaleKeyboard(keyboard, targetScale, true));
    }

    public static void HideKeyboard(GameObject keyboard)
    {
        if (keyboard == null)
            return;

        if (!KeyboardScales.TryGetValue(keyboard, out Vector3 targetScale))
            targetScale = keyboard.transform.localScale;

        StopKeyboardScaleRoutine(keyboard);
        KeyboardScaleRoutines[keyboard] = Instance.StartCoroutine(ScaleKeyboard(keyboard, targetScale, false));
    }

    private static void StopKeyboardScaleRoutine(GameObject keyboard)
    {
        if (keyboard == null || !KeyboardScaleRoutines.TryGetValue(keyboard, out Coroutine routine) || routine == null)
            return;

        Instance.StopCoroutine(routine);
        KeyboardScaleRoutines.Remove(keyboard);
    }

    private static IEnumerator ScaleKeyboard(GameObject keyboard, Vector3 targetScale, bool show)
    {
        if (keyboard == null)
            yield break;

        keyboard.SetActive(true);
        Vector3 from = show ? Vector3.zero : keyboard.transform.localScale;
        Vector3 to = show ? targetScale : Vector3.zero;

        yield return MenuEffects.PopMenu(keyboard, to, from, show);

        if (keyboard == null)
            yield break;

        keyboard.transform.localScale = show ? targetScale : targetScale;
        if (!show)
            keyboard.SetActive(false);

        KeyboardScaleRoutines.Remove(keyboard);
    }


    public void SetKeyboardPreview(GameObject keyboard, string text, bool animateLastCharacter)
    {
        if (keyboard == null)
            return;

        TMP_Text preview = GetKeyboardPreviewText(keyboard);
        if (preview == null)
            return;

        preview.gameObject.SetActive(true);
        preview.enabled = true;
        preview.text = text ?? "";
        preview.ForceMeshUpdate();

        if (keyboardPreviewCharRoutine != null)
        {
            StopCoroutine(keyboardPreviewCharRoutine);
            keyboardPreviewCharRoutine = null;
        }

        if (animateLastCharacter && !string.IsNullOrEmpty(preview.text))
            keyboardPreviewCharRoutine = StartCoroutine(ScaleKeyboardPreviewCharacter(preview, preview.text.Length - 1));
    }

    private TMP_Text GetKeyboardPreviewText(GameObject keyboard)
    {
        if (keyboard == null)
            return null;

        if (keyboardPreviewText != null && keyboardPreviewText.transform != null && keyboardPreviewText.transform.IsChildOf(keyboard.transform))
            return keyboardPreviewText;

        Transform previewText = keyboard.transform.Find("Top/Title") ?? keyboard.transform.Find("Preview/Text");
        if (previewText == null)
        {
            Transform top = FindChildByName(keyboard.transform, "Top");
            previewText = top != null ? top.Find("Title") ?? FindChildByName(top, "Title") : null;
        }
        if (previewText == null)
        {
            Transform preview = FindChildByName(keyboard.transform, "Preview");
            previewText = preview != null ? preview.Find("Text") ?? FindChildByName(preview, "Text") : null;
        }

        keyboardPreviewText = previewText != null
            ? previewText.GetComponent<TMP_Text>() ?? previewText.GetComponentInChildren<TMP_Text>(true)
            : null;

        if (keyboardPreviewText == null)
            Debug.LogWarning("[TUP] Keyboard preview text missing at Keyboard/Top/Title or Keyboard/Preview/Text");

        return keyboardPreviewText;
    }

    private IEnumerator ScaleKeyboardPreviewCharacter(TMP_Text preview, int charIndex)
    {
        const float duration = 0.16f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(0.05f, 1f, t * t * (3f - 2f * t));
            ApplyKeyboardPreviewCharacterScale(preview, charIndex, scale);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyKeyboardPreviewCharacterScale(preview, charIndex, 1f);
        if (keyboardPreviewCharRoutine != null)
            keyboardPreviewCharRoutine = null;
    }

    private static void ApplyKeyboardPreviewCharacterScale(TMP_Text preview, int charIndex, float scale)
    {
        if (preview == null || charIndex < 0)
            return;

        preview.ForceMeshUpdate();
        TMP_TextInfo textInfo = preview.textInfo;
        if (textInfo == null || charIndex >= textInfo.characterCount)
            return;

        TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
        if (!charInfo.isVisible)
            return;

        int materialIndex = charInfo.materialReferenceIndex;
        int vertexIndex = charInfo.vertexIndex;
        Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
        Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;

        for (int i = 0; i < 4; i++)
            vertices[vertexIndex + i] = center + ((vertices[vertexIndex + i] - center) * scale);

        preview.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }
    private void HandleKeyInput(string key)
    {
        switch (key)
        {
            case "Enter":  Mods.ConfirmRename(); break;
            case "Delete": Mods.DeleteChar();    break;
            case "Space":  Mods.TypeChar(' ');   break;
            case "Close":  Mods.CancelRename();  break;
            default:
                if (key.Length == 1) Mods.TypeChar(key[0]);
                break;
        }
    }
}
