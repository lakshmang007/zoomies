using System.Net.Http;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Zoomies.Services;

/// <summary>
/// Keeps an installed copy up to date from the project's GitHub Releases. An update downloads quietly and is applied
/// the next time Zoomies starts (or straight away with "Restart to update"). Only the release list is fetched;
/// nothing about the user's files is sent. A copy that wasn't installed (a developer build) never checks.
/// </summary>
internal static class Updater
{
    public const string RepoUrl = "https://github.com/lakshmang007/zoomies";

    private static DispatcherTimer? _timer;

    /// <summary>True when running from the installer's copy, the only kind that can update itself.</summary>
    public static bool IsInstalled => Create() is { IsInstalled: true };

    /// <summary>A downloaded version waiting to be applied, if any.</summary>
    public static string? PendingVersion => Create()?.UpdatePendingRestart?.Version.ToString();

    /// <summary>Checks GitHub for a newer version and downloads it. Returns that version, or null when up to date.</summary>
    public static async Task<string?> CheckAndDownloadAsync()
    {
        var manager = Create();
        if (manager is not { IsInstalled: true }) return null;
        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null) return null;
        await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
        return update.TargetFullRelease.Version.ToString();
    }

    /// <summary>Hands the downloaded update to the updater, which applies it once Zoomies has exited, then reopens it.</summary>
    public static bool PrepareRestart()
    {
        var manager = Create();
        if (manager?.UpdatePendingRestart is not { } pending) return false;
        manager.WaitExitThenApplyUpdates(pending, silent: true, restart: true);
        return true;
    }

    /// <summary>
    /// A check a few minutes after launch, so it never competes with startup, then one a day while the app stays open.
    /// </summary>
    public static void StartBackgroundChecks(Func<bool> enabled, Action<string> downloaded)
    {
        if (_timer is not null || !IsInstalled) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(3) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromDays(1);
            if (!enabled() || PendingVersion is not null) return;
            try
            {
                if (await CheckAndDownloadAsync() is { } version) downloaded(version);
            }
            catch (HttpRequestException) { } // offline: try again tomorrow
            catch (Exception ex) { Log.Error(ex, "Update check"); }
        };
        _timer.Start();
    }

    private static UpdateManager? Create()
    {
        try { return new UpdateManager(new GithubSource(RepoUrl, null, false)); }
        catch (Exception ex) { Log.Error(ex, "Updater"); return null; }
    }
}
