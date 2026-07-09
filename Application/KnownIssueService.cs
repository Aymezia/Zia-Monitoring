namespace ZiaMonitoring_App.Application;

public sealed record KnownIssueInfo(string Title, string Explanation, string Url);

/// <summary>
/// Corrélation entre un diagnostic détecté (code d'arrêt BSOD, module GPU
/// fautif) et une source officielle vérifiée — jamais un lien "communautaire"
/// deviné : la référence Microsoft des codes d'arrêt (stable depuis des
/// années) pour les BSOD, et les pages support des fabricants déjà utilisées
/// ailleurs dans l'app (SecurityScanService) pour les pilotes GPU.
/// </summary>
public static class KnownIssueService
{
    private const string MicrosoftBugCheckReferenceUrl = "https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/bug-check-code-reference2";

    private static readonly (string Code, string Explanation)[] BugcheckExplanations =
    [
        ("KMODE_EXCEPTION_NOT_HANDLED", "Exception non gérée par un pilote en mode noyau — souvent un pilote tiers instable (antivirus, périphérique)."),
        ("IRQL_NOT_LESS_OR_EQUAL", "Accès mémoire invalide à un niveau d'interruption incorrect — très souvent un pilote réseau, GPU ou de stockage."),
        ("SYSTEM_SERVICE_EXCEPTION", "Exception pendant une routine système — fréquemment lié à un pilote GPU obsolète."),
        ("DPC_WATCHDOG_VIOLATION", "Une procédure système a mis trop de temps à répondre — souvent un pilote de stockage ou un SSD en fin de vie."),
        ("CRITICAL_PROCESS_DIED", "Un processus système critique s'est arrêté de façon inattendue — vérifiez l'intégrité système (DISM/SFC)."),
        ("WHEA_UNCORRECTABLE_ERROR", "Erreur matérielle non corrigible détectée par le CPU — overclock CPU/RAM instable ou défaillance matérielle."),
        ("PAGE_FAULT_IN_NONPAGED_AREA", "Accès à de la mémoire qui aurait dû rester résidente — pilote défectueux ou RAM défaillante."),
        ("MEMORY_MANAGEMENT", "Erreur de gestion mémoire — RAM défaillante ou profil XMP/EXPO instable à vérifier en priorité."),
        ("CLOCK_WATCHDOG_TIMEOUT", "Un cœur processeur ne répond plus dans le temps imparti — souvent un signe d'overclock CPU instable."),
        ("KERNEL_SECURITY_CHECK_FAILURE", "Une structure de données critique du noyau a été corrompue — pilote défectueux ou RAM instable."),
    ];

    public static KnownIssueInfo? MatchBugcheck(string? bugcheckText)
    {
        if (string.IsNullOrWhiteSpace(bugcheckText))
            return null;

        foreach (var (code, explanation) in BugcheckExplanations)
        {
            if (bugcheckText.Contains(code, StringComparison.OrdinalIgnoreCase))
                return new KnownIssueInfo(code, explanation, MicrosoftBugCheckReferenceUrl);
        }

        return null;
    }

    // Noms réels de modules fautifs (fichiers .sys/.dll) — différents des
    // chaînes "Name" de Win32_PnPSignedDriver (ex: "NVIDIA GeForce RTX 4060")
    // que cible SecurityScanService.DetectDriverVendor : nvlddmkm.sys ne
    // contient pas le mot "nvidia", donc ce service a sa propre détection.
    private static readonly (string Prefix, string Vendor, string UpdateUrl)[] FaultingModulePrefixes =
    [
        ("nvlddmkm", "NVIDIA", "https://www.nvidia.com/Download/index.aspx"),
        ("nvwgf2um", "NVIDIA", "https://www.nvidia.com/Download/index.aspx"),
        ("nvoglv", "NVIDIA", "https://www.nvidia.com/Download/index.aspx"),
        ("nv4_disp", "NVIDIA", "https://www.nvidia.com/Download/index.aspx"),
        ("atikmpag", "AMD", "https://www.amd.com/en/support"),
        ("atikmdag", "AMD", "https://www.amd.com/en/support"),
        ("amdkmdag", "AMD", "https://www.amd.com/en/support"),
        ("amdxx64", "AMD", "https://www.amd.com/en/support"),
        ("igdkmd", "Intel", "https://www.intel.com/content/www/us/en/support/detect.html"),
        ("igdumdim", "Intel", "https://www.intel.com/content/www/us/en/support/detect.html"),
    ];

    public static KnownIssueInfo? MatchFaultingModule(string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || moduleName.Equals("inconnu", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var (prefix, vendor, updateUrl) in FaultingModulePrefixes)
        {
            if (moduleName.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                return new KnownIssueInfo(vendor, $"Le module fautif '{moduleName}' appartient probablement au pilote {vendor}.", updateUrl);
        }

        return null;
    }
}
