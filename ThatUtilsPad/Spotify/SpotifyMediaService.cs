using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private const string NativeLibrary = "ThatUtilsPad.MediaBridge.dll";
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
            Task.Run(PollNative);
        }

        internal static void Shutdown()
        {
            if (Interlocked.Exchange(ref shutdown, 1) != 0)
                return;

            Task.Run(() =>
            {
                try
                {
                    NativeMethods.TUP_Shutdown();
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (BadImageFormatException) { }
                catch { }
            });
        }

        private static void PollNative()
        {
            try
            {
                SpotifyMediaSnapshot snapshot = ReadNativeSnapshot();
                if (!snapshot.HasTrack && !snapshot.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase))
                {
                    SpotifyMediaSnapshot fallback = ReadSpotifyWindowTitle();
                    if (fallback.HasTrack)
                        snapshot = fallback;
                }
                Volatile.Write(ref latest, snapshot);
            }
            catch (DllNotFoundException)
            {
                Volatile.Write(ref latest, ReadSpotifyWindowTitle());
            }
            catch (EntryPointNotFoundException)
            {
                Volatile.Write(ref latest, ReadSpotifyWindowTitle());
            }
            catch (BadImageFormatException)
            {
                Volatile.Write(ref latest, ReadSpotifyWindowTitle());
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

        private static SpotifyMediaSnapshot ReadNativeSnapshot()
        {
            IntPtr resultPointer = IntPtr.Zero;
            int callResult = NativeMethods.TUP_GetMediaSnapshot(out resultPointer);
            if (callResult < 0)
                Marshal.ThrowExceptionForHR(callResult);
            if (resultPointer == IntPtr.Zero)
                throw new InvalidOperationException("Media bridge returned no result.");

            try
            {
                NativeResult result = Marshal.PtrToStructure<NativeResult>(resultPointer);
                if (result.AbiVersion != 1 || result.StructSize < Marshal.SizeOf<NativeResult>())
                    throw new InvalidOperationException("Unsupported media bridge ABI.");
                if (result.ErrorCode < 0)
                {
                    string nativeError = ReadString(result.ErrorMessage);
                    if (string.IsNullOrEmpty(nativeError))
                        nativeError = "Native media query failed (0x" + result.ErrorCode.ToString("X8") + ").";
                    throw new InvalidOperationException(nativeError);
                }

                string title = Clean(ReadString(result.Title));
                string artist = Clean(ReadString(result.Artist));
                string source = Clean(ReadString(result.Source));
                bool hasTrack = IsUsefulTitle(title);
                if (!hasTrack)
                    title = "No song";
                if (string.IsNullOrEmpty(artist))
                    artist = "Unknown Artist";

                byte[] thumbnail = null;
                if (result.ThumbnailBytes != IntPtr.Zero && result.ThumbnailLength > 0 &&
                    result.ThumbnailLength <= int.MaxValue)
                {
                    thumbnail = new byte[(int)result.ThumbnailLength];
                    Marshal.Copy(result.ThumbnailBytes, thumbnail, 0, thumbnail.Length);
                }

                string status = result.PlaybackStatus == 5 ? "Playing" :
                    result.PlaybackStatus == 6 ? "Paused" : (hasTrack ? "Stopped" : "Offline");
                string duration = result.EndSeconds > 0
                    ? FormatTime(result.ElapsedSeconds) + " / " + FormatTime(result.EndSeconds)
                    : "--:--";
                return new SpotifyMediaSnapshot(title, artist, source, duration, status, hasTrack,
                    result.StartSeconds, result.EndSeconds, result.ElapsedSeconds, thumbnail);
            }
            finally
            {
                NativeMethods.TUP_FreeMediaResult(resultPointer);
            }
        }

        private static SpotifyMediaSnapshot ReadSpotifyWindowTitle()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("Spotify");
                List<string> titles = GetWindowTitles(processes);
                string title = titles.FirstOrDefault(IsUsefulTitle) ??
                    processes.Select(process => process.MainWindowTitle).FirstOrDefault(IsUsefulTitle);
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
        }

        private static List<string> GetWindowTitles(Process[] processes)
        {
            HashSet<uint> processIds = new HashSet<uint>(processes.Select(process => (uint)process.Id));
            List<string> titles = new List<string>();
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
                if (!string.IsNullOrWhiteSpace(title) && !titles.Contains(title))
                    titles.Add(title);
                return true;
            }, IntPtr.Zero);
            return titles;
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

        private static string ReadString(IntPtr value) =>
            value == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value) ?? "";

        private static string Clean(string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace("\r", " ").Replace("\n", " ");

        private static string FormatTime(double seconds)
        {
            seconds = Math.Max(0, seconds);
            return ((int)(seconds / 60)) + ":" + ((int)(seconds % 60)).ToString("00");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeResult
        {
            internal uint AbiVersion;
            internal uint StructSize;
            internal int ErrorCode;
            internal int PlaybackStatus;
            internal IntPtr Title;
            internal IntPtr Artist;
            internal IntPtr Source;
            internal IntPtr ErrorMessage;
            internal double StartSeconds;
            internal double EndSeconds;
            internal double ElapsedSeconds;
            internal IntPtr ThumbnailBytes;
            internal uint ThumbnailLength;
        }

        private static class NativeMethods
        {
            internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

            [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int TUP_GetMediaSnapshot(out IntPtr result);

            [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void TUP_FreeMediaResult(IntPtr result);

            [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void TUP_Shutdown();

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
