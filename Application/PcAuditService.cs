using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public enum AuditSeverity { Critical, Warning, Info }

public enum AuditCategory { Securite, Confidentialite, Stabilite, Materiel, Performance, Reseau }

public sealed record PcAuditFinding(AuditSeverity Severity, AuditCategory Category, string Title, string Detail, string Recommendation)
{
    public string SeverityLabel => Severity switch
    {
        AuditSeverity.Critical => "Critique",
        AuditSeverity.Warning => "À surveiller",
        _ => "Info"
    };

    public string CategoryLabel => Category switch
    {
        AuditCategory.Securite => "Sécurité",
        AuditCategory.Confidentialite => "Confidentialité",
        AuditCategory.Stabilite => "Stabilité",
        AuditCategory.Materiel => "Matériel",
        AuditCategory.Performance => "Performance",
        AuditCategory.Reseau => "Réseau",
        _ => "Autre"
    };
}

public sealed record PcAuditReport(DateTime GeneratedAt, int Score, IReadOnlyList<PcAuditFinding> Findings)
{
    public int CriticalCount => Findings.Count(f => f.Severity == AuditSeverity.Critical);
    public int WarningCount => Findings.Count(f => f.Severity == AuditSeverity.Warning);
    public int InfoCount => Findings.Count(f => f.Severity == AuditSeverity.Info);

    public string ScoreLabel => Score switch
    {
        >= 90 => $"{Score}/100 — Excellent",
        >= 75 => $"{Score}/100 — Bon",
        >= 50 => $"{Score}/100 — Moyen",
        _ => $"{Score}/100 — À améliorer"
    };

    public string SummaryLabel => Findings.Count == 0
        ? "Aucun point à signaler — configuration au taquet."
        : $"{CriticalCount} critique(s) · {WarningCount} à surveiller · {InfoCount} info.";
}

/// <summary>
/// Audit complet du PC : agrège en une seule passe tous les signaux déjà
/// collectés ailleurs dans l'app (sécurité, confidentialité, débloat,
/// démarrage, stabilité, matériel, mises à jour) en une liste unique de
/// constats priorisés avec un score global — plutôt que de faire naviguer
/// l'utilisateur entre dix pages pour se faire sa propre opinion.
/// Certains contrôles (sécurité, débloat, démarrage) prennent quelques
/// secondes (WMI/registre/sous-process) : à appeler hors du thread UI.
/// </summary>
public sealed class PcAuditService
{
    private readonly SecurityScanService _security;
    private readonly PrivacyScanService _privacy;
    private readonly DebloatService _debloat;
    private readonly StartupManagerService _startup;
    private readonly CrashDiagnosticsService _crash;
    private readonly WindowsUpdateHistoryService _windowsUpdates;
    private readonly MetricsHistoryService _metricsHistory;

    public PcAuditService(
        SecurityScanService security,
        PrivacyScanService privacy,
        DebloatService debloat,
        StartupManagerService startup,
        CrashDiagnosticsService crash,
        WindowsUpdateHistoryService windowsUpdates,
        MetricsHistoryService metricsHistory)
    {
        _security = security;
        _privacy = privacy;
        _debloat = debloat;
        _startup = startup;
        _crash = crash;
        _windowsUpdates = windowsUpdates;
        _metricsHistory = metricsHistory;
    }

    /// <param name="memoryLeakSuspects">
    /// Fourni par l'appelant (MonitoringService.MemoryLeakSuspects) plutôt
    /// qu'injecté ici : évite de coupler ce service, testable en isolation,
    /// à toute la chaîne de collecteurs matériels de MonitoringService.
    /// </param>
    public PcAuditReport RunFullAudit(SystemSnapshot snapshot, PcProfile profile, IReadOnlyList<LeakSuspect> memoryLeakSuspects)
    {
        var findings = new List<PcAuditFinding>();

        findings.AddRange(BuildRealtimeFindings(snapshot, profile));
        findings.AddRange(BuildSecurityFindings(_security.BuildReport()));
        findings.AddRange(BuildPrivacyFindings(_privacy.Scan()));
        findings.AddRange(BuildDebloatFindings(_debloat.Scan()));
        findings.AddRange(BuildStartupFindings(_startup.GetEntries()));
        findings.AddRange(BuildStabilityFindings(
            _crash.GetRecentAppCrashes(days: 7),
            _crash.GetWheaSummary(),
            _crash.GetLastBsod(),
            _crash.GetServiceCrashLoops(),
            memoryLeakSuspects,
            _metricsHistory.GetThermalDriftWarnings()));
        findings.AddRange(BuildHardwareFindings(
            MemoryConfigDiagnosticsService.Analyze(),
            PcieLinkService.Detect(),
            SsdWearService.Analyze(),
            BatteryService.GetBatteryHealth()));
        findings.AddRange(BuildUpdateFindings(_windowsUpdates.GetInstalledUpdates()));

        var ordered = findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Category)
            .ToList();

        return new PcAuditReport(DateTime.Now, ComputeScore(ordered), ordered);
    }

    internal static int ComputeScore(IReadOnlyList<PcAuditFinding> findings)
    {
        var score = 100;
        score -= findings.Count(f => f.Severity == AuditSeverity.Critical) * 14;
        score -= findings.Count(f => f.Severity == AuditSeverity.Warning) * 6;
        score -= findings.Count(f => f.Severity == AuditSeverity.Info) * 2;
        return Math.Clamp(score, 0, 100);
    }

    internal static IReadOnlyList<PcAuditFinding> BuildRealtimeFindings(SystemSnapshot snapshot, PcProfile profile)
    {
        var findings = new List<PcAuditFinding>();
        var ramPct = snapshot.MemoryTotalMb <= 0 ? 0 : snapshot.MemoryUsedMb / snapshot.MemoryTotalMb * 100;

        if (snapshot.CpuPercent > 85)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Performance, "Charge CPU élevée",
                $"Utilisation CPU à {snapshot.CpuPercent:F0}% au moment de l'audit.",
                "Vérifiez les processus les plus gourmands dans le Dashboard ; envisagez de fermer les apps inutiles au démarrage."));

        if (ramPct > 90)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Performance, "Mémoire vive saturée",
                $"RAM utilisée à {ramPct:F0}%.",
                "Fermez les applications lourdes en arrière-plan ou envisagez une extension de RAM."));

        if (snapshot.CpuTemperatureC is { } cpuTemp)
        {
            if (cpuTemp > 90)
                findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Materiel, "Surchauffe CPU",
                    $"Température CPU à {cpuTemp:F0}°C — seuil critique dépassé.",
                    "Nettoyez les ventilateurs, remplacez la pâte thermique, vérifiez l'airflow du boîtier."));
            else if (cpuTemp > 80)
                findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Températures CPU élevées",
                    $"Température CPU à {cpuTemp:F0}°C.",
                    "Vérifiez la ventilation et la qualité de la pâte thermique."));
        }

        if (snapshot.GpuTemperatureC is { } gpuTemp)
        {
            if (gpuTemp > 85)
                findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Materiel, "Surchauffe GPU",
                    $"Température GPU à {gpuTemp:F0}°C.",
                    "Nettoyez les ventilateurs GPU, vérifiez les pads thermiques et l'airflow."));
        }

        var systemDisk = profile.Disks.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
        if (systemDisk is not null && systemDisk.TotalGb > 0)
        {
            var freePct = systemDisk.FreeGb / systemDisk.TotalGb * 100;
            if (freePct < 10)
                findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Performance, "Disque système presque plein",
                    $"Seulement {systemDisk.FreeGb:F0} Go libres sur {systemDisk.Name} ({freePct:F0}%).",
                    "Nettoyez les fichiers temporaires (page Maintenance) ou libérez de l'espace rapidement."));
            else if (freePct < 15)
                findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Performance, "Disque système bientôt plein",
                    $"{systemDisk.FreeGb:F0} Go libres sur {systemDisk.Name} ({freePct:F0}%).",
                    "Planifiez un nettoyage ou une extension de stockage."));
        }

        if (profile.TotalRamGb is > 0 and < 16)
            findings.Add(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Materiel, "RAM sous le confort actuel",
                $"{profile.TotalRamGb:F0} Go installés.",
                "16 Go est un minimum confortable aujourd'hui, 32 Go pour le multitâche/streaming lourd."));

        return findings;
    }

    internal static IReadOnlyList<PcAuditFinding> BuildSecurityFindings(SecurityReport report)
    {
        var findings = new List<PcAuditFinding>();

        if (!report.FirewallEnabled)
            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Securite, "Pare-feu Windows désactivé",
                "Le pare-feu ne protège plus le PC contre les connexions entrantes non sollicitées.",
                "Réactivez le pare-feu Windows dans la page Sécurité."));

        if (!report.UacEnabled)
            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Securite, "Contrôle de compte utilisateur (UAC) désactivé",
                "N'importe quel programme peut effectuer des changements systèmes sans confirmation.",
                "Réactivez l'UAC (Panneau de configuration > Comptes d'utilisateurs)."));

        if (report.DiskSmartWarnings.Count > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Materiel, "Défaillance disque prévue (S.M.A.R.T)",
                string.Join(" · ", report.DiskSmartWarnings),
                "Sauvegardez vos données immédiatement et prévoyez le remplacement du disque."));

        if (report.MaliciousProcessMatches.Count > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Securite, "Signature malveillante détectée",
                $"{report.MaliciousProcessMatches.Count} processus correspondant à une signature connue.",
                "Lancez un scan antivirus complet immédiatement (page Sécurité pour le détail)."));

        if (report.KeyloggerHookWarnings.Count > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Securite, "Hook clavier suspect détecté",
                $"{report.KeyloggerHookWarnings.Count} indicateur(s) de capture clavier possible.",
                "Vérifiez les processus concernés (page Sécurité) — désinstallez tout logiciel non reconnu."));

        if (report.SuspiciousStartupEntries.Count > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Securite, "Entrées de démarrage suspectes",
                $"{report.SuspiciousStartupEntries.Count} entrée(s) correspondant à des motifs connus de logiciels malveillants.",
                "Vérifiez la liste dans la page Sécurité et supprimez celles que vous ne reconnaissez pas."));

        if (report.HasAntivirusConflict)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Securite, "Plusieurs antivirus actifs",
                $"{report.AntivirusProducts.Count(p => p.IsEnabled)} antivirus tiers actifs simultanément.",
                "Désactivez tous les antivirus sauf un : ils se gênent mutuellement et ralentissent le PC."));

        if (report.ObsoleteDrivers.Count >= 3)
            findings.Add(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Materiel, "Pilotes anciens (> 3 ans)",
                $"{report.ObsoleteDrivers.Count} pilote(s) non mis à jour depuis plus de 3 ans.",
                "Mettez à jour les pilotes GPU/chipset en priorité (liens dans la page Sécurité)."));

        return findings;
    }

    internal static IReadOnlyList<PcAuditFinding> BuildPrivacyFindings(IReadOnlyList<PrivacyCheck> checks)
    {
        var exposed = checks.Where(c => !c.IsPrivacyFriendly).ToList();
        if (exposed.Count == 0)
            return [];

        return
        [
            new PcAuditFinding(AuditSeverity.Info, AuditCategory.Confidentialite, "Réglages de confidentialité exposés",
                $"{exposed.Count} réglage(s) encore par défaut : {string.Join(", ", exposed.Select(c => c.Title))}.",
                "Corrigez-les en un clic depuis la page Sécurité (bouton \"Tout corriger\").")
        ];
    }

    internal static IReadOnlyList<PcAuditFinding> BuildDebloatFindings(IReadOnlyList<DebloatItem> items)
    {
        var findings = new List<PcAuditFinding>();

        var telemetryActive = items.Count(i => i.Category == DebloatCategory.Telemetry && !i.IsClean);
        if (telemetryActive > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Confidentialite, "Télémétrie Windows active",
                $"{telemetryActive} service(s) de télémétrie encore actif(s).",
                "Désactivez-les depuis la page Sécurité (section Débloat)."));

        var tasksActive = items.Count(i => i.Category == DebloatCategory.ScheduledTask && !i.IsClean);
        if (tasksActive > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Performance, "Tâches planifiées de diagnostic actives",
                $"{tasksActive} tâche(s) planifiée(s) de diagnostic/télémétrie encore active(s).",
                "Désactivez-les depuis la page Sécurité (section Débloat)."));

        return findings;
    }

    internal static IReadOnlyList<PcAuditFinding> BuildStartupFindings(IReadOnlyList<StartupEntry> entries)
    {
        var heavy = entries.Count(e => e.IsEnabled && e.Impact.Contains("élevé", StringComparison.OrdinalIgnoreCase));
        if (heavy == 0)
            return [];

        return
        [
            new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Performance, "Démarrage Windows chargé",
                $"{heavy} application(s) à fort impact se lancent automatiquement à l'ouverture de session.",
                "Désactivez celles qui ne sont pas indispensables au quotidien (page Réglages > Gestionnaire de démarrage).")
        ];
    }

    internal static IReadOnlyList<PcAuditFinding> BuildStabilityFindings(
        IReadOnlyList<AppCrashGroup> crashes,
        WheaSummary whea,
        BsodInfo? bsod,
        IReadOnlyList<ServiceCrashLoopWarning> serviceCrashLoops,
        IReadOnlyList<LeakSuspect> leakSuspects,
        IReadOnlyList<string> thermalDriftWarnings)
    {
        var findings = new List<PcAuditFinding>();

        if (crashes.Count > 0)
        {
            var worst = crashes[0];
            var moduleIssue = KnownIssueService.MatchFaultingModule(worst.MostCommonModule);
            var crashRecommendation = "Vérifiez les mises à jour du pilote/de l'application concernée (page Maintenance pour le détail).";
            if (moduleIssue is not null)
                crashRecommendation += $" Piste probable : {moduleIssue.Explanation} → {moduleIssue.Url}";

            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Stabilite, "Crashs d'applications récents",
                $"{worst.AppName} a crashé {worst.Count} fois cette semaine (module : {worst.MostCommonModule}).",
                crashRecommendation));
        }

        if (whea.CorrectedErrorCount > 0)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Erreurs matérielles corrigées (WHEA)",
                whea.Label,
                "Signal précoce d'un overclock RAM/CPU instable : baissez XMP/EXPO ou l'OC si récemment modifié."));

        if (bsod is not null && DateTime.Now - bsod.OccurredAt <= TimeSpan.FromDays(7))
        {
            var bugcheckIssue = KnownIssueService.MatchBugcheck(bsod.BugcheckText);
            var bsodRecommendation = bugcheckIssue is not null
                ? $"{bugcheckIssue.Explanation} Référence Microsoft : {bugcheckIssue.Url}"
                : "Lancez le diagnostic mémoire Windows (page Maintenance) et vérifiez les pilotes récemment installés.";

            findings.Add(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Stabilite, "Écran bleu récent",
                bsod.Label, bsodRecommendation));
        }

        foreach (var loop in serviceCrashLoops)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Stabilite, "Service Windows instable",
                loop.Label,
                "Consultez l'Observateur d'événements pour la cause ; envisagez une réparation système (DISM/SFC)."));

        foreach (var leak in leakSuspects.Take(3))
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Stabilite, "Fuite mémoire suspectée",
                leak.Label,
                "Redémarrez l'application concernée régulièrement en attendant une mise à jour du logiciel."));

        foreach (var drift in thermalDriftWarnings)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Dérive thermique long terme", drift,
                "Un nettoyage physique (poussière, pâte thermique) est probablement nécessaire."));

        return findings;
    }

    internal static IReadOnlyList<PcAuditFinding> BuildHardwareFindings(
        MemoryConfigDiagnosis memory,
        IReadOnlyList<PcieLinkInfo> pcieLinks,
        IReadOnlyList<SsdWearInfo> ssdWear,
        IReadOnlyList<BatteryHealthInfo> batteries)
    {
        var findings = new List<PcAuditFinding>();

        if (memory.XmpLikelyDisabled)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Profil XMP/EXPO désactivé",
                memory.SpeedLabel,
                "Activez le profil XMP/EXPO dans le BIOS pour profiter de la vitesse RAM annoncée."));

        if (memory.Channel.Status is ChannelStatus.SingleChannel or ChannelStatus.SingleChannelMisconfigured)
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Configuration mémoire mono-canal",
                memory.Channel.Label,
                "Passez en dual-canal (2 barrettes dans les bons slots) pour un gain de bande passante mémoire notable."));

        foreach (var link in pcieLinks.Where(l => l.IsBridged))
            findings.Add(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Materiel, "Lien PCIe du GPU bridé", link.Label,
                "Vérifiez que la carte graphique est bien enclenchée dans le slot PCIe principal (x16)."));

        foreach (var ssd in ssdWear.Where(s => s.WearPercent is >= 70))
            findings.Add(new PcAuditFinding(
                ssd.WearPercent >= 90 ? AuditSeverity.Critical : AuditSeverity.Warning,
                AuditCategory.Materiel, "Usure SSD avancée", ssd.Label,
                "Planifiez une sauvegarde et le remplacement de ce disque prochainement."));

        foreach (var battery in batteries.Where(b => b.WearPercent >= 40))
            findings.Add(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Materiel, "Batterie usée",
                $"{battery.Name} : {battery.WearPercent:F0}% d'usure ({battery.CycleCount?.ToString() ?? "?"} cycles).",
                "L'autonomie réelle a probablement bien baissé ; un remplacement de batterie est à envisager."));

        return findings;
    }

    internal static IReadOnlyList<PcAuditFinding> BuildUpdateFindings(IReadOnlyList<InstalledUpdateInfo> updates)
    {
        var lastInstall = updates.Where(u => u.InstalledOn.HasValue).Select(u => u.InstalledOn!.Value).OrderDescending().FirstOrDefault();
        if (lastInstall == default || DateTime.Now - lastInstall <= TimeSpan.FromDays(60))
            return [];

        return
        [
            new PcAuditFinding(AuditSeverity.Info, AuditCategory.Securite, "Windows Update pas vérifié récemment",
                $"Dernière mise à jour installée le {lastInstall:dd/MM/yyyy}, il y a {(DateTime.Now - lastInstall).Days} jours.",
                "Lancez une recherche de mises à jour Windows manuellement.")
        ];
    }
}
