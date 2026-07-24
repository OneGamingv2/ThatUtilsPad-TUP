using System;
using System.Collections;
using GorillaLocomotion;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GorillaGameModes;
using GorillaNetworking;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PlayFab;
using PlayFab.ClientModels;
using Photon.Voice.Unity;
using TMPro;
using ThatUtilsPad.MenuComponents;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public static partial class Mods
{
private static GameObject notificationPrefab;

public static void ShowNotification(string text, float duration)
{
    text ??= "";
    duration = Mathf.Max(0f, duration);

    queuedNotifications.Enqueue(new NotificationRequest(text, duration));
    if (notificationQueueRoutine == null)
        notificationQueueRoutine = GetNotificationCoroutineHandler().StartCoroutine(ProcessNotificationQueue());
}

public static void ShowNotification(string text)
{
    ShowNotification(text, NotificationDefaultDuration);
}

private static IEnumerator ProcessNotificationQueue()
{
    while (queuedNotifications.Count > 0)
    {
        while (activeNotifications.Count >= NotificationMaxVisible || Time.time < notificationStackSettlesAt)
            yield return null;

        NotificationRequest request = queuedNotifications.Dequeue();
        TryShowNotificationNow(request.Text, request.Duration);
        yield return null;
    }

    notificationQueueRoutine = null;
}

private static void TryShowNotificationNow(string text, float duration)
{
    if (Main.notifBundle == null)
    {
        Debug.LogWarning("[TUP] Notification bundle is not loaded.");
        return;
    }

    Transform headTransform = GTPlayer.Instance?.headCollider?.transform;
    if (headTransform == null)
    {
        Debug.LogWarning("[TUP] Cannot show notification without a head transform.");
        return;
    }

    if (notificationPrefab == null)
        notificationPrefab = Main.notifBundle.LoadAsset<GameObject>(NotificationPrefabPath);
    if (notificationPrefab == null)
    {
        Debug.LogWarning("[TUP] Notification prefab not found: " + NotificationPrefabPath);
        return;
    }

    GameObject notifObject = Object.Instantiate(notificationPrefab);
    notifObject.transform.localScale = Vector3.one * 0.026f;
    NotificationFollower follower = notifObject.AddComponent<NotificationFollower>();

    TextMeshProUGUI notifText = notifObject.transform.Find("Base/Text")?.GetComponent<TextMeshProUGUI>();
    RectTransform notifImageRect = notifObject.transform.Find("Base")?.GetComponent<RectTransform>();
    if (notifText == null || notifImageRect == null)
    {
        Debug.LogWarning("[TUP] Notification prefab is missing Base/Text or Base RectTransform.");
        Object.Destroy(notifObject);
        return;
    }

    float targetWidth = NotificationBaseWidth + (text.Length * NotificationWidthPerCharacter);
    notifText.text = text;
    notifText.alpha = 0f;
    notifImageRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);

    var entry = new NotificationEntry(notifObject, notifText, notifImageRect, follower, targetWidth);
    activeNotifications.Add(entry);
    RestackNotifications(false);

    Main.PlayNotificationSound();
    GetNotificationCoroutineHandler().StartCoroutine(NotificationLifetime(entry, duration));
}
private static CoroutineHandler GetNotificationCoroutineHandler()
{
    if (CoroutineHandler.Instance != null)
        return CoroutineHandler.Instance;

    return new GameObject("TUP_CoroutineHandler").AddComponent<CoroutineHandler>();
}

private static IEnumerator NotificationLifetime(NotificationEntry entry, float duration)
{
    yield return AnimateNotificationWidth(entry, 0f, entry.TargetWidth, NotificationWidthAnimDuration);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return FadeNotificationText(entry, 0f, 1f, NotificationTextFadeInDuration);
    yield return new WaitForSeconds(duration);

    while (entry.Object != null && activeNotifications.Count > 0 && activeNotifications[0] != entry)
        yield return null;
    yield return MoveNotification(entry, GetNotificationStackPosition(0), NotificationMoveDuration, 0f);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return FadeNotificationText(entry, 1f, 0f, NotificationTextFadeOutDuration);
    yield return new WaitForSeconds(NotificationTextScaleDelay);
    yield return AnimateNotificationWidth(entry, entry.TargetWidth, 0f, NotificationWidthAnimDuration);

    RemoveNotification(entry);

    RestackNotifications(true);
    MarkNotificationStackSettling();
}

private static IEnumerator AnimateNotificationWidth(NotificationEntry entry, float from, float to, float duration)
{
    if (entry.Rect == null)
        yield break;

    if (duration <= 0f)
    {
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, to);
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Rect != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        t = 1f - ((1f - t) * (1f - t));
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Lerp(from, to, t));
        yield return null;
    }

    if (entry.Rect != null)
        entry.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, to);
}

private static IEnumerator FadeNotificationText(NotificationEntry entry, float from, float to, float duration)
{
    if (entry.Text == null)
        yield break;

    if (duration <= 0f)
    {
        entry.Text.alpha = to;
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Text != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        t = 1f - ((1f - t) * (1f - t));
        entry.Text.alpha = Mathf.Lerp(from, to, t);
        yield return null;
    }

    if (entry.Text != null)
        entry.Text.alpha = to;
}

private static void RestackNotifications(bool animate)
{
    activeNotifications.RemoveAll(entry => entry.Object == null);

    for (int i = 0; i < activeNotifications.Count; i++)
    {
        NotificationEntry entry = activeNotifications[i];
        GameObject notifObject = entry.Object;
        if (notifObject == null) continue;

        Vector3 targetPosition = GetNotificationStackPosition(i);

        if (!animate)
        {
            entry.Follower.LocalPosition = targetPosition;
            continue;
        }

        if (entry.MoveRoutine != null && CoroutineHandler.Instance != null)
            CoroutineHandler.Instance.StopCoroutine(entry.MoveRoutine);

        entry.MoveRoutine = GetNotificationCoroutineHandler().StartCoroutine(
            MoveNotification(entry, targetPosition, NotificationMoveDuration, NotificationMoveWaveDelay * i)
        );
    }
}

private static void RemoveNotification(NotificationEntry entry)
{
    activeNotifications.Remove(entry);
    if (entry.Object != null)
        Object.Destroy(entry.Object);
}

private static void MarkNotificationStackSettling()
{
    if (activeNotifications.Count <= 0)
    {
        notificationStackSettlesAt = Time.time;
        return;
    }

    notificationStackSettlesAt = Time.time + NotificationMoveDuration + (NotificationMoveWaveDelay * (activeNotifications.Count - 1));
}

private static Vector3 GetNotificationStackPosition(int index)
{
    return new Vector3(
        0f,
        NotificationBaseY + (NotificationStackSpacing * index),
        0.6f
    );
}
private static IEnumerator MoveNotification(NotificationEntry entry, Vector3 targetPosition, float duration, float delay)
{
    if (delay > 0f)
        yield return new WaitForSeconds(delay);

    if (entry.Object == null)
        yield break;

    NotificationFollower follower = entry.Follower;
    Vector3 startPosition = follower.LocalPosition;

    if (duration <= 0f)
    {
        follower.LocalPosition = targetPosition;
        yield break;
    }

    float elapsed = 0f;
    while (elapsed < duration && entry.Object != null)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        t = 1f - ((1f - t) * (1f - t));
        follower.LocalPosition = Vector3.Lerp(startPosition, targetPosition, t);
        yield return null;
    }

    if (entry.Object != null)
        follower.LocalPosition = targetPosition;

    entry.MoveRoutine = null;
}

private sealed class NotificationEntry
{
    public readonly GameObject Object;
    public readonly TextMeshProUGUI Text;
    public readonly RectTransform Rect;
    public readonly NotificationFollower Follower;
    public readonly float TargetWidth;
    public Coroutine MoveRoutine;

    public NotificationEntry(GameObject obj, TextMeshProUGUI text, RectTransform rect, NotificationFollower follower, float targetWidth)
    {
        Object = obj;
        Text = text;
        Rect = rect;
        Follower = follower;
        TargetWidth = targetWidth;
    }
}

private readonly struct NotificationRequest
{
    public readonly string Text;
    public readonly float Duration;

    public NotificationRequest(string text, float duration)
    {
        Text = text;
        Duration = duration;
    }
}
}
