using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ThatUtilsPad.Spotify
{
    internal sealed class SpotifyMediaSnapshot
    {
        internal static readonly SpotifyMediaSnapshot Offline =
            new SpotifyMediaSnapshot("No song", "Spotify not open", "", "--:--", "Offline", false, 0, 0, 0, null);

        internal SpotifyMediaSnapshot(
            string song,
            string artist,
            string source,
            string duration,
            string status,
            bool hasTrack,
            double startSeconds,
            double endSeconds,
            double elapsedSeconds,
            byte[] thumbnailBytes)
        {
            Song = song ?? "No song";
            Artist = artist ?? "Unknown Artist";
            Source = source ?? "";
            Duration = duration ?? "--:--";
            Status = status ?? "Offline";
            HasTrack = hasTrack;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            ElapsedSeconds = elapsedSeconds;
            ThumbnailBytes = thumbnailBytes;
        }

        internal string Song { get; }
        internal string Artist { get; }
        internal string Source { get; }
        internal string Duration { get; }
        internal string Status { get; }
        internal bool HasTrack { get; }
        internal double StartSeconds { get; }
        internal double EndSeconds { get; }
        internal double ElapsedSeconds { get; }
        internal byte[] ThumbnailBytes { get; }
    }

    internal static class SpotifyMediaService
    {
        private const long PollIntervalMilliseconds = 1000;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static SpotifyMediaSnapshot latest = SpotifyMediaSnapshot.Offline;
        private static long nextPollAt;
        private static int polling;
        private static int shutdown;

        internal static SpotifyMediaSnapshot Latest
        {
            get
            {
                RequestRefresh(false);
                return Volatile.Read(ref latest);
            }
        }

        internal static void RequestRefresh(bool force)
        {
            if (Volatile.Read(ref shutdown) != 0)
                return;

            long now = Clock.ElapsedMilliseconds;
            if (!force && now < Interlocked.Read(ref nextPollAt))
                return;
            if (Interlocked.CompareExchange(ref polling, 1, 0) != 0)
                return;

            Interlocked.Exchange(ref nextPollAt, now + PollIntervalMilliseconds);
            Task.Run(PollMedia);
        }

        internal static void Shutdown()
        {
            if (Interlocked.Exchange(ref shutdown, 1) != 0)
                return;

            Task.Run(() =>
            {
                try { WindowsMediaSessionProvider.Shutdown(); }
                catch { }
            });
        }

        private static void PollMedia()
        {
            try
            {
                SpotifyMediaSnapshot snapshot = ReadManagedSnapshot();
                if (!snapshot.HasTrack && !snapshot.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase))
                {
                    SpotifyMediaSnapshot fallback = ReadSpotifyWindowTitle();
                    if (fallback.HasTrack)
                        snapshot = fallback;
                }
                Volatile.Write(ref latest, snapshot);
            }
            catch (Exception error)
            {
                SpotifyMediaSnapshot fallback = ReadSpotifyWindowTitle();
                Volatile.Write(ref latest, fallback.HasTrack
                    ? fallback
                    : new SpotifyMediaSnapshot("No song", "Spotify error: " + error.Message, "", "--:--", "Offline", false, 0, 0, 0, null));
            }
            finally
            {
                Interlocked.Exchange(ref polling, 0);
            }
        }

        private static SpotifyMediaSnapshot ReadManagedSnapshot()
        {
            WindowsMediaSessionProvider.MediaSnapshot result = WindowsMediaSessionProvider.GetSnapshot();
            if (result.ErrorCode < 0)
            {
                string message = string.IsNullOrEmpty(result.ErrorMessage)
                    ? "Native media query failed (0x" + result.ErrorCode.ToString("X8") + ")."
                    : result.ErrorMessage;
                throw new InvalidOperationException(message);
            }

            string title = Clean(result.Title);
            string artist = Clean(result.Artist);
            string source = Clean(result.Source);
            bool hasTrack = IsUsefulTitle(title);
            if (!hasTrack)
                title = "No song";
            if (string.IsNullOrEmpty(artist))
                artist = "Unknown Artist";

            string status = result.PlaybackStatus == 4 ? "Playing" :
                result.PlaybackStatus == 5 ? "Paused" :
                (hasTrack ? "Stopped" : "Offline");
            string duration = result.EndSeconds > 0
                ? FormatTime(result.ElapsedSeconds) + " / " + FormatTime(result.EndSeconds)
                : "--:--";

            return new SpotifyMediaSnapshot(
                title,
                artist,
                source,
                duration,
                status,
                hasTrack,
                result.StartSeconds,
                result.EndSeconds,
                result.ElapsedSeconds,
                result.ThumbnailBytes);
        }

        private static SpotifyMediaSnapshot ReadSpotifyWindowTitle()
        {
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName("Spotify");
                string title = GetFirstUsefulWindowTitle(processes);
                if (string.IsNullOrWhiteSpace(title))
                {
                    foreach (Process process in processes)
                    {
                        string mainWindowTitle = process.MainWindowTitle;
                        if (IsUsefulTitle(mainWindowTitle))
                        {
                            title = mainWindowTitle;
                            break;
                        }
                    }
                }
                if (string.IsNullOrWhiteSpace(title))
                    return SpotifyMediaSnapshot.Offline;

                title = title.Trim();
                string artist = "Unknown Artist";
                string song = title;
                int separator = title.IndexOf(" - ", StringComparison.Ordinal);
                if (separator > 0)
                {
                    artist = title.Substring(0, separator).Trim();
                    song = title.Substring(separator + 3).Trim();
                }
                if (!IsUsefulTitle(song))
                    return SpotifyMediaSnapshot.Offline;

                return new SpotifyMediaSnapshot(song, artist, "Spotify.exe", "--:--", "Playing", true, 0, 0, 0, null);
            }
            catch
            {
                return SpotifyMediaSnapshot.Offline;
            }
            finally
            {
                if (processes != null)
                {
                    foreach (Process process in processes)
                    {
                        try { process?.Dispose(); }
                        catch { }
                    }
                }
            }
        }

        private static string GetFirstUsefulWindowTitle(Process[] processes)
        {
            HashSet<uint> processIds = new HashSet<uint>();
            foreach (Process process in processes)
                processIds.Add((uint)process.Id);

            string usefulTitle = null;
            NativeMethods.EnumWindows((window, parameter) =>
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint processId);
                if (!processIds.Contains(processId))
                    return true;

                int length = NativeMethods.GetWindowTextLength(window);
                if (length <= 0)
                    return true;

                StringBuilder builder = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(window, builder, builder.Capacity);
                string title = builder.ToString().Trim();
                if (IsUsefulTitle(title))
                {
                    usefulTitle = title;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return usefulTitle;
        }

        private static bool IsUsefulTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string title = value.Trim();
            if (title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("unknown artist", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase))
                return false;
            return title.IndexOf("Window (Spotify.exe)", StringComparison.OrdinalIgnoreCase) < 0 &&
                   title.IndexOf("Spotify.exe", StringComparison.OrdinalIgnoreCase) < 0 &&
                   !title.StartsWith("GDI+", StringComparison.OrdinalIgnoreCase) &&
                   title.IndexOf("Default IME", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string Clean(string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace("\r", " ").Replace("\n", " ");

        private static string FormatTime(double seconds)
        {
            seconds = Math.Max(0, seconds);
            return ((int)(seconds / 60)) + ":" + ((int)(seconds % 60)).ToString("00");
        }

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

            [DllImport("user32.dll")]
            internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

            [DllImport("user32.dll")]
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern int GetWindowTextLength(IntPtr window);
        }
    }
}
