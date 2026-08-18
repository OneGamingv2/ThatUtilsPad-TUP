using System;
using System.Collections;
using System.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;

public partial class Main
{
    private bool updateCheckStarted;

    private IEnumerator CheckForUpdatesRoutine()
    {
        if (updateCheckStarted)
            yield break;
        updateCheckStarted = true;

        yield return new WaitForSeconds(4.5f);

        string json = null;
        Exception fetchError = null;
        bool done = false;

        try
        {
            using WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "ThatUtilsPad/" + PadVersion;
            client.DownloadStringCompleted += (_, e) =>
            {
                if (e.Error != null)
                    fetchError = e.Error;
                else
                    json = e.Result;
                done = true;
            };
            client.DownloadStringAsync(new Uri(UpdateApiUrl));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Update check start failed: " + ex.Message);
            yield break;
        }

        float timeoutAt = Time.realtimeSinceStartup + 12f;
        while (!done && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        if (!done || fetchError != null || string.IsNullOrWhiteSpace(json))
        {
            if (fetchError != null)
            {
                // Endpoint may not exist yet — quiet skip for 404.
                string msg = fetchError.Message ?? "";
                if (msg.IndexOf("(404)", StringComparison.OrdinalIgnoreCase) < 0 &&
                    msg.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) < 0)
                    Debug.LogWarning("[TUP] Update check failed: " + msg);
            }
            yield break;
        }

        try
        {
            HandleUpdateApiPayload(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TUP] Update parse failed: " + ex.Message);
        }
    }

    private static void HandleUpdateApiPayload(string json)
    {
        JObject root = JObject.Parse(json);
        if (root == null)
            return;

        if (root["ok"] != null && root["ok"].Type == JTokenType.Boolean && !(bool)root["ok"])
            return;
        if (root["exists"] != null && root["exists"].Type == JTokenType.Boolean && !(bool)root["exists"])
            return;

        string remoteVersion = root["version"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(remoteVersion))
            return;

        remoteVersion = remoteVersion.Trim().TrimStart('v', 'V');
        if (!IsRemoteVersionNewer(PadVersion, remoteVersion))
            return;

        string downloadUrl = root["downloadUrl"]?.ToString();
        if (string.IsNullOrWhiteSpace(downloadUrl))
            downloadUrl = PaidDownloadUrl;

        string message = PadDisplayName + " " + remoteVersion + " is available (you have " + PadVersion + ")";
        Mods.ShowNotification(message, 5.5f);

        string changelog = root["changelog"]?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(changelog))
        {
            string shortNotes = changelog.Trim().Replace("\r\n", "\n").Replace('\n', ' ');
            if (shortNotes.Length > 120)
                shortNotes = shortNotes.Substring(0, 117) + "...";
            Mods.ShowNotification(shortNotes, 6.5f);
        }
        else
        {
            Mods.ShowNotification("Download: " + downloadUrl, 4.5f);
        }
    }

    private static bool IsRemoteVersionNewer(string localVersion, string remoteVersion)
    {
        if (!TryParseVersion(localVersion, out Version local))
            local = new Version(0, 0, 0);
        if (!TryParseVersion(remoteVersion, out Version remote))
            return false;
        return remote > local;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Trim().TrimStart('v', 'V');
        int dash = cleaned.IndexOf('-');
        if (dash > 0)
            cleaned = cleaned.Substring(0, dash);

        string[] parts = cleaned.Split('.');
        if (parts.Length < 2)
            return false;

        try
        {
            int major = int.Parse(parts[0]);
            int minor = int.Parse(parts[1]);
            int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            version = new Version(major, minor, build);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
