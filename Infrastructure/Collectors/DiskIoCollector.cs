using System.Management;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class DiskIoCollector
{

    public (double ReadMbps, double WriteMbps) GetDiskIoMbps()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");

            long read = 0, write = 0;
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["DiskReadBytesPersec"] is ulong r) read += (long)r;
                if (obj["DiskWriteBytesPersec"] is ulong w) write += (long)w;
            }

            var readMbps = read / 1024.0 / 1024.0;
            var writeMbps = write / 1024.0 / 1024.0;

            return (Math.Round(readMbps, 2), Math.Round(writeMbps, 2));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture des compteurs disque impossible", ex);
            return (0, 0);
        }
    }
}
