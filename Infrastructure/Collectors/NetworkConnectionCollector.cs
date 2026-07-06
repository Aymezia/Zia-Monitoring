using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class NetworkConnectionCollector
{
    private const int AfInet = 2;

    private enum TcpTableClass
    {
        TcpTableOwnerPidAll = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        nint pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        TcpTableClass tblClass,
        uint reserved);

    public IReadOnlyList<TcpConnectionInfo> GetActiveTcpConnections()
    {
        nint buffer = nint.Zero;

        try
        {
            var bufferLength = 0;
            _ = GetExtendedTcpTable(buffer, ref bufferLength, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);
            if (bufferLength <= 0)
                return Array.Empty<TcpConnectionInfo>();

            buffer = Marshal.AllocHGlobal(bufferLength);
            var result = GetExtendedTcpTable(buffer, ref bufferLength, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);
            if (result != 0)
                return Array.Empty<TcpConnectionInfo>();

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = nint.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var rows = new List<TcpConnectionInfo>(Math.Min(rowCount, 300));

            for (var index = 0; index < rowCount && rows.Count < 300; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(nint.Add(rowPtr, index * rowSize));
                var pid = unchecked((int)row.OwningPid);
                var processName = GetProcessName(pid);
                var local = FormatEndpoint(row.LocalAddr, row.LocalPort);
                var remote = FormatEndpoint(row.RemoteAddr, row.RemotePort);
                var state = ((TcpState)row.State).ToString();

                rows.Add(new TcpConnectionInfo(pid, processName, local, remote, state));
            }

            return rows
                .OrderByDescending(c => c.State == "Established")
                .ThenBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Enumeration des connexions TCP impossible", ex);
            return Array.Empty<TcpConnectionInfo>();
        }
        finally
        {
            if (buffer != nint.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    public IReadOnlyList<ProcessNetworkUsage> GetTopNetworkProcesses(IReadOnlyList<TcpConnectionInfo> connections)
    {
        return connections
            .Where(c => c.Pid > 0)
            .GroupBy(c => new { c.Pid, c.ProcessName })
            .Select(group => new ProcessNetworkUsage(
                group.Key.Pid,
                group.Key.ProcessName,
                group.Count(c => c.State.Equals("Established", StringComparison.OrdinalIgnoreCase)),
                EstimateTrafficScore(group)))
            .OrderByDescending(row => row.ActiveConnections)
            .ThenBy(row => row.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    }

    private static double EstimateTrafficScore(IEnumerable<TcpConnectionInfo> connections)
    {
        var established = connections.Count(c => c.State.Equals("Established", StringComparison.OrdinalIgnoreCase));
        return established <= 0 ? 0 : established * 16;
    }

    private static string GetProcessName(int pid)
    {
        if (pid <= 0)
            return "System";

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return $"PID {pid}";
        }
    }

    private static string FormatEndpoint(uint address, uint port)
    {
        var ip = new IPAddress(address).ToString();
        return $"{ip}:{ConvertPort(port)}";
    }

    private static int ConvertPort(uint port)
    {
        var networkPort = unchecked((ushort)port);
        return unchecked((ushort)IPAddress.NetworkToHostOrder((short)networkPort));
    }

    private enum TcpState
    {
        Closed = 1,
        Listen = 2,
        SynSent = 3,
        SynReceived = 4,
        Established = 5,
        FinWait1 = 6,
        FinWait2 = 7,
        CloseWait = 8,
        Closing = 9,
        LastAck = 10,
        TimeWait = 11,
        DeleteTcb = 12
    }
}
