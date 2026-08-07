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

            int tileSlot = i;
            group.Tiles[i] = tile.gameObject;
            group.TileRects[i] = tile as RectTransform ?? tile.GetComponent<RectTransform>();
            group.Texts[i] = tile.GetComponentInChildren<TMP_Text>(true);
            Transform outline = tile.Find("Outline") ?? FindChildByName(tile, "Outline");
            group.OutlineRects[i] = outline as RectTransform ?? outline?.GetComponent<RectTransform>();
            if (group.Texts[i] != null && font != null)
            {
                group.Texts[i].font = font;
                group.Texts[i].richText = true;
                group.Texts[i].fontStyle = FontStyles.Bold;
                if (string.Equals(titleName, "ModsTitle", StringComparison.OrdinalIgnoreCase))
                    group.Texts[i].fontSize = 2.55f;
            }

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
                WireInteractive(tile, titleName + "_TileFocus_" + i, () => FocusCheckerTile(group, tileSlot), tileCollider);

            SetCheckerTileWidth(group, i, CheckerTileDefaultWidth);
            tile.gameObject.SetActive(false);
        }

        WireCheckerPageArrows(group, title, main, font);

        if (titleName.Equals("ModsTitle", StringComparison.OrdinalIgnoreCase))
            modsTileGroup = group;
        else if (titleName.Equals("CheatsTitle", StringComparison.OrdinalIgnoreCase))
            cheatsTileGroup = group;
    }

    private void WireCheckerPageArrows(CheckerTileGroup group, Transform title, Transform main, TMP_FontAsset font)
    {
        if (group == null)
            return;

        Transform root = title != null ? title : main;
        if (root == null)
            return;

        group.NextArrow =
            FindDirectNamedChild(root, "ModPageNext") ??
            FindDirectNamedChild(main, "ModPageNext") ??
            FindChildByName(root, "ModPageNext") ??
            FindChildByName(main, "ModPageNext");
        group.BackArrow =
            FindDirectNamedChild(root, "ModPageBack") ??
            FindDirectNamedChild(main, "ModPageBack") ??
            FindChildByName(root, "ModPageBack") ??
            FindChildByName(main, "ModPageBack");

        Transform moreDetails =
            FindChildByName(root, "MoreDetails") ??
            FindChildByName(root, "More Details") ??
            FindChildByName(root, "MORE DETAILS") ??
            FindChildByName(main, "MoreDetails") ??
            FindChildByName(main, "More Details");

        if (group.NextArrow == null && moreDetails != null)
            group.NextArrow = moreDetails;

        if (group.NextArrow == null || group.BackArrow == null)
            CreateDedicatedCheckerPageArrows(group, root);

        if (group.NextArrow != null)
        {
            Transform nextCollider = GetInteractiveCollider(group.NextArrow, "Collider") ?? group.NextArrow;
            WireInteractive(group.NextArrow, group.TitleName + "_PageNext", () => ShiftCheckerPage(group, 1), nextCollider);
            group.NextArrow.gameObject.SetActive(true);
        }

        if (group.BackArrow != null)
        {
            Transform backCollider = GetInteractiveCollider(group.BackArrow, "Collider") ?? group.BackArrow;
            WireInteractive(group.BackArrow, group.TitleName + "_PageBack", () => ShiftCheckerPage(group, -1), backCollider);
            group.BackArrow.gameObject.SetActive(true);
        }

        Transform pageLabel =
            FindChildByName(root, "PageText") ??
            FindChildByName(root, "PageLabel") ??
            FindChildByName(main, "PageText");
        if (pageLabel != null)
        {
            group.PageText = pageLabel.GetComponent<TMP_Text>() ?? pageLabel.GetComponentInChildren<TMP_Text>(true);
            if (group.PageText != null && font != null)
                group.PageText.font = font;
        }
    }

    private static Transform FindDirectNamedChild(Transform parent, string name)
    {
        if (parent == null || string.IsNullOrEmpty(name))
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private void CreateDedicatedCheckerPageArrows(CheckerTileGroup group, Transform parent)
    {
        if (group == null || parent == null)
            return;

        if (group.BackArrow == null)
        {
            group.BackArrow = CreateSimpleCheckerArrow(parent, "TUP_ModPageBack", new Vector3(-0.02f, -0.02f, 0f), true);
            group.OwnsPageArrows = true;
        }

        if (group.NextArrow == null)
        {
            group.NextArrow = CreateSimpleCheckerArrow(parent, "TUP_ModPageNext", new Vector3(0.02f, -0.02f, 0f), false);
            group.OwnsPageArrows = true;
        }
    }

    private Transform CreateSimpleCheckerArrow(Transform parent, string name, Vector3 localPos, bool back)
    {
        GameObject arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arrow.name = name;
        arrow.transform.SetParent(parent, false);
        arrow.transform.localPosition = localPos;
        arrow.transform.localRotation = Quaternion.identity;
        arrow.transform.localScale = new Vector3(0.012f, 0.012f, 0.004f);
        arrow.layer = 2;

        Collider col = arrow.GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        Renderer rend = arrow.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = new Material(ShaderCache.TextShader != null ? ShaderCache.TextShader : Shader.Find("GUI/Text Shader"));
            rend.material.color = MenuTheme.Current.Accent;
        }

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(arrow.transform, false);
        labelObj.transform.localPosition = Vector3.zero;
        labelObj.transform.localScale = Vector3.one * 0.02f;
        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = back ? "<" : ">";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 6f;
        tmp.color = Color.white;
        return arrow.transform;
    }

    private void UpdateCheckerPageChrome(CheckerTileGroup group)
    {
        if (group == null)
            return;

        int pageCount = GetCheckerPageCount(group);
        bool multiPage = group.Values.Length > GetCheckerPageSize(group);

        if (group.NextArrow != null && group.NextArrow.name.StartsWith("TUP_ModPage", StringComparison.OrdinalIgnoreCase))
            group.NextArrow.gameObject.SetActive(multiPage);
        else if (group.NextArrow != null)
            group.NextArrow.gameObject.SetActive(true);

        if (group.BackArrow != null && group.BackArrow.name.StartsWith("TUP_ModPage", StringComparison.OrdinalIgnoreCase))
            group.BackArrow.gameObject.SetActive(multiPage);
        else if (group.BackArrow != null)
            group.BackArrow.gameObject.SetActive(true);

        if (group.PageText != null)
        {
            group.PageText.gameObject.SetActive(multiPage || group.Values.Length > 0);
            group.PageText.text = multiPage
                ? (group.PageIndex + 1) + " / " + pageCount
                : group.Values.Length > 0 ? "1 / 1" : "0 / 0";
        }

        if (moreInfoModsExpanded && ReferenceEquals(group, modsTileGroup) && moreInfoPageText != null)
        {
            moreInfoPageText.gameObject.SetActive(group.Values.Length > 0);
            moreInfoPageText.text = multiPage
                ? (group.PageIndex + 1) + " / " + pageCount
                : group.Values.Length > 0 ? "1 / 1" : "0 / 0";
        }
    }

    private static int GetCheckerPageSize(CheckerTileGroup group) =>
        group != null && group.Tiles.Length > 0 ? group.Tiles.Length : 3;

    private static int GetCheckerPageCount(CheckerTileGroup group)
    {
        int pageSize = GetCheckerPageSize(group);
        if (group == null || group.Values.Length == 0 || pageSize <= 0)
            return 1;
        return (group.Values.Length + pageSize - 1) / pageSize;
    }

    private static int GetCheckerAbsoluteIndex(CheckerTileGroup group, int tileSlot)
    {
        if (group == null)
            return -1;
        return group.PageIndex * GetCheckerPageSize(group) + tileSlot;
    }

    private void ShiftCheckerPage(CheckerTileGroup group, int delta)
    {
        if (group == null || group.Values.Length == 0)
            return;

        int pageCount = GetCheckerPageCount(group);
        if (pageCount <= 1)
            return;

        if (group.FocusedIndex >= 0)
            UnfocusCheckerTile(group);

        group.PageIndex = (group.PageIndex + delta) % pageCount;
        if (group.PageIndex < 0)
            group.PageIndex += pageCount;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(ApplyCheckerTiles(group));
        UpdateCheckerPageChrome(group);
    }

    private void SetCheckerTiles(CheckerTileGroup group, string[] values)
    {
        if (group == null)
            return;

        values ??= Array.Empty<string>();
        group.Values = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        group.FocusedIndex = -1;
        group.FocusedValueIndex = -1;
        group.PageIndex = 0;
        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(ApplyCheckerTiles(group));
        UpdateCheckerPageChrome(group);
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

        int pageSize = GetCheckerPageSize(group);
        int start = group.PageIndex * pageSize;

        for (int i = 0; i < group.Tiles.Length; i++)
        {
            int valueIndex = start + i;
            bool visible = valueIndex < group.Values.Length;
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
                    group.Texts[i].richText = true;
                    group.Texts[i].text = ShortTileName(group.Values[valueIndex]);
                    group.Texts[i].alpha = 1f;
                    if (string.Equals(group.TitleName, "ModsTitle", StringComparison.OrdinalIgnoreCase))
                    {
                        group.Texts[i].fontSize = 2.55f;
                        group.Texts[i].fontStyle = FontStyles.Bold;
                        group.Texts[i].color = Color.white;
                    }
                }
            }
            else
            {
                SetCheckerTileWidth(group, i, 0f);
            }
        }

        UpdateCheckerPageChrome(group);
    }

    private void FocusCheckerTile(CheckerTileGroup group, int tileSlot)
    {
        if (group == null || tileSlot < 0 || tileSlot >= group.Tiles.Length)
            return;

        int valueIndex = GetCheckerAbsoluteIndex(group, tileSlot);
        if (valueIndex < 0 || valueIndex >= group.Values.Length || group.FocusedIndex == tileSlot)
            return;

        if (group.Routine != null)
            StopCoroutine(group.Routine);
        group.Routine = StartCoroutine(FocusCheckerTileRoutine(group, tileSlot, valueIndex));
    }

    private IEnumerator FocusCheckerTileRoutine(CheckerTileGroup group, int tileSlot, int valueIndex)
    {
        group.FocusedIndex = tileSlot;
        group.FocusedValueIndex = valueIndex;

        yield return FadeOtherCheckerTileTexts(group, tileSlot, 1f, 0f, CheckerTileTextFadeDuration);
        yield return AnimateOtherCheckerTilesWidth(group, tileSlot, 0f, CheckerTileWidthDuration, true);

        if (group.Texts[tileSlot] != null)
        {
            group.Texts[tileSlot].alpha = 1f;
            group.Texts[tileSlot].richText = true;
            group.Texts[tileSlot].text = group.Values[valueIndex];
            group.Texts[tileSlot].enableWordWrapping = true;
            group.Texts[tileSlot].overflowMode = TextOverflowModes.Overflow;
            if (string.Equals(group.TitleName, "ModsTitle", StringComparison.OrdinalIgnoreCase))
            {
                group.Texts[tileSlot].fontSize = 3.05f;
                group.Texts[tileSlot].fontStyle = FontStyles.Bold;
                group.Texts[tileSlot].color = Color.white;
            }
        }

        yield return AnimateCheckerTileWidth(group, tileSlot, GetRectWidth(group.TileRects[tileSlot]), CheckerTileExpandedWidth, CheckerTileWidthDuration, false);

        if (group.BackObjs[tileSlot] != null)
        {
            group.BackObjs[tileSlot].SetActive(true);
            RestoreCheckerBackRect(group, tileSlot);
            group.BackObjs[tileSlot].transform.localScale = Vector3.zero;
            yield return AnimateLocalScale(group.BackObjs[tileSlot].transform, Vector3.zero, CheckerBackTargetScale, CheckerTileBackDuration);
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
        int focusedValue = group.FocusedValueIndex;
        group.FocusedIndex = -1;
        group.FocusedValueIndex = -1;

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
                string value = focusedValue >= 0 && focusedValue < group.Values.Length
                    ? group.Values[focusedValue]
                    : "";
                group.Texts[focused].text = ShortTileName(value);
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

    private static bool IsCheckerTileOnPage(CheckerTileGroup group, int tileSlot)
    {
        if (group == null || tileSlot < 0 || tileSlot >= group.Tiles.Length)
            return false;
        int absolute = GetCheckerAbsoluteIndex(group, tileSlot);
        return absolute >= 0 && absolute < group.Values.Length;
    }

    private static IEnumerator FadeOtherCheckerTileTexts(CheckerTileGroup group, int focusedIndex, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        List<TMP_Text> texts = new List<TMP_Text>();
        for (int i = 0; i < group.Texts.Length; i++)
        {
            if (i == focusedIndex || !IsCheckerTileOnPage(group, i) || group.Texts[i] == null)
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
            if (i == focusedIndex || !IsCheckerTileOnPage(group, i) || group.Tiles[i] == null)
                continue;

            int valueIndex = GetCheckerAbsoluteIndex(group, i);
            group.Tiles[i].SetActive(true);
            SetCheckerTileWidth(group, i, 0f);
            if (group.Texts[i] != null)
            {
                group.Texts[i].text = ShortTileName(group.Values[valueIndex]);
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
            if (i == focusedIndex || !IsCheckerTileOnPage(group, i) || group.TileRects[i] == null)
                continue;
            group.Tiles[i]?.SetActive(true);
            from[i] = GetRectWidth(group.TileRects[i]);
        }

        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float p = EaseOutQuad(Mathf.Clamp01(time / duration));
            for (int i = 0; i < group.TileRects.Length; i++)
            {
                if (i == focusedIndex || !IsCheckerTileOnPage(group, i) || group.TileRects[i] == null)
                    continue;
                SetCheckerTileWidth(group, i, Mathf.LerpUnclamped(from[i], to, p));
            }
            yield return null;
        }

        for (int i = 0; i < group.TileRects.Length; i++)
        {
            if (i == focusedIndex || !IsCheckerTileOnPage(group, i) || group.TileRects[i] == null)
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
        string plain = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", "");
        if (plain.Length <= 28)
            return value;
        return plain.Length <= 28 ? value : plain.Substring(0, 25) + "...";
    }

    private static void StyleModTileText(TMP_Text tmp, MoreInfoModKind kind, bool focused)
    {
        if (tmp == null)
            return;

        tmp.richText = true;
        tmp.enableWordWrapping = focused;
        tmp.overflowMode = focused ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        tmp.fontSize = focused ? 3.05f : 2.55f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alpha = 1f;
    }

    private static Color GetMoreInfoKindColor(MoreInfoModKind kind)
    {
        switch (kind)
        {
            case MoreInfoModKind.Illegal: return new Color(1f, 0.33f, 0.33f, 1f);
            case MoreInfoModKind.Untrusted: return new Color(1f, 0.62f, 0.26f, 1f);
            case MoreInfoModKind.Legal: return new Color(0.33f, 1f, 0.53f, 1f);
            default: return new Color(1f, 0.82f, 0.4f, 1f);
        }
    }

    private static string GetMoreInfoKindHex(MoreInfoModKind kind)
    {
        switch (kind)
        {
            case MoreInfoModKind.Illegal: return "#FF5555";
            case MoreInfoModKind.Untrusted: return "#FF9F43";
            case MoreInfoModKind.Legal: return "#55FF88";
            default: return "#FFD166";
        }
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
        Transform monkeColorTransform = menuObj.transform.Find("SideHolder/CheckerMonke/MonkeColor") ??
                                        menuObj.transform.Find("SideHolder/MonkeColor");
        checkerMonkeColorImage = monkeColorTransform != null ? monkeColorTransform.GetComponent<Image>() : null;
        checkerMonkeColorSprite = checkerMonkeColorImage == null && monkeColorTransform != null
            ? monkeColorTransform.GetComponent<SpriteRenderer>()
            : null;
        InitCheckerTileGroup(sideTransform, "ModsTitle", out modsTileObjs, out modsTileTextComps, figtreeFont);

        InitCheckerTileGroup(sideTransform, "CheatsTitle", out cheatsTileObjs, out cheatsTileTextComps, figtreeFont);
        ConfigureModsInfoSection(sideTransform, figtreeFont);
        
        if (nameTextComp != null) { nameTextComp.text = "-----"; nameTextComp.font = figtreeFont; }
        if (fpsPingTextComp != null) { fpsPingTextComp.text = "--Hz \u2022 --Ms"; fpsPingTextComp.font = figtreeFont; MakeCheckerTextShow(fpsPingTextComp); }
        if (platformTextComp != null) { platformTextComp.text = "Platform"; platformTextComp.font = figtreeFont; }
        InitCheckerPlatformIcons(sideTransform);
        HideCheckerMonkeBoxPermanently();
        if (dateTextComp != null) { dateTextComp.text = "Date: --/--/----"; dateTextComp.font = figtreeFont; }
        CacheCheckerPanelTitle(sideTransform);
        if (modsCountTextComp != null) { modsCountTextComp.text = "0"; modsCountTextComp.font = figtreeFont; }
        if (cheatsCountTextComp != null) { cheatsCountTextComp.text = "0"; cheatsCountTextComp.font = figtreeFont; }
        lastCheckerPlatform = "Unknown";
        lastCheckerDate = "Date: --/--/----";
        lastCheckerFpsPing = "--Hz \u2022 --Ms";
        lastCheckerColorStr = "--";
        lastCheckerLegalMods = "None";
        lastCheckerIllegalMods = "None";
        lastCheckerUnknownPropList = Array.Empty<string>();
        SetMoreInfoVisible(false);
        ReplaceAddToSavedWithVersion(sideTransform, figtreeFont);
        InitVolumeSlider(sideTransform);
        InitVolumeButtons(sideTransform);

        InitNavButtons(menuObj);
        CleanupStolenCheckerNavClones(sideTransform);
        HideShieldIcons(menuObj.transform);
    }

    private static void HideShieldIcons(Transform root)
    {
        if (root == null)
            return;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null)
                continue;

            string name = child.name ?? "";
            bool isShield =
                name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Equals("ModsRating", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("Trust", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Verified", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Badge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Checkmark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("CheckMark", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isShield)
                continue;

            if (name.IndexOf("SelectorBtn", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            child.gameObject.SetActive(false);
        }
    }

    private void CacheCheckerPanelTitle(Transform sideTransform)
    {
        if (sideTransform == null)
            return;

        TMP_Text[] allLabels = sideTransform.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text best = null;
        for (int i = 0; i < allLabels.Length; i++)
        {
            TMP_Text label = allLabels[i];
            if (label == null || string.IsNullOrWhiteSpace(label.text))
                continue;

            string text = label.text.Trim();
            if (text.Equals("Player Checker", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Outfit", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Outfits", StringComparison.OrdinalIgnoreCase))
            {
                best = label;
                break;
            }
        }

        if (best == null)
        {
            Transform titleRoot =
                sideTransform.Find("CheckerTitle") ??
                FindChildByName(sideTransform, "CheckerTitle") ??
                sideTransform.Find("InfoMain/CheckerTitle") ??
                FindChildByName(sideTransform, "InfoMain");
            if (titleRoot != null)
            {
                TMP_Text[] labels = titleRoot.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    TMP_Text label = labels[i];
                    if (label == null)
                        continue;
                    string text = (label.text ?? "").Trim();
                    if (text.IndexOf("Checker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf("Outfit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        best == null)
                        best = label;
                    if (!string.IsNullOrEmpty(text) &&
                        text.IndexOf("Player Checker", StringComparison.OrdinalIgnoreCase) >= 0)
                        break;
                }
            }
        }

        if (best == null)
            return;

        checkerPanelTitleText = best;
        string current = (best.text ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(current) &&
            current.IndexOf("Outfit", StringComparison.OrdinalIgnoreCase) < 0)
            checkerPanelTitleDefault = current;
    }

    private void SetCheckerPanelTitle(string title)
    {
        if (menuObj == null)
            return;

        Transform side = menuObj.transform.Find("SideHolder");
        if (side == null)
            return;

        if (checkerPanelTitleText == null)
            CacheCheckerPanelTitle(side);

        TMP_Text[] labels = side.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text label = labels[i];
            if (label == null)
                continue;

            string text = (label.text ?? "").Trim();
            bool isHeader =
                text.Equals("Player Checker", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Outfit", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Outfits", StringComparison.OrdinalIgnoreCase) ||
                label == checkerPanelTitleText;
            if (!isHeader)
                continue;

            label.text = title;
            checkerPanelTitleText = label;
        }

        if (checkerPanelTitleText != null)
            checkerPanelTitleText.text = title;
    }

    private void ReplaceAddToSavedWithVersion(Transform sideTransform, TMP_FontAsset font)
    {
        Transform addToSaved =
            sideTransform.Find("AddToSaved") ??
            FindChildByName(sideTransform, "AddToSaved") ??
            FindChildByName(sideTransform, "SavedPlayers") ??
            FindChildByName(sideTransform, "Saved Players");

        if (addToSaved == null)
            return;

        Collider[] colliders = addToSaved.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        ButtonTrigger[] triggers = addToSaved.GetComponentsInChildren<ButtonTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            if (triggers[i] != null)
                triggers[i].enabled = false;
        }

        HideVersionToggleVisuals(addToSaved);

        Transform parent = addToSaved.parent;
        if (parent != null)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling == null || sibling == addToSaved)
                    continue;

                string n = sibling.name;
                bool namedWithAddToSaved = n.IndexOf("AddToSaved", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Saved", StringComparison.OrdinalIgnoreCase) >= 0;
                bool looksLikeSwitch = n.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.Equals("Knob", StringComparison.OrdinalIgnoreCase);

                if (namedWithAddToSaved && looksLikeSwitch)
                    sibling.gameObject.SetActive(false);
            }
        }

        TMP_Text[] labels = addToSaved.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text label = labels[i];
            if (label == null)
                continue;

            string text = label.text != null ? label.text.Trim() : "";
            if (text.Length == 0 ||
                text.IndexOf("Saved", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                label.text = PadDisplayName + " " + PadVersion;
                if (font != null)
                    label.font = font;
            }
        }

        if (labels.Length == 0)
        {
            GameObject labelObj = new GameObject("VersionLabel");
            labelObj.transform.SetParent(addToSaved, false);
            labelObj.transform.localPosition = Vector3.zero;
            labelObj.transform.localScale = Vector3.one * 0.01f;
            TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
            tmp.text = PadDisplayName + " " + PadVersion;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 1.4f;
            tmp.color = Color.white;
            if (font != null)
                tmp.font = font;
        }
    }

    private void HideVersionToggleVisuals(Transform root)
    {
        if (root == null)
            return;

        Transform slider = FindButtonChild(root, "Slider");
        Transform knob = FindButtonChild(root, "Slider/Knob") ?? FindButtonChild(root, "Knob");
        if (slider != null)
            slider.gameObject.SetActive(false);
        if (knob != null)
            knob.gameObject.SetActive(false);

        ButtonTrigger[] triggers = root.GetComponentsInChildren<ButtonTrigger>(true);
        for (int i = 0; i < triggers.Length; i++)
        {
            ButtonTrigger trigger = triggers[i];
            if (trigger == null)
                continue;

            trigger.enabled = false;
            trigger.IsToggle = false;
            trigger.CustomAction = null;
            if (trigger.Slider != null)
                trigger.Slider.gameObject.SetActive(false);
            if (trigger.Knob != null)
                trigger.Knob.gameObject.SetActive(false);
            trigger.Slider = null;
            trigger.Knob = null;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform part = all[i];
            if (part == null || part == root)
                continue;

            string n = part.name;
            if (n.Equals("Slider", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("Knob", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                part.gameObject.SetActive(false);
                continue;
            }

            if (part.GetComponent<TMP_Text>() != null)
                continue;

            if (part.GetComponentInChildren<TMP_Text>(true) != null)
                continue;

            Renderer[] renderers = part.GetComponents<Renderer>();
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null)
                    renderers[r].enabled = false;
            }

            Image[] images = part.GetComponents<Image>();
            for (int img = 0; img < images.Length; img++)
            {
                if (images[img] != null)
                    images[img].enabled = false;
            }
        }
    }

    private void ConfigureModsInfoSection(Transform sideTransform, TMP_FontAsset font)
    {
        cheatsTitleTransform = FindChildByName(sideTransform, "CheatsTitle");
        if (cheatsTitleTransform != null)
            cheatsTitleTransform.gameObject.SetActive(false);

        Transform brokenCard = sideTransform.Find("TUP_MoreInfoCard") ?? FindChildByName(sideTransform, "TUP_MoreInfoCard");
        if (brokenCard != null)
        {
            Transform maybeMods = FindChildByName(brokenCard, "ModsTitle");
            if (maybeMods != null && maybeMods.parent == brokenCard)
                maybeMods.SetParent(sideTransform, true);
            UnityEngine.Object.Destroy(brokenCard.gameObject);
        }

        modsTitleTransform = FindChildByName(sideTransform, "ModsTitle");
        if (modsTitleTransform == null)
            return;

        SanitizeModsTitleScale();

        modsTitleTransform.gameObject.SetActive(false);
        moreInfoVisible = false;
        moreInfoCardRoot = null;

        TMP_Text[] labels = modsTitleTransform.GetComponentsInChildren<TMP_Text>(true);
        bool titled = false;
        bool subtitled = false;
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text label = labels[i];
            if (label == null || label == modsCountTextComp)
                continue;

            string text = label.text != null ? label.text.Trim() : "";
            bool looksLikeTitle =
                text.Equals("MODS", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("CHEATS", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("MORE INFO", StringComparison.OrdinalIgnoreCase) ||
                (text.IndexOf("MOD", StringComparison.OrdinalIgnoreCase) >= 0 && text.Length <= 12);
            bool looksLikeSubtitle =
                text.IndexOf("CATEGORY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("NONE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("DETECT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("PLAYER DETAILS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("TAP TO GO BACK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeTitle && !titled)
            {
                label.text = "MORE INFO";
                titled = true;
            }
            else if (looksLikeSubtitle && !subtitled)
            {
                label.text = "PLAYER DETAILS";
                subtitled = true;
            }

            if (font != null)
                label.font = font;
        }

        EnsureMoreInfoBodyText(font);
        EnsureMoreInfoPageArrows(font);
        WireMoreInfoOpenButton();

        if (modsTileGroup != null)
        {
            for (int i = 0; i < modsTileGroup.Tiles.Length; i++)
            {
                if (modsTileGroup.Tiles[i] != null)
                    modsTileGroup.Tiles[i].SetActive(false);
            }

            if (modsTileGroup.NoneTitle != null)
                modsTileGroup.NoneTitle.SetActive(false);
        }
    }

    private void SanitizeModsTitleScale()
    {
        if (modsTitleTransform == null)
            return;

        Vector3 scale = modsTitleTransform.localScale;
        float maxAbs = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        if (maxAbs >= 0.45f)
        {
            Vector3 safe = new Vector3(0.02f, 0.02f, 0.02f);
            if (Tools.TryGetRememberedSize(modsTitleTransform, out Vector3 remembered))
            {
                float remMax = Mathf.Max(Mathf.Abs(remembered.x), Mathf.Abs(remembered.y), Mathf.Abs(remembered.z));
                if (remMax > 0.0001f && remMax < 0.45f)
                    safe = remembered;
            }
            modsTitleTransform.localScale = safe;
        }
    }

    private void WireMoreInfoOpenButton()
    {
        if (modsTitleTransform == null)
            return;

        Transform clickRoot = EnsureMoreInfoClickCollider(modsTitleTransform);
        ForceMoreInfoColliderSize(clickRoot);

        moreInfoOpenTrigger = WireInteractive(
            modsTitleTransform,
            "MoreInfo_OpenMods",
            ToggleMoreInfoModsExpanded,
            clickRoot);
        if (moreInfoOpenTrigger != null)
        {
            moreInfoOpenTrigger.SkipSwitchAnimation = true;
            moreInfoOpenTrigger.IsToggle = false;
            moreInfoOpenTrigger.CustomAction = ToggleMoreInfoModsExpanded;
            moreInfoOpenTrigger.ButtonRoot = modsTitleTransform;
        }

        ForceMoreInfoColliderSize(clickRoot);
        AttachRootPress(modsTitleTransform, moreInfoOpenTrigger);

        Transform main = modsTitleTransform.Find("Main") ?? FindChildByName(modsTitleTransform, "Main");
        if (main != null && moreInfoOpenTrigger != null)
            AttachRootPress(main, moreInfoOpenTrigger);
    }

    private Transform EnsureMoreInfoClickCollider(Transform modsTitle)
    {
        Transform existing =
            FindChildByName(modsTitle, "MoreInfoCollider") ??
            FindDirectNamedChild(modsTitle, "MoreInfoCollider");

        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            existing.gameObject.layer = 2;
            existing.SetParent(modsTitle, false);
            existing.localPosition = new Vector3(0f, 0.02f, -0.01f);
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            Collider col = existing.GetComponent<Collider>();
            if (col != null)
                col.enabled = true;
            ForceMoreInfoColliderSize(existing);
            return existing;
        }

        GameObject colObj = new GameObject("MoreInfoCollider");
        colObj.transform.SetParent(modsTitle, false);
        colObj.transform.localPosition = new Vector3(0f, 0.02f, -0.01f);
        colObj.transform.localRotation = Quaternion.identity;
        colObj.transform.localScale = Vector3.one;
        colObj.layer = 2;

        BoxCollider box = colObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        ForceMoreInfoColliderSize(colObj.transform);
        return colObj.transform;
    }

    private static void ForceMoreInfoColliderSize(Transform clickRoot)
    {
        if (clickRoot == null)
            return;

        BoxCollider box = clickRoot.GetComponent<BoxCollider>();
        if (box == null)
            box = clickRoot.gameObject.AddComponent<BoxCollider>();

        box.isTrigger = true;
        box.enabled = true;
        box.center = new Vector3(0f, 0.015f, 0f);
        box.size = new Vector3(0.42f, 0.32f, 0.10f);
    }

    private void ToggleMoreInfoModsExpanded()
    {
        if (modsTitleTransform == null || !modsTitleTransform.gameObject.activeInHierarchy)
            return;

        moreInfoVisible = true;
        moreInfoModsExpanded = !moreInfoModsExpanded;
        moreInfoModsPage = 0;
        UpdateMoreInfoBodyAndChrome();
    }

    private void EnsureMoreInfoBodyText(TMP_FontAsset font)
    {
        if (modsTitleTransform == null)
            return;

        if (moreInfoBodyText != null)
            return;

        Transform main = modsTitleTransform.Find("Main") ?? FindChildByName(modsTitleTransform, "Main") ?? modsTitleTransform;
        Transform existing =
            FindChildByName(main, "MoreInfoBody") ??
            FindChildByName(modsTitleTransform, "MoreInfoBody");

        if (existing != null)
        {
            if (existing.parent != main)
                existing.SetParent(main, false);
            existing.localPosition = new Vector3(0.002f, 0.012f, 0f);
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one * 0.0085f;

            moreInfoBodyText = existing.GetComponent<TMP_Text>() ?? existing.GetComponentInChildren<TMP_Text>(true);
            if (moreInfoBodyText != null)
            {
                if (font != null) moreInfoBodyText.font = font;
                moreInfoBodyText.enableWordWrapping = true;
                moreInfoBodyText.richText = true;
                moreInfoBodyText.overflowMode = TextOverflowModes.Overflow;
                moreInfoBodyText.alignment = TextAlignmentOptions.TopLeft;
                moreInfoBodyText.fontSize = 1.55f;
                moreInfoBodyText.text = "";
                return;
            }
        }

        GameObject bodyObj = new GameObject("MoreInfoBody");
        bodyObj.transform.SetParent(main, false);
        bodyObj.transform.localPosition = new Vector3(0.002f, 0.012f, 0f);
        bodyObj.transform.localRotation = Quaternion.identity;
        bodyObj.transform.localScale = Vector3.one * 0.0085f;
        bodyObj.layer = 2;

        TextMeshPro tmp = bodyObj.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.fontSize = 1.55f;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.text = "";
        if (font != null)
            tmp.font = font;

        RectTransform rect = bodyObj.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(22f, 14f);
            rect.pivot = new Vector2(0f, 1f);
        }

        moreInfoBodyText = tmp;
    }

    private void EnsureMoreInfoPageArrows(TMP_FontAsset font)
    {
        if (modsTitleTransform == null || modsTileGroup == null)
            return;

        moreInfoBackArrow = modsTileGroup.BackArrow;
        moreInfoNextArrow = modsTileGroup.NextArrow;
        moreInfoPageText = modsTileGroup.PageText;

        Transform parent = modsTitleTransform.Find("Main") ?? FindChildByName(modsTitleTransform, "Main") ?? modsTitleTransform;

        if (moreInfoBackArrow == null)
            moreInfoBackArrow = CreateSimpleCheckerArrow(parent, "TUP_MoreInfoBack", new Vector3(-0.035f, -0.055f, 0f), true);

        if (moreInfoNextArrow == null)
            moreInfoNextArrow = CreateSimpleCheckerArrow(parent, "TUP_MoreInfoNext", new Vector3(0.035f, -0.055f, 0f), false);

        if (moreInfoPageText == null)
        {
            GameObject pageObj = new GameObject("MoreInfoPageText");
            pageObj.transform.SetParent(parent, false);
            pageObj.transform.localPosition = new Vector3(0f, -0.055f, 0f);
            pageObj.transform.localScale = Vector3.one * 0.008f;
            TextMeshPro pageTmp = pageObj.AddComponent<TextMeshPro>();
            pageTmp.alignment = TextAlignmentOptions.Center;
            pageTmp.fontSize = 1.2f;
            pageTmp.color = new Color(0.85f, 0.85f, 0.9f, 1f);
            pageTmp.text = "1 / 1";
            if (font != null)
                pageTmp.font = font;
            moreInfoPageText = pageTmp;
        }

        if (moreInfoBackArrow != null)
        {
            Transform backCol =
                GetInteractiveCollider(moreInfoBackArrow, "Collider") ??
                EnsureChildNamedCollider(moreInfoBackArrow, "Collider");
            ButtonTrigger back = WireInteractive(
                moreInfoBackArrow,
                "MoreInfo_PageBack",
                () =>
                {
                    if (moreInfoModsExpanded)
                        ShiftMoreInfoModsPage(-1);
                    else if (modsTileGroup != null)
                        ShiftCheckerPage(modsTileGroup, -1);
                },
                backCol);
            if (back != null)
            {
                back.SkipSwitchAnimation = true;
                back.IsToggle = false;
                back.CustomAction = () =>
                {
                    if (moreInfoModsExpanded)
                        ShiftMoreInfoModsPage(-1);
                    else if (modsTileGroup != null)
                        ShiftCheckerPage(modsTileGroup, -1);
                };
            }

            moreInfoBackArrow.gameObject.SetActive(false);
        }

        if (moreInfoNextArrow != null)
        {
            Transform nextCol =
                GetInteractiveCollider(moreInfoNextArrow, "Collider") ??
                EnsureChildNamedCollider(moreInfoNextArrow, "Collider");
            ButtonTrigger next = WireInteractive(
                moreInfoNextArrow,
                "MoreInfo_PageNext",
                () =>
                {
                    if (moreInfoModsExpanded)
                        ShiftMoreInfoModsPage(1);
                    else if (modsTileGroup != null)
                        ShiftCheckerPage(modsTileGroup, 1);
                },
                nextCol);
            if (next != null)
            {
                next.SkipSwitchAnimation = true;
                next.IsToggle = false;
                next.CustomAction = () =>
                {
                    if (moreInfoModsExpanded)
                        ShiftMoreInfoModsPage(1);
                    else if (modsTileGroup != null)
                        ShiftCheckerPage(modsTileGroup, 1);
                };
            }

            moreInfoNextArrow.gameObject.SetActive(false);
        }
    }

    private static Transform EnsureChildNamedCollider(Transform root, string colliderName)
    {
        if (root == null)
            return null;

        Transform existing = root.Find(colliderName);
        if (existing != null)
            return existing;

        GameObject colObj = new GameObject(colliderName);
        colObj.transform.SetParent(root, false);
        colObj.transform.localPosition = Vector3.zero;
        colObj.transform.localRotation = Quaternion.identity;
        colObj.transform.localScale = Vector3.one;
        colObj.layer = 2;

        BoxCollider box = colObj.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = Vector3.one;

        Collider rootCol = root.GetComponent<Collider>();
        if (rootCol != null)
            rootCol.enabled = false;

        return colObj.transform;
    }

    private void ShiftMoreInfoModsPage(int delta)
    {
        if (!moreInfoModsExpanded)
            return;

        int pageCount = GetMoreInfoModsPageCount();
        if (pageCount <= 1)
            return;

        moreInfoModsPage = (moreInfoModsPage + delta) % pageCount;
        if (moreInfoModsPage < 0)
            moreInfoModsPage += pageCount;

        UpdateMoreInfoBodyAndChrome();
    }

    private int GetMoreInfoModsPageCount()
    {
        if (moreInfoModList == null || moreInfoModList.Length == 0)
            return 1;
        return (moreInfoModList.Length + MoreInfoModsPerPage - 1) / MoreInfoModsPerPage;
    }

    private void UpdateMoreInfoBodyAndChrome()
    {
        string dateValue = string.IsNullOrWhiteSpace(lastCheckerDate) ? "--/--/----" : lastCheckerDate.Replace("Date: ", "");
        string platformValue = string.IsNullOrWhiteSpace(lastCheckerPlatform) ? "Unknown" : lastCheckerPlatform;
        string colorValue = string.IsNullOrWhiteSpace(lastCheckerColorStr) ? "--" : lastCheckerColorStr;
        string fpsValue = string.IsNullOrWhiteSpace(lastCheckerFpsPing) ? "--Hz \u2022 --Ms" : lastCheckerFpsPing;

        int pageCount = GetMoreInfoModsPageCount();
        moreInfoModsPage = Mathf.Clamp(moreInfoModsPage, 0, pageCount - 1);

        if (moreInfoBodyText != null)
        {
            moreInfoBodyText.richText = true;
            moreInfoBodyText.gameObject.SetActive(true);

            if (!moreInfoModsExpanded)
            {
                int total = moreInfoModList != null ? moreInfoModList.Length : 0;
                string platformColored = FormatMoreInfoPlatformLine(platformValue);
                moreInfoBodyText.text =
                    platformColored + "\n" +
                    "Creation Date: " + dateValue + "\n" +
                    "Color: " + colorValue + "\n" +
                    fpsValue + "\n\n" +
                    (total == 0
                        ? "No props / mods detected\n<color=#BDBDBD>Tap MORE INFO</color>"
                        : total + " signal" + (total == 1 ? "" : "s") + " detected\n" +
                          "<color=#CBA6F7>Tap MORE INFO to open list</color>");
                UpdateMoreInfoPlatformIcon(platformValue);
            }
            else
            {
                moreInfoBodyText.text = BuildExpandedModsPageText(pageCount);
                if (moreInfoPlatformIconImage != null)
                    moreInfoPlatformIconImage.enabled = false;
            }
        }
        else if (moreInfoModsExpanded && moreInfoPlatformIconImage != null)
        {
            moreInfoPlatformIconImage.enabled = false;
        }

        UpdateMoreInfoHeaderLabels();
        ApplyMoreInfoModsTiles();

        if (modsCountTextComp != null)
            modsCountTextComp.text = (moreInfoModList != null ? moreInfoModList.Length : 0).ToString();
    }

    private void ApplyMoreInfoModsTiles()
    {
        if (modsTileGroup == null)
            return;

        if (!moreInfoModsExpanded)
        {
            for (int i = 0; i < modsTileGroup.Tiles.Length; i++)
            {
                if (modsTileGroup.Tiles[i] != null)
                    modsTileGroup.Tiles[i].SetActive(false);
            }

            if (modsTileGroup.NoneTitle != null)
                modsTileGroup.NoneTitle.SetActive(false);

            if (moreInfoBackArrow != null)
                moreInfoBackArrow.gameObject.SetActive(false);
            if (moreInfoNextArrow != null)
                moreInfoNextArrow.gameObject.SetActive(false);
            if (moreInfoPageText != null)
                moreInfoPageText.gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < modsTileGroup.Tiles.Length; i++)
        {
            if (modsTileGroup.Tiles[i] != null)
                modsTileGroup.Tiles[i].SetActive(false);
        }
        if (modsTileGroup.NoneTitle != null)
            modsTileGroup.NoneTitle.SetActive(false);

        int pageCount = GetMoreInfoModsPageCount();
        if (moreInfoBackArrow != null)
            moreInfoBackArrow.gameObject.SetActive(pageCount > 1);
        if (moreInfoNextArrow != null)
            moreInfoNextArrow.gameObject.SetActive(pageCount > 1);
        if (moreInfoPageText != null)
        {
            moreInfoPageText.gameObject.SetActive(true);
            moreInfoPageText.text = (moreInfoModsPage + 1) + " / " + pageCount;
        }
    }

    private static string FormatMoreInfoTileName(MoreInfoModEntry entry)
    {
        if (entry.Name == null)
            return "";

        string hex = GetMoreInfoKindHex(entry.Kind);
        string badge = entry.Kind == MoreInfoModKind.Illegal
            ? "ILLEGAL"
            : entry.Kind == MoreInfoModKind.Untrusted
                ? "WARN"
                : entry.Kind == MoreInfoModKind.Legal
                    ? "LEGAL"
                    : "UNKNOWN";
        return "<color=" + hex + "><b>" + badge + "</b></color>  <color=#F5F5F5>" +
               EscapeTmpText(entry.Name) + "</color>";
    }

    private string BuildExpandedModsPageText(int pageCount)
    {
        if (moreInfoModList == null || moreInfoModList.Length == 0)
            return "<size=120%><color=#BDBDBD>No props / mods detected</color></size>\n\n<color=#9AD7FF>Tap to go back</color>";

        int start = moreInfoModsPage * MoreInfoModsPerPage;
        int end = Mathf.Min(start + MoreInfoModsPerPage, moreInfoModList.Length);
        System.Text.StringBuilder sb = new System.Text.StringBuilder(320);
        sb.Append("<size=130%><color=#9AD7FF><b>PROPS / MODS</b></color></size>  ")
            .Append("<color=#BDBDBD>").Append(moreInfoModsPage + 1).Append(" / ").Append(pageCount).Append("</color>")
            .Append("\n<color=#8A8A8A>Tap header to go back</color>\n")
            .Append("<color=#55FF88><b>LEGAL</b></color>   ")
            .Append("<color=#FF5555><b>ILLEGAL</b></color>   ")
            .Append("<color=#FF9F43><b>WARN</b></color>   ")
            .Append("<color=#FFD166><b>UNKNOWN</b></color>\n\n");

        for (int i = start; i < end; i++)
        {
            MoreInfoModEntry entry = moreInfoModList[i];
            string color = GetMoreInfoKindHex(entry.Kind);
            sb.Append("<size=115%><color=").Append(color).Append("><b>• ")
                .Append(EscapeTmpText(entry.Name))
                .Append("</b></color></size>\n");
        }

        return sb.ToString().TrimEnd();
    }

    private void UpdateMoreInfoHeaderLabels()
    {
        if (modsTitleTransform == null)
            return;

        TMP_Text[] labels = modsTitleTransform.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text label = labels[i];
            if (label == null || label == moreInfoBodyText || label == moreInfoPageText || label == modsCountTextComp)
                continue;

            string text = label.text != null ? label.text.Trim() : "";
            if (text.Equals("MORE INFO", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("MODS", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("CHEATS", StringComparison.OrdinalIgnoreCase) ||
                (text.IndexOf("MOD", StringComparison.OrdinalIgnoreCase) >= 0 && text.Length <= 12))
            {
                label.text = moreInfoModsExpanded ? "MODS" : "MORE INFO";
            }
            else if (text.Equals("PLAYER DETAILS", StringComparison.OrdinalIgnoreCase) ||
                     text.Equals("TAP FOR MODS", StringComparison.OrdinalIgnoreCase) ||
                     text.IndexOf("TAP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     text.IndexOf("BACK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     text.IndexOf("DETAIL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                label.text = moreInfoModsExpanded ? "TAP TO GO BACK" : "PLAYER DETAILS";
            }
        }
    }

    private static string EscapeTmpText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("<", "(").Replace(">", ")");
    }

    public void SetMoreInfoVisible(bool visible)
    {
        bool alreadyOpen = moreInfoVisible && visible;
        moreInfoVisible = visible;

        if (cheatsTitleTransform != null)
            cheatsTitleTransform.gameObject.SetActive(false);

        if (modsTitleTransform == null)
            return;

        if (visible)
        {
            SanitizeModsTitleScale();
            modsTitleTransform.gameObject.SetActive(true);
            if (Tools.TryGetRememberedSize(modsTitleTransform, out Vector3 remembered))
            {
                float remMax = Mathf.Max(Mathf.Abs(remembered.x), Mathf.Abs(remembered.y), Mathf.Abs(remembered.z));
                if (remMax > 0.0001f && remMax < 0.45f)
                    modsTitleTransform.localScale = remembered;
            }
            else if (modsTitleTransform.localScale.sqrMagnitude < 0.0001f)
            {
                modsTitleTransform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
            }

            SanitizeModsTitleScale();

            if (!alreadyOpen)
            {
                moreInfoModsExpanded = false;
                moreInfoModsPage = 0;
            }

            WireMoreInfoOpenButton();
            if (moreInfoPlatformIconImage == null && moreInfoBodyText != null)
            {
                moreInfoPlatformIconImage = CreateOrGetPlatformIconImage(
                    moreInfoBodyText.transform.parent != null ? moreInfoBodyText.transform.parent : modsTitleTransform,
                    "TUP_MoreInfoPlatformIcon",
                    moreInfoBodyText.transform,
                    new Vector3(-0.012f, 0.012f, 0f));
            }

            UpdateMoreInfoPlatformIcon(lastCheckerPlatform);
            RefreshCheckerModsInfo();
        }
        else
        {
            modsTitleTransform.gameObject.SetActive(false);
            moreInfoModList = Array.Empty<MoreInfoModEntry>();
            moreInfoModsPage = 0;
            moreInfoModsExpanded = false;
            lastCheckerUnknownPropList = Array.Empty<string>();
            if (modsCountTextComp != null)
                modsCountTextComp.text = "0";
            if (moreInfoBodyText != null)
            {
                moreInfoBodyText.text = "";
                moreInfoBodyText.gameObject.SetActive(false);
            }
            if (moreInfoBackArrow != null)
                moreInfoBackArrow.gameObject.SetActive(false);
            if (moreInfoNextArrow != null)
                moreInfoNextArrow.gameObject.SetActive(false);
            if (moreInfoPageText != null)
                moreInfoPageText.gameObject.SetActive(false);
            DestroyPlayerModelPreview();
        }
    }

    public void ClearCheckerSelectionUi()
    {
        lastCheckerName = "-----";
        lastCheckerFpsPing = "--Hz \u2022 --Ms";
        lastCheckerPlatform = "Unknown";
        lastCheckerDate = "Date: --/--/----";
        lastCheckerColorStr = "--";
        lastCheckerLegalMods = "None";
        lastCheckerIllegalMods = "None";
        lastCheckerUnknownPropList = Array.Empty<string>();
        hasLastCheckerColor = false;
        moreInfoModList = Array.Empty<MoreInfoModEntry>();
        moreInfoModsPage = 0;
        moreInfoModsExpanded = false;

        if (nameTextComp != null) nameTextComp.text = "-----";
        if (fpsPingTextComp != null) { MakeCheckerTextShow(fpsPingTextComp); fpsPingTextComp.text = lastCheckerFpsPing; }
        if (platformTextComp != null) platformTextComp.text = "Platform";
        UpdateCheckerPlatformIcon("Unknown");
        if (dateTextComp != null) dateTextComp.text = lastCheckerDate;

        DestroyPlayerModelPreview();
        SetMoreInfoVisible(false);
        RefreshSelectUserButtonStates();
    }

    private void RefreshCheckerModsInfo()
    {
        if (!moreInfoVisible)
            return;

        if (cheatsTitleTransform != null && cheatsTitleTransform.gameObject.activeSelf)
            cheatsTitleTransform.gameObject.SetActive(false);

        string[] legalList = SplitCheckerList(lastCheckerLegalMods);
        string[] illegalList = SplitCheckerList(lastCheckerIllegalMods);
        string[] unknownList = lastCheckerUnknownPropList ?? Array.Empty<string>();
        List<MoreInfoModEntry> modEntries = new List<MoreInfoModEntry>(
            legalList.Length + illegalList.Length + unknownList.Length + 4);

        for (int i = 0; i < illegalList.Length; i++)
        {
            string name = illegalList[i];
            if (name.StartsWith("Untrusted:", StringComparison.OrdinalIgnoreCase))
                modEntries.Add(new MoreInfoModEntry(name.Substring("Untrusted:".Length).Trim(), MoreInfoModKind.Untrusted));
            else
                modEntries.Add(new MoreInfoModEntry(name, MoreInfoModKind.Illegal));
        }
        for (int i = 0; i < legalList.Length; i++)
            modEntries.Add(new MoreInfoModEntry(legalList[i], MoreInfoModKind.Legal));
        for (int i = 0; i < unknownList.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(unknownList[i]))
                continue;
            modEntries.Add(new MoreInfoModEntry(unknownList[i].Trim(), MoreInfoModKind.Unknown));
        }

        moreInfoModList = modEntries.ToArray();
        int pageCount = GetMoreInfoModsPageCount();
        moreInfoModsPage = Mathf.Clamp(moreInfoModsPage, 0, pageCount - 1);

        UpdateMoreInfoBodyAndChrome();
    }

    private static void CleanupStolenCheckerNavClones(Transform sideTransform)
    {
        if (sideTransform == null)
            return;

        Transform[] children = sideTransform.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null)
                continue;
            if (!child.name.Equals("PageNext", StringComparison.OrdinalIgnoreCase) &&
                !child.name.Equals("PageBack", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = GetTransformPath(child);
            if (path.IndexOf("ModsTitle", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("CheatsTitle", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            UnityEngine.Object.Destroy(child.gameObject);
        }
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
        if (!float.IsNaN(lastVolumeDisplayWidth) && Mathf.Abs(lastVolumeDisplayWidth - targetWidth) < 0.01f)
            return;
        lastVolumeDisplayWidth = targetWidth;

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

    private void InitCheckerPlatformIcons(Transform sideTransform)
    {
        EnsurePlatformSpritesLoaded();

        if (platformTextComp != null)
            checkerPlatformIconImage = CreateOrGetPlatformIconImage(
                platformTextComp.transform.parent != null ? platformTextComp.transform.parent : sideTransform,
                "TUP_PlatformIcon",
                platformTextComp.transform,
                new Vector3(-0.018f, 0f, 0f));

        if (moreInfoBodyText != null)
        {
            moreInfoPlatformIconImage = CreateOrGetPlatformIconImage(
                moreInfoBodyText.transform.parent != null ? moreInfoBodyText.transform.parent : sideTransform,
                "TUP_MoreInfoPlatformIcon",
                moreInfoBodyText.transform,
                new Vector3(-0.012f, 0.012f, 0f));
        }

        UpdateCheckerPlatformIcon("Unknown");
    }

    private Image CreateOrGetPlatformIconImage(Transform parent, string name, Transform alignTo, Vector3 localOffset)
    {
        if (parent == null)
            return null;

        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
        }

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(24f, 24f);
            rect.localScale = Vector3.one * 0.0012f;
            if (alignTo != null)
            {
                rect.position = alignTo.position;
                rect.rotation = alignTo.rotation;
                rect.localPosition = alignTo.localPosition + localOffset;
            }
            else
            {
                rect.localPosition = localOffset;
            }
        }
        else
        {
            go.transform.localScale = Vector3.one * 0.012f;
            if (alignTo != null)
            {
                go.transform.position = alignTo.position;
                go.transform.rotation = alignTo.rotation;
                go.transform.localPosition = alignTo.localPosition + localOffset;
            }
        }

        Image image = go.GetComponent<Image>();
        if (image == null)
            image = go.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = false;
        go.SetActive(false);
        return image;
    }

    private void EnsurePlatformSpritesLoaded()
    {
        if (platformSpritesLoaded)
            return;
        platformSpritesLoaded = true;

        try
        {
            GameObject prefab = Mods.PeekNameTagPrefab();
            if (prefab == null)
                return;

            SpriteRenderer[] sprites = prefab.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer sr = sprites[i];
                if (sr == null || sr.sprite == null)
                    continue;

                string name = sr.gameObject.name ?? "";
                string spriteName = sr.sprite != null ? (sr.sprite.name ?? "") : "";
                string combined = name + " " + spriteName;
                if (platformSpriteSteam == null && combined.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0)
                    platformSpriteSteam = sr.sprite;
                else if (platformSpriteMeta == null &&
                         (combined.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Standalone", StringComparison.OrdinalIgnoreCase) >= 0))
                    platformSpriteMeta = sr.sprite;
                else if (platformSpritePc == null &&
                         (combined.IndexOf("IconPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("IconPc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("OculusPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          (combined.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           combined.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) < 0)))
                    platformSpritePc = sr.sprite;
                else if (platformSpriteUnknown == null &&
                         (combined.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Question", StringComparison.OrdinalIgnoreCase) >= 0))
                    platformSpriteUnknown = sr.sprite;
            }

            Image[] images = prefab.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null || img.sprite == null)
                    continue;

                string name = img.gameObject.name ?? "";
                string spriteName = img.sprite != null ? (img.sprite.name ?? "") : "";
                string combined = name + " " + spriteName;
                if (platformSpriteSteam == null && combined.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0)
                    platformSpriteSteam = img.sprite;
                else if (platformSpriteMeta == null &&
                         (combined.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Standalone", StringComparison.OrdinalIgnoreCase) >= 0))
                    platformSpriteMeta = img.sprite;
                else if (platformSpritePc == null &&
                         (combined.IndexOf("IconPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("IconPc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("OculusPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          (combined.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 &&
                           combined.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) < 0)))
                    platformSpritePc = img.sprite;
                else if (platformSpriteUnknown == null &&
                         (combined.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          combined.IndexOf("Question", StringComparison.OrdinalIgnoreCase) >= 0))
                    platformSpriteUnknown = img.sprite;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Platform sprite load failed: " + ex.Message);
        }
    }

    private Sprite GetSpriteForPlatform(string platform)
    {
        EnsurePlatformSpritesLoaded();
        switch (platform)
        {
            case "Steam":
                return platformSpriteSteam != null ? platformSpriteSteam
                    : (platformSpritePc != null ? platformSpritePc : platformSpriteMeta);
            case "PC":
            case "Oculus PC":
            case "Oculus":
                return platformSpritePc != null ? platformSpritePc : platformSpriteSteam;
            case "Quest":
            case "Meta":
            case "Standalone":
            case "StandaloneVR":
            case "PSVR":
            case "Pico":
                return platformSpriteMeta != null ? platformSpriteMeta : platformSpriteUnknown;
            default:
                return platformSpriteUnknown;
        }
    }

    private void UpdateCheckerPlatformIcon(string platform)
    {
        ApplyPlatformIcon(checkerPlatformIconImage, platform);
        UpdateMoreInfoPlatformIcon(platform);
    }

    private void UpdateMoreInfoPlatformIcon(string platform)
    {
        ApplyPlatformIcon(moreInfoPlatformIconImage, platform);
    }

    private static void ApplyPlatformIcon(Image image, string platform)
    {
        if (image == null)
            return;

        Main self = Instance;
        Sprite sprite = self != null ? self.GetSpriteForPlatform(platform) : null;
        if (sprite == null ||
            string.Equals(platform, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platform, "Loading...", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(platform, "Platform", StringComparison.OrdinalIgnoreCase))
        {
            image.sprite = null;
            image.enabled = false;
            image.gameObject.SetActive(false);
            return;
        }

        image.sprite = sprite;
        image.enabled = true;
        image.gameObject.SetActive(true);
        image.color = Color.white;
    }

    private static string FormatMoreInfoPlatformLine(string platform)
    {
        string color = platform switch
        {
            "Steam" => "#66C0F4",
            "Oculus PC" => "#1A9FFF",
            "PC" => "#1A9FFF",
            "Quest" => "#A855F7",
            "Meta" => "#A855F7",
            "Standalone" => "#A855F7",
            "PSVR" => "#003087",
            "Pico" => "#00B2A9",
            "Loading..." => "#BDBDBD",
            _ => "#BDBDBD"
        };

        return "<color=#BDBDBD>Platform:</color> <color=" + color + ">" + EscapeTmpText(platform) + "</color>";
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
        string name = string.IsNullOrWhiteSpace(plrName) ? "-----" : plrName;
        if (!string.Equals(lastCheckerName, name, StringComparison.Ordinal))
        {
            lastCheckerName = name;
            if (nameTextComp != null) nameTextComp.text = name;
        }

        string fpsPing = $"{plrFPS}Hz \u2022 {plrPing}Ms";
        bool fpsChanged = !string.Equals(lastCheckerFpsPing, fpsPing, StringComparison.Ordinal);
        if (fpsChanged)
        {
            lastCheckerFpsPing = fpsPing;
            if (fpsPingTextComp != null) { MakeCheckerTextShow(fpsPingTextComp); fpsPingTextComp.text = fpsPing; }
        }

        string platform = string.IsNullOrWhiteSpace(plrPlatform) ? "Unknown" : plrPlatform;
        bool platformChanged = !string.Equals(lastCheckerPlatform, platform, StringComparison.Ordinal);
        if (platformChanged)
        {
            lastCheckerPlatform = platform;
            if (platformTextComp != null) platformTextComp.text = platform;
            UpdateCheckerPlatformIcon(platform);
        }

        string date = "Date: " + (string.IsNullOrWhiteSpace(plrCreationDate) ? "--/--/----" : plrCreationDate);
        bool dateChanged = !string.Equals(lastCheckerDate, date, StringComparison.Ordinal);
        if (dateChanged)
        {
            lastCheckerDate = date;
            if (dateTextComp != null) dateTextComp.text = date;
        }

        string colorStr = string.IsNullOrWhiteSpace(plrColor) ? "--" : plrColor;
        bool colorStrChanged = !string.Equals(lastCheckerColorStr, colorStr, StringComparison.Ordinal);
        if (colorStrChanged)
            lastCheckerColorStr = colorStr;

        if (!hasLastCheckerColor || lastCheckerColor != colorBaseForm)
        {
            hasLastCheckerColor = true;
            lastCheckerColor = colorBaseForm;
            if (checkerMonkeColorImage != null) checkerMonkeColorImage.color = colorBaseForm;
            else if (checkerMonkeColorSprite != null) checkerMonkeColorSprite.color = colorBaseForm;
        }

        if (fpsChanged || platformChanged || dateChanged || colorStrChanged)
            RefreshCheckerModsInfo();
    }

    public void UpdateCheckerProperties(string legalMods, string illegalMods, IList<string> unknownProps = null)
    {
        legalMods ??= "None";
        illegalMods ??= "None";
        string[] unknownArray = unknownProps != null && unknownProps.Count > 0
            ? unknownProps.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray()
            : Array.Empty<string>();

        if (string.Equals(lastCheckerLegalMods, legalMods, StringComparison.Ordinal) &&
            string.Equals(lastCheckerIllegalMods, illegalMods, StringComparison.Ordinal) &&
            UnknownPropListsEqual(lastCheckerUnknownPropList, unknownArray))
            return;

        lastCheckerLegalMods = legalMods;
        lastCheckerIllegalMods = illegalMods;
        lastCheckerUnknownPropList = unknownArray;
        RefreshCheckerModsInfo();
    }

    private static bool UnknownPropListsEqual(string[] a, string[] b)
    {
        a ??= Array.Empty<string>();
        b ??= Array.Empty<string>();
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }
        return true;
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
