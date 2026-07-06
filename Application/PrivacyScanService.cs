using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record PrivacyCheck(
    string Key,
    string Title,
    string Description,
    bool IsPrivacyFriendly)
{
    public string StatusLabel => IsPrivacyFriendly ? "OK" : "Exposé";
}

/// <summary>
/// Scan de confidentialité Windows : identifiant publicitaire, télémétrie,
/// historique d'activité, suggestions, expériences personnalisées, fréquence
/// de feedback. Chaque point exposé peut être corrigé d'un clic (écritures
/// registre documentées, les mêmes que les toggles des Paramètres Windows).
/// </summary>
public sealed class PrivacyScanService
{
    public IReadOnlyList<PrivacyCheck> Scan()
    {
        return
        [
            new PrivacyCheck("advertising-id", "Identifiant publicitaire",
                "Permet aux applications de vous profiler pour la publicité ciblée.",
                ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1) == 0),

            new PrivacyCheck("tailored-experiences", "Expériences personnalisées",
                "Utilise vos données de diagnostic pour des conseils et publicités adaptés.",
                ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 1) == 0),

            new PrivacyCheck("start-suggestions", "Suggestions du menu Démarrer",
                "Affiche des applications suggérées (publicité) dans le menu Démarrer.",
                ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 1) == 0),

            new PrivacyCheck("feedback-frequency", "Demandes de feedback Windows",
                "Windows sollicite régulièrement votre avis et envoie des données associées.",
                ReadHkcuDword(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 1) == 0),

            new PrivacyCheck("activity-history", "Historique d'activité",
                "Journalise les applications et documents ouverts (timeline).",
                ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "PublishUserActivities",
                    ReadHklmDword(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 1)) == 0),

            new PrivacyCheck("input-personalization", "Personnalisation de la saisie",
                "Envoie vos données d'écriture manuscrite et de frappe à Microsoft.",
                ReadHkcuDword(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 0) == 1)
        ];
    }

    /// <summary>Applique la valeur respectueuse de la vie privée pour ce point.</summary>
    public bool Fix(string checkKey)
    {
        try
        {
            switch (checkKey)
            {
                case "advertising-id":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
                    return true;
                case "tailored-experiences":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0);
                    return true;
                case "start-suggestions":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0);
                    return true;
                case "feedback-frequency":
                    WriteHkcuDword(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0);
                    return true;
                case "activity-history":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "PublishUserActivities", 0);
                    return true;
                case "input-personalization":
                    WriteHkcuDword(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1);
                    WriteHkcuDword(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Correction confidentialité '{checkKey}' impossible", ex);
            return false;
        }
    }

    /// <summary>Corrige tous les points exposés. Retourne le nombre corrigé.</summary>
    public int FixAll() => Scan().Where(c => !c.IsPrivacyFriendly).Count(c => Fix(c.Key));

    private static int ReadHkcuDword(string subKey, string name, int defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) is int value ? value : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static int ReadHklmDword(string subKey, string name, int defaultValue)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) is int value ? value : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void WriteHkcuDword(string subKey, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }
}
