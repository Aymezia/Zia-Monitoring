namespace ZiaMonitoring_App.Infrastructure.Collectors;

public static class SteamPlaytimeReader
{
    public static Dictionary<string, TimeSpan> ReadPlaytimes()
    {
        var result = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        var steamRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var userdataRoot = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdataRoot))
            return result;

        foreach (var userDir in Directory.EnumerateDirectories(userdataRoot).Take(5))
        {
            var localConfig = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(localConfig))
                continue;

            try
            {
                ParseLocalConfig(localConfig, result);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Lecture du temps de jeu Steam (localconfig.vdf) impossible", ex);
            }
        }

        return result;
    }

    private static void ParseLocalConfig(string filePath, Dictionary<string, TimeSpan> result)
    {
        // Simple VDF text parser for "LastPlayed" / "Playtime2wks" fields
        var lines = File.ReadLines(filePath).Take(20_000).ToList();
        string? currentAppId = null;
        string? currentName = null;
        long playtimeMinutes = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            // Detect app ID block: key is a numeric string
            if (line.StartsWith('"') && line.TrimStart('"').TrimEnd('"').All(char.IsDigit))
            {
                currentAppId = line.Trim('"');
                currentName = null;
                playtimeMinutes = 0;
                continue;
            }

            if (currentAppId is null)
                continue;

            // Extract name
            if (line.StartsWith("\"name\"", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    currentName = parts[1].Trim();
                continue;
            }

            // playtime_forever is stored in minutes
            if (line.StartsWith("\"playtime_forever\"", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1].Trim(), out var mins))
                    playtimeMinutes = mins;
                continue;
            }

            if (line == "}" && currentName is not null && playtimeMinutes > 0)
            {
                result[currentName] = TimeSpan.FromMinutes(playtimeMinutes);
                currentAppId = null;
                currentName = null;
                playtimeMinutes = 0;
            }
        }
    }
}
