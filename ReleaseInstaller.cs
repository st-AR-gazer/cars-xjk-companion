using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MoreCars.Companion;

internal sealed partial class ReleaseInstaller(CompanionSettings settings, CompanionApi api)
{
    internal const string PublishedReleaseId = "legacy-cars-2026-08-23.1";
    internal const long PublishedReleaseByteSize = 4493;
    internal const string PublishedReleaseSha256 = "fd4f10cc4c498e4e7d1c358d179f20b28355c2de24e4f8057a917ff59289bf23";
    private const string OwnershipRelativePath = ".morecars/ownership-v1.json";
    private const string JournalRelativePath = ".morecars/install-transaction.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, string> VehicleArchives = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bay"] = "GameData/Vehicles/Skins/BayCar.zip",
        ["canyon"] = "GameData/Vehicles/Skins/HDModelPainted_CanyonCar.zip",
        ["coast"] = "GameData/Vehicles/Skins/CoastCar.zip",
        ["desert"] = "GameData/Vehicles/Skins/DesertCar.zip",
        ["island"] = "GameData/Vehicles/Skins/IslandCar.zip",
        ["lagoon"] = "GameData/Vehicles/Skins/HDModelPainted_LagoonCar.zip",
        ["rally"] = "GameData/Vehicles/Skins/RallyCar.zip",
        ["snow"] = "GameData/Vehicles/Skins/SnowCar.zip",
        ["stadium"] = "GameData/Vehicles/Skins/StadiumCar.zip",
        ["traffic"] = "GameData/Vehicles/Skins/TrafficCar.zip",
        ["valley"] = "GameData/Vehicles/Skins/ValleyCar.zip"
    };

    public async Task InstallAsync(Func<int, string, Task> report, CancellationToken cancellationToken)
    {
        ValidateRoot();
        await RecoverInstallAsync(cancellationToken);
        var release = await FetchReleaseAsync(cancellationToken);
        var files = await FetchPackageFilesAsync(release, cancellationToken);
        var ownership = LoadOwnership() ?? NewOwnership();
        var installedAt = ownership.InstalledAt;
        ownership.ReleaseId = PublishedReleaseId;
        ownership.ReleaseManifestSha256 = PublishedReleaseSha256;

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var progress = 50 + (int)Math.Floor(850d * index / Math.Max(files.Count, 1));
            await report(progress, $"Installing {index + 1}/{files.Count}: {file.File.LogicalPath}");
            await InstallFileAsync(file, ownership, cancellationToken);
        }

        ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        SaveOwnership(ownership);
        await report(950, $"Verified {files.Count} managed vehicle files.");
        _ = installedAt;
    }

    public async Task<CleanupResult> CleanupAsync(Func<int, string, Task> report, CancellationToken cancellationToken)
    {
        ValidateRoot();
        await RecoverInstallAsync(cancellationToken);
        var ownership = LoadOwnership();
        if (ownership is null)
        {
            await report(50, "Looking for an exact legacy release to adopt safely.");
            ownership = await AdoptCurrentReleaseAsync(cancellationToken);
        }
        if (ownership is null || ownership.Files.Count == 0)
            return new CleanupResult(0, 0, []);

        var conflicts = new List<string>();
        var removed = 0;
        var missing = 0;
        var retained = new List<OwnedFile>();
        for (var index = 0; index < ownership.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = ownership.Files[index];
            await report(50 + (int)Math.Floor(850d * index / ownership.Files.Count), $"Checking {file.LogicalPath}");
            var destination = ResolveManagedPath(file.LogicalPath);
            if (!File.Exists(destination))
            {
                missing++;
                continue;
            }
            var currentSize = new FileInfo(destination).Length;
            var currentHash = await Sha256FileAsync(destination, cancellationToken);
            var overlay = ownership.Overlays.SingleOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, file.LogicalPath));
            var matchesBase = currentSize == file.ByteSize && currentHash == file.Sha256;
            var matchesOverlay = overlay is not null && currentSize == overlay.ReplacementByteSize && currentHash == overlay.ReplacementSha256;
            if (!matchesBase && !matchesOverlay)
            {
                conflicts.Add(file.LogicalPath);
                retained.Add(file);
                continue;
            }
            File.Delete(destination);
            removed++;
            PruneEmptyParents(Path.GetDirectoryName(destination));
        }

        ownership.Files.Clear();
        ownership.Files.AddRange(retained);
        ownership.Overlays.RemoveAll(overlay => !retained.Any(file =>
            StringComparer.OrdinalIgnoreCase.Equals(file.LogicalPath, overlay.LogicalPath)));
        ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        if (retained.Count == 0) DeleteOwnershipFiles();
        else SaveOwnership(ownership);
        return new CleanupResult(removed, missing, conflicts);
    }

    public async Task<SkinApplicationResult> ApplySkinAsync(string carId, string skinId, Func<int, string, Task> report, CancellationToken cancellationToken)
    {
        ValidateRoot();
        if (!VehicleArchives.TryGetValue(carId, out var logicalPath) || !SkinIdPattern().IsMatch(skinId))
            throw new InvalidDataException("The skin command is invalid.");
        var ownership = LoadOwnership() ?? throw new InvalidOperationException("Install the managed vehicle release before applying a skin.");
        var owned = ownership.Files.SingleOrDefault(file =>
            StringComparer.OrdinalIgnoreCase.Equals(file.LogicalPath, logicalPath))
            ?? throw new InvalidOperationException("The selected car's vehicle archive is not owned by this installation.");
        var destination = ResolveManagedPath(logicalPath);
        var currentHash = File.Exists(destination) ? await Sha256FileAsync(destination, cancellationToken) : "";
        var previousOverlay = ownership.Overlays.SingleOrDefault(overlay =>
            StringComparer.OrdinalIgnoreCase.Equals(overlay.LogicalPath, logicalPath));
        var currentIsBase = File.Exists(destination) && new FileInfo(destination).Length == owned.ByteSize && currentHash == owned.Sha256;
        var currentIsOverlay = previousOverlay is not null && File.Exists(destination) &&
            new FileInfo(destination).Length == previousOverlay.ReplacementByteSize && currentHash == previousOverlay.ReplacementSha256;
        if (!currentIsBase && !currentIsOverlay)
            throw new IOException("The active vehicle archive is missing or contains unknown modifications; it was preserved.");

        var cacheRoot = Path.Combine(CompanionStorage.CacheDirectory, settings.DeviceId);
        Directory.CreateDirectory(cacheRoot);
        var baseCache = Path.Combine(cacheRoot, owned.Sha256 + ".zip");
        if (!File.Exists(baseCache) || new FileInfo(baseCache).Length != owned.ByteSize ||
            await Sha256FileAsync(baseCache, cancellationToken) != owned.Sha256)
        {
            DeleteIfExists(baseCache);
            await report(100, "Downloading the verified factory vehicle archive.");
            await api.DownloadToFileAsync($"/dist/v1/blobs/sha256/{owned.Sha256}", baseCache, cancellationToken);
            if (new FileInfo(baseCache).Length != owned.ByteSize || await Sha256FileAsync(baseCache, cancellationToken) != owned.Sha256)
                throw new InvalidDataException("The factory vehicle archive failed verification.");
        }

        var liveryPath = Path.Combine(cacheRoot, $"skin-{carId}-{skinId}.zip");
        DeleteIfExists(liveryPath);
        await report(250, "Downloading the selected community livery.");
        await api.DownloadToFileAsync($"/api/v1/skins/{Uri.EscapeDataString(carId)}/{Uri.EscapeDataString(skinId)}/download", liveryPath, cancellationToken);
        var liveryInfo = new FileInfo(liveryPath);
        if (liveryInfo.Length is <= 0 or > 512 * 1024 * 1024) throw new InvalidDataException("The livery archive size is unsafe.");
        var liverySha256 = await Sha256FileAsync(liveryPath, cancellationToken);

        var partial = destination + ".morecars-skin.partial";
        var backup = destination + ".morecars-skin.backup";
        DeleteIfExists(partial);
        await report(500, "Composing the complete vehicle archive locally.");
        await SkinArchiveComposer.ComposeAsync(baseCache, liveryPath, partial, cancellationToken);
        var replacementInfo = new FileInfo(partial);
        var replacementSha256 = await Sha256FileAsync(partial, cancellationToken);
        var nextOverlay = new OwnershipOverlay
        {
            LogicalPath = logicalPath,
            Reference = skinId,
            BaseSha256 = owned.Sha256,
            BaseByteSize = owned.ByteSize,
            ReplacementSha256 = replacementSha256,
            ReplacementByteSize = replacementInfo.Length,
            AppliedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        ownership.Overlays.RemoveAll(overlay =>
            StringComparer.OrdinalIgnoreCase.Equals(overlay.LogicalPath, logicalPath));
        ownership.Overlays.Add(nextOverlay);
        ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        SaveOwnership(ownership);
        await report(800, "Replacing the inactive vehicle archive.");
        DeleteIfExists(backup);
        File.Move(destination, backup);
        try
        {
            File.Move(partial, destination);
            if (new FileInfo(destination).Length != nextOverlay.ReplacementByteSize ||
                await Sha256FileAsync(destination, cancellationToken) != nextOverlay.ReplacementSha256)
                throw new InvalidDataException("The composed vehicle archive failed verification after installation.");
            DeleteIfExists(backup);
        }
        catch
        {
            DeleteIfExists(destination);
            if (File.Exists(backup)) File.Move(backup, destination);
            ownership.Overlays.Remove(nextOverlay);
            if (previousOverlay is not null) ownership.Overlays.Add(previousOverlay);
            ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            SaveOwnership(ownership);
            throw;
        }
        return new SkinApplicationResult(liverySha256, liveryInfo.Length);
    }

    public async Task RestoreSkinAsync(string carId, Func<int, string, Task> report, CancellationToken cancellationToken)
    {
        ValidateRoot();
        if (!VehicleArchives.TryGetValue(carId, out var logicalPath)) throw new InvalidDataException("The car ID is invalid.");
        var ownership = LoadOwnership() ?? throw new InvalidOperationException("There is no managed installation to restore.");
        var owned = ownership.Files.SingleOrDefault(file =>
            StringComparer.OrdinalIgnoreCase.Equals(file.LogicalPath, logicalPath))
            ?? throw new InvalidOperationException("The selected car's vehicle archive is not owned.");
        var overlay = ownership.Overlays.SingleOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, logicalPath));
        if (overlay is null) return;
        var destination = ResolveManagedPath(logicalPath);
        if (!File.Exists(destination) || new FileInfo(destination).Length != overlay.ReplacementByteSize ||
            await Sha256FileAsync(destination, cancellationToken) != overlay.ReplacementSha256)
            throw new IOException("The active skinned archive contains unknown modifications; it was preserved.");
        var partial = destination + ".morecars-skin.partial";
        var backup = destination + ".morecars-skin.backup";
        DeleteIfExists(partial);
        await report(350, "Downloading the verified factory vehicle archive.");
        await api.DownloadToFileAsync($"/dist/v1/blobs/sha256/{owned.Sha256}", partial, cancellationToken);
        if (new FileInfo(partial).Length != owned.ByteSize || await Sha256FileAsync(partial, cancellationToken) != owned.Sha256)
            throw new InvalidDataException("The factory vehicle archive failed verification.");
        DeleteIfExists(backup);
        File.Move(destination, backup);
        try
        {
            File.Move(partial, destination);
            DeleteIfExists(backup);
            ownership.Overlays.Remove(overlay);
            ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            SaveOwnership(ownership);
        }
        catch
        {
            DeleteIfExists(destination);
            if (File.Exists(backup)) File.Move(backup, destination);
            throw;
        }
    }

    private async Task<ReleaseManifest> FetchReleaseAsync(CancellationToken cancellationToken)
    {
        var bytes = await api.DownloadAsync($"/dist/v1/releases/{PublishedReleaseId}/release.json", cancellationToken);
        VerifyBytes(bytes, PublishedReleaseByteSize, PublishedReleaseSha256, "release manifest");
        var release = JsonSerializer.Deserialize<ReleaseManifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The release manifest is empty.");
        if (release.Schema != "moretm20cars.release.v1" || release.ReleaseId != PublishedReleaseId || release.Packages.Count is < 1 or > 64)
            throw new InvalidDataException("The release manifest identity is invalid.");
        return release;
    }

    private async Task<List<ReleaseFile>> FetchPackageFilesAsync(ReleaseManifest release, CancellationToken cancellationToken)
    {
        var files = new List<ReleaseFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in release.Packages)
        {
            if (!SafeManifestPath().IsMatch(reference.ManifestPath) || reference.ManifestByteSize <= 0 || !Sha256Pattern().IsMatch(reference.ManifestSha256))
                throw new InvalidDataException("A release package reference is invalid.");
            var bytes = await api.DownloadAsync(
                $"/dist/v1/releases/{PublishedReleaseId}/{reference.ManifestPath}",
                cancellationToken);
            VerifyBytes(bytes, reference.ManifestByteSize, reference.ManifestSha256, $"package {reference.PackageId}");
            var package = JsonSerializer.Deserialize<PackageManifest>(bytes, JsonOptions)
                ?? throw new InvalidDataException("A package manifest is empty.");
            if (package.Schema != "moretm20cars.package.v1" || package.PackageId != reference.PackageId ||
                package.PackageVersion != reference.PackageVersion || package.CarId != reference.CarId)
                throw new InvalidDataException("A package manifest does not match its release reference.");
            foreach (var file in package.Files)
            {
                if (!IsManagedPath(file.LogicalPath) || !Sha256Pattern().IsMatch(file.Sha256) || file.ByteSize is <= 0 or > 1073741824)
                    throw new InvalidDataException($"Package file facts are invalid for {file.LogicalPath}.");
                if (!paths.Add(file.LogicalPath)) throw new InvalidDataException($"More than one package owns {file.LogicalPath}.");
                files.Add(new ReleaseFile(package.PackageId, package.PackageVersion, file));
            }
        }
        if (files.Count is < 1 or > 1024) throw new InvalidDataException("The release file count is unsafe.");
        return files.OrderBy(file => file.File.LogicalPath, StringComparer.Ordinal).ToList();
    }

    private async Task InstallFileAsync(ReleaseFile releaseFile, OwnershipManifest ownership, CancellationToken cancellationToken)
    {
        var file = releaseFile.File;
        var destination = ResolveManagedPath(file.LogicalPath);
        var partial = destination + ".morecars.partial";
        var backup = destination + ".morecars.backup";
        var existing = ownership.Files.SingleOrDefault(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, file.LogicalPath));

        if (File.Exists(destination))
        {
            var size = new FileInfo(destination).Length;
            var hash = await Sha256FileAsync(destination, cancellationToken);
            if (size == file.ByteSize && hash == file.Sha256)
            {
                UpsertOwnership(ownership, releaseFile);
                SaveOwnership(ownership);
                return;
            }
            var overlay = ownership.Overlays.SingleOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, file.LogicalPath));
            var matchesOwned = existing is not null && size == existing.ByteSize && hash == existing.Sha256;
            var matchesOverlay = overlay is not null && size == overlay.ReplacementByteSize && hash == overlay.ReplacementSha256;
            if (!matchesOwned && !matchesOverlay)
                throw new IOException($"An unknown or modified file occupies {file.LogicalPath}; it was preserved.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        SaveJournal(new InstallJournal { LogicalPath = file.LogicalPath, Sha256 = file.Sha256, ByteSize = file.ByteSize });
        if (File.Exists(partial)) File.Delete(partial);
        await api.DownloadToFileAsync($"/dist/v1/blobs/sha256/{file.Sha256}", partial, cancellationToken);
        if (new FileInfo(partial).Length != file.ByteSize || await Sha256FileAsync(partial, cancellationToken) != file.Sha256)
        {
            File.Delete(partial);
            throw new InvalidDataException($"The downloaded bytes failed verification for {file.LogicalPath}.");
        }

        if (File.Exists(backup)) File.Delete(backup);
        if (File.Exists(destination)) File.Move(destination, backup);
        try
        {
            File.Move(partial, destination);
            if (new FileInfo(destination).Length != file.ByteSize || await Sha256FileAsync(destination, cancellationToken) != file.Sha256)
                throw new InvalidDataException($"The installed bytes failed verification for {file.LogicalPath}.");
            if (File.Exists(backup)) File.Delete(backup);
            ownership.Overlays.RemoveAll(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, file.LogicalPath));
            UpsertOwnership(ownership, releaseFile);
            ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            SaveOwnership(ownership);
            DeleteJournal();
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            if (File.Exists(backup)) File.Move(backup, destination);
            throw;
        }
    }

    private async Task RecoverInstallAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(settings.TrackmaniaRoot, JournalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<InstallJournal>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("The install journal is invalid.");
        if (journal.Schema != "morecars.install-transaction.v1" || !IsManagedPath(journal.LogicalPath))
            throw new InvalidDataException("The install journal is unsafe.");
        var destination = ResolveManagedPath(journal.LogicalPath);
        var partial = destination + ".morecars.partial";
        var backup = destination + ".morecars.backup";
        if (File.Exists(destination) && new FileInfo(destination).Length == journal.ByteSize &&
            await Sha256FileAsync(destination, cancellationToken) == journal.Sha256)
        {
            if (File.Exists(backup)) File.Delete(backup);
            if (File.Exists(partial)) File.Delete(partial);
            DeleteJournal();
            return;
        }
        if (File.Exists(destination)) File.Delete(destination);
        if (File.Exists(backup)) File.Move(backup, destination);
        if (File.Exists(partial)) File.Delete(partial);
        DeleteJournal();
    }

    private async Task<OwnershipManifest?> AdoptCurrentReleaseAsync(CancellationToken cancellationToken)
    {
        var release = await FetchReleaseAsync(cancellationToken);
        var files = await FetchPackageFilesAsync(release, cancellationToken);
        var ownership = NewOwnership();
        foreach (var releaseFile in files)
        {
            var destination = ResolveManagedPath(releaseFile.File.LogicalPath);
            if (!File.Exists(destination) || new FileInfo(destination).Length != releaseFile.File.ByteSize) continue;
            if (await Sha256FileAsync(destination, cancellationToken) != releaseFile.File.Sha256) continue;
            UpsertOwnership(ownership, releaseFile);
        }
        if (ownership.Files.Count == 0) return null;
        SaveOwnership(ownership);
        return ownership;
    }

    private OwnershipManifest NewOwnership()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return new OwnershipManifest
        {
            InstallationId = settings.DeviceId,
            ReleaseId = PublishedReleaseId,
            ReleaseManifestSha256 = PublishedReleaseSha256,
            InstalledAt = now,
            UpdatedAt = now
        };
    }

    private OwnershipManifest? LoadOwnership()
    {
        var path = Path.Combine(settings.TrackmaniaRoot, OwnershipRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        var manifest = JsonSerializer.Deserialize<OwnershipManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The ownership manifest is empty.");
        if (manifest.Schema != "morecars.ownership.v1" || manifest.Files.Count > 1024 || manifest.Overlays.Count > 32)
            throw new InvalidDataException("The ownership manifest is invalid.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!IsManagedPath(file.LogicalPath) || !Sha256Pattern().IsMatch(file.Sha256) || file.ByteSize is <= 0 or > 1073741824 || !paths.Add(file.LogicalPath))
                throw new InvalidDataException("The ownership manifest contains invalid file facts.");
        }
        return manifest;
    }

    private void SaveOwnership(OwnershipManifest ownership)
    {
        var path = Path.Combine(settings.TrackmaniaRoot, OwnershipRelativePath.Replace('/', Path.DirectorySeparatorChar));
        CompanionStorage.AtomicWrite(path, JsonSerializer.Serialize(ownership, JsonOptions));
    }

    private void SaveJournal(InstallJournal journal)
    {
        var path = Path.Combine(settings.TrackmaniaRoot, JournalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        CompanionStorage.AtomicWrite(path, JsonSerializer.Serialize(journal, JsonOptions));
    }

    private void DeleteJournal()
    {
        var path = Path.Combine(settings.TrackmaniaRoot, JournalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        DeleteIfExists(path);
        DeleteIfExists(path + ".partial");
        DeleteIfExists(path + ".backup");
    }

    private void DeleteOwnershipFiles()
    {
        var path = Path.Combine(settings.TrackmaniaRoot, OwnershipRelativePath.Replace('/', Path.DirectorySeparatorChar));
        DeleteIfExists(path);
        DeleteIfExists(path + ".partial");
        DeleteIfExists(path + ".backup");
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void UpsertOwnership(OwnershipManifest ownership, ReleaseFile releaseFile)
    {
        ownership.Files.RemoveAll(candidate =>
            StringComparer.OrdinalIgnoreCase.Equals(candidate.LogicalPath, releaseFile.File.LogicalPath));
        ownership.Files.Add(new OwnedFile
        {
            LogicalPath = releaseFile.File.LogicalPath,
            Sha256 = releaseFile.File.Sha256,
            ByteSize = releaseFile.File.ByteSize,
            PackageId = releaseFile.PackageId,
            PackageVersion = releaseFile.PackageVersion,
            Role = releaseFile.File.Role
        });
        ownership.Files.Sort((left, right) => StringComparer.Ordinal.Compare(left.LogicalPath, right.LogicalPath));
    }

    private string ResolveManagedPath(string logicalPath)
    {
        if (!IsManagedPath(logicalPath)) throw new InvalidDataException("A managed path is unsafe.");
        var root = Path.GetFullPath(settings.TrackmaniaRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A managed path escaped the Trackmania root.");
        return destination;
    }

    private void PruneEmptyParents(string? directory)
    {
        var vehiclesRoot = Path.GetFullPath(Path.Combine(settings.TrackmaniaRoot, "GameData", "Vehicles"));
        while (!string.IsNullOrWhiteSpace(directory) &&
               directory.StartsWith(vehiclesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private void ValidateRoot()
    {
        if (string.IsNullOrWhiteSpace(settings.TrackmaniaRoot) || !Directory.Exists(settings.TrackmaniaRoot) ||
            !Directory.Exists(Path.Combine(settings.TrackmaniaRoot, "GameData")))
            throw new DirectoryNotFoundException("Choose the Trackmania installation folder containing GameData.");
    }

    internal static bool IsManagedPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Contains('\\') || value.Contains(':') ||
            !value.StartsWith("GameData/Vehicles/", StringComparison.Ordinal)) return false;
        foreach (var segment in value.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                !SafeSegment().IsMatch(segment) || ReservedName().IsMatch(segment)) return false;
        }
        return true;
    }

    private static void VerifyBytes(byte[] bytes, long expectedSize, string expectedHash, string label)
    {
        if (bytes.LongLength != expectedSize || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedHash)
            throw new InvalidDataException($"The {label} failed its byte-size or SHA-256 pin.");
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^packages/[a-z0-9]+(?:-[a-z0-9]+)*/[0-9]+\\.[0-9]+\\.[0-9]+/package\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeManifestPath();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._ -]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSegment();

    [GeneratedRegex("^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();

    [GeneratedRegex("^[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkinIdPattern();

    private sealed record ReleaseFile(string PackageId, string PackageVersion, PackageFile File);
}

internal sealed record CleanupResult(int Removed, int Missing, IReadOnlyList<string> Conflicts);
internal sealed record SkinApplicationResult(string ArchiveSha256, long ByteSize);
