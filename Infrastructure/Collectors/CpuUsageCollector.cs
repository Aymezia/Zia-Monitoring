using System.Runtime.InteropServices;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class CpuUsageCollector
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevious;

    public double GetCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleNow = ToUInt64(idle);
        var kernelNow = ToUInt64(kernel);
        var userNow = ToUInt64(user);

        if (!_hasPrevious)
        {
            _prevIdle = idleNow;
            _prevKernel = kernelNow;
            _prevUser = userNow;
            _hasPrevious = true;
            return 0;
        }

        var idleDelta = idleNow - _prevIdle;
        var kernelDelta = kernelNow - _prevKernel;
        var userDelta = userNow - _prevUser;

        _prevIdle = idleNow;
        _prevKernel = kernelNow;
        _prevUser = userNow;

        var totalDelta = kernelDelta + userDelta;
        if (totalDelta == 0)
        {
            return 0;
        }

        var busy = totalDelta - idleDelta;
        return Math.Clamp((double)busy / totalDelta * 100, 0, 100);
    }

    private static ulong ToUInt64(FILETIME time)
    {
        return ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }
}
