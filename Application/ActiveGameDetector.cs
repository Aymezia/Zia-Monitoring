using System.Diagnostics;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class ActiveGameDetector
{
    private static readonly HashSet<string> KnownGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "valorant", "valorant-win64-shipping", "league of legends", "leagueclient", "leagueoflegends",
        "csgo", "cs2", "dota2", "fortnite", "fortniteclient-win64-shipping",
        "minecraft", "javaw", "cod", "modernwarfare", "warzone",
        "overwatch", "overwatch2", "apexlegends", "rocketleague",
        "gta5", "gtav", "eldenring", "fifa", "r5apex", "destiny2",
        "rainbowsix", "r6", "battlefieldv", "bf1", "bf4",
        "pubg", "tslgame", "warframe", "borderlands3",
        "cyberpunk2077", "witcher3", "hogwartslecacy",
        "dota", "hearthstone", "starcraft", "diablo"
    };

    private DateTime? _sessionStart;
    private string? _activeGame;

    public ActiveGameSession? DetectActiveGame()
    {
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                var name = proc.ProcessName;
                if (!KnownGameProcessNames.Contains(name))
                    continue;

                double cpu = 0;
                double memMb = 0;
                try
                {
                    memMb = proc.WorkingSet64 / 1024.0 / 1024.0;
                }
                catch { }

                if (_activeGame != name)
                {
                    _activeGame = name;
                    _sessionStart = DateTime.Now;
                }

                var duration = _sessionStart.HasValue
                    ? DateTime.Now - _sessionStart.Value
                    : TimeSpan.Zero;

                return new ActiveGameSession(name, cpu, memMb, duration);
            }
        }
        catch { }

        _activeGame = null;
        _sessionStart = null;
        return null;
    }
}
