using System.Runtime.InteropServices;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class MemoryCollector
{
    public (double usedMb, double totalMb) GetMemoryUsageMb()
    {
        var status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0);
        }

        var totalMb = status.ullTotalPhys / 1024d / 1024d;
        var availMb = status.ullAvailPhys / 1024d / 1024d;
        return (totalMb - availMb, totalMb);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
