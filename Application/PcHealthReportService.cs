using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Historise le score de l'audit PC (PcAuditService) et génère un rapport
/// hebdomadaire (HTML + PDF minimal, même technique que
/// SecurityReportExportService) avec la tendance du score dans le temps —
/// utile pour repérer une dégradation progressive avant qu'elle devienne un
/// problème visible.
/// </summary>
public sealed class PcHealthReportService : IDisposable
{
    private readonly SqliteConnection? _connection;
    private readonly string _stateFile;
    private DateTime? _lastReportGeneratedAt;

    public PcHealthReportService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);

        try
        {
            _connection = new SqliteConnection($"Data Source={Path.Combine(dir, "metrics.db")}");
            _connection.Open();

            using var command = _connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS pc_audit_score_history(
                    day INTEGER PRIMARY KEY,
                    score INTEGER NOT NULL,
                    critical_count INTEGER NOT NULL,
                    warning_count INTEGER NOT NULL);
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Base d'historique d'audit indisponible", ex);
            _connection = null;
        }

        _stateFile = Path.Combine(dir, "weekly-report-state.json");
        LoadState();
    }

    public void RecordAuditResult(PcAuditReport report)
    {
        if (_connection is null)
            return;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO pc_audit_score_history(day, score, critical_count, warning_count)
                VALUES ($day, $score, $critical, $warning)
                """;
            command.Parameters.AddWithValue("$day", DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
            command.Parameters.AddWithValue("$score", report.Score);
            command.Parameters.AddWithValue("$critical", report.CriticalCount);
            command.Parameters.AddWithValue("$warning", report.WarningCount);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Enregistrement du score d'audit impossible", ex);
        }
    }

    public IReadOnlyList<(DateTime Date, int Score)> GetScoreHistory(int days = 90)
    {
        if (_connection is null)
            return [];

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT day, score FROM pc_audit_score_history WHERE day >= $since ORDER BY day ASC";
            command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400 - days);

            var result = new List<(DateTime, int)>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add((DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0) * 86400).UtcDateTime, reader.GetInt32(1)));
            return result;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique de score impossible", ex);
            return [];
        }
    }

    public bool IsWeeklyReportDue(bool enabled) => IsDue(enabled, _lastReportGeneratedAt, DateTime.UtcNow);

    internal static bool IsDue(bool enabled, DateTime? lastGeneratedAt, DateTime now) =>
        enabled && (lastGeneratedAt is null || now - lastGeneratedAt.Value >= TimeSpan.FromDays(7));

    public (string HtmlPath, string PdfPath) GenerateWeeklyReport(PcAuditReport latestReport)
    {
        var history = GetScoreHistory();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var htmlPath = Path.Combine(desktop, $"ZiaMonitoring-RapportHebdo-{stamp}.html");
        var pdfPath = Path.Combine(desktop, $"ZiaMonitoring-RapportHebdo-{stamp}.pdf");

        File.WriteAllText(htmlPath, BuildHtml(latestReport, history), Encoding.UTF8);
        File.WriteAllBytes(pdfPath, BuildPdf(latestReport, history));

        _lastReportGeneratedAt = DateTime.UtcNow;
        SaveState();

        return (htmlPath, pdfPath);
    }

    internal static string BuildHtml(PcAuditReport report, IReadOnlyList<(DateTime Date, int Score)> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'/><title>Zia Monitoring - Rapport hebdomadaire</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#0a0906;color:#ecdcb8;padding:24px}h1{color:#e0a030}h2{color:#e0a030;margin-top:22px}table{width:100%;border-collapse:collapse}td,th{border:1px solid #2b2318;padding:8px;text-align:left}th{background:#100e0b}</style></head><body>");
        sb.AppendLine("<h1>Zia Monitoring - Rapport de santé hebdomadaire</h1>");
        sb.AppendLine($"<p>Généré le {report.GeneratedAt:dd/MM/yyyy HH:mm}</p>");
        sb.AppendLine($"<h2>Score actuel : {report.ScoreLabel}</h2>");
        sb.AppendLine($"<p>{WebUtility.HtmlEncode(report.SummaryLabel)}</p>");

        sb.AppendLine("<h2>Tendance du score</h2><table><tr><th>Date</th><th>Score</th></tr>");
        foreach (var (date, score) in history)
            sb.AppendLine($"<tr><td>{date:dd/MM/yyyy}</td><td>{score}/100</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine($"<h2>Constats ({report.Findings.Count})</h2><ul>");
        if (report.Findings.Count == 0)
        {
            sb.AppendLine("<li>Aucun point à signaler.</li>");
        }
        else
        {
            foreach (var finding in report.Findings)
                sb.AppendLine($"<li><b>[{WebUtility.HtmlEncode(finding.SeverityLabel)}]</b> {WebUtility.HtmlEncode(finding.Title)} — {WebUtility.HtmlEncode(finding.Detail)}</li>");
        }
        sb.AppendLine("</ul></body></html>");
        return sb.ToString();
    }

    internal static byte[] BuildPdf(PcAuditReport report, IReadOnlyList<(DateTime Date, int Score)> history)
    {
        var lines = new List<string>
        {
            "Zia Monitoring - Rapport de sante hebdomadaire",
            $"Genere: {report.GeneratedAt:yyyy-MM-dd HH:mm}",
            $"Score actuel: {report.ScoreLabel}",
            report.SummaryLabel,
            "",
            "Tendance du score:"
        };
        lines.AddRange(history.Select(h => $"  {h.Date:yyyy-MM-dd} : {h.Score}/100"));
        lines.Add("");
        lines.Add($"Constats ({report.Findings.Count}):");
        lines.AddRange(report.Findings.Take(30).Select(f => $"  [{f.SeverityLabel}] {f.Title} - {f.Detail}"));

        var escapedLines = lines.Select(EscapePdfText).ToList();
        var content = new StringBuilder();
        content.AppendLine("BT /F1 10 Tf 50 790 Td 14 TL");
        foreach (var line in escapedLines.Take(52))
            content.AppendLine($"({line}) Tj T*");
        content.AppendLine("ET");

        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}endstream"
        };

        var pdf = new StringBuilder();
        var offsets = new List<int> { 0 };
        pdf.AppendLine("%PDF-1.4");
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.AppendLine($"{i + 1} 0 obj");
            pdf.AppendLine(objects[i]);
            pdf.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.AppendLine("xref");
        pdf.AppendLine($"0 {objects.Count + 1}");
        pdf.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
            pdf.AppendLine($"{offset:D10} 00000 n ");
        pdf.AppendLine("trailer");
        pdf.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        pdf.AppendLine("startxref");
        pdf.AppendLine(xrefOffset.ToString());
        pdf.AppendLine("%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string EscapePdfText(string text)
    {
        var ascii = new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
        return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
                _lastReportGeneratedAt = JsonSerializer.Deserialize<DateTime?>(File.ReadAllText(_stateFile));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état du rapport hebdomadaire impossible", ex);
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_lastReportGeneratedAt));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de l'état du rapport hebdomadaire impossible", ex);
        }
    }

    public void Dispose() => _connection?.Dispose();
}
