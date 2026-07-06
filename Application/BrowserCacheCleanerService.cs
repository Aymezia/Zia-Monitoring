namespace ZiaMonitoring_App.Application;

public sealed class BrowserCacheCleanerService
{
    private static readonly (string Browser, string[] Paths)[] BrowserCachePaths =
    [
        ("Chrome", [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Default\Code Cache"),
        ]),
        ("Edge", [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data\Default\Code Cache"),
        ]),
        ("Firefox", [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Mozilla\Firefox\Profiles"),
        ]),
    ];

    public record CleanResult(string Browser, long FreedBytes, int DeletedFiles, string? Error);

    public IReadOnlyList<CleanResult> Clean()
    {
        var results = new List<CleanResult>();

        foreach (var (browser, paths) in BrowserCachePaths)
        {
            long freed = 0;
            int deleted = 0;
            string? error = null;

            try
            {
                foreach (var basePath in paths)
                {
                    if (!Directory.Exists(basePath))
                        continue;

                    // For Firefox, enumerate profile cache2 folders
                    var searchRoot = browser == "Firefox"
                        ? Directory.EnumerateDirectories(basePath, "*.default*")
                              .SelectMany(p => new[] {
                                  Path.Combine(p, "cache2", "entries"),
                                  Path.Combine(p, "cache2")
                              })
                              .Where(Directory.Exists)
                        : new[] { basePath }.AsEnumerable();

                    foreach (var dir in searchRoot)
                    {
                        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(50_000))
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                freed += info.Length;
                                info.Delete();
                                deleted++;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            results.Add(new CleanResult(browser, freed, deleted, error));
        }

        return results;
    }

    public record CacheSizeInfo(string Browser, long TotalBytes, bool Exists);

    public IReadOnlyList<CacheSizeInfo> EstimateSizes()
    {
        var results = new List<CacheSizeInfo>();

        foreach (var (browser, paths) in BrowserCachePaths)
        {
            long total = 0;
            bool exists = false;

            foreach (var path in paths)
            {
                if (!Directory.Exists(path))
                    continue;

                exists = true;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(10_000))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
            }

            results.Add(new CacheSizeInfo(browser, total, exists));
        }

        return results;
    }
}
