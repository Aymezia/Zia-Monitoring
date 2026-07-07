using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class GamerTroubleshootingService
{
    private readonly Dictionary<string, (DateTime stamp, AutoCollectReport report)> _autoCollectCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly List<IssueFixItem> _knowledgeBase =
    [
        new IssueFixItem(
            Product: "Valorant",
            ErrorCode: "VAN 9003",
            Symptom: "Valorant refuses to start because secure boot or TPM is missing.",
            RootCauses: ["Secure Boot disabled", "TPM not enabled in BIOS", "Windows 11 security requirement mismatch"],
            Solutions:
            [
                "Enable TPM 2.0 and Secure Boot in BIOS/UEFI.",
                "Reboot and verify Windows Security reports TPM ready.",
                "Update Vanguard and restart PC."
            ],
            Sources:
            [
                "https://support-valorant.riotgames.com/",
                "https://support.microsoft.com/windows"
            ]),

        new IssueFixItem(
            Product: "Valorant",
            ErrorCode: "VAN 1067",
            Symptom: "Vanguard service cannot initialize correctly.",
            RootCauses: ["vgc service stopped", "driver/service conflict", "outdated Vanguard"],
            Solutions:
            [
                "Set vgc service to Automatic and restart PC.",
                "Reinstall Riot Vanguard from Valorant launcher.",
                "Update Windows and chipset drivers."
            ],
            Sources:
            [
                "https://support-valorant.riotgames.com/",
                "https://learn.microsoft.com/windows/"
            ]),

        new IssueFixItem(
            Product: "Valorant",
            ErrorCode: "VAL 43",
            Symptom: "Connection timeout while joining game services.",
            RootCauses: ["Service instability", "DNS mismatch", "temporary network route issue"],
            Solutions:
            [
                "Flush DNS and renew network lease.",
                "Check Riot service status before reconnecting.",
                "Avoid VPN or test with another DNS."
            ],
            Sources:
            [
                "https://status.riotgames.com/",
                "https://support-valorant.riotgames.com/"
            ]),

        new IssueFixItem(
            Product: "Valorant",
            ErrorCode: "VAL 5",
            Symptom: "Session issue or account disconnection while joining.",
            RootCauses: ["Network session expired", "Riot service instability", "Firewall or DNS issue"],
            Solutions:
            [
                "Sign out and sign in again in Riot Client.",
                "Flush DNS and restart router.",
                "Check Riot service status before relaunching."
            ],
            Sources:
            [
                "https://status.riotgames.com/",
                "https://support-valorant.riotgames.com/",
                "https://learn.microsoft.com/windows-server/administration/windows-commands/ipconfig"
            ]),

        new IssueFixItem(
            Product: "OBS",
            ErrorCode: "NVENC Error: init failed",
            Symptom: "OBS cannot initialize NVENC encoder.",
            RootCauses: ["Outdated GPU driver", "GPU overloaded", "conflicting app captures encoder"],
            Solutions:
            [
                "Update NVIDIA driver to latest stable version.",
                "Set OBS process priority to Above Normal and lower output load.",
                "Switch temporarily to x264 or QSV while troubleshooting."
            ],
            Sources:
            [
                "https://obsproject.com/kb/",
                "https://obsproject.com/tools/analyzer",
                "https://www.nvidia.com/Download/index.aspx"
            ]),

        new IssueFixItem(
            Product: "OBS",
            ErrorCode: "AMF Error",
            Symptom: "AMD encoder initialization fails in OBS.",
            RootCauses: ["Driver mismatch", "AMF runtime issue", "unsupported profile settings"],
            Solutions:
            [
                "Update AMD Adrenalin driver.",
                "Use OBS auto recommended preset for AMD.",
                "Reduce B-frames and profile complexity."
            ],
            Sources:
            [
                "https://obsproject.com/kb/",
                "https://www.amd.com/en/support"
            ]),

        new IssueFixItem(
            Product: "OBS",
            ErrorCode: "Encoder Overloaded",
            Symptom: "OBS drops frames and warns that the encoder cannot keep up.",
            RootCauses: ["Bitrate too high", "Preset too heavy", "GPU/CPU saturation"],
            Solutions:
            [
                "Use hardware encoder (NVENC/AMF/QSV) when available.",
                "Lower output resolution or FPS.",
                "Switch to a faster encoder preset and cap game FPS."
            ],
            Sources:
            [
                "https://obsproject.com/kb/",
                "https://obsproject.com/forum/",
                "https://obsproject.com/tools/analyzer"
            ]),

        new IssueFixItem(
            Product: "OBS",
            ErrorCode: "Failed to Start Recording",
            Symptom: "Recording cannot start due to output path or encoder constraints.",
            RootCauses: ["Invalid output path", "permission denied", "encoder unavailable"],
            Solutions:
            [
                "Set output path to a writable local folder.",
                "Run OBS as administrator once to validate permissions.",
                "Apply OBS auto recommended preset and retry."
            ],
            Sources:
            [
                "https://obsproject.com/kb/",
                "https://obsproject.com/tools/analyzer"
            ]),

        new IssueFixItem(
            Product: "OBS",
            ErrorCode: "Dropped Frames (Network)",
            Symptom: "Stream stutters with dropped network frames.",
            RootCauses: ["Unstable upload", "High bitrate for current uplink", "Ingest server congestion"],
            Solutions:
            [
                "Run bandwidth test and lower bitrate by 20-30%.",
                "Enable dynamic bitrate in OBS.",
                "Switch to a closer ingest server."
            ],
            Sources:
            [
                "https://obsproject.com/kb/",
                "https://inspector.twitch.tv/"
            ]),

        new IssueFixItem(
            Product: "Fortnite",
            ErrorCode: "EAC Error / Easy Anti-Cheat failed to load",
            Symptom: "Game refuses to launch, Easy Anti-Cheat service fails to initialize.",
            RootCauses: ["EAC service not running", "Corrupted EAC install", "Conflicting overlay/injector software"],
            Solutions:
            [
                "Repair Easy Anti-Cheat from the game launcher (right-click > Repair).",
                "Run the game once as administrator to reinstall the EAC driver.",
                "Close overlay tools (RGB software, injectors) that hook into the game process."
            ],
            Sources:
            [
                "https://www.epicgames.com/help/",
                "https://www.easy.ac/en-us/support/"
            ]),

        new IssueFixItem(
            Product: "Fortnite",
            ErrorCode: "Video memory could not be allocated",
            Symptom: "Crash or black screen on launch due to a DirectX video memory allocation failure.",
            RootCauses: ["VRAM exhausted by background overlays/capture tools", "Outdated GPU driver", "Texture settings too aggressive for available VRAM"],
            Solutions:
            [
                "Close background GPU-heavy apps (browsers, capture software) before launching.",
                "Update the GPU driver to the latest stable version.",
                "Lower texture quality / resolution scale in the game's video settings."
            ],
            Sources:
            [
                "https://www.epicgames.com/help/",
                "https://www.nvidia.com/Download/index.aspx"
            ]),

        new IssueFixItem(
            Product: "CS2",
            ErrorCode: "VAC was unable to verify the game session",
            Symptom: "Cannot join VAC-secured servers; VAC verification fails at connect.",
            RootCauses: ["Modified/corrupted game files", "Background injector or overlay tool flagged as suspicious", "VAC service temporarily unstable"],
            Solutions:
            [
                "Verify integrity of game files via Steam (Properties > Installed Files).",
                "Disable third-party overlays/injectors (including unofficial FPS/skin tools) before playing.",
                "Restart Steam completely and retry."
            ],
            Sources:
            [
                "https://help.steampowered.com/",
                "https://support.steampowered.com/kb_article.php?ref=7849-radz-6869"
            ]),

        new IssueFixItem(
            Product: "CS2",
            ErrorCode: "Shader Cache Stutter",
            Symptom: "Noticeable stutter/freezes during the first minutes after a driver update or game update, then smooths out.",
            RootCauses: ["GPU driver update purges the shader cache", "Shaders recompiling on first use of new effects/maps", "Low free disk space slowing cache writes"],
            Solutions:
            [
                "Play a warm-up deathmatch/casual match before a ranked game after any driver update.",
                "Ensure at least a few GB of free disk space on the system drive.",
                "Avoid manually deleting the GPU shader cache folders while mid-session."
            ],
            Sources:
            [
                "https://help.steampowered.com/",
                "https://www.nvidia.com/Download/index.aspx"
            ]),

        new IssueFixItem(
            Product: "Apex Legends",
            ErrorCode: "EAC error at launch",
            Symptom: "Game closes immediately or shows an Easy Anti-Cheat initialization error before reaching the menu.",
            RootCauses: ["EAC driver not installed/corrupted", "Secure Boot or driver signature conflict", "Leftover EAC service from a previous install"],
            Solutions:
            [
                "Repair Easy Anti-Cheat from the platform (Origin/Steam) game properties.",
                "Reinstall the game's EAC component from the install folder if repair fails.",
                "Ensure Secure Boot is enabled if the platform requires it."
            ],
            Sources:
            [
                "https://www.easy.ac/en-us/support/",
                "https://help.ea.com/"
            ]),

        new IssueFixItem(
            Product: "Rainbow Six Siege",
            ErrorCode: "BattlEye Initialization Failed",
            Symptom: "Game does not launch and reports that BattlEye failed to start.",
            RootCauses: ["BEService not running", "Driver signature enforcement disabled (conflicts with anti-cheat)", "Third-party overlay interfering with the anti-cheat driver"],
            Solutions:
            [
                "Re-enable driver signature enforcement in Windows startup settings if it was disabled.",
                "Restart the BEService via Task Manager or reinstall BattlEye from the game folder.",
                "Disable conflicting overlays (some RGB/monitoring tools included) and retry."
            ],
            Sources:
            [
                "https://www.battleye.com/support/",
                "https://www.ubisoft.com/help"
            ]),

        new IssueFixItem(
            Product: "League of Legends",
            ErrorCode: "Client Won't Patch / Update Stuck",
            Symptom: "The League client hangs or fails while downloading a patch.",
            RootCauses: ["Corrupted patcher cache", "Firewall/antivirus blocking the patcher", "Insufficient permissions on the install folder"],
            Solutions:
            [
                "Run the official Hextech Repair Tool to clear the patcher cache.",
                "Allow LeagueClient.exe and RiotClientServices.exe through the firewall/antivirus.",
                "Run the client once as administrator to rule out permission issues."
            ],
            Sources:
            [
                "https://support-leagueoflegends.riotgames.com/",
                "https://status.riotgames.com/"
            ]),

        new IssueFixItem(
            Product: "DirectX",
            ErrorCode: "DXGI_ERROR_DEVICE_REMOVED",
            Symptom: "Game crashes to desktop; the GPU driver resets (TDR event) during play.",
            RootCauses: ["Unstable GPU overclock", "Driver timeout from overheating or sustained overload", "Corrupted or outdated GPU driver"],
            Solutions:
            [
                "Revert any GPU/VRAM overclock to stock settings and retest.",
                "Clean-reinstall the GPU driver using DDU in Safe Mode, then install the latest stable version.",
                "Check GPU temperatures for thermal throttling during the crash window (page Sante)."
            ],
            Sources:
            [
                "https://learn.microsoft.com/windows-hardware/drivers/display/timeout-detection-and-recovery",
                "https://www.nvidia.com/Download/index.aspx"
            ]),

        new IssueFixItem(
            Product: "DirectX",
            ErrorCode: "DXGI_ERROR_DEVICE_HUNG",
            Symptom: "Game freezes then crashes; the GPU command queue stops responding.",
            RootCauses: ["Unresponsive GPU command queue under heavy load", "Power delivery instability under load spikes", "Aggressive driver-level overclock profile"],
            Solutions:
            [
                "Disable any driver-level auto-overclock/boost profile (e.g. GPU vendor tuning utilities).",
                "Update the GPU driver to the latest stable (non-beta) version.",
                "Verify the PSU comfortably covers the GPU's peak power draw."
            ],
            Sources:
            [
                "https://learn.microsoft.com/windows-hardware/drivers/display/timeout-detection-and-recovery",
                "https://www.amd.com/en/support"
            ])
    ];

    public AutoCollectReport AutoCollectLogs(string product)
    {
        if (_autoCollectCache.TryGetValue(product, out var cached)
            && DateTime.Now - cached.stamp <= CacheTtl)
        {
            return cached.report;
        }

        var obsData = CollectObsLogFindings();
        var events = CollectWindowsEventsOptimized();
        var detection = DetectGpuAndEncoder(obsData.findings, events);

        var tags = BuildCorrelationTags(product, obsData.findings, events, detection.gpuDriver, detection.encoder);
        var links = BuildUsefulLinks(product);

        var report = new AutoCollectReport(
            Product: product,
            CollectedAt: DateTime.Now,
            ObsLogPath: obsData.logPath,
            ObsLinesScanned: obsData.linesScanned,
            DetectedGpuDriver: detection.gpuDriver,
            DetectedEncoder: detection.encoder,
            ObsLogFindings: obsData.findings,
            WindowsEvents: events,
            CorrelationTags: tags,
            UsefulLinks: links);

        _autoCollectCache[product] = (DateTime.Now, report);
        return report;
    }

    public DiagnosisReport Diagnose(string product, string errorQuery, AutoCollectReport? autoCollect)
    {
        var matched = Search(product, errorQuery);
        var observations = BuildObservations(errorQuery, autoCollect);
        var suggestions = BuildSuggestions(errorQuery, matched, autoCollect);
        var sources = suggestions.SelectMany(x => x.Sources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new DiagnosisReport(
            Product: product,
            ErrorQuery: errorQuery,
            Observations: observations,
            Suggestions: suggestions,
            Sources: sources,
            AutoCollect: autoCollect);
    }

    public SafeFixResult ApplySafeFix(string actionKey)
    {
        try
        {
            switch (actionKey)
            {
                case "flush_dns":
                    RunCommand("ipconfig", "/flushdns");
                    return new SafeFixResult(true, actionKey, "DNS cache flushed successfully.");
                case "obs_open_analyzer":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://obsproject.com/tools/analyzer",
                        UseShellExecute = true
                    });
                    return new SafeFixResult(true, actionKey, "OBS Analyzer opened in browser.");
                case "open_riot_status":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://status.riotgames.com/",
                        UseShellExecute = true
                    });
                    return new SafeFixResult(true, actionKey, "Riot status page opened.");
                case "disable_obs_startup":
                    DisableObsStartup();
                    return new SafeFixResult(true, actionKey, "OBS startup entry disabled in HKCU Run.");
                case "obs_apply_recommended_preset":
                    var presetPath = WriteObsRecommendedPreset();
                    return new SafeFixResult(true, actionKey, $"Recommended OBS preset created at: {presetPath}");
                case "tpm_secureboot_guided":
                    var guide = RunTpmSecureBootGuidedCheck();
                    return new SafeFixResult(true, actionKey, guide);
                default:
                    return new SafeFixResult(false, actionKey, "Unknown safe action key.");
            }
        }
        catch (Exception ex)
        {
            return new SafeFixResult(false, actionKey, ex.Message);
        }
    }

    public IReadOnlyList<IssueFixItem> Search(string product, string query)
    {
        var productFiltered = _knowledgeBase
            .Where(x => x.Product.Contains(product, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(query))
        {
            return productFiltered.ToList();
        }

        return productFiltered
            .Where(x =>
                x.ErrorCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Symptom.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.RootCauses.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static (List<string> findings, string logPath, int linesScanned) CollectObsLogFindings()
    {
        var findings = new List<string>();
        var obsLogRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "logs");

        if (!Directory.Exists(obsLogRoot))
        {
            return (["OBS logs folder not found."], "N/A", 0);
        }

        var latest = Directory
            .EnumerateFiles(obsLogRoot, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest))
        {
            return (["No OBS log file found."], "N/A", 0);
        }

        var tail = ReadFileTail(latest, 1500);
        var lines = tail
            .Where(line => Regex.IsMatch(line, "error|warning|failed|dropped|overloaded|nvenc|amf|qsv", RegexOptions.IgnoreCase))
            .Take(40)
            .ToList();

        if (lines.Count == 0)
        {
            findings.Add("No critical line detected in latest OBS log.");
        }
        else
        {
            findings.AddRange(lines);
        }

        return (findings, latest, tail.Count);
    }

    private static List<EventExcerpt> CollectWindowsEventsOptimized()
    {
        var entries = new List<EventExcerpt>();
        var threshold = DateTime.Now.AddHours(-8);

        try
        {
            var query = new EventLogQuery("System", PathType.LogName)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            for (var i = 0; i < 300; i++)
            {
                using var eventRecord = reader.ReadEvent();
                if (eventRecord is null)
                {
                    break;
                }

                if (eventRecord.TimeCreated is null || eventRecord.TimeCreated.Value < threshold)
                {
                    continue;
                }

                var source = eventRecord.ProviderName ?? string.Empty;
                if (!IsInterestingSource(source))
                {
                    continue;
                }

                var level = eventRecord.LevelDisplayName ?? "Info";
                var message = eventRecord.FormatDescription() ?? string.Empty;
                if (message.Length > 260)
                {
                    message = message[..260] + "...";
                }

                entries.Add(new EventExcerpt(source, level, eventRecord.TimeCreated.Value, message));

                if (entries.Count >= 40)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // EventLog access may fail depending on policy.
            Infrastructure.AppLog.Warn("Lecture du journal d'evenements impossible", ex);
        }

        return entries
            .OrderByDescending(x => x.Time)
            .ToList();
    }

    private static List<string> ReadFileTail(string path, int maxLines)
    {
        var queue = new Queue<string>(maxLines);
        foreach (var line in File.ReadLines(path))
        {
            if (queue.Count == maxLines)
            {
                queue.Dequeue();
            }

            queue.Enqueue(line);
        }

        return queue.ToList();
    }

    private static bool IsInterestingSource(string source)
    {
        return source.Contains("Display", StringComparison.OrdinalIgnoreCase)
            || source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase)
            || source.Contains("amdkmdag", StringComparison.OrdinalIgnoreCase)
            || source.Contains("Netwtw", StringComparison.OrdinalIgnoreCase)
            || source.Contains("Tcpip", StringComparison.OrdinalIgnoreCase)
            || source.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
            || source.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildCorrelationTags(string product, IReadOnlyList<string> obsFindings, IReadOnlyList<EventExcerpt> events, string? gpuDriver, string? encoder)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (obsFindings.Any(x => x.Contains("overloaded", StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add("encoder_overload_signal");
        }

        if (obsFindings.Any(x => x.Contains("dropped", StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add("network_drop_signal");
        }

        if (events.Any(x => x.Source.Contains("Display", StringComparison.OrdinalIgnoreCase) || x.Source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add("gpu_driver_events_detected");
        }

        if (events.Any(x => x.Source.Contains("Tcpip", StringComparison.OrdinalIgnoreCase) || x.Source.Contains("WLAN", StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add("network_events_detected");
        }

        if (!string.IsNullOrWhiteSpace(gpuDriver))
        {
            tags.Add($"gpu_driver:{gpuDriver}");
        }

        if (!string.IsNullOrWhiteSpace(encoder))
        {
            tags.Add($"encoder:{encoder}");
        }

        if (product.Contains("valorant", StringComparison.OrdinalIgnoreCase)
            || product.Contains("fortnite", StringComparison.OrdinalIgnoreCase)
            || product.Contains("apex", StringComparison.OrdinalIgnoreCase)
            || product.Contains("rainbow", StringComparison.OrdinalIgnoreCase)
            || product.Contains("siege", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("anti_cheat_sensitive_context");
        }

        return tags.ToList();
    }

    private static List<string> BuildUsefulLinks(string product)
    {
        if (product.Contains("valorant", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "https://support-valorant.riotgames.com/",
                "https://status.riotgames.com/",
                "https://learn.microsoft.com/windows/security/"
            ];
        }

        if (product.Contains("fortnite", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://www.epicgames.com/help/", "https://www.easy.ac/en-us/support/"];
        }

        if (product.Contains("cs2", StringComparison.OrdinalIgnoreCase) || product.Contains("counter", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://help.steampowered.com/", "https://help.steampowered.com/en/wizard/HelpWithGame"];
        }

        if (product.Contains("apex", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://www.easy.ac/en-us/support/", "https://help.ea.com/"];
        }

        if (product.Contains("rainbow", StringComparison.OrdinalIgnoreCase) || product.Contains("siege", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://www.battleye.com/support/", "https://www.ubisoft.com/help"];
        }

        if (product.Contains("league", StringComparison.OrdinalIgnoreCase))
        {
            return ["https://support-leagueoflegends.riotgames.com/", "https://status.riotgames.com/"];
        }

        if (product.Contains("directx", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "https://learn.microsoft.com/windows-hardware/drivers/display/timeout-detection-and-recovery",
                "https://www.nvidia.com/Download/index.aspx",
                "https://www.amd.com/en/support"
            ];
        }

        return
        [
            "https://obsproject.com/kb/",
            "https://obsproject.com/tools/analyzer",
            "https://inspector.twitch.tv/"
        ];
    }

    private static (string? gpuDriver, string? encoder) DetectGpuAndEncoder(IReadOnlyList<string> obsFindings, IReadOnlyList<EventExcerpt> events)
    {
        string? gpuDriver = null;
        string? encoder = null;

        if (events.Any(x => x.Source.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase))
            || obsFindings.Any(x => x.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)))
        {
            gpuDriver = "NVIDIA";
        }
        else if (events.Any(x => x.Source.Contains("amdkmdag", StringComparison.OrdinalIgnoreCase))
            || obsFindings.Any(x => x.Contains("AMD", StringComparison.OrdinalIgnoreCase)))
        {
            gpuDriver = "AMD";
        }
        else if (obsFindings.Any(x => x.Contains("Intel", StringComparison.OrdinalIgnoreCase) || x.Contains("igfx", StringComparison.OrdinalIgnoreCase)))
        {
            gpuDriver = "Intel";
        }

        if (obsFindings.Any(x => x.Contains("NVENC", StringComparison.OrdinalIgnoreCase)))
        {
            encoder = "NVENC";
        }
        else if (obsFindings.Any(x => x.Contains("AMF", StringComparison.OrdinalIgnoreCase)))
        {
            encoder = "AMF";
        }
        else if (obsFindings.Any(x => x.Contains("QSV", StringComparison.OrdinalIgnoreCase) || x.Contains("Quick Sync", StringComparison.OrdinalIgnoreCase)))
        {
            encoder = "QSV";
        }
        else if (obsFindings.Any(x => x.Contains("x264", StringComparison.OrdinalIgnoreCase)))
        {
            encoder = "x264";
        }

        return (gpuDriver, encoder);
    }

    private static List<string> BuildObservations(string errorQuery, AutoCollectReport? autoCollect)
    {
        var observations = new List<string>
        {
            string.IsNullOrWhiteSpace(errorQuery)
                ? "No explicit error code entered by user."
                : $"User query: {errorQuery}"
        };

        if (autoCollect is null)
        {
            observations.Add("Auto collect not executed yet.");
            return observations;
        }

        observations.Add($"OBS findings: {autoCollect.ObsLogFindings.Count}");
        observations.Add($"Windows events considered: {autoCollect.WindowsEvents.Count}");
        observations.Add($"OBS log scanned lines: {autoCollect.ObsLinesScanned}");
        observations.Add($"Detected GPU driver: {autoCollect.DetectedGpuDriver ?? "Unknown"}");
        observations.Add($"Detected encoder: {autoCollect.DetectedEncoder ?? "Unknown"}");

        foreach (var tag in autoCollect.CorrelationTags)
        {
            observations.Add($"Correlation tag: {tag}");
        }

        return observations;
    }

    private static List<SuggestedFix> BuildSuggestions(string errorQuery, IReadOnlyList<IssueFixItem> matched, AutoCollectReport? autoCollect)
    {
        var list = new List<SuggestedFix>();

        foreach (var issue in matched)
        {
            foreach (var solution in issue.Solutions)
            {
                var score = 40;

                if (!string.IsNullOrWhiteSpace(errorQuery) && issue.ErrorCode.Contains(errorQuery, StringComparison.OrdinalIgnoreCase))
                {
                    score += 35;
                }

                if (!string.IsNullOrWhiteSpace(errorQuery) && solution.Contains(errorQuery, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (autoCollect is not null)
                {
                    if (solution.Contains("bitrate", StringComparison.OrdinalIgnoreCase)
                        && autoCollect.CorrelationTags.Contains("network_drop_signal", StringComparer.OrdinalIgnoreCase))
                    {
                        score += 18;
                    }

                    if (solution.Contains("encoder", StringComparison.OrdinalIgnoreCase)
                        && autoCollect.CorrelationTags.Contains("encoder_overload_signal", StringComparer.OrdinalIgnoreCase))
                    {
                        score += 20;
                    }

                    if (solution.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                        && autoCollect.CorrelationTags.Contains("gpu_driver:NVIDIA", StringComparer.OrdinalIgnoreCase))
                    {
                        score += 16;
                    }

                    if (solution.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                        && autoCollect.CorrelationTags.Contains("gpu_driver:AMD", StringComparer.OrdinalIgnoreCase))
                    {
                        score += 16;
                    }

                    if (solution.Contains("DNS", StringComparison.OrdinalIgnoreCase)
                        && autoCollect.CorrelationTags.Contains("network_events_detected", StringComparer.OrdinalIgnoreCase))
                    {
                        score += 14;
                    }
                }

                score = Math.Clamp(score, 20, 99);
                var actionKey = GetSafeActionKey(solution);

                list.Add(new SuggestedFix(
                    Title: $"{issue.ErrorCode} -> {solution}",
                    Why: issue.Symptom,
                    ConfidenceScore: score,
                    ConfidenceLabel: GetConfidenceLabel(score),
                    SafeActionKey: actionKey,
                    IsSafeOneClick: actionKey is not null,
                    Sources: issue.Sources));
            }
        }

        return list
            .OrderByDescending(x => x.ConfidenceScore)
            .ToList();
    }

    private static string? GetSafeActionKey(string solution)
    {
        if (solution.Contains("Flush DNS", StringComparison.OrdinalIgnoreCase))
        {
            return "flush_dns";
        }

        if (solution.Contains("Set vgc service to Automatic", StringComparison.OrdinalIgnoreCase)
            || solution.Contains("TPM", StringComparison.OrdinalIgnoreCase)
            || solution.Contains("Secure Boot", StringComparison.OrdinalIgnoreCase))
        {
            return "tpm_secureboot_guided";
        }

        if (solution.Contains("Apply OBS auto recommended preset", StringComparison.OrdinalIgnoreCase)
            || solution.Contains("auto recommended preset", StringComparison.OrdinalIgnoreCase))
        {
            return "obs_apply_recommended_preset";
        }

        if (solution.Contains("OBS", StringComparison.OrdinalIgnoreCase) && solution.Contains("startup", StringComparison.OrdinalIgnoreCase))
        {
            return "disable_obs_startup";
        }

        if (solution.Contains("Riot service status", StringComparison.OrdinalIgnoreCase))
        {
            return "open_riot_status";
        }

        if (solution.Contains("OBS", StringComparison.OrdinalIgnoreCase) || solution.Contains("analyzer", StringComparison.OrdinalIgnoreCase))
        {
            return "obs_open_analyzer";
        }

        return null;
    }

    private static string GetConfidenceLabel(int score)
    {
        if (score >= 80)
        {
            return "Tres elevee";
        }

        if (score >= 65)
        {
            return "Elevee";
        }

        if (score >= 50)
        {
            return "Moyenne";
        }

        return "Faible";
    }

    private static void RunCommand(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        process.WaitForExit(5000);
    }

    private static void DisableObsStartup()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (runKey is null)
        {
            return;
        }

        foreach (var name in runKey.GetValueNames())
        {
            var value = runKey.GetValue(name)?.ToString() ?? string.Empty;
            if (name.Contains("OBS", StringComparison.OrdinalIgnoreCase)
                || value.Contains("obs64", StringComparison.OrdinalIgnoreCase))
            {
                runKey.DeleteValue(name, false);
            }
        }
    }

    private static string WriteObsRecommendedPreset()
    {
        var profileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "basic", "profiles", "ZiaAutoPreset");
        Directory.CreateDirectory(profileRoot);

        var content =
            "[General]\n" +
            "Name=Zia Auto Preset\n" +
            "\n" +
            "[Output]\n" +
            "Mode=Simple\n" +
            "RecQuality=Small\n" +
            "VBitrate=6000\n" +
            "ABitrate=160\n" +
            "\n" +
            "[Video]\n" +
            "FPSCommon=60\n" +
            "BaseCX=1920\n" +
            "BaseCY=1080\n" +
            "OutputCX=1600\n" +
            "OutputCY=900\n";

        var file = Path.Combine(profileRoot, "basic.ini");
        File.WriteAllText(file, content);
        return file;
    }

    private static string RunTpmSecureBootGuidedCheck()
    {
        var secureBoot = "Unknown";
        var tpm = "Unknown";

        try
        {
            secureBoot = RunPowerShellCapture("Confirm-SecureBootUEFI")?.Trim() ?? "Unknown";
        }
        catch
        {
            secureBoot = "Unavailable";
        }

        try
        {
            tpm = RunPowerShellCapture("(Get-Tpm).TpmPresent")?.Trim() ?? "Unknown";
        }
        catch
        {
            tpm = "Unavailable";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:windowsdefender",
            UseShellExecute = true
        });

        Process.Start(new ProcessStartInfo
        {
            FileName = "msinfo32.exe",
            UseShellExecute = true
        });

        return $"Guided check launched. SecureBoot={secureBoot}, TPM={tpm}. Opened Windows Security and System Information for verification.";
    }

    private static string? RunPowerShellCapture(string command)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }
}
