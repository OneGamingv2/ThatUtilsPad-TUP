using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Photon.Pun;
using UnityEngine;

namespace ThatUtilsPad
{
    /// <summary>
    /// Discord Rich Presence via Win32 CreateFile → discord-ipc-* pipes.
    /// Mono/BepInEx FileStream-only opens often fail on named pipes.
    /// </summary>
    public static class TupDiscordRpc
    {
        private const string PrefKey = "TUP_DiscordAppId";
        private const string PrefEnabled = "TUP_DiscordRpcEnabled";
        private const string DefaultApplicationId = "1535313488048689252";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        private static string _appId = "";
        private static bool _enabled = true;
        private static FileStream _pipe;
        private static Thread _thread;
        private static volatile bool _running;
        private static long _startUnix;
        private static string _pendingActivity = "";
        private static string _lastSent = "";
        private static readonly object _gate = new object();
        private static float _nextBuildAt;
        private static float _nextStatusLogAt;
        private static string _status = "idle";
        private static string _lastLoggedStatus = "";

        public static string Status => _status;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0);
                if (!value)
                {
                    QueueActivity("null");
                    SetStatus("disabled");
                }
                else
                {
                    ForceRebuild();
                    if (!_running)
                        StartWorker();
                }
            }
        }

        public static void SetApplicationId(string clientId)
        {
            _appId = (clientId ?? "").Trim();
            PlayerPrefs.SetString(PrefKey, _appId);
            Restart();
        }

        public static string GetApplicationId() => _appId;

        public static void Init()
        {
            // Ship the current Discord Application Client ID.
            _appId = DefaultApplicationId;
            PlayerPrefs.SetString(PrefKey, _appId);

            _enabled = PlayerPrefs.GetInt(PrefEnabled, 1) == 1;
            _startUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _partySessionId = Guid.NewGuid().ToString("D");

            if (!_enabled)
            {
                SetStatus("disabled");
                Debug.Log("[TUP Discord] RPC disabled.");
                return;
            }

            ForceRebuild();
            StartWorker();
            Debug.Log("[TUP Discord] RPC starting (app " + _appId + "). Open Discord desktop first; lobby not required.");
        }

        public static void Shutdown()
        {
            _running = false;
            QueueActivity("null");
            try { Thread.Sleep(100); } catch { }
            ClosePipe();
            _thread = null;
            SetStatus("stopped");
        }

        public static void Tick()
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_appId))
                return;

            if (Time.unscaledTime >= _nextBuildAt)
            {
                _nextBuildAt = Time.unscaledTime + 5f;
                ForceRebuild();
            }

            // Surface worker status on the main thread so it appears in BepInEx log.
            if (Time.unscaledTime >= _nextStatusLogAt)
            {
                _nextStatusLogAt = Time.unscaledTime + 8f;
                if (!string.Equals(_status, _lastLoggedStatus, StringComparison.Ordinal))
                {
                    _lastLoggedStatus = _status;
                    Debug.Log("[TUP Discord] Status: " + _status);
                }
            }
        }

        private static void SetStatus(string status)
        {
            _status = status ?? "";
        }

        private static void ForceRebuild()
        {
            QueueActivity(BuildActivityJson());
        }

        private static void QueueActivity(string activity)
        {
            lock (_gate)
                _pendingActivity = activity ?? "null";
        }

        private static void Restart()
        {
            Shutdown();
            if (!string.IsNullOrWhiteSpace(_appId) && _enabled)
            {
                _running = false;
                ForceRebuild();
                StartWorker();
            }
        }

        private static void StartWorker()
        {
            if (_running || string.IsNullOrWhiteSpace(_appId))
                return;

            _running = true;
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "TUP_DiscordRpc"
            };
            _thread.Start();
        }

        private static void WorkerLoop()
        {
            int waitLogs = 0;
            while (_running)
            {
                try
                {
                    if (_pipe == null)
                    {
                        if (!TryConnect())
                        {
                            SetStatus("waiting for Discord desktop (open Discord .exe, not browser/Store)");
                            waitLogs++;
                            if (waitLogs <= 3 || waitLogs % 10 == 0)
                                Debug.LogWarning("[TUP Discord] No discord-ipc pipe yet. Is Discord desktop running?");
                            Thread.Sleep(3000);
                            continue;
                        }

                        waitLogs = 0;
                    }

                    string activity;
                    lock (_gate)
                        activity = _pendingActivity;

                    if (!string.IsNullOrEmpty(activity) &&
                        !string.Equals(activity, _lastSent, StringComparison.Ordinal))
                    {
                        SendSetActivity(activity);
                        _lastSent = activity;
                        SetStatus(activity == "null" ? "cleared" : "presence set — check Activity Privacy");
                        Debug.Log("[TUP Discord] SET_ACTIVITY sent (" +
                                  (activity == "null" ? "clear" : "update") + ").");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("error: " + ex.Message);
                    Debug.LogWarning("[TUP Discord] " + ex.Message);
                    ClosePipe();
                    _lastSent = "";
                }

                Thread.Sleep(1500);
            }
        }

        private static bool TryConnect()
        {
            bool discordProc = IsDiscordProcessRunning();
            if (!discordProc)
                Debug.LogWarning("[TUP Discord] Discord.exe / DiscordPTB / DiscordCanary not found in process list.");

            for (int i = 0; i < 10; i++)
            {
                string path = @"\\.\pipe\discord-ipc-" + i;
                try
                {
                    SafeFileHandle handle = CreateFileW(
                        path,
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (handle == null || handle.IsInvalid)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (i == 0)
                            Debug.Log("[TUP Discord] CreateFile " + path + " failed win32=" + err +
                                      " (2=not found — Discord not exposing RPC; 5=access denied)");
                        continue;
                    }

                    // bufferSize > 0, isAsync false — required for pipe frame I/O on Mono.
                    _pipe = new FileStream(handle, FileAccess.ReadWrite, 4096, false);

                    WriteFrame(0, "{\"v\":1,\"client_id\":\"" + Escape(_appId) + "\"}");
                    ReadFrame(out int op, out string payload);

                    if (!string.IsNullOrEmpty(payload) &&
                        payload.IndexOf("\"evt\":\"ERROR\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.LogWarning("[TUP Discord] Handshake ERROR: " + Truncate(payload, 220));
                        ClosePipe();
                        continue;
                    }

                    Debug.Log("[TUP Discord] Connected " + path + " READY op=" + op);
                    SetStatus("connected " + path);
                    _lastSent = "";
                    return true;
                }
                catch (Exception ex)
                {
                    ClosePipe();
                    if (i == 0)
                        Debug.Log("[TUP Discord] Connect " + path + ": " + ex.Message);
                }
            }

            if (discordProc)
                Debug.LogWarning("[TUP Discord] Discord is running but no IPC pipe. Enable Settings → Activity Privacy → Display current activity, then fully quit Discord (tray) and reopen.");

            return false;
        }

        private static bool IsDiscordProcessRunning()
        {
            try
            {
                string[] names = { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment" };
                for (int i = 0; i < names.Length; i++)
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(names[i]);
                    if (procs != null && procs.Length > 0)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string _partySessionId = Guid.NewGuid().ToString("D");

        private static void SendSetActivity(string activityJsonOrNull)
        {
            string nonce = Guid.NewGuid().ToString("D");
            string cmd =
                "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + GetPid() +
                ",\"activity\":" + activityJsonOrNull +
                "},\"nonce\":\"" + nonce + "\"}";
            WriteFrame(1, cmd);
        }

        /// <summary>
        /// C Discord_UpdatePresence equivalent → SET_ACTIVITY.
        /// discordPresence.details / state / startTimestamp / partyId / partySize / partyMax
        /// (no joinSecret — avoids Ask-to-Join; no room codes / usernames).
        /// Bold title = Discord Application name in the Developer Portal.
        /// </summary>
        private static string BuildActivityJson()
        {
            string version = "v" + ThatUtilsPad.Main.PadVersion;

            // details ≈ discordPresence.details
            string details = "ThatUtilsPad Paid";
            // state ≈ discordPresence.state
            string state = version;
            int partySize = 1;
            int partyMax = 1;
            bool withParty = false;

            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    partySize = Math.Max(1, (int)PhotonNetwork.CurrentRoom.PlayerCount);
                    partyMax = Math.Max(partySize, (int)PhotonNetwork.CurrentRoom.MaxPlayers);
                    withParty = true;
                    // Like the sample "Playing Solo" when alone; otherwise lobby.
                    state = partySize <= 1
                        ? version + " · Playing Solo"
                        : version + " · In a lobby";
                }
                else if (PhotonNetwork.IsConnected)
                {
                    state = version + " · In stump";
                }
                else
                {
                    state = version + " · Loading";
                }
            }
            catch
            {
                state = version;
            }

            // Matches Discord_UpdatePresence fields used by the portal visualizer.
            var sb = new StringBuilder(320);
            sb.Append("{\"type\":0");
            sb.Append(",\"details\":\"").Append(Escape(details)).Append('"');
            sb.Append(",\"state\":\"").Append(Escape(state)).Append('"');
            // startTimestamp only — identical start/end makes Discord show "0:0 left"
            sb.Append(",\"timestamps\":{\"start\":").Append(_startUnix).Append('}');
            if (withParty)
            {
                sb.Append(",\"party\":{");
                sb.Append("\"id\":\"").Append(Escape(_partySessionId)).Append('"');
                sb.Append(",\"size\":[").Append(partySize).Append(',').Append(partyMax).Append(']');
                sb.Append('}');
            }
            // Optional: upload Rich Presence assets and set large_image / small_image keys.
            // sb.Append(",\"assets\":{\"large_image\":\"tup\",\"large_text\":\"ThatUtilsPad Paid\"}");
            sb.Append(",\"instance\":false}");
            return sb.ToString();
        }

        private static int GetPid()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }

        private static void WriteFrame(int op, string json)
        {
            if (_pipe == null)
                throw new IOException("Discord pipe closed");

            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];
            WriteInt32LE(header, 0, op);
            WriteInt32LE(header, 4, data.Length);
            _pipe.Write(header, 0, 8);
            _pipe.Write(data, 0, data.Length);
            _pipe.Flush();
        }

        private static void ReadFrame(out int op, out string json)
        {
            byte[] header = new byte[8];
            ReadExact(header, 8);
            op = ReadInt32LE(header, 0);
            int len = ReadInt32LE(header, 4);
            if (len < 0 || len > 1024 * 256)
                throw new IOException("Bad Discord frame length " + len);
            byte[] data = len > 0 ? new byte[len] : Array.Empty<byte>();
            if (len > 0)
                ReadExact(data, len);
            json = Encoding.UTF8.GetString(data);
        }

        private static void ReadExact(byte[] buffer, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = _pipe.Read(buffer, off, count - off);
                if (n <= 0)
                    throw new EndOfStreamException("Discord pipe EOF");
                off += n;
            }
        }

        private static void ClosePipe()
        {
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }

        private static void WriteInt32LE(byte[] buf, int offset, int value)
        {
            unchecked
            {
                buf[offset] = (byte)value;
                buf[offset + 1] = (byte)(value >> 8);
                buf[offset + 2] = (byte)(value >> 16);
                buf[offset + 3] = (byte)(value >> 24);
            }
        }

        private static int ReadInt32LE(byte[] buf, int offset)
        {
            return buf[offset]
                   | (buf[offset + 1] << 8)
                   | (buf[offset + 2] << 16)
                   | (buf[offset + 3] << 24);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s;
            return s.Substring(0, max) + "...";
        }
    }
}
