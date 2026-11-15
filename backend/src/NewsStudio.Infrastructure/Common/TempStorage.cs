using Microsoft.Extensions.Configuration;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Single source of truth for where temporary media (audio, subtitles, MP4) lives.
///
/// The API and the Worker are separate processes with different working directories. A relative
/// setting such as "./tmp/newsstudio" used to resolve to NewsStudio.Worker/tmp when the Worker
/// rendered a video and to NewsStudio.Api/tmp when the API tried to serve it — so finished videos
/// came back as 404 (empty player, 0:00). This helper always returns the same absolute folder for
/// both processes.
/// </summary>
public static class TempStorage
{
    private const string DefaultRelativePath = "./tmp/newsstudio";

    /// <summary>
    /// Absolute temp-storage root. Reads Storage:TempStoragePath (preferred) or the legacy
    /// Storage:TempPath. Relative values are resolved against the backend folder (the one holding
    /// NewsStudio.sln) so API and Worker agree; outside a source checkout (e.g. Docker) they fall
    /// back to the current directory.
    /// </summary>
    public static string GetRoot(IConfiguration configuration)
    {
        var configured = configuration["Storage:TempStoragePath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = configuration["Storage:TempPath"];
        if (string.IsNullOrWhiteSpace(configured)) configured = DefaultRelativePath;

        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        var baseDir = FindBackendDirectory() ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, configured));
    }

    private static string? FindBackendDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NewsStudio.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
