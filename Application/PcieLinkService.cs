using System.Diagnostics;

namespace ZiaMonitoring_App.Application;

public sealed record PcieLinkInfo(string GpuName, int CurrentWidth, int MaxWidth, int CurrentGen, int MaxGen)
{
    public bool IsBridged => CurrentWidth > 0 && MaxWidth > 0 && CurrentWidth < MaxWidth;

    public string Label => IsBridged
        ? $"{GpuName} : le GPU tourne en x{CurrentWidth} au lieu de x{MaxWidth} (PCIe Gen{CurrentGen}/Gen{MaxGen} max) — port secondaire, bifurcation partagée (M.2) ou carte mal enclenchée."
        : $"{GpuName} : x{CurrentWidth}/x{MaxWidth} en PCIe Gen{CurrentGen} — lien PCIe nominal, aucun bridage détecté.";
}

/// <summary>
/// Détection de bridage PCIe (GPU qui tourne en x8/x4 au lieu de x16) via
/// nvidia-smi — seule source fiable et déjà installée avec le pilote NVIDIA.
/// Aucun équivalent officiel simple n'existe pour AMD/Intel : le résultat est
/// vide sur ces GPU plutôt que d'afficher une valeur inventée.
/// </summary>
public static class PcieLinkService
{
    public static IReadOnlyList<PcieLinkInfo> Detect()
    {
        try
        {
            var output = RunNvidiaSmi();
            return ParseNvidiaSmiCsv(output);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture nvidia-smi impossible (GPU non-NVIDIA ou pilote absent)", ex);
            return [];
        }
    }

    private static string RunNvidiaSmi()
    {
        var psi = new ProcessStartInfo("nvidia-smi",
            "--query-gpu=name,pcie.link.width.current,pcie.link.width.max,pcie.link.gen.current,pcie.link.gen.max --format=csv,noheader,nounits")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("nvidia-smi introuvable.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }

    internal static IReadOnlyList<PcieLinkInfo> ParseNvidiaSmiCsv(string csv)
    {
        var result = new List<PcieLinkInfo>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',').Select(f => f.Trim()).ToList();
            if (fields.Count < 5)
                continue;

            if (int.TryParse(fields[1], out var currentWidth)
                && int.TryParse(fields[2], out var maxWidth)
                && int.TryParse(fields[3], out var currentGen)
                && int.TryParse(fields[4], out var maxGen))
            {
                result.Add(new PcieLinkInfo(fields[0], currentWidth, maxWidth, currentGen, maxGen));
            }
        }

        return result;
    }
}
