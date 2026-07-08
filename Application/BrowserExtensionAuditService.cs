using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record BrowserExtensionInfo(string Browser, string Profile, string Name, string Version, string Id)
{
    public string Label => $"{Name} {Version}";
    public string DetailLabel => $"{Browser} · {Profile} · {Id}";
}

/// <summary>
/// Inventaire des extensions de navigateur installées (Chrome/Edge : dossiers
/// Extensions des profils ; Firefox : extensions.json). Lecture seule — le but
/// est la visibilité : les extensions sont un vecteur d'infection fréquent et
/// beaucoup d'utilisateurs ignorent ce qui est installé.
/// </summary>
public sealed class BrowserExtensionAuditService
{
    public IReadOnlyList<BrowserExtensionInfo> ScanAll()
    {
        var results = new List<BrowserExtensionInfo>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        ScanChromiumBrowser(results, "Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"));
        ScanChromiumBrowser(results, "Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"));
        ScanFirefox(results, Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"));

        return results
            .OrderBy(e => e.Browser)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ScanChromiumBrowser(List<BrowserExtensionInfo> results, string browser, string userDataRoot)
    {
        if (!Directory.Exists(userDataRoot))
            return;

        try
        {
            var profiles = Directory.EnumerateDirectories(userDataRoot)
                .Where(d => Path.GetFileName(d).Equals("Default", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));

            foreach (var profileDir in profiles.Take(10))
            {
                var extensionsRoot = Path.Combine(profileDir, "Extensions");
                if (!Directory.Exists(extensionsRoot))
                    continue;

                foreach (var extensionDir in Directory.EnumerateDirectories(extensionsRoot).Take(200))
                {
                    var versionDir = Directory.EnumerateDirectories(extensionDir)
                        .OrderByDescending(Path.GetFileName)
                        .FirstOrDefault();
                    if (versionDir is null)
                        continue;

                    var manifestPath = Path.Combine(versionDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                        continue;

                    try
                    {
                        var name = ResolveChromiumExtensionName(versionDir, File.ReadAllText(manifestPath));
                        if (name is null)
                            continue;

                        results.Add(new BrowserExtensionInfo(
                            browser,
                            Path.GetFileName(profileDir),
                            name,
                            Path.GetFileName(versionDir).Split('_')[0],
                            Path.GetFileName(extensionDir)));
                    }
                    catch
                    {
                        // Manifest illisible : extension ignorée.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Audit des extensions {browser} impossible", ex);
        }
    }

    /// <summary>
    /// Résout le nom depuis le manifest ; les noms « __MSG_clé__ » sont
    /// traduits via _locales/{default_locale}/messages.json.
    /// </summary>
    private static string? ResolveChromiumExtensionName(string versionDir, string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;
        var rawName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(rawName))
            return null;

        if (!rawName.StartsWith("__MSG_", StringComparison.Ordinal))
            return rawName;

        var defaultLocale = root.TryGetProperty("default_locale", out var localeProp) ? localeProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(defaultLocale))
            return rawName;

        var messagesPath = Path.Combine(versionDir, "_locales", defaultLocale, "messages.json");
        if (!File.Exists(messagesPath))
            return rawName;

        return ResolveLocalizedName(rawName, File.ReadAllText(messagesPath)) ?? rawName;
    }

    /// <summary>Partie pure et testable : « __MSG_appName__ » + messages.json → nom traduit.</summary>
    internal static string? ResolveLocalizedName(string msgToken, string messagesJson)
    {
        var key = msgToken.Trim('_');
        if (!key.StartsWith("MSG_", StringComparison.Ordinal))
            return null;
        key = key["MSG_".Length..];

        try
        {
            using var doc = JsonDocument.Parse(messagesJson);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    && property.Value.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }
            }
        }
        catch
        {
            // messages.json invalide : on gardera le token brut.
        }

        return null;
    }

    private static void ScanFirefox(List<BrowserExtensionInfo> results, string profilesRoot)
    {
        if (!Directory.Exists(profilesRoot))
            return;

        try
        {
            foreach (var profileDir in Directory.EnumerateDirectories(profilesRoot).Take(10))
            {
                var extensionsJson = Path.Combine(profileDir, "extensions.json");
                if (!File.Exists(extensionsJson))
                    continue;

                results.AddRange(ParseFirefoxExtensions(File.ReadAllText(extensionsJson), Path.GetFileName(profileDir)));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Audit des extensions Firefox impossible", ex);
        }
    }

    /// <summary>Partie pure et testable : extensions.json de Firefox → liste (extensions actives, hors modules système).</summary>
    internal static IReadOnlyList<BrowserExtensionInfo> ParseFirefoxExtensions(string extensionsJson, string profileName)
    {
        var results = new List<BrowserExtensionInfo>();

        try
        {
            using var doc = JsonDocument.Parse(extensionsJson);
            if (!doc.RootElement.TryGetProperty("addons", out var addons))
                return results;

            foreach (var addon in addons.EnumerateArray())
            {
                var type = addon.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type != "extension")
                    continue;

                var location = addon.TryGetProperty("location", out var loc) ? loc.GetString() : null;
                if (location is not null && location.Contains("builtin", StringComparison.OrdinalIgnoreCase))
                    continue; // extensions systeme Mozilla, pas installées par l'utilisateur.

                var name = addon.TryGetProperty("defaultLocale", out var dl) && dl.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var version = addon.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                var id = addon.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                results.Add(new BrowserExtensionInfo("Firefox", profileName, name, version, id));
            }
        }
        catch
        {
            // Fichier invalide : liste partielle.
        }

        return results;
    }
}
