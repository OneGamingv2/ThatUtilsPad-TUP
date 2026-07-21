using System;
using System.Runtime.InteropServices;
using ThatUtilsPad.Spotify;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad
{
    public static partial class Mods
    {
        public readonly struct SpotifyTrackInfo
        {
            public readonly string Song;
            public readonly string Artist;
            public readonly string Duration;
            public readonly string Status;
            public readonly bool HasTrack;
            public readonly byte[] ThumbnailBytes;
            public readonly float StartTime;
            public readonly float EndTime;
            public readonly float ElapsedTime;

            public SpotifyTrackInfo(string song, string artist, string duration, string status, bool hasTrack)
                : this(song, artist, duration, status, hasTrack, 0, 0, 0, null)
            {
            }

            public SpotifyTrackInfo(
                string song,
                string artist,
                string duration,
                string status,
                bool hasTrack,
                float startTime,
                float endTime,
                float elapsedTime,
                byte[] thumbnailBytes = null)
            {
                Song = song;
                Artist = artist;
                Duration = duration;
                Status = status;
                HasTrack = hasTrack;
                StartTime = startTime;
                EndTime = endTime;
                ElapsedTime = elapsedTime;
                ThumbnailBytes = thumbnailBytes;
            }
        }

        private const byte SpotifyKeyPrevious = 0xB1;
        private const byte SpotifyKeyPlayPause = 0xB3;
        private const byte SpotifyKeyNext = 0xB0;
        private const uint SpotifyKeyUp = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        public static SpotifyTrackInfo GetSpotifyTrackInfo()
        {
            SpotifyMediaSnapshot snapshot = SpotifyMediaService.Latest;
            return new SpotifyTrackInfo(
                snapshot.Song,
                snapshot.Artist,
                snapshot.Duration,
                snapshot.Status,
                snapshot.HasTrack,
                (float)snapshot.StartSeconds,
                (float)snapshot.EndSeconds,
                (float)snapshot.ElapsedSeconds,
                snapshot.ThumbnailBytes);
        }

        public static void ForgetSpotifyInfo()
        {
            SpotifyMediaService.RequestRefresh(true);
        }

        public static void SpotifyPlayPause()
        {
            TapSpotifyMediaKey(SpotifyKeyPlayPause);
            RefreshSpotifyAfterControl();
        }

        public static void SpotifyNext()
        {
            TapSpotifyMediaKey(SpotifyKeyNext);
            RefreshSpotifyAfterControl();
        }

        public static void SpotifyPrevious()
        {
            TapSpotifyMediaKey(SpotifyKeyPrevious);
            RefreshSpotifyAfterControl();
        }

        public static void SpotifyRefresh()
        {
            ForgetSpotifyInfo();
            Main.Instance?.PaintSpotifyHud();
        }

        internal static void ShutdownSpotifyMedia()
        {
            SpotifyMediaService.Shutdown();
        }

        private static void RefreshSpotifyAfterControl()
        {
            ForgetSpotifyInfo();
            Main.Instance?.NudgeSpotifyHudSoon(0.35f);
        }

        private static void TapSpotifyMediaKey(byte virtualKey)
        {
            try
            {
                keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
                keybd_event(virtualKey, 0, SpotifyKeyUp, UIntPtr.Zero);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[TUP] Spotify media key failed: " + error.Message);
            }
        }
    }
}
