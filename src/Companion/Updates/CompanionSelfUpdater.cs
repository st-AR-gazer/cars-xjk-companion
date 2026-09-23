using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoreCars.Companion;

internal sealed record CompanionUpdateDownload(string Version, string FileName, string Sha256, long Bytes);

internal static class CompanionSelfUpdater
{
    private const int MaxFeedBytes = 128 * 1024;
    private const long MaxDownloadBytes = 256L * 1024 * 1024;

    internal static CompanionUpdateDownload ParseDownload(ReadOnlyMemory<byte> feed, string currentVersion)
    {
        if (feed.Length is < 1 or > MaxFeedBytes)
            throw new InvalidDataException("The companion update feed has an unsafe size.");
        using var document = JsonDocument.Parse(feed);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || ReadString(root, "schema") != "morecars.companion-updates.v1" ||
            !TryVersion(ReadString(root, "version"), out var available) ||
            !TryVersion(currentVersion, out var installed) || available <= installed ||
            !root.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Object ||
            !downloads.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The companion update feed does not describe a newer Windows build.");

        var file = ReadString(windows, "file");
        var hash = ReadString(windows, "sha256");
        if (!Regex.IsMatch(file, @"^MoreCarsCompanion-[A-Za-z0-9][A-Za-z0-9.-]{0,90}\.exe$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(hash, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant) ||
            !windows.TryGetProperty("bytes", out var size) || size.ValueKind != JsonValueKind.Number ||
            !size.TryGetInt64(out var bytes) ||
            bytes is < 1 or > MaxDownloadBytes)
            throw new InvalidDataException("The companion update download details are invalid.");
        return new CompanionUpdateDownload(available.ToString(3), file, hash, bytes);
    }

    internal static async Task StartAsync(CompanionSettings settings, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Automatic companion updates are currently available on Windows.");
        using var api = new CompanionApi(settings);
        var download = ParseDownload(await api.DownloadAsync("/downloads/companion-release.json", token), CompanionBuild.Version);
        Directory.CreateDirectory(CompanionStorage.CacheDirectory);
        var path = Path.Combine(CompanionStorage.CacheDirectory,
            "MoreCarsCompanion-update-" + Guid.NewGuid().ToString("N") + ".exe");
        ReleaseInstaller.RequireNoLinks(path);
        try
        {
            await api.DownloadToFileAsync("/downloads/" + download.FileName, path, token);
            if (new FileInfo(path).Length != download.Bytes)
                throw new InvalidDataException("The companion update size did not match the release feed.");
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(actual, download.Sha256))
                throw new InvalidDataException("The companion update hash did not match the release feed.");
            var start = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = CompanionStorage.CacheDirectory };
            start.ArgumentList.Add("--update-from-notice");
            using var process = Process.Start(start);
            if (process is null)
                throw new IOException("Windows did not start the downloaded companion update.");
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private static string ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static bool TryVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!Regex.IsMatch(text, "^(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})\\.(0|[1-9][0-9]{0,5})$", RegexOptions.CultureInvariant) ||
            !Version.TryParse(text, out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }
}
