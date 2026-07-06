using System.Text.Json;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class BoostHistoryService
{
    private readonly string _historyFile;

    public BoostHistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring", "history");
        Directory.CreateDirectory(dir);
        _historyFile = Path.Combine(dir, "boost-history.json");
    }

    public void Record(BoostHistoryEntry entry)
    {
        var list = Load().ToList();
        list.Add(entry);
        if (list.Count > 100)
            list.RemoveAt(0);
        Save(list);
    }

    public IReadOnlyList<BoostHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_historyFile))
                return new List<BoostHistoryEntry>();

            var json = File.ReadAllText(_historyFile);
            return JsonSerializer.Deserialize<List<BoostHistoryEntry>>(json) ?? new List<BoostHistoryEntry>();
        }
        catch
        {
            return new List<BoostHistoryEntry>();
        }
    }

    private void Save(List<BoostHistoryEntry> list)
    {
        try
        {
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyFile, json);
        }
        catch { }
    }
}
