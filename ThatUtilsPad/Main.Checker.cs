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
    private TMP_Text FindText(GameObject obj, string parentName)
    {
        Transform parent = obj.transform.Find(parentName) ?? FindChildByName(obj.transform, parentName);
        if (parent == null)
        {
            Debug.LogError($"[TUP] Missing parent: {parentName}");
            return null;
        }

        TMP_Text tmp = parent.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            Debug.LogError($"[TUP] No TMP_Text under: {parentName}");

        return tmp;
    }

    private TMP_Text FindTextPath(Transform root, string path)
    {
        Transform target = root.Find(path);
        if (target == null)
        {
            string[] parts = path.Split('/' );
            target = root;
            foreach (string part in parts)
            {
                target = FindChildByName(target, part);
                if (target == null) break;
            }
        }

        if (target == null)
        {
            Debug.LogError("[TUP] Missing checker text path: " + path);
            return null;
        }

        TMP_Text tmp = target.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null)
            Debug.LogError("[TUP] No TMP_Text under checker path: " + path);
        return tmp;
    }


    private TMP_Text FindCheckerTextAny(Transform root, params string[] names)
    {
        if (root == null)
            return null;

        foreach (string name in names)
        {
            Transform target = root.Find(name) ?? FindChildByName(root, name);
            TMP_Text text = target != null ? target.GetComponent<TMP_Text>() ?? target.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
                return text;
        }

        Debug.LogError("[TUP] Missing checker FPS/Ping text");
        return null;
    }

    private static void MakeCheckerTextShow(TMP_Text text)
    {
        if (text == null)
            return;

        text.gameObject.SetActive(true);
        text.enabled = true;
        Color color = text.color;
        if (color.a <= 0.01f)
        {
            color.a = 1f;
            text.color = color;
        }
    }
    private void InitCheckerTileGroup(Transform side, string titleName, out GameObject[] tileObjs, out TMP_Text[] tileTexts, TMP_FontAsset font)
    {
        string[] tileNames = { "Tile", "Tile2", "Tile3" };
        CheckerTileGroup group = new CheckerTileGroup
        {
            TitleName = titleName,
            Tiles = new GameObject[tileNames.Length],
            Texts = new TMP_Text[tileNames.Length],
            TileRects = new RectTransform[tileNames.Length],
            BackObjs = new GameObject[tileNames.Length],
            BackRects = new RectTransform[tileNames.Length],
            BackSizes = new Vector2[tileNames.Length],
            OutlineRects = new RectTransform[tileNames.Length]
        };

        tileObjs = group.Tiles;
        tileTexts = group.Texts;

        Transform title = FindChildByName(side, titleName);
        Transform main = side.Find(titleName + "/Main") ?? FindChildByName(title, "Main");
        if (main == null)
        {
            Debug.LogError("[TUP] Missing checker tile holder: " + titleName + "/Main");
            return;
        }

        Transform noneTitle = main.Find("NoneTitle") ?? main.Find("NoneTile") ?? FindChildByName(main, "NoneTitle") ?? FindChildByName(main, "NoneTile");
        if (noneTitle != null)
        {
            group.NoneTitle = noneTitle.gameObject;
            group.NoneRect = noneTitle as RectTransform ?? noneTitle.GetComponent<RectTransform>();
            group.NoneText = noneTitle.GetComponent<TMP_Text>() ?? noneTitle.GetComponentInChildren<TMP_Text>(true);
            if (group.NoneText != null && font != null)
                group.NoneText.font = font;
            group.NoneTitle.SetActive(false);
            SetRectWidth(group.NoneRect, 0f);
        }

        for (int i = 0; i < tileNames.Length; i++)
        {
            Transform tile = main.Find(tileNames[i]) ?? FindChildByName(main, tileNames[i]);
            if (tile == null)
            {
                Debug.LogError("[TUP] Missing checker tile: " + titleName + "/Main/" + tileNames[i]);
                continue;
            }

            int index = i;
            group.Tiles[i] = tile.gameObject;
            group.TileRects[i] = tile as RectTransform ?? tile.GetComponent<RectTransform>();
            group.Texts[i] = tile.GetComponentInChildren<TMP_Text>(true);
            Transform outline = tile.Find("Outline") ?? FindChildByName(tile, "Outline");
            group.OutlineRects[i] = outline as RectTransform ?? outline?.GetComponent<RectTransform>();
            if (group.Texts[i] != null && font != null)
                group.Texts[i].font = font;

            Transform back = tile.Find("Back") ?? FindChildByName(tile, "Back");
            if (back != null)
            {
                group.BackObjs[i] = back.gameObject;
                group.BackRects[i] = back as RectTransform ?? back.GetComponent<RectTransform>();
                group.BackSizes[i] = group.BackRects[i] != null ? group.BackRects[i].sizeDelta : Vector2.zero;
                back.gameObject.SetActive(false);
                RestoreCheckerBackRect(group, i);
                if (group.BackObjs[i] != null) group.BackObjs[i].transform.localScale = Vector3.zero;
                Transform backCollider = back.Find("Collider") ?? FindChildByName(back, "Collider");
                if (backCollider != null)
                    WireInteractive(back, titleName + "_TileBack_" + i, () => UnfocusCheckerTile(group), backCollider);
            }

            Transform tileCollider = tile.Find("Collider") ?? FindChildByName(tile, "Collider");
            if (tileCollider != null)
                WireInteractive(tile, titleName + "_TileFocus_" + i, () => FocusCheckerTile(group, index), tileCollider);

            SetCheckerTileWidth(group, i, CheckerTileDefaultWidth);
            tile.gameObject.SetActive(false);
        }

        if (titleName.Equals("ModsTitle", StringComparison.OrdinalIgnoreCase))
            modsTileGroup = group;
        else if (titleName.Equals("CheatsTitle", StringComparison.OrdinalIgnoreCase))
            cheatsTileGroup = group;
    }

    private void SetCheckerTiles(CheckerTileGroup group, string[] values)
    {
        if (group == null)
            return;

        values ??= Array.Empty<string>();
        group.Values = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        group.FocusedIndex = -1;
        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(ApplyCheckerTiles(group));
    }

    private IEnumerator ApplyCheckerTiles(CheckerTileGroup group)
    {
        bool any = group.Values.Length > 0;
        if (group.NoneTitle != null)
        {
            group.NoneTitle.SetActive(!any);
            if (!any)
            {
                if (group.NoneText != null)
                    group.NoneText.text = "NONE DETECTED";
                yield return AnimateRectWidth(group.NoneRect, GetRectWidth(group.NoneRect), CheckerTileExpandedWidth, CheckerTileDuration, false);
            }
            else
            {
                SetRectWidth(group.NoneRect, 0f);
                group.NoneTitle.SetActive(false);
            }
        }

        for (int i = 0; i < group.Tiles.Length; i++)
        {
            bool visible = i < group.Values.Length;
            if (group.Tiles[i] != null)
                group.Tiles[i].SetActive(visible);
            if (group.BackObjs[i] != null)
                group.BackObjs[i].SetActive(false);
            RestoreCheckerBackRect(group, i);
            if (group.BackObjs[i] != null) group.BackObjs[i].transform.localScale = Vector3.zero;
            if (visible)
            {
                SetCheckerTileWidth(group, i, CheckerTileDefaultWidth);
                if (group.Texts[i] != null)
                {
                    group.Texts[i].text = ShortTileName(group.Values[i]);
                    group.Texts[i].alpha = 1f;
                }
            }
            else
            {
                SetCheckerTileWidth(group, i, 0f);
            }
        }
    }

    private void FocusCheckerTile(CheckerTileGroup group, int index)
    {
        if (group == null || index < 0 || index >= group.Values.Length || group.FocusedIndex == index)
            return;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(FocusCheckerTileRoutine(group, index));
    }

    private IEnumerator FocusCheckerTileRoutine(CheckerTileGroup group, int index)
    {
        group.FocusedIndex = index;

        yield return FadeOtherCheckerTileTexts(group, index, 1f, 0f, CheckerTileTextFadeDuration);
        yield return AnimateOtherCheckerTilesWidth(group, index, 0f, CheckerTileWidthDuration, true);

        if (group.Texts[index] != null)
        {
            group.Texts[index].alpha = 1f;
            group.Texts[index].text = group.Values[index];
        }

        yield return AnimateCheckerTileWidth(group, index, GetRectWidth(group.TileRects[index]), CheckerTileExpandedWidth, CheckerTileWidthDuration, false);

        if (group.BackObjs[index] != null)
        {
            group.BackObjs[index].SetActive(true);
            RestoreCheckerBackRect(group, index);
            group.BackObjs[index].transform.localScale = Vector3.zero;
            yield return AnimateLocalScale(group.BackObjs[index].transform, Vector3.zero, CheckerBackTargetScale, CheckerTileBackDuration);
        }
    }

    private void UnfocusCheckerTile(CheckerTileGroup group)
    {
        if (group == null || group.FocusedIndex < 0)
            return;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(UnfocusCheckerTileRoutine(group));
    }

    private IEnumerator UnfocusCheckerTileRoutine(CheckerTileGroup group)
    {
        int focused = group.FocusedIndex;
        group.FocusedIndex = -1;

        if (focused >= 0 && focused < group.BackObjs.Length && group.BackObjs[focused] != null)
        {
            yield return AnimateLocalScale(group.BackObjs[focused].transform, group.BackObjs[focused].transform.localScale, Vector3.zero, CheckerTileBackDuration);
            group.BackObjs[focused].SetActive(false);
            RestoreCheckerBackRect(group, focused);
        }

        if (focused >= 0 && focused < group.TileRects.Length)
        {
            if (group.Texts[focused] != null)
            {
                group.Texts[focused].text = ShortTileName(group.Values[focused]);
                group.Texts[focused].alpha = 1f;
            }

            yield return AnimateCheckerTileWidth(group, focused, GetRectWidth(group.TileRects[focused]), CheckerTileDefaultWidth, CheckerTileWidthDuration, false);
        }

        PrepareOtherCheckerTilesForReturn(group, focused);
        yield return AnimateOtherCheckerTilesWidth(group, focused, CheckerTileDefaultWidth, CheckerTileWidthDuration, false);
        yield return FadeOtherCheckerTileTexts(group, focused, 0f, 1f, CheckerTileTextFadeDuration);
    }

    private const float CheckerTileDefaultWidth = 394.8f;
    private const float CheckerTileExpandedWidth = 1263.3f;
    private const float CheckerTileDuration = 0.25f;
    private const float CheckerTileWidthDuration = 0.25f;
    private const float CheckerTileTextFadeDuration = 0.2f;
    private const float CheckerTileBackDuration = 0.2f;
    private static readonly Vector3 CheckerBackTargetScale = new Vector3(0.603059828f, 0.603059828f, 0.603059828f);


    private static IEnumerator FadeOtherCheckerTileTexts(CheckerTileGroup group, int focusedIndex, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        List<TMP_Text> texts = new List<TMP_Text>();
        for (int i = 0; i < group.Texts.Length; i++)
        {
            if (i == focusedIndex || i >= group.Values.Length || group.Texts[i] == null)
                continue;
            group.Texts[i].alpha = from;
            texts.Add(group.Texts[i]);
        }

        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            float alpha = Mathf.LerpUnclamped(from, to, p);
            foreach (TMP_Text text in texts)
                if (text != null)
                    text.alpha = alpha;
            yield return null;
        }

        foreach (TMP_Text text in texts)
            if (text != null)
                text.alpha = to;
    }

    private static void PrepareOtherCheckerTilesForReturn(CheckerTileGroup group, int focusedIndex)
    {
        if (group == null)
            return;

        for (int i = 0; i < group.Tiles.Length; i++)
        {
            if (i == focusedIndex || i >= group.Values.Length || group.Tiles[i] == null)
                continue;

            group.Tiles[i].SetActive(true);
            SetCheckerTileWidth(group, i, 0f);
            if (group.Texts[i] != null)
            {
                group.Texts[i].text = ShortTileName(group.Values[i]);
                group.Texts[i].alpha = 0f;
            }
        }
    }

    private static IEnumerator AnimateOtherCheckerTilesWidth(CheckerTileGroup group, int focusedIndex, float to, float duration, bool disableAtZero)
    {
        if (group == null)
            yield break;

        float[] from = new float[group.TileRects.Length];
        for (int i = 0; i < group.TileRects.Length; i++)
        {
            if (i == focusedIndex || i >= group.Values.Length || group.TileRects[i] == null)
                continue;
            group.Tiles[i]?.SetActive(true);
            from[i] = GetRectWidth(group.TileRects[i]);
        }

        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            for (int i = 0; i < group.TileRects.Length; i++)
            {
                if (i == focusedIndex || i >= group.Values.Length || group.TileRects[i] == null)
                    continue;
                SetCheckerTileWidth(group, i, Mathf.LerpUnclamped(from[i], to, p));
            }
            yield return null;
        }

        for (int i = 0; i < group.TileRects.Length; i++)
        {
            if (i == focusedIndex || i >= group.Values.Length || group.TileRects[i] == null)
                continue;
            SetCheckerTileWidth(group, i, to);
            if (disableAtZero && to <= 0.001f)
                group.Tiles[i]?.SetActive(false);
        }
    }
    private static IEnumerator AnimateCheckerTileWidth(CheckerTileGroup group, int index, float from, float to, float duration, bool disableAtZero)
    {
        RectTransform rect = group != null && index >= 0 && index < group.TileRects.Length ? group.TileRects[index] : null;
        RectTransform outline = group != null && index >= 0 && index < group.OutlineRects.Length ? group.OutlineRects[index] : null;
        yield return AnimateRectWidthPair(rect, outline, from, to, duration, disableAtZero);
    }

    private static IEnumerator AnimateRectWidthPair(RectTransform rect, RectTransform outline, float from, float to, float duration, bool disableAtZero)
    {
        if (outline == null)
        {
            yield return AnimateRectWidth(rect, from, to, duration, disableAtZero);
            yield break;
        }

        if (rect == null)
            yield break;

        GameObject owner = rect.gameObject;
        owner.SetActive(true);
        outline.gameObject.SetActive(true);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            float width = Mathf.LerpUnclamped(from, to, p);
            SetRectWidth(rect, width);
            SetRectWidth(outline, width);
            yield return null;
        }

        SetRectWidth(rect, to);
        SetRectWidth(outline, to);
        if (disableAtZero && to <= 0.001f)
            owner.SetActive(false);
    }

    private static IEnumerator AnimateRectWidth(RectTransform rect, float from, float to, float duration, bool disableAtZero)
    {
        if (rect == null)
            yield break;

        GameObject owner = rect.gameObject;
        owner.SetActive(true);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            SetRectWidth(rect, Mathf.LerpUnclamped(from, to, p));
            yield return null;
        }

        SetRectWidth(rect, to);
        if (disableAtZero && to <= 0.001f)
            owner.SetActive(false);
    }

    private static IEnumerator AnimateCheckerTextWave(TMP_Text text, string previousValue, string value)
    {
        if (text == null)
            yield break;

        previousValue ??= string.Empty;
        value ??= string.Empty;
        int revealStart = Math.Min(previousValue.Length, value.Length);
        if (value.Length <= revealStart)
        {
            text.text = value;
            yield break;
        }

        text.text = value;
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        int charCount = info.characterCount;
        if (charCount == 0)
            yield break;

        Vector3[][] baseVertices = new Vector3[info.meshInfo.Length][];
        for (int i = 0; i < info.meshInfo.Length; i++)
            baseVertices[i] = (Vector3[])info.meshInfo[i].vertices.Clone();

        float total = Mathf.Max(0f, (charCount - revealStart - 1) * 0.05f) + 0.125f;
        for (float time = 0f; time < total; time += Time.deltaTime)
        {
            for (int m = 0; m < info.meshInfo.Length; m++)
                Array.Copy(baseVertices[m], info.meshInfo[m].vertices, baseVertices[m].Length);

            for (int c = revealStart; c < charCount; c++)
            {
                TMP_CharacterInfo ch = info.characterInfo[c];
                if (!ch.isVisible)
                    continue;

                float p = EaseOutQuad(Mathf.Clamp01((time - (c - revealStart) * 0.05f) / 0.125f));
                int mat = ch.materialReferenceIndex;
                int vert = ch.vertexIndex;
                Vector3[] verts = info.meshInfo[mat].vertices;
                Vector3 center = (baseVertices[mat][vert] + baseVertices[mat][vert + 2]) * 0.5f;
                for (int v = 0; v < 4; v++)
                    verts[vert + v] = center + (baseVertices[mat][vert + v] - center) * p;
            }

            for (int m = 0; m < info.meshInfo.Length; m++)
                text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            yield return null;
        }

        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            Array.Copy(baseVertices[m], info.meshInfo[m].vertices, baseVertices[m].Length);
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    private static IEnumerator AnimateLocalScale(Transform target, Vector3 from, Vector3 to, float duration)
    {
        if (target == null)
            yield break;

        target.localScale = from;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            target.localScale = Vector3.LerpUnclamped(from, to, p);
            yield return null;
        }

        target.localScale = to;
    }
    private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

    private static float GetRectWidth(RectTransform rect) => rect != null ? rect.sizeDelta.x : 0f;
    private static void RestoreCheckerBackRect(CheckerTileGroup group, int index)
    {
        if (group == null || index < 0 || index >= group.BackRects.Length)
            return;

        RectTransform rect = group.BackRects[index];
        if (rect == null || index >= group.BackSizes.Length)
            return;

        Vector2 size = group.BackSizes[index];
        if (size == Vector2.zero)
            return;

        rect.sizeDelta = size;
    }
    private static void SetCheckerTileWidth(CheckerTileGroup group, int index, float width)
    {
        if (group == null || index < 0)
            return;
        if (index < group.TileRects.Length)
            SetRectWidth(group.TileRects[index], width);
        if (index < group.OutlineRects.Length)
            SetRectWidth(group.OutlineRects[index], width);
    }

    private static void SetRectWidth(RectTransform rect, float width)
    {
        if (rect == null)
            return;

        Vector2 size = rect.sizeDelta;
        size.x = Mathf.Max(0f, width);
        rect.sizeDelta = size;
    }

    private static string ShortTileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        return value.Length <= 9 ? value : value.Substring(0, 6) + "...";
    }    

    private void InitCheckerText(GameObject menuObj)
    {
        string figPrefabPath = "assets/fonts/figtree.asset";
        AssetBundle figBundle = FontCache.figtreeBundle;
        TMP_FontAsset figtreeFont = figBundle.LoadAsset<TMP_FontAsset>(figPrefabPath);

        GameObject side = menuObj.transform.Find("SideHolder").gameObject;
        Transform sideTransform = side.transform;
        nameTextComp        = FindText(side, "Name");
        platformTextComp    = FindText(side, "Platform");
        fpsPingTextComp     = FindCheckerTextAny(sideTransform, "FPS/Ping", "FPS", "Ping");
        fpsTextComp         = fpsPingTextComp;
        pingTextComp        = fpsPingTextComp;
        dateTextComp        = FindText(side, "Date");
        modsCountTextComp   = FindTextPath(sideTransform, "ModsTitle/NumberActive");
        cheatsCountTextComp = FindTextPath(sideTransform, "CheatsTitle/NumberActive");
        InitCheckerTileGroup(sideTransform, "ModsTitle", out modsTileObjs, out modsTileTextComps, figtreeFont);
        InitCheckerTileGroup(sideTransform, "CheatsTitle", out cheatsTileObjs, out cheatsTileTextComps, figtreeFont);
        
        if (nameTextComp != null) { nameTextComp.text = "-----"; nameTextComp.font = figtreeFont; }
        if (fpsPingTextComp != null) { fpsPingTextComp.text = "--Hz \u2022 --Ms"; fpsPingTextComp.font = figtreeFont; MakeCheckerTextShow(fpsPingTextComp); }
        if (platformTextComp != null) { platformTextComp.text = "Platform"; platformTextComp.font = figtreeFont; }
        if (dateTextComp != null) { dateTextComp.text = "Date: --/--/----"; dateTextComp.font = figtreeFont; }
        if (modsCountTextComp != null) { modsCountTextComp.text = "0"; modsCountTextComp.font = figtreeFont; }
        if (cheatsCountTextComp != null) { cheatsCountTextComp.text = "0"; cheatsCountTextComp.font = figtreeFont; }
        SetCheckerTiles(modsTileGroup, Array.Empty<string>());
        SetCheckerTiles(cheatsTileGroup, Array.Empty<string>());
        InitVolumeSlider(sideTransform);
        InitVolumeButtons(sideTransform);
    }

    private void InitVolumeButtons(Transform sideTransform)
    {
        WireVolumeButton(sideTransform, "PlusVolume", "PlusVolumeCollider", "VolumeUp");
        WireVolumeButton(sideTransform, "MinusVolume", "MinusVolumeCollider", "VolumeDown");
        WireVolumeButton(sideTransform, "Mute", "MuteCollider", "Mute");
        WireVolumeButton(sideTransform, "MuteElse", "MuteElseCollider", "MuteElse");
    }

    private void WireVolumeButton(Transform sideTransform, string visualName, string colliderName, string identifier)
    {
        Transform visual = FindChildAtPath(sideTransform, $"VolumeControls/Main/{visualName}") ?? FindChildByName(sideTransform, visualName);
        Transform collider = FindChildAtPath(sideTransform, $"VolumeControls/{colliderName}") ?? FindChildByName(sideTransform, colliderName);
        if (visual == null || collider == null)
        {
            Debug.LogWarning($"[TUP] Volume button missing visual={visualName} collider={colliderName}");
            return;
        }

        ButtonTrigger trigger = WireInteractive(visual, identifier, null, collider);
        trigger.ButtonRoot = visual;
    }

    private void InitVolumeSlider(Transform sideTransform)
    {
        Transform slider = FindChildAtPath(sideTransform, "VolumeControls/Main/Slider") ?? FindChildByName(sideTransform, "Slider");
        Transform inner = slider != null ? slider.Find("Inner") ?? FindChildByName(slider, "Inner") : null;
        if (slider == null || inner == null)
        {
            Debug.LogWarning("[TUP] Volume display missing. Need VolumeControls/Main/Slider/Inner.");
            return;
        }

        volumeInnerRect = inner as RectTransform ?? inner.GetComponent<RectTransform>();
        SetVolumeDisplayVolume(Mods.GetSelectedVolume(), false);
    }

    public void SetVolumeSliderValue01(float value)
    {
        SetVolumeDisplayVolume(Mathf.Clamp01(value) * 2f, true);
    }

    public void SetVolumeDisplayVolume(float volume, bool smooth = true)
    {
        float targetWidth = Mathf.Clamp(volume, 0f, 2f) * (VolumeDisplayMaxWidth * 0.5f);
        if (volumeInnerRect == null)
            return;

        if (volumeInnerRoutine != null)
            StopCoroutine(volumeInnerRoutine);
        if (smooth)
            volumeInnerRoutine = StartCoroutine(AnimateVolumeInnerWidth(GetRectWidth(volumeInnerRect), targetWidth));
        else
            SetRectWidth(volumeInnerRect, targetWidth);
    }

    private IEnumerator AnimateVolumeInnerWidth(float from, float to)
    {
        if (volumeInnerRect == null)
            yield break;

        for (float time = 0f; time < VolumeDisplayTweenDuration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / VolumeDisplayTweenDuration));
            SetRectWidth(volumeInnerRect, Mathf.LerpUnclamped(from, to, p));
            yield return null;
        }

        SetRectWidth(volumeInnerRect, to);
        volumeInnerRoutine = null;
    }

    private static void FitColliderToRect(RectTransform rect, Collider collider)
    {
        BoxCollider box = collider as BoxCollider;
        if (rect == null || box == null) return;
        Vector2 size = rect.rect.size;
        if (size.sqrMagnitude <= 0.001f) size = rect.sizeDelta;
        if (size.sqrMagnitude > 1f)
            size *= 0.0001f;
        if (size.sqrMagnitude <= 0.001f) return;
        box.center = Vector3.zero;
        box.size = new Vector3(Mathf.Max(0.002f, size.x), Mathf.Max(0.002f, size.y), 0.01f);
    }

    public void UpdateCheckerText(string plrName, int plrFPS, string plrPlatform, string plrCreationDate, string plrColor, Color colorBaseForm, int plrPing)
    {
        if (nameTextComp != null) nameTextComp.text = string.IsNullOrWhiteSpace(plrName) ? "-----" : plrName;
        if (fpsPingTextComp != null) { MakeCheckerTextShow(fpsPingTextComp); fpsPingTextComp.text = $"{plrFPS}Hz \u2022 {plrPing}Ms"; }
        if (platformTextComp != null) platformTextComp.text = string.IsNullOrWhiteSpace(plrPlatform) ? "Unknown" : plrPlatform;
        if (dateTextComp != null) dateTextComp.text = "Date: " + (string.IsNullOrWhiteSpace(plrCreationDate) ? "--/--/----" : plrCreationDate);
        Transform monkeColorTransform = Main.menuObj?.transform.Find("SideHolder/CheckerMonke/MonkeColor") ?? Main.menuObj?.transform.Find("SideHolder/MonkeColor");
        Image monkeColorImage = monkeColorTransform != null ? monkeColorTransform.GetComponent<Image>() : null;
        if (monkeColorImage != null) monkeColorImage.color = colorBaseForm;
        else
        {
            SpriteRenderer monkeColor = monkeColorTransform != null ? monkeColorTransform.GetComponent<SpriteRenderer>() : null;
            if (monkeColor != null) monkeColor.color = colorBaseForm;
        }
    }

    public void UpdateCheckerProperties(string legalMods, string illegalMods)
    {
        string[] legalList = SplitCheckerList(legalMods);
        string[] illegalList = SplitCheckerList(illegalMods);
        if (modsCountTextComp != null) modsCountTextComp.text = legalList.Length.ToString();
        if (cheatsCountTextComp != null) cheatsCountTextComp.text = illegalList.Length.ToString();
        SetCheckerTiles(modsTileGroup, legalList);
        SetCheckerTiles(cheatsTileGroup, illegalList);
    }

    private static string[] SplitCheckerList(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0 && !item.Equals("None", StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    TMP_Text roomInfoText;
    TMP_Text roomNameText;
    TMP_Text dateTimeText;

    private void InitRoomInfo(GameObject menuObj)
    {
        Transform menu = menuObj.transform;

        roomInfoText = FindChildByName(menu, "RoomInfo")?.GetComponent<TMP_Text>();
        roomNameText = FindChildByName(menu, "RoomName")?.GetComponent<TMP_Text>();
        dateTimeText = FindChildByName(menu, "DateTime")?.GetComponent<TMP_Text>();

        if (roomInfoText == null) Debug.LogError("RoomInfo not found");
        if (roomNameText == null) Debug.LogError("RoomName not found");
        if (dateTimeText == null) Debug.LogError("DateTime not found");
    }
    
    private IEnumerator RoomInfoUpdater()
    {
        WaitForSeconds wait = new WaitForSeconds(1f);

        while (true)
        {
            if (isMenuOpened)
            {
                string date = DateTime.Now.ToString("MM/dd/yy");
                string time = DateTime.Now.ToString("HH:mm");

                if (dateTimeText != null) dateTimeText.text = $"{date}   |   {time}";

                string roomName = Photon.Pun.PhotonNetwork.CurrentRoom?.Name ?? "----";
                if (roomNameText != null) roomNameText.text = roomName;

                int players = Photon.Pun.PhotonNetwork.CurrentRoom?.PlayerCount ?? 0;
                int max = Photon.Pun.PhotonNetwork.CurrentRoom?.MaxPlayers ?? 10;
                int ping = Photon.Pun.PhotonNetwork.GetPing();
                string name = Photon.Pun.PhotonNetwork.NickName;

                if (roomInfoText != null) roomInfoText.text = $"{players}/{max} Players  |  {ping} ms  |  {name}";
            }

            yield return wait;
        }
    }
}
