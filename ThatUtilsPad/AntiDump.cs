using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace ThatUtilsPad;

/// <summary>
/// In-memory anti-dump for the loaded ThatUtilsPad module.
/// Wipes PE headers / section names after load and re-applies if tools restore them.
/// Does not wipe managed metadata sections (those would crash Harmony/Reflection).
/// </summary>
internal static class AntiDump
{
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageReadWrite = 0x04;
    private const int GuardianIntervalMs = 2500;

    private static IntPtr moduleBase = IntPtr.Zero;
    private static int headerBytes;
    private static byte[] wipePattern = Array.Empty<byte>();
    private static int[] sectionNameOffsets = Array.Empty<int>();
    private static volatile bool armed;
    private static int applyCount;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    internal static void Apply()
    {
#if DEBUG
        // Keep dumpable in Debug so local tooling still works.
        return;
#else
        if (armed)
            return;

        try
        {
            if (!TryResolveModuleBase(out moduleBase))
                return;

            if (!TryReadHeaderLayout(moduleBase, out headerBytes, out sectionNameOffsets))
            {
                // Fallback: wipe first page only.
                headerBytes = 0x1000;
                sectionNameOffsets = Array.Empty<int>();
            }

            headerBytes = Math.Max(0x200, Math.Min(headerBytes, 0x2000));
            wipePattern = BuildNoise(headerBytes);

            if (!EraseHeaders())
                return;

            armed = true;
            Interlocked.Increment(ref applyCount);

            Thread guardian = new Thread(GuardianLoop)
            {
                IsBackground = true,
                Name = "TUP_ModuleGuard",
                Priority = ThreadPriority.BelowNormal
            };
            guardian.Start();
        }
        catch
        {
            // Never take down the plugin for protection failures.
        }
#endif
    }

    private static int dumpScanTick;

    private static void GuardianLoop()
    {
        while (armed)
        {
            try
            {
                Thread.Sleep(GuardianIntervalMs);

                if (moduleBase == IntPtr.Zero)
                    continue;

                bool mzPresent = SafeReadByte(moduleBase) == (byte)'M'
                                 && SafeReadByte(moduleBase + 1) == (byte)'Z';

                dumpScanTick++;
                bool dumpTool = (dumpScanTick % 6) == 0 && DetectDumpToolProcess();
                if (mzPresent || dumpTool || applyCount < 3)
                {
                    EraseHeaders();
                    ScrambleSectionNames();
                    Interlocked.Increment(ref applyCount);
                }
            }
            catch
            {
                Thread.Sleep(GuardianIntervalMs);
            }
        }
    }

    private static bool EraseHeaders()
    {
        if (moduleBase == IntPtr.Zero || wipePattern == null || wipePattern.Length == 0)
            return false;

        if (!VirtualProtect(moduleBase, (UIntPtr)wipePattern.Length, PageExecuteReadWrite, out uint oldProtect)
            && !VirtualProtect(moduleBase, (UIntPtr)wipePattern.Length, PageReadWrite, out oldProtect))
            return false;

        try
        {
            Marshal.Copy(wipePattern, 0, moduleBase, wipePattern.Length);
            ScrambleSectionNamesUnlocked();
            // Poison classic dump scanners looking for residual PE markers.
            if (wipePattern.Length > 2)
            {
                Marshal.WriteByte(moduleBase, 0, 0x00);
                Marshal.WriteByte(moduleBase + 1, 0, 0x00);
            }
            return true;
        }
        finally
        {
            VirtualProtect(moduleBase, (UIntPtr)wipePattern.Length, oldProtect, out _);
        }
    }

    private static void ScrambleSectionNames()
    {
        if (moduleBase == IntPtr.Zero || sectionNameOffsets.Length == 0)
            return;

        if (!VirtualProtect(moduleBase, (UIntPtr)Math.Max(headerBytes, 0x1000), PageExecuteReadWrite, out uint oldProtect)
            && !VirtualProtect(moduleBase, (UIntPtr)Math.Max(headerBytes, 0x1000), PageReadWrite, out oldProtect))
            return;

        try
        {
            ScrambleSectionNamesUnlocked();
        }
        finally
        {
            VirtualProtect(moduleBase, (UIntPtr)Math.Max(headerBytes, 0x1000), oldProtect, out _);
        }
    }

    private static void ScrambleSectionNamesUnlocked()
    {
        byte[] noise = BuildNoise(8);
        for (int i = 0; i < sectionNameOffsets.Length; i++)
        {
            int offset = sectionNameOffsets[i];
            if (offset <= 0 || offset + 8 > headerBytes)
                continue;

            for (int b = 0; b < 8; b++)
                Marshal.WriteByte(moduleBase + offset + b, noise[(b + i) % noise.Length]);
        }
    }

    private static bool TryResolveModuleBase(out IntPtr baseAddress)
    {
        baseAddress = IntPtr.Zero;

        Assembly asm = Assembly.GetExecutingAssembly();
        string location = asm.Location ?? string.Empty;

        try
        {
            using Process process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    if (module == null)
                        continue;

                    string fileName = module.FileName ?? string.Empty;
                    bool pathMatch = !string.IsNullOrEmpty(location)
                                     && fileName.Equals(location, StringComparison.OrdinalIgnoreCase);
                    bool nameMatch = fileName.EndsWith("ThatUtilsPad.dll", StringComparison.OrdinalIgnoreCase)
                                     || (module.ModuleName != null
                                         && module.ModuleName.Equals("ThatUtilsPad.dll", StringComparison.OrdinalIgnoreCase));

                    if (pathMatch || nameMatch)
                    {
                        baseAddress = module.BaseAddress;
                        if (baseAddress != IntPtr.Zero)
                            return true;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        try
        {
            IntPtr h = Marshal.GetHINSTANCE(asm.ManifestModule);
            if (h != IntPtr.Zero && h != new IntPtr(-1))
            {
                baseAddress = h;
                return true;
            }
        }
        catch
        {
        }

        try
        {
            string name = System.IO.Path.GetFileName(location);
            if (!string.IsNullOrEmpty(name))
            {
                IntPtr handle = GetModuleHandle(name);
                if (handle != IntPtr.Zero)
                {
                    baseAddress = handle;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryReadHeaderLayout(IntPtr baseAddr, out int sizeOfHeaders, out int[] sectionNameOffs)
    {
        sizeOfHeaders = 0x1000;
        sectionNameOffs = Array.Empty<int>();

        try
        {
            // DOS e_lfanew @ 0x3C
            int eLfanew = Marshal.ReadInt32(baseAddr, 0x3C);
            if (eLfanew <= 0 || eLfanew > 0x1000)
                return false;

            // PE signature "PE\0\0"
            int peSig = Marshal.ReadInt32(baseAddr, eLfanew);
            if (peSig != 0x00004550)
                return false;

            // COFF FileHeader starts at e_lfanew + 4
            int coff = eLfanew + 4;
            ushort numberOfSections = (ushort)Marshal.ReadInt16(baseAddr, coff + 2);
            ushort sizeOfOptionalHeader = (ushort)Marshal.ReadInt16(baseAddr, coff + 16);

            int optional = coff + 20;
            ushort magic = (ushort)Marshal.ReadInt16(baseAddr, optional);
            int sizeOfHeadersOff = optional + 60;
            sizeOfHeaders = Marshal.ReadInt32(baseAddr, sizeOfHeadersOff);
            if (sizeOfHeaders < 0x200 || sizeOfHeaders > 0x4000)
                sizeOfHeaders = 0x1000;

            int sectionTable = optional + sizeOfOptionalHeader;
            if (numberOfSections > 96)
                numberOfSections = 96;

            int[] names = new int[numberOfSections];
            for (int i = 0; i < numberOfSections; i++)
                names[i] = sectionTable + (i * 40);

            sectionNameOffs = names;
            return magic == 0x10B || magic == 0x20B;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildNoise(int length)
    {
        byte[] data = new byte[Math.Max(1, length)];
        int seed = Environment.TickCount ^ Process.GetCurrentProcess().Id ^ Guid.NewGuid().GetHashCode();
        for (int i = 0; i < data.Length; i++)
        {
            seed = unchecked((seed * 1103515245 + 12345) & int.MaxValue);
            data[i] = (byte)(seed >> 16);
        }
        return data;
    }

    private static byte SafeReadByte(IntPtr address)
    {
        try
        {
            return Marshal.ReadByte(address);
        }
        catch
        {
            return 0;
        }
    }

    private static bool DetectDumpToolProcess()
    {
        try
        {
            Process[] procs = Process.GetProcesses();
            for (int i = 0; i < procs.Length; i++)
            {
                string name = string.Empty;
                try
                {
                    name = procs[i].ProcessName ?? string.Empty;
                }
                catch
                {
                    continue;
                }

                if (name.Length == 0)
                    continue;

                string n = name.ToLowerInvariant();
                if (n.IndexOf("extremedumper", StringComparison.Ordinal) >= 0
                    || n.IndexOf("megadumper", StringComparison.Ordinal) >= 0
                    || n.IndexOf("dnspy", StringComparison.Ordinal) >= 0
                    || n.IndexOf("ilspy", StringComparison.Ordinal) >= 0
                    || n.IndexOf("dotpeek", StringComparison.Ordinal) >= 0
                    || n.IndexOf("die.dll", StringComparison.Ordinal) >= 0
                    || n.IndexOf("scylla", StringComparison.Ordinal) >= 0
                    || n.IndexOf("x64dbg", StringComparison.Ordinal) >= 0
                    || n.IndexOf("x32dbg", StringComparison.Ordinal) >= 0
                    || n.IndexOf("ollydbg", StringComparison.Ordinal) >= 0
                    || n.IndexOf("httpanalyzer", StringComparison.Ordinal) >= 0
                    || n.IndexOf("processhacker", StringComparison.Ordinal) >= 0
                    || n.IndexOf("systeminformer", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
