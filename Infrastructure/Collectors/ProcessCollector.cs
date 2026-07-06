using System.Diagnostics;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class ProcessCollector
{
    public IReadOnlyList<ProcessInfo> GetTopProcesses(int top)
    {
        var result = new List<ProcessInfo>(top);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var memoryMb = process.WorkingSet64 / 1024d / 1024d;
                result.Add(new ProcessInfo(process.Id, process.ProcessName, 0, memoryMb));
            }
            catch
            {
                // Access denied on protected processes is expected.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.OrderByDescending(x => x.MemoryMb).Take(top).ToList();
    }
}
