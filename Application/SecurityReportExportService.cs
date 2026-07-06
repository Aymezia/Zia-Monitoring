using System.Net;
using System.Text;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class SecurityReportExportService
{
    public (string HtmlPath, string PdfPath) Export(SecurityReport report)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var htmlPath = Path.Combine(desktop, $"ZiaMonitoring-Security-{stamp}.html");
        var pdfPath = Path.Combine(desktop, $"ZiaMonitoring-Security-{stamp}.pdf");

        File.WriteAllText(htmlPath, BuildHtml(report), Encoding.UTF8);
        File.WriteAllBytes(pdfPath, BuildSimplePdf(report));
        return (htmlPath, pdfPath);
    }

    private static string BuildHtml(SecurityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'/><title>Zia Monitoring - Securite</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#101014;color:#f4f4f5;padding:24px}h1{color:#d478ff}h2{color:#f23053;margin-top:22px}table{width:100%;border-collapse:collapse}td,th{border:1px solid #333;padding:8px;text-align:left}th{background:#191724}</style></head><body>");
        sb.AppendLine("<h1>Zia Monitoring - Rapport de securite</h1>");
        sb.AppendLine($"<p>Genere le {report.GeneratedAt:dd/MM/yyyy HH:mm}</p>");
        sb.AppendLine($"<h2>Score de risque global: {report.RiskScore}/100</h2>");
        sb.AppendLine("<table><tr><th>Controle</th><th>Etat</th></tr>");
        sb.AppendLine($"<tr><td>Pare-feu Windows</td><td>{State(report.FirewallEnabled)}</td></tr>");
        sb.AppendLine($"<tr><td>UAC</td><td>{State(report.UacEnabled)}</td></tr>");
        sb.AppendLine("</table>");
        AppendList(sb, "Processus avec signature connue", report.MaliciousProcessMatches);
        AppendList(sb, "Hooks clavier probables", report.KeyloggerHookWarnings);
        AppendList(sb, "Entrees de demarrage suspectes", report.SuspiciousStartupEntries);
        AppendList(sb, "Ports TCP ouverts", report.OpenPorts);
        AppendList(sb, "Pilotes obsoletes", report.ObsoleteDrivers);
        AppendList(sb, "Avertissements S.M.A.R.T", report.DiskSmartWarnings);
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine($"<h2>{WebUtility.HtmlEncode(title)} ({items.Count})</h2><ul>");
        if (items.Count == 0)
        {
            sb.AppendLine("<li>Aucun element detecte.</li>");
        }
        else
        {
            foreach (var item in items)
                sb.AppendLine($"<li>{WebUtility.HtmlEncode(item)}</li>");
        }

        sb.AppendLine("</ul>");
    }

    private static string State(bool enabled) => enabled ? "Actif" : "Inactif";

    private static byte[] BuildSimplePdf(SecurityReport report)
    {
        var lines = new List<string>
        {
            "Zia Monitoring - Rapport de securite",
            $"Genere: {report.GeneratedAt:yyyy-MM-dd HH:mm}",
            $"Score de risque: {report.RiskScore}/100",
            $"Pare-feu: {State(report.FirewallEnabled)}",
            $"UAC: {State(report.UacEnabled)}",
            $"Signatures connues: {report.MaliciousProcessMatches.Count}",
            $"Hooks clavier probables: {report.KeyloggerHookWarnings.Count}",
            $"Demarrage suspect: {report.SuspiciousStartupEntries.Count}",
            $"Ports ouverts: {report.OpenPorts.Count}",
            $"Pilotes obsoletes: {report.ObsoleteDrivers.Count}",
            $"SMART: {report.DiskSmartWarnings.Count}"
        };

        lines.AddRange(report.MaliciousProcessMatches.Take(10).Select(item => $"Signature: {item}"));
        lines.AddRange(report.KeyloggerHookWarnings.Take(10).Select(item => $"Hook: {item}"));
        lines.AddRange(report.SuspiciousStartupEntries.Take(10).Select(item => $"Startup: {item}"));

        var escapedLines = lines.Select(EscapePdfText).ToList();
        var content = new StringBuilder();
        content.AppendLine("BT /F1 10 Tf 50 790 Td 14 TL");
        foreach (var line in escapedLines.Take(48))
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
}
