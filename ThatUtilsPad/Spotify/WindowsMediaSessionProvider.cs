using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ThatUtilsPad.Spotify
{
    internal static class WindowsMediaSessionProvider
    {
        private const string ManagerClassName = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
        private static readonly Guid ManagerStaticsIid = new Guid("2050C4EE-11A0-57DE-AED7-C97C70338245");
        private static readonly Guid AsyncInfoIid = new Guid("00000036-0000-0000-C000-000000000046");
        private static readonly Guid ClosableIid = new Guid("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
        private static readonly Guid RandomAccessStreamIid = new Guid("905A0FE1-BC53-11DF-8C49-001E4FC686DA");
        private static readonly Guid DataReaderFactoryIid = new Guid("D7527847-57DA-4E15-914C-06806699A098");

        private static readonly object Gate = new object();
        private static IntPtr cachedManager;
        private static bool apartmentReady;
        private static bool apartmentFailed;
        private static string apartmentError = "";

        internal sealed class MediaSnapshot
        {
            internal string Title = "";
            internal string Artist = "";
            internal string Source = "";
            internal string ErrorMessage = "";
            internal int ErrorCode;
            internal int PlaybackStatus;
            internal double StartSeconds;
            internal double EndSeconds;
            internal double ElapsedSeconds;
            internal byte[] ThumbnailBytes;
        }

        internal static MediaSnapshot GetSnapshot()
        {
            lock (Gate)
            {
                try
                {
                    EnsureApartment();
                    if (apartmentFailed)
                    {
                        return new MediaSnapshot
                        {
                            ErrorCode = unchecked((int)0x80004005),
                            ErrorMessage = apartmentError
                        };
                    }

                    IntPtr manager = EnsureManager();
                    if (manager == IntPtr.Zero)
                        return new MediaSnapshot();

                    return ReadBestSession(manager);
                }
                catch (Exception error)
                {
                    Release(ref cachedManager);
                    return new MediaSnapshot
                    {
                        ErrorCode = Marshal.GetHRForException(error),
                        ErrorMessage = error.Message
                    };
                }
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                Release(ref cachedManager);
            }
        }

        private static void EnsureApartment()
        {
            if (apartmentReady || apartmentFailed)
                return;

            int hr = RoInitialize(1);
            if (hr == 0 || hr == 1 || hr == unchecked((int)0x80010106))
            {
                apartmentReady = true;
                return;
            }

            apartmentFailed = true;
            apartmentError = "RoInitialize failed: 0x" + hr.ToString("X8");
        }

        private static IntPtr EnsureManager()
        {
            if (cachedManager != IntPtr.Zero)
                return cachedManager;

            IntPtr className = IntPtr.Zero;
            IntPtr factory = IntPtr.Zero;
            IntPtr asyncOp = IntPtr.Zero;
            try
            {
                ThrowOnFailure(WindowsCreateString(ManagerClassName, (uint)ManagerClassName.Length, out className));
                Guid iid = ManagerStaticsIid;
                ThrowOnFailure(RoGetActivationFactory(className, ref iid, out factory));
                ThrowOnFailure(Vtable.Call1(factory, 6, out asyncOp));
                IntPtr manager = Await(asyncOp);
                asyncOp = IntPtr.Zero;
                cachedManager = manager;
                return cachedManager;
            }
            finally
            {
                Release(ref asyncOp);
                Release(ref factory);
                if (className != IntPtr.Zero)
                    WindowsDeleteString(className);
            }
        }

        private static MediaSnapshot ReadBestSession(IntPtr manager)
        {
            IntPtr sessions = IntPtr.Zero;
            try
            {
                ThrowOnFailure(Vtable.Call1(manager, 7, out sessions));
                ThrowOnFailure(Vtable.CallOutInt(sessions, 7, out int count));

                MediaSnapshot best = null;
                int bestScore = -1;

                for (int index = 0; index < count; index++)
                {
                    IntPtr session = IntPtr.Zero;
                    try
                    {
                        ThrowOnFailure(Vtable.CallGetAt(sessions, 6, index, out session));
                        MediaSnapshot candidate = ReadSession(session);
                        int score = ScoreSession(candidate);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Release(ref session);
                    }
                }

                return best ?? new MediaSnapshot();
            }
            finally
            {
                Release(ref sessions);
            }
        }

        private static MediaSnapshot ReadSession(IntPtr session)
        {
            MediaSnapshot result = new MediaSnapshot();
            IntPtr mediaAsync = IntPtr.Zero;
            IntPtr media = IntPtr.Zero;
            IntPtr playback = IntPtr.Zero;
            IntPtr timeline = IntPtr.Zero;
            try
            {
                result.Source = GetHString(session, 6);
                ThrowOnFailure(Vtable.Call1(session, 7, out mediaAsync));
                media = Await(mediaAsync);
                mediaAsync = IntPtr.Zero;

                result.Title = GetHString(media, 6);
                result.Artist = GetHString(media, 9);

                ThrowOnFailure(Vtable.Call1(session, 9, out playback));
                ThrowOnFailure(Vtable.CallOutInt(playback, 7, out result.PlaybackStatus));

                ThrowOnFailure(Vtable.Call1(session, 8, out timeline));
                result.StartSeconds = GetTimeSpanSeconds(timeline, 6);
                result.EndSeconds = GetTimeSpanSeconds(timeline, 7);
                result.ElapsedSeconds = GetTimeSpanSeconds(timeline, 10);

                result.ThumbnailBytes = TryReadThumbnail(media);
                return result;
            }
            finally
            {
                Release(ref timeline);
                Release(ref playback);
                Release(ref media);
                Release(ref mediaAsync);
            }
        }

        private static byte[] TryReadThumbnail(IntPtr media)
        {
            IntPtr thumbnailRef = IntPtr.Zero;
            IntPtr openAsync = IntPtr.Zero;
            IntPtr contentStream = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;
            try
            {
                int hr = Vtable.Call1(media, 15, out thumbnailRef);
                if (hr < 0 || thumbnailRef == IntPtr.Zero)
                    return null;

                ThrowOnFailure(Vtable.Call1(thumbnailRef, 6, out openAsync));
                contentStream = Await(openAsync);
                openAsync = IntPtr.Zero;
                if (contentStream == IntPtr.Zero)
                    return null;

                Guid rasIid = RandomAccessStreamIid;
                ThrowOnFailure(Marshal.QueryInterface(contentStream, ref rasIid, out stream));
                Release(ref contentStream);

                ThrowOnFailure(Vtable.CallOutULong(stream, 6, out ulong size));
                if (size == 0 || size > 8 * 1024 * 1024)
                    return null;

                return ReadStreamBytes(stream, (uint)size);
            }
            catch
            {
                return null;
            }
            finally
            {
                Release(ref stream);
                Release(ref contentStream);
                Release(ref openAsync);
                Release(ref thumbnailRef);
            }
        }

        private static byte[] ReadStreamBytes(IntPtr stream, uint size)
        {
            IntPtr inputStream = IntPtr.Zero;
            IntPtr dataReaderFactory = IntPtr.Zero;
            IntPtr dataReader = IntPtr.Zero;
            IntPtr loadAsync = IntPtr.Zero;
            IntPtr className = IntPtr.Zero;
            try
            {
                ThrowOnFailure(Vtable.CallGetInputStreamAt(stream, 8, 0UL, out inputStream));

                const string dataReaderClass = "Windows.Storage.Streams.DataReader";
                Guid factoryIid = DataReaderFactoryIid;
                ThrowOnFailure(WindowsCreateString(dataReaderClass, (uint)dataReaderClass.Length, out className));
                ThrowOnFailure(RoGetActivationFactory(className, ref factoryIid, out dataReaderFactory));
                ThrowOnFailure(Vtable.CallCreateDataReader(dataReaderFactory, 6, inputStream, out dataReader));
                ThrowOnFailure(Vtable.CallLoadAsync(dataReader, 29, size, out loadAsync));
                AwaitUInt(loadAsync);
                loadAsync = IntPtr.Zero;

                byte[] bytes = new byte[size];
                GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    ThrowOnFailure(Vtable.CallReadBytes(dataReader, 14, size, handle.AddrOfPinnedObject()));
                }
                finally
                {
                    handle.Free();
                }
                return bytes;
            }
            catch
            {
                return null;
            }
            finally
            {
                Release(ref loadAsync);
                Release(ref dataReader);
                Release(ref dataReaderFactory);
                Release(ref inputStream);
                if (className != IntPtr.Zero)
                    WindowsDeleteString(className);
            }
        }

        private static int ScoreSession(MediaSnapshot snapshot)
        {
            if (snapshot == null || !IsUsefulTitle(snapshot.Title))
                return -1;

            bool spotify = (snapshot.Source ?? "").IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0;
            bool playing = snapshot.PlaybackStatus == 4;
            if (spotify)
                return 100;
            if (playing)
                return 10;
            return -1;
        }

        private static bool IsUsefulTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;
            title = title.Trim();
            if (title.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("msctfime ui", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("spotify", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("spotify premium", StringComparison.OrdinalIgnoreCase) ||
                title.Equals("spotify free", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("GDI+", StringComparison.OrdinalIgnoreCase))
                return false;
            return title.IndexOf("Window (Spotify.exe)", StringComparison.OrdinalIgnoreCase) < 0 &&
                   title.IndexOf("Spotify.exe", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static IntPtr Await(IntPtr asyncOp)
        {
            if (asyncOp == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr asyncInfo = IntPtr.Zero;
            try
            {
                Guid asyncInfoIid = AsyncInfoIid;
                ThrowOnFailure(Marshal.QueryInterface(asyncOp, ref asyncInfoIid, out asyncInfo));
                for (;;)
                {
                    ThrowOnFailure(Vtable.CallOutUInt(asyncInfo, 7, out uint status));
                    if (status != 0)
                    {
                        if (status != 1)
                        {
                            Vtable.CallOutUInt(asyncInfo, 8, out uint errorCode);
                            throw new InvalidOperationException("WinRT async failed: 0x" + errorCode.ToString("X8"));
                        }
                        break;
                    }
                    Thread.Sleep(5);
                }

                ThrowOnFailure(Vtable.Call1(asyncOp, 8, out IntPtr result));
                return result;
            }
            finally
            {
                if (asyncInfo != IntPtr.Zero)
                    Marshal.Release(asyncInfo);
                CloseAndRelease(ref asyncOp);
            }
        }

        private static void AwaitUInt(IntPtr asyncOp)
        {
            if (asyncOp == IntPtr.Zero)
                return;

            IntPtr asyncInfo = IntPtr.Zero;
            try
            {
                Guid asyncInfoIid = AsyncInfoIid;
                ThrowOnFailure(Marshal.QueryInterface(asyncOp, ref asyncInfoIid, out asyncInfo));
                for (;;)
                {
                    ThrowOnFailure(Vtable.CallOutUInt(asyncInfo, 7, out uint status));
                    if (status != 0)
                    {
                        if (status != 1)
                        {
                            Vtable.CallOutUInt(asyncInfo, 8, out uint errorCode);
                            throw new InvalidOperationException("WinRT async failed: 0x" + errorCode.ToString("X8"));
                        }
                        break;
                    }
                    Thread.Sleep(5);
                }
                Vtable.CallOutUInt(asyncOp, 8, out _);
            }
            finally
            {
                if (asyncInfo != IntPtr.Zero)
                    Marshal.Release(asyncInfo);
                CloseAndRelease(ref asyncOp);
            }
        }

        private static string GetHString(IntPtr obj, int slot)
        {
            ThrowOnFailure(Vtable.Call1(obj, slot, out IntPtr hstring));
            if (hstring == IntPtr.Zero)
                return "";
            try
            {
                IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
                return buffer == IntPtr.Zero ? "" : Marshal.PtrToStringUni(buffer, (int)length) ?? "";
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        private static double GetTimeSpanSeconds(IntPtr timeline, int slot)
        {
            ThrowOnFailure(Vtable.CallOutLong(timeline, slot, out long ticks));
            return ticks / 10000000.0;
        }

        private static void CloseAndRelease(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero)
                return;

            IntPtr closablePtr = IntPtr.Zero;
            Guid closableIid = ClosableIid;
            if (Marshal.QueryInterface(obj, ref closableIid, out closablePtr) >= 0 && closablePtr != IntPtr.Zero)
            {
                try { Vtable.Call0(closablePtr, 6); } catch { }
                Marshal.Release(closablePtr);
            }
            Marshal.Release(obj);
            obj = IntPtr.Zero;
        }

        private static void Release(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero)
                return;
            try { Marshal.Release(obj); } catch { }
            obj = IntPtr.Zero;
        }

        private static void ThrowOnFailure(int hr)
        {
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static class Vtable
        {
            private static IntPtr Get(IntPtr obj, int slot) =>
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int Fn0(IntPtr thisPtr);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnOutPtr(IntPtr thisPtr, out IntPtr value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnOutInt(IntPtr thisPtr, out int value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnOutUInt(IntPtr thisPtr, out uint value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnOutLong(IntPtr thisPtr, out long value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnOutULong(IntPtr thisPtr, out ulong value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnGetAt(IntPtr thisPtr, uint index, out IntPtr value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnGetInputStreamAt(IntPtr thisPtr, ulong position, out IntPtr value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnCreateDataReader(IntPtr thisPtr, IntPtr inputStream, out IntPtr value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnLoadAsync(IntPtr thisPtr, uint count, out IntPtr operation);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int FnReadBytes(IntPtr thisPtr, uint count, IntPtr value);

            internal static int Call0(IntPtr obj, int slot) =>
                Marshal.GetDelegateForFunctionPointer<Fn0>(Get(obj, slot))(obj);

            internal static int Call1(IntPtr obj, int slot, out IntPtr value) =>
                Marshal.GetDelegateForFunctionPointer<FnOutPtr>(Get(obj, slot))(obj, out value);

            internal static int CallOutInt(IntPtr obj, int slot, out int value) =>
                Marshal.GetDelegateForFunctionPointer<FnOutInt>(Get(obj, slot))(obj, out value);

            internal static int CallOutUInt(IntPtr obj, int slot, out uint value) =>
                Marshal.GetDelegateForFunctionPointer<FnOutUInt>(Get(obj, slot))(obj, out value);

            internal static int CallOutLong(IntPtr obj, int slot, out long value) =>
                Marshal.GetDelegateForFunctionPointer<FnOutLong>(Get(obj, slot))(obj, out value);

            internal static int CallOutULong(IntPtr obj, int slot, out ulong value) =>
                Marshal.GetDelegateForFunctionPointer<FnOutULong>(Get(obj, slot))(obj, out value);

            internal static int CallGetAt(IntPtr obj, int slot, int index, out IntPtr value) =>
                Marshal.GetDelegateForFunctionPointer<FnGetAt>(Get(obj, slot))(obj, (uint)index, out value);

            internal static int CallGetInputStreamAt(IntPtr obj, int slot, ulong position, out IntPtr value) =>
                Marshal.GetDelegateForFunctionPointer<FnGetInputStreamAt>(Get(obj, slot))(obj, position, out value);

            internal static int CallCreateDataReader(IntPtr obj, int slot, IntPtr inputStream, out IntPtr value) =>
                Marshal.GetDelegateForFunctionPointer<FnCreateDataReader>(Get(obj, slot))(obj, inputStream, out value);

            internal static int CallLoadAsync(IntPtr obj, int slot, uint count, out IntPtr operation) =>
                Marshal.GetDelegateForFunctionPointer<FnLoadAsync>(Get(obj, slot))(obj, count, out operation);

            internal static int CallReadBytes(IntPtr obj, int slot, uint count, IntPtr value) =>
                Marshal.GetDelegateForFunctionPointer<FnReadBytes>(Get(obj, slot))(obj, count, value);
        }

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoInitialize(uint initType);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
    }
}