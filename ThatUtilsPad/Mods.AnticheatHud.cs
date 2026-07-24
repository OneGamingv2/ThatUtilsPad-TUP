using System;
using System.Collections.Generic;
using System.Text;
using GorillaLocomotion;
using Photon.Pun;
using Photon.Realtime;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ThatUtilsPad;

public static partial class Mods
{
    private static GameObject anticheatHudRoot;
    private static TMP_Text anticheatHudBody;
    private static TMP_Text anticheatHudTitle;
    private static TMP_Text anticheatHudSubtitle;
    private static Image anticheatHudPanel;
    private static Image anticheatHudAccent;
    private static bool anticheatHudEnabled;
    private static float nextAnticheatHudRefresh;
    private static string lastAnticheatHudText = "";
    private const float AnticheatHudRefreshSeconds = 0.75f;
    private const int AnticheatHudMaxPlayers = 12;
    private const int AnticheatHudMaxCheatsPerPlayer = 6;

    private static void ToggleAnticheatHud()
    {
        if (anticheatHudEnabled)
            HideAnticheatHud();
        else
            ShowAnticheatHud();
    }

    private static void ShowAnticheatHud()
    {
        EnsureAnticheatHud();
        if (anticheatHudRoot == null)
        {
            ShowNotification("Anticheat HUD failed to create", NotificationDefaultDuration);
            return;
        }

        anticheatHudEnabled = true;
        anticheatHudRoot.SetActive(true);
        PositionAnticheatHud(true);
        nextAnticheatHudRefresh = 0f;
        RefreshAnticheatHud(true);
        ShowNotification("Anticheat panel open", NotificationDefaultDuration);
    }

    private static void HideAnticheatHud()
    {
        anticheatHudEnabled = false;
        if (anticheatHudRoot != null)
            anticheatHudRoot.SetActive(false);
        ShowNotification("Anticheat panel closed", NotificationDefaultDuration);
    }

    internal static void ShutdownAnticheatHud()
    {
        anticheatHudEnabled = false;
        if (anticheatHudRoot == null)
            return;

        Object.Destroy(anticheatHudRoot);
        anticheatHudRoot = null;
        anticheatHudBody = null;
        anticheatHudTitle = null;
        anticheatHudSubtitle = null;
        anticheatHudPanel = null;
        anticheatHudAccent = null;
        lastAnticheatHudText = "";
    }

    public static void AnticheatHudLoop()
    {
        if (!anticheatHudEnabled || anticheatHudRoot == null || !anticheatHudRoot.activeSelf)
            return;

        PositionAnticheatHud(false);
        if (Time.time < nextAnticheatHudRefresh)
            return;

        nextAnticheatHudRefresh = Time.time + AnticheatHudRefreshSeconds;
        RefreshAnticheatHud(false);
    }

    private static void EnsureAnticheatHud()
    {
        if (anticheatHudRoot != null)
            return;

        anticheatHudRoot = new GameObject("TUP_AnticheatHud");
        Object.DontDestroyOnLoad(anticheatHudRoot);

        Canvas canvas = anticheatHudRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 80;

        RectTransform canvasRect = anticheatHudRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(920f, 1180f);
        anticheatHudRoot.transform.localScale = Vector3.one * 0.00115f;

        CanvasScaler scaler = anticheatHudRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;
        anticheatHudRoot.AddComponent<GraphicRaycaster>();

        GameObject panelGo = CreateHudUiObject("Panel", anticheatHudRoot.transform, Vector2.zero, new Vector2(900f, 1160f));
        anticheatHudPanel = panelGo.AddComponent<Image>();
        anticheatHudPanel.color = new Color(0.035f, 0.035f, 0.055f, 0.94f);

        GameObject accentGo = CreateHudUiObject("Accent", panelGo.transform, new Vector2(0f, 545f), new Vector2(900f, 8f));
        anticheatHudAccent = accentGo.AddComponent<Image>();
        anticheatHudAccent.color = new Color(1f, 0.35f, 0.38f, 1f);

        anticheatHudTitle = CreateHudLabel("Title", panelGo.transform, new Vector2(0f, 480f), new Vector2(840f, 70f), 46f, FontStyles.Bold);
        anticheatHudTitle.text = "ANTICHEAT";
        anticheatHudTitle.color = Color.white;
        anticheatHudTitle.alignment = TextAlignmentOptions.Center;

        anticheatHudSubtitle = CreateHudLabel("Subtitle", panelGo.transform, new Vector2(0f, 415f), new Vector2(840f, 42f), 26f, FontStyles.Normal);
        anticheatHudSubtitle.color = new Color(0.75f, 0.75f, 0.82f, 1f);
        anticheatHudSubtitle.alignment = TextAlignmentOptions.Center;

        anticheatHudBody = CreateHudLabel("Body", panelGo.transform, new Vector2(0f, -40f), new Vector2(840f, 920f), 28f, FontStyles.Normal);
        anticheatHudBody.color = new Color(0.92f, 0.92f, 0.95f, 1f);
        anticheatHudBody.alignment = TextAlignmentOptions.TopLeft;
        anticheatHudBody.enableWordWrapping = true;
        anticheatHudBody.overflowMode = TextOverflowModes.Truncate;
        anticheatHudBody.richText = true;
        anticheatHudBody.lineSpacing = 8f;

        ApplyAnticheatHudTheme();
        anticheatHudRoot.SetActive(false);
    }

    private static GameObject CreateHudUiObject(string name, Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return go;
    }

    private static TMP_Text CreateHudLabel(string name, Transform parent, Vector2 anchoredPos, Vector2 size, float fontSize, FontStyles style)
    {
        GameObject go = CreateHudUiObject(name, parent, anchoredPos, size);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.raycastTarget = false;

        if (FontCache.figtreeBundle != null)
        {
            TMP_FontAsset font = FontCache.figtreeBundle.LoadAsset<TMP_FontAsset>("assets/fonts/figtree.asset");
            if (font != null)
                text.font = font;
        }

        return text;
    }

    private static void ApplyAnticheatHudTheme()
    {
        MenuThemePalette theme = MenuTheme.Current;
        if (anticheatHudPanel != null)
            anticheatHudPanel.color = new Color(theme.Main.r / 255f, theme.Main.g / 255f, theme.Main.b / 255f, 0.94f);
        if (anticheatHudAccent != null)
            anticheatHudAccent.color = new Color(1f, 0.35f, 0.38f, 1f);
    }

    private static void PositionAnticheatHud(bool snap)
    {
        if (anticheatHudRoot == null)
            return;

        Transform head = GTPlayer.Instance?.headCollider?.transform
                         ?? GorillaTagger.Instance?.mainCamera?.transform
                         ?? Main.GetActiveCamera()?.transform;
        if (head == null)
            return;

        Vector3 targetPos = head.position + head.forward * 0.58f + head.up * 0.02f;
        Quaternion targetRot = Quaternion.LookRotation(head.position - targetPos, head.up);

        if (snap)
        {
            anticheatHudRoot.transform.position = targetPos;
            anticheatHudRoot.transform.rotation = targetRot;
            return;
        }

        float t = 1f - Mathf.Exp(-10f * Time.deltaTime);
        anticheatHudRoot.transform.position = Vector3.Lerp(anticheatHudRoot.transform.position, targetPos, t);
        anticheatHudRoot.transform.rotation = Quaternion.Slerp(anticheatHudRoot.transform.rotation, targetRot, t);
    }

    private static void RefreshAnticheatHud(bool force)
    {
        if (anticheatHudBody == null)
            return;

        ApplyAnticheatHudTheme();

        if (!PhotonNetwork.InRoom)
        {
            SetAnticheatHudText("Not in a room", "Waiting for lobby...", force);
            return;
        }

        LoadPropertyListIfNeeded();
        ScanLobbyMode mode = (ScanLobbyMode)Mathf.Clamp(scanModeIndex, 0, scanModeNames.Length - 1);

        StringBuilder body = new StringBuilder(1024);
        int flaggedPlayers = 0;
        int totalCheats = 0;
        int totalMods = 0;
        int totalUnknown = 0;
        int listed = 0;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (player == null || player.IsLocal)
                continue;

            PlayerPropScanResult scan = ScanPlayerCustomProperties(player);
            List<string> illegal = scan.IllegalMods ?? new List<string>();
            List<string> legal = scan.LegalMods ?? new List<string>();
            List<string> unknown = scan.UnknownProps ?? new List<string>();

            totalCheats += illegal.Count;
            totalMods += legal.Count;
            totalUnknown += unknown.Count;

            bool show =
                (mode == ScanLobbyMode.CheatsOnly && illegal.Count > 0) ||
                (mode == ScanLobbyMode.ModsOnly && (legal.Count > 0 || unknown.Count > 0)) ||
                (mode == ScanLobbyMode.ModsAndCheats && (illegal.Count > 0 || legal.Count > 0 || unknown.Count > 0));

            if (!show)
                continue;

            flaggedPlayers++;
            if (listed >= AnticheatHudMaxPlayers)
                continue;

            listed++;
            string name = string.IsNullOrWhiteSpace(player.NickName) ? "Player " + player.ActorNumber : player.NickName;
            body.Append("<color=#FF7A7A><b>").Append(EscapeHudText(name)).Append("</b></color>\n");

            if (mode != ScanLobbyMode.ModsOnly && illegal.Count > 0)
            {
                int shown = 0;
                foreach (string cheat in illegal)
                {
                    if (shown >= AnticheatHudMaxCheatsPerPlayer)
                    {
                        body.Append("  <color=#FF9A9A>+").Append(illegal.Count - shown).Append(" more</color>\n");
                        break;
                    }

                    body.Append("  <color=#FF5C5C>• ").Append(EscapeHudText(cheat)).Append("</color>\n");
                    shown++;
                }
            }

            if (mode != ScanLobbyMode.CheatsOnly && legal.Count > 0)
            {
                int shown = 0;
                foreach (string mod in legal)
                {
                    if (shown >= AnticheatHudMaxCheatsPerPlayer)
                    {
                        body.Append("  <color=#A8B0C0>+").Append(legal.Count - shown).Append(" more</color>\n");
                        break;
                    }

                    body.Append("  <color=#9AD0FF>• ").Append(EscapeHudText(mod)).Append("</color>\n");
                    shown++;
                }
            }

            if (mode != ScanLobbyMode.CheatsOnly && unknown.Count > 0)
            {
                int shown = 0;
                foreach (string prop in unknown)
                {
                    if (shown >= AnticheatHudMaxCheatsPerPlayer)
                    {
                        body.Append("  <color=#FFE08A>+").Append(unknown.Count - shown).Append(" more props</color>\n");
                        break;
                    }

                    body.Append("  <color=#FFD166>• ").Append(EscapeHudText(prop)).Append("</color>\n");
                    shown++;
                }
            }

            body.Append('\n');
        }

        string subtitle = $"{GetScanModeLabel()}  •  {flaggedPlayers} players  •  {totalCheats} cheats  •  {totalMods} mods  •  {totalUnknown} unknown";
        string text = body.Length == 0
            ? "<color=#A8B0C0>No anticheat / mod / unknown prop signals found.</color>\n\nTry Scan Lobby or wait for players to load."
            : body.ToString().TrimEnd();

        if (flaggedPlayers > AnticheatHudMaxPlayers)
            text += $"\n\n<color=#A8B0C0>+{flaggedPlayers - AnticheatHudMaxPlayers} more players not shown</color>";

        SetAnticheatHudText(subtitle, text, force);
    }

    private static void SetAnticheatHudText(string subtitle, string body, bool force)
    {
        string key = subtitle + "\n" + body;
        if (!force && key == lastAnticheatHudText)
            return;

        lastAnticheatHudText = key;
        if (anticheatHudSubtitle != null)
            anticheatHudSubtitle.text = subtitle;
        if (anticheatHudBody != null)
            anticheatHudBody.text = body;
        if (anticheatHudTitle != null)
            anticheatHudTitle.text = "ANTICHEAT";
    }

    private static string EscapeHudText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("<", "(").Replace(">", ")");
    }
}
