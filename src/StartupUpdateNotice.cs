using System.Text;
using System.Text.Json;

namespace MoreCars.Companion;

internal sealed record StartupUpdateNotice(bool CompanionUpdate, bool CarsNeedRepair, string Title, string Details)
{
    internal bool HasModifiedCars { get; init; }
    internal string WebsiteAction => CompanionUpdate ? "update" : "repair";
    internal string InstallLabel => CompanionUpdate ?
        OperatingSystem.IsWindows() ? "Install companion update" : "Review companion update" :
        HasModifiedCars ? "Review modified files" : "Install car update";
}

internal static class StartupUpdateProbe
{
    private const int MaxFeedBytes = 128 * 1024;

    internal static async Task<StartupUpdateNotice?> CheckAsync(CompanionSettings settings, CancellationToken token)
    {
        if (!settings.GameConfirmed || !CompanionGame.IsRoot(settings.TrackmaniaRoot) || settings.DeviceToken.Length == 0)
            return null;

        using var api = new CompanionApi(settings);
        var notes = "";
        var currentReleaseNotes = "";
        var newerCompanion = false;
        try
        {
            var feed = await api.DownloadAsync("/downloads/companion-release.json", token);
            if (feed.Length is > 0 and <= MaxFeedBytes)
            {
                (newerCompanion, notes) = ParseCompanionNotes(feed, CompanionBuild.Version);
                currentReleaseNotes = ParseCurrentReleaseNotes(feed, CompanionBuild.Version);
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidDataException)
        {
        }

        InstallationStatus? cars = null;
        try { cars = await new ReleaseInstaller(settings, api).InspectAsync(token, recover: false); }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException)
        {
        }
        var repair = cars is { Installed: false };
        var modified = repair ? cars!.BlockingModifications : 0;
        var adoptable = repair ? cars!.AdoptableMatches : 0;
        var newerCars = repair && cars!.ReleaseId.Length > 0 &&
            !StringComparer.Ordinal.Equals(cars.ReleaseId, ReleaseInstaller.PublishedReleaseId);
        if (!newerCompanion && !repair) return null;

        var details = new StringBuilder();
        if (newerCompanion) details.AppendLine(notes.TrimEnd());
        if (repair)
        {
            if (details.Length > 0) details.AppendLine().AppendLine();
            var missing = cars!.Files.Count(file => file.State == "missing") + cars.UnownedMissingFiles;
            details.AppendLine(cars.Files.Count == 0
                ? "Cars are not installed in the selected Trackmania folder."
                : newerCars ? "A newer managed car release is available."
                : "Your managed cars do not match this companion's current car release.");
            if (missing > 0) details.AppendLine($"Missing managed files: {missing}.");
            if (adoptable > 0) details.AppendLine($"Files already matching this release: {adoptable}. The companion will adopt them without replacing them.");
            if (modified > 0) details.AppendLine($"Files with local changes: {modified}. They will be preserved; review them before installing a car update.");
            details.AppendLine($"Current car release: {ReleaseInstaller.PublishedReleaseId}.");
            if (newerCars && currentReleaseNotes.Length > 0)
                details.AppendLine().AppendLine("Latest release notes:").AppendLine(currentReleaseNotes.TrimEnd());
        }
        details.AppendLine().Append(newerCompanion
            ? OperatingSystem.IsWindows()
                ? "Install the companion update now, or choose Not now to keep playing. Trackmania can stay open."
                : "Review the companion update on the Cars website, or choose Not now to keep playing."
            : modified > 0
                ? "Review the changed files before repairing. No car files will be replaced from this notice."
                : "Install the managed car files now, or choose Not now to keep playing. New car files will load on your next Trackmania start.");
        var title = newerCompanion && repair ? "Companion and cars need an update" :
            newerCompanion ? "A companion update is available" :
            modified > 0 ? "Your car files have local changes" :
            newerCars ? "A car update is available" : "Your cars need repair";
        return new StartupUpdateNotice(newerCompanion, repair, title, details.ToString()) { HasModifiedCars = modified > 0 };
    }

    internal static (bool Newer, string Notes) ParseCompanionNotes(ReadOnlyMemory<byte> feed, string installedVersion)
    {
        using var document = JsonDocument.Parse(feed);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || ReadText(root, "schema", 64) != "morecars.companion-updates.v1" ||
            !TryVersion(ReadText(root, "version", 24), out var available) ||
            !TryVersion(installedVersion, out var installed) || available <= installed ||
            !root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return (false, "");

        var details = new StringBuilder();
        var shown = 0;
        foreach (var release in releases.EnumerateArray())
        {
            if (shown >= 8 || release.ValueKind != JsonValueKind.Object ||
                !TryVersion(ReadText(release, "version", 24), out var version) || version <= installed)
                continue;
            var title = ReadText(release, "title", 160);
            var summary = ReadText(release, "summary", 400);
            if (title.Length == 0 || summary.Length == 0) continue;
            if (shown++ > 0) details.AppendLine();
            details.AppendLine($"{title} · {version}");
            details.AppendLine(summary);
            if (!release.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            var changeCount = 0;
            foreach (var change in changes.EnumerateArray())
            {
                if (changeCount++ >= 12 || change.ValueKind != JsonValueKind.Object) break;
                var changeTitle = ReadText(change, "title", 100);
                var description = ReadText(change, "description", 400);
                if (changeTitle.Length > 0 && description.Length > 0)
                    details.AppendLine($"• {changeTitle}: {description}");
            }
        }
        return shown > 0 ? (true, details.ToString()) : (false, "");
    }

    internal static string ParseCurrentReleaseNotes(ReadOnlyMemory<byte> feed, string currentVersion)
    {
        using var document = JsonDocument.Parse(feed);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || ReadText(root, "schema", 64) != "morecars.companion-updates.v1" ||
            !root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return "";
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || ReadText(release, "version", 24) != currentVersion) continue;
            var title = ReadText(release, "title", 160);
            var summary = ReadText(release, "summary", 400);
            if (title.Length == 0 || summary.Length == 0) return "";
            var details = new StringBuilder().AppendLine($"{title} · {currentVersion}").AppendLine(summary);
            if (release.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var change in changes.EnumerateArray())
                {
                    if (count++ >= 12 || change.ValueKind != JsonValueKind.Object) break;
                    var changeTitle = ReadText(change, "title", 100);
                    var description = ReadText(change, "description", 400);
                    if (changeTitle.Length > 0 && description.Length > 0)
                        details.AppendLine($"• {changeTitle}: {description}");
                }
            }
            return details.ToString();
        }
        return "";
    }

    private static string ReadText(JsonElement parent, string key, int limit)
    {
        if (!parent.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString()?.Trim() ?? "";
        return text.Length <= limit && !text.Any(char.IsControl) ? text : "";
    }

    private static bool TryVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})$"))
            return false;
        if (!Version.TryParse(value, out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }
}

internal sealed class GameStartUpdateMonitor(
    Func<bool> isRunning,
    Func<CancellationToken, Task<StartupUpdateNotice?>> check,
    Func<StartupUpdateNotice, CancellationToken, Task> show)
{
    private bool _wasRunning;

    internal async Task TickAsync(CancellationToken token)
    {
        var running = isRunning();
        if (!running || _wasRunning) { _wasRunning = running; return; }
        _wasRunning = true;
        try
        {
            var notice = await check(token);
            if (notice is not null && isRunning()) await show(notice, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { }
    }
}
