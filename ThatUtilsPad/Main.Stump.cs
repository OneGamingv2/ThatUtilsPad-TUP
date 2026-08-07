using System.Collections;
using GorillaNetworking;
using TMPro;
using UnityEngine;

namespace ThatUtilsPad;

public partial class Main
{
    private const string StumpCreditMessage = "ThatUtilsPad " + PadVersion + "\nby Onegamingv2";

    private static readonly Vector3 StumpCreditFallback = new Vector3(-66.9f, 12.4f, -82.55f);

    private Vector3 stumpCreditBasePosition;
    private Coroutine stumpCreditFloatRoutine;
    private Coroutine stumpCreditAnchorRoutine;
    private Transform stumpCreditLookTarget;

    private void InitStumpCreditText()
    {
        if (stumpInfo != null)
            return;

        stumpInfo = new GameObject("TUP_StumpCredit");
        stumpInfo.layer = LayerMask.NameToLayer("Ignore Raycast");
        stumpCreditBasePosition = StumpCreditFallback;

        stumpInfo.transform.position = stumpCreditBasePosition;
        stumpInfo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        stumpInfo.transform.localScale = Vector3.one * 0.32f;

        TextMeshPro tmp = stumpInfo.AddComponent<TextMeshPro>();
        tmp.text = StumpCreditMessage;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 1.15f;
        tmp.color = new Color(1f, 1f, 1f, 0.95f);
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;

        if (FontCache.figtreeBundle != null)
        {
            TMP_FontAsset font = FontCache.figtreeBundle.LoadAsset<TMP_FontAsset>("assets/fonts/figtree.asset");
            if (font != null)
                tmp.font = font;
        }

        RectTransform rect = stumpInfo.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = new Vector2(12f, 2.5f);

        if (TryAnchorStumpCredit())
            FaceStumpCreditTowardCenter();

        if (stumpCreditFloatRoutine != null)
            StopCoroutine(stumpCreditFloatRoutine);
        stumpCreditFloatRoutine = StartCoroutine(FloatStumpCredit());

        if (stumpCreditAnchorRoutine != null)
            StopCoroutine(stumpCreditAnchorRoutine);
        stumpCreditAnchorRoutine = StartCoroutine(RetryStumpCreditAnchor());
    }

    private IEnumerator RetryStumpCreditAnchor()
    {
        for (int i = 0; i < 60; i++)
        {
            if (TryAnchorStumpCredit())
            {
                stumpCreditAnchorRoutine = null;
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }

        stumpCreditBasePosition = StumpCreditFallback;
        if (stumpInfo != null)
            stumpInfo.transform.position = stumpCreditBasePosition;
        FaceStumpCreditTowardCenter();
        stumpCreditAnchorRoutine = null;
    }

    private bool TryAnchorStumpCredit()
    {
        if (stumpInfo == null)
            return false;

        if (!TryResolveStumpCenter(out Vector3 center, out Transform lookAt))
            return false;

        stumpCreditLookTarget = lookAt;
        stumpCreditBasePosition = center + Vector3.up * 0.85f;
        stumpInfo.transform.SetParent(null, true);
        stumpInfo.transform.position = stumpCreditBasePosition;
        FaceStumpCreditTowardCenter();
        return true;
    }

    private static bool TryResolveStumpCenter(out Vector3 center, out Transform lookAt)
    {
        center = StumpCreditFallback;
        lookAt = null;

        GorillaComputer computer = GorillaComputer.instance;
        if (computer != null)
        {
            Transform computerT = computer.transform;
            if (IsNearForestStump(computerT.position))
            {
                center = computerT.position;
                lookAt = computerT;
                return true;
            }
        }

        string[] paths =
        {
            "Environment Objects/LocalObjects_Prefab/TreeRoom/CodeOfConductHeadingText",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/COCBodyText_TitleData",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/CodeOfConduct",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/GorillaComputerObject",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables",
            "Environment Objects/LocalObjects_Prefab/TreeRoom"
        };

        for (int i = 0; i < paths.Length; i++)
        {
            GameObject found = GameObject.Find(paths[i]);
            if (found == null)
                continue;
            if (!IsUnderTreeRoom(found.transform))
                continue;
            if (!IsNearForestStump(found.transform.position))
                continue;

            center = found.transform.position;
            lookAt = found.transform;
            return true;
        }

        GameObject treeRoom = GameObject.Find("Environment Objects/LocalObjects_Prefab/TreeRoom");
        if (treeRoom != null && IsNearForestStump(treeRoom.transform.position))
        {
            Transform stumpMesh = FindChildRecursive(treeRoom.transform, "Stump")
                                  ?? FindChildRecursive(treeRoom.transform, "stump");
            if (stumpMesh != null && IsNearForestStump(stumpMesh.position))
            {
                center = stumpMesh.position;
                lookAt = stumpMesh;
                return true;
            }

            center = treeRoom.transform.position;
            lookAt = treeRoom.transform;
            return true;
        }

        return false;
    }

    private static bool IsNearForestStump(Vector3 position)
    {
        Vector3 delta = position - StumpCreditFallback;
        delta.y = 0f;
        return delta.sqrMagnitude <= 40f * 40f;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = FindChildRecursive(root.GetChild(i), name);
            if (hit != null)
                return hit;
        }

        return null;
    }

    private static bool IsUnderTreeRoom(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            string n = p.name ?? "";
            if (n.IndexOf("ForestToCanyon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("ForestSlide", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Forest_Slide", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Canyon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Mountain", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Clouds", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Cave", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (n.IndexOf("TreeRoom", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Equals("Stump", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void FaceStumpCreditTowardCenter()
    {
        if (stumpInfo == null)
            return;

        Vector3 lookTarget = stumpCreditBasePosition + Vector3.back;
        if (stumpCreditLookTarget != null)
            lookTarget = stumpCreditLookTarget.position;
        else if (GorillaComputer.instance != null)
            lookTarget = GorillaComputer.instance.transform.position;

        Vector3 flat = lookTarget - stumpInfo.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
            stumpInfo.transform.rotation = Quaternion.LookRotation(-flat.normalized, Vector3.up);
    }

    private IEnumerator FloatStumpCredit()
    {
        const float bobHeight = 0.08f;
        const float bobSpeed = 1.1f;
        float time = 0f;

        while (stumpInfo != null)
        {
            time += Time.deltaTime * bobSpeed;
            float y = stumpCreditBasePosition.y + Mathf.Sin(time) * bobHeight;
            stumpInfo.transform.position = new Vector3(stumpCreditBasePosition.x, y, stumpCreditBasePosition.z);
            FaceStumpCreditTowardCenter();
            yield return null;
        }

        stumpCreditFloatRoutine = null;
    }
}
