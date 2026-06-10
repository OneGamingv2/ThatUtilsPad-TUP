using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace ThatUtilsPad
{
    public enum NotificationDisplayMode
    {
        Auto,
        Vr,
        Pc
    }

    public class NotificationLib : MonoBehaviour
    {
        private const float PcSpacing = 92f;
        private const float PcMargin = 24f;
        private const float PcSlideDuration = 0.44f;
        private const float VrSpacing = 0.115f;

        private static NotificationLib instance;
        private static GameObject prefab;
        private static Vector3 prefabScale = Vector3.one;
        private static readonly List<NotificationEntry> activeNotifications = new List<NotificationEntry>();
        private static Coroutine layoutRoutine;

        public static NotificationDisplayMode DisplayMode { get; set; } = NotificationDisplayMode.Pc;

        public class NotificationEntry
        {
            public GameObject Root;
            public CanvasGroup Group;
            public RectTransform Rect;
            public Coroutine MoveRoutine;
            public float Duration;
            public bool Removing;
        }

        public static void Initialize(AssetBundle notifBundle)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("TUP_NotificationLib");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<NotificationLib>();
            }

            if (notifBundle == null)
            {
                Debug.LogError("[TUP Notif] notifBundle is null");
                return;
            }

            prefab = notifBundle.LoadAllAssets<GameObject>()
                .FirstOrDefault(obj => obj.name.ToLowerInvariant().Contains("notif"))
                ?? notifBundle.LoadAllAssets<GameObject>().FirstOrDefault();

            if (prefab == null)
            {
                Debug.LogError("[TUP Notif] no notification prefab found in notifBundle");
                return;
            }

            prefabScale = prefab.transform.localScale;
            foreach (string assetName in notifBundle.GetAllAssetNames())
                Debug.Log("[TUP Notif Bundle] " + assetName);
        }

        public static NotificationEntry SendNotification(string title, string text, float duration = 3f)
        {
            if (instance == null || prefab == null)
            {
                Debug.LogWarning("[TUP Notif] notification sent before Initialize");
                return null;
            }

            NotificationEntry entry = instance.CreateNotification(title, text, Mathf.Max(0.1f, duration));
            activeNotifications.Add(entry);
            UpdateLayout();
            instance.StartCoroutine(instance.RemoveAfter(entry));
            return entry;
        }

        public static NotificationEntry SendNotification(string text, bool important, float duration)
        {
            PlayNotificationSound(important);
            return SendNotification(important ? "Important" : "ThatUtilsPad", text, duration);
        }

        private NotificationEntry CreateNotification(string title, string text, float duration)
        {
            GameObject root = Instantiate(prefab);
            root.name = "TUP_Notification";
            root.SetActive(true);

            TMP_Text titleText = FindText(root.transform, "NotifBundle.NotificationTitle", "NotificationTitle");
            TMP_Text bodyText = FindText(root.transform, "NotifBundle.NotificationText", "NotificationText");
            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = text;

            CanvasGroup group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            RectTransform rect = ConfigureForMode(root, titleText, bodyText);
            StartCoroutine(Fade(root, group, 0f, 1f, 0.16f));

            return new NotificationEntry
            {
                Root = root,
                Group = group,
                Rect = rect,
                Duration = duration
            };
        }

        private static RectTransform ConfigureForMode(GameObject root, TMP_Text titleText, TMP_Text bodyText)
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true) ?? root.AddComponent<Canvas>();
            Camera camera = Main.GetActiveCamera() ?? Camera.main;

            if (UseVrMode())
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                if (camera != null)
                {
                    root.transform.SetParent(camera.transform, false);
                    root.transform.localPosition = new Vector3(0f, -0.28f, 1.15f);
                    root.transform.localRotation = Quaternion.identity;
                }
                root.transform.localScale = prefabScale == Vector3.zero ? Vector3.one * 0.0018f : prefabScale;
                return null;
            }
            else
            {
                root.transform.SetParent(null, false);
                foreach (Canvas childCanvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    childCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    childCanvas.sortingOrder = 10000;
                    childCanvas.transform.localScale = Vector3.one;
                    StretchToScreen(childCanvas.GetComponent<RectTransform>());
                }
                StretchToScreen(root.GetComponent<RectTransform>());

                RectTransform rect = CreatePcWrapper(root, canvas) ?? FindNotificationRect(root, canvas, titleText, bodyText);
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(1f, 0f);
                    rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(GetOffscreenX(rect), PcMargin);
                }
                root.transform.localScale = Vector3.one;
                return rect;
            }
        }

        private static void UpdateLayout()
        {
            if (UseVrMode())
            {
                for (int i = 0; i < activeNotifications.Count; i++)
                {
                    NotificationEntry entry = activeNotifications[i];
                    if (entry.Root != null)
                        entry.Root.transform.localPosition = new Vector3(0f, -0.28f + i * VrSpacing, 1.15f);
                }
                return;
            }

            if (instance == null)
                return;

            if (layoutRoutine != null)
                instance.StopCoroutine(layoutRoutine);
            layoutRoutine = instance.StartCoroutine(LayoutPcWave());
        }

        private static IEnumerator LayoutPcWave()
        {
            int visibleIndex = 0;
            foreach (NotificationEntry entry in activeNotifications.ToArray())
            {
                if (entry.Root == null || entry.Removing)
                    continue;

                RectTransform rect = entry.Rect;
                if (rect == null)
                {
                    Canvas canvas = entry.Root.GetComponentInChildren<Canvas>(true);
                    rect = canvas != null ? canvas.GetComponent<RectTransform>() : entry.Root.GetComponent<RectTransform>();
                    entry.Rect = rect;
                }

                if (rect != null)
                {
                    MovePcEntry(entry, new Vector2(-PcMargin, PcMargin + GetStackOffset(rect, visibleIndex)));
                    visibleIndex++;
                    yield return new WaitForSeconds(0.02f);
                }
            }

            layoutRoutine = null;
        }

        private IEnumerator RemoveAfter(NotificationEntry entry)
        {
            yield return new WaitForSeconds(entry.Duration);

            if (entry.Root != null)
            {
                entry.Removing = true;
                bool vr = UseVrMode();
                if (!vr && entry.Rect != null)
                {
                    if (layoutRoutine != null)
                        yield return layoutRoutine;
                    if (entry.MoveRoutine != null)
                        yield return entry.MoveRoutine;

                    yield return SlidePc(entry, entry.Rect.anchoredPosition, new Vector2(GetOffscreenX(entry.Rect), entry.Rect.anchoredPosition.y), 0.36f);
                }

                if (entry.Group != null)
                    yield return Fade(entry.Root, entry.Group, entry.Group.alpha, 0f, 0.08f);
            }

            activeNotifications.Remove(entry);
            if (entry.Root != null)
                Destroy(entry.Root);
            UpdateLayout();
        }

        private static void MovePcEntry(NotificationEntry entry, Vector2 target)
        {
            if (entry.Rect == null || instance == null)
                return;

            if (entry.MoveRoutine != null)
                instance.StopCoroutine(entry.MoveRoutine);

            entry.MoveRoutine = instance.StartCoroutine(SlidePc(entry, entry.Rect.anchoredPosition, target, PcSlideDuration));
        }

        private static IEnumerator SlidePc(NotificationEntry entry, Vector2 from, Vector2 to, float duration)
        {
            float time = 0f;
            while (time < duration)
            {
                if (entry.Root == null || entry.Rect == null)
                    yield break;

                float t = time / duration;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                entry.Rect.anchoredPosition = Vector2.Lerp(from, to, eased);
                time += Time.deltaTime;
                yield return null;
            }

            if (entry.Rect != null)
                entry.Rect.anchoredPosition = to;
            entry.MoveRoutine = null;
        }

        private static float GetOffscreenX(RectTransform rect)
        {
            float width = rect != null && rect.rect.width > 1f ? rect.rect.width : rect.sizeDelta.x;
            if (width < 1f) width = 420f;
            return width + PcMargin;
        }

        private static float GetStackOffset(RectTransform rect, int index)
        {
            float height = rect != null && rect.rect.height > 1f ? rect.rect.height : rect != null ? rect.sizeDelta.y : 0f;
            if (height < 1f) height = PcSpacing;
            return index * Mathf.Min(height + 12f, PcSpacing);
        }

        private static RectTransform FindNotificationRect(GameObject root, Canvas canvas, TMP_Text titleText, TMP_Text bodyText)
        {
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == "NotifBundle")
                    return rect;
            }

            Transform common = FindCommonParent(titleText != null ? titleText.transform : null, bodyText != null ? bodyText.transform : null);
            while (common != null && common != root.transform && common.GetComponent<Canvas>() == null)
            {
                if (common.TryGetComponent(out RectTransform rect) && rect.rect.width > 1f && rect.rect.height > 1f)
                    return rect;
                common = common.parent;
            }

            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.GetComponent<Canvas>() != null)
                    continue;
                if (rect.rect.width > 1f && rect.rect.height > 1f)
                    return rect;
            }

            return root.GetComponent<RectTransform>() ?? canvas.GetComponent<RectTransform>();
        }

        private static void StretchToScreen(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static Transform FindCommonParent(Transform first, Transform second)
        {
            if (first == null) return second;
            if (second == null) return first;

            Transform cursor = first;
            while (cursor != null)
            {
                Transform other = second;
                while (other != null)
                {
                    if (cursor == other)
                        return cursor;
                    other = other.parent;
                }
                cursor = cursor.parent;
            }

            return first;
        }

        private static IEnumerator Fade(GameObject root, CanvasGroup group, float from, float to, float duration)
        {
            float time = 0f;
            while (time < duration)
            {
                if (root == null || group == null)
                    yield break;

                group.alpha = Mathf.Lerp(from, to, time / duration);
                time += Time.deltaTime;
                yield return null;
            }

            if (group != null)
                group.alpha = to;
        }

        private static TMP_Text FindText(Transform root, params string[] names)
        {
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (string name in names)
            {
                TMP_Text exact = texts.FirstOrDefault(t => t.name == name);
                if (exact != null) return exact;

                TMP_Text suffix = texts.FirstOrDefault(t => t.name.EndsWith(name));
                if (suffix != null) return suffix;
            }

            return texts.FirstOrDefault();
        }

        private static bool UseVrMode()
        {
            if (DisplayMode == NotificationDisplayMode.Vr) return true;
            if (DisplayMode == NotificationDisplayMode.Pc) return false;
            return false;
        }

        private static RectTransform CreatePcWrapper(GameObject root, Canvas canvas)
        {
            if (canvas == null)
                return null;

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect == null)
                return null;

            GameObject wrapperObj = new GameObject("TUP_NotificationWrapper");
            RectTransform wrapper = wrapperObj.AddComponent<RectTransform>();
            wrapper.SetParent(canvas.transform, false);
            wrapper.anchorMin = new Vector2(1f, 0f);
            wrapper.anchorMax = new Vector2(1f, 0f);
            wrapper.pivot = new Vector2(1f, 0f);
            wrapper.anchoredPosition = Vector2.zero;
            wrapper.localScale = Vector3.one;

            List<Transform> children = new List<Transform>();
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                Transform child = canvas.transform.GetChild(i);
                if (child != wrapper)
                    children.Add(child);
            }

            foreach (Transform child in children)
                child.SetParent(wrapper, true);

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(wrapper);
            if (bounds.size.x > 1f && bounds.size.y > 1f)
            {
                foreach (Transform child in children)
                {
                    Vector3 local = child.localPosition;
                    local.x -= bounds.max.x;
                    local.y -= bounds.min.y;
                    child.localPosition = local;
                }

                wrapper.sizeDelta = new Vector2(bounds.size.x, bounds.size.y);
            }

            return wrapper;
        }

        private static void PlayNotificationSound(bool important)
        {
            try
            {
                GorillaTagger.Instance?.offlineVRRig?.PlayHandTapLocal(important ? 40 : 84, true, 5f);
            }
            catch
            {
                Debug.LogWarning("[TUP Notif] failed to play notification sound");
            }
        }
    }
}
