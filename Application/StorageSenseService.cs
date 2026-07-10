using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record StorageSenseConfig(bool Enabled, int Cadence, bool CleanTemp, int RecycleBinDays, int DownloadsDays)
{
    public string CadenceLabel => Cadence switch
    {
        0 => "Quand l'espace disque est faible",
        1 => "Chaque jour",
        7 => "Chaque semaine",
        30 => "Chaque mois",
        _ => $"Personnalisé ({Cadence})"
    };

    public string RecycleBinLabel => RetentionLabel(RecycleBinDays);
    public string DownloadsLabel => RetentionLabel(DownloadsDays);

    private static string RetentionLabel(int days) => days switch
    {
        0 => "Jamais",
        1 => "1 jour",
        _ => $"{days} jours"
    };
}

/// <summary>
/// Pilote l'Assistant de stockage (Storage Sense) natif de Windows depuis
/// l'app : activation, fréquence, et rétention de la corbeille / du dossier
/// Téléchargements. Tout est stocké en HKCU (aucun droit administrateur
/// requis) sous la même StoragePolicy que la page Paramètres de Windows.
/// </summary>
public sealed class StorageSenseService
{
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    // Noms de valeurs (DWORD) tels que Windows les stocke.
    private const string EnableValue = "01";
    private const string CadenceValue = "2048";
    private const string CleanTempValue = "04";
    private const string RecycleBinDaysValue = "256";
    private const string DownloadsDaysValue = "512";

    public StorageSenseConfig GetConfig()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PolicyKey, writable: false);
        return new StorageSenseConfig(
            Enabled: ReadInt(key, EnableValue, 0) == 1,
            Cadence: ReadInt(key, CadenceValue, 0),
            CleanTemp: ReadInt(key, CleanTempValue, 0) == 1,
            RecycleBinDays: ReadInt(key, RecycleBinDaysValue, 0),
            DownloadsDays: ReadInt(key, DownloadsDaysValue, 0));
    }

    public (bool Success, string Message) Apply(StorageSenseConfig config)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PolicyKey, writable: true);
            key.SetValue(EnableValue, config.Enabled ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(CadenceValue, config.Cadence, RegistryValueKind.DWord);
            key.SetValue(CleanTempValue, config.CleanTemp ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(RecycleBinDaysValue, config.RecycleBinDays, RegistryValueKind.DWord);
            key.SetValue(DownloadsDaysValue, config.DownloadsDays, RegistryValueKind.DWord);
            return (true, config.Enabled
                ? $"Assistant de stockage activé ({config.CadenceLabel})."
                : "Assistant de stockage désactivé.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Configuration de l'Assistant de stockage impossible", ex);
            return (false, ex.Message);
        }
    }

    private static int ReadInt(RegistryKey? key, string name, int defaultValue) =>
        key?.GetValue(name) is int v ? v : defaultValue;
}
