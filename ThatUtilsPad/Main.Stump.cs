using System.Collections;
using GorillaNetworking;
using TMPro;
using UnityEngine;

namespace ThatUtilsPad;

public partial class Main
{
    private const string StumpCreditMessage = "updated and maintained by Onegamingv2 :)";
    private Vector3 stumpCreditBasePosition;
    private Coroutine stumpCreditFloatRoutine;

    private void InitStumpCreditText()
    {
        if (stumpInfo != null)
            return;

        stumpInfo = new GameObject("TUP_StumpCredit");
        stumpInfo.layer = LayerMask.NameToLayer("Ignore Raycast");

        Transform anchor = FindStumpCreditAnchor();
        if (anchor != null)
        {
            stumpInfo.transform.SetParent(null, true);
            stumpCreditBasePosition = anchor.position + anchor.up * 0.35f + anchor.forward * 0.15f;
        }
        else
        {

            stumpCreditBasePosition = new Vector3(-66.9f, 12.15f, -82.55f);
        }

        stumpInfo.transform.position = stumpCreditBasePosition;
        stumpInfo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        stumpInfo.transform.localScale = Vector3.one * 0.35f;

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

        FaceStumpCreditTowardCenter();
        if (stumpCreditFloatRoutine != null)
            StopCoroutine(stumpCreditFloatRoutine);
        stumpCreditFloatRoutine = StartCoroutine(FloatStumpCredit());
    }

    private static Transform FindStumpCreditAnchor()
    {
        string[] paths =
        {
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI/CodeOfConduct",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables/UI",
            "Environment Objects/LocalObjects_Prefab/TreeRoom",
            "TreeRoom"
        };

        for (int i = 0; i < paths.Length; i++)
        {
            GameObject found = GameObject.Find(paths[i]);
            if (found != null)
                return found.transform;
        }

        GorillaComputer computer = GorillaComputer.instance;
        if (computer != null)
            return computer.transform;

        return null;
    }

    private void FaceStumpCreditTowardCenter()
    {
        if (stumpInfo == null)
            return;

        Vector3 lookTarget = stumpCreditBasePosition + Vector3.forward;
        Camera cam = FirstPersonCamera != null ? FirstPersonCamera : Camera.main;
        if (cam != null)
            lookTarget = cam.transform.position;

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
