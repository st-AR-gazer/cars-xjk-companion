using System.Security.Cryptography;

namespace MoreCars.Companion;

internal sealed partial class ReleaseInstaller
{
    private static (string Factory, string Current, string Alias) ThumbnailPaths(string carId)
    {
        if (!VehicleArchives.TryGetValue(carId, out var archive)) throw new InvalidDataException("Unknown car thumbnail.");
        var stem = archive[..^4];
        return ($"{stem}_Icon_Factory.dds", $"{stem}_Icon_Current.dds", $"{stem}_Icon.dds");
    }

    // Older installations have no icon sidecars until Repair files installs the
    // new release. A partial thumbnail set is an error, not a reason to adopt it.
    private async Task<bool> ValidateThumbnailsAsync(OwnershipManifest ownership, string carId, CancellationToken token)
    {
        var paths = ThumbnailPaths(carId);
        var names = new[] { paths.Factory, paths.Current, paths.Alias };
        var owned = names.Select(name => ownership.Files.SingleOrDefault(file =>
            StringComparer.OrdinalIgnoreCase.Equals(file.LogicalPath, name))).ToArray();
        if (owned.All(file => file is null)) return false;
        if (owned.Any(file => file is null)) throw new IOException("This car has an incomplete thumbnail set. Repair files first.");
        for (var index = 0; index < names.Length; index++)
        {
            var file = owned[index]!;
            var destination = ResolveManagedPath(file.LogicalPath);
            if (!File.Exists(destination)) throw new IOException("A managed car thumbnail is missing. Repair files first.");
            var hash = await Sha256FileAsync(destination, token);
            var size = new FileInfo(destination).Length;
            var overlay = ownership.Overlays.SingleOrDefault(item =>
                StringComparer.OrdinalIgnoreCase.Equals(item.LogicalPath, file.LogicalPath));
            if (index == 0 && overlay is not null)
                throw new IOException("The factory car thumbnail has an unexpected overlay.");
            if (!(size == file.ByteSize && hash == file.Sha256) &&
                !(overlay is not null && size == overlay.ReplacementByteSize && hash == overlay.ReplacementSha256))
                throw new IOException($"A car thumbnail has unknown changes: {file.LogicalPath}. It was preserved.");
        }
        return true;
    }

    internal async Task SyncThumbnailsAsync(string carId, byte[]? skinIcon, string skinId, CancellationToken token)
    {
        var ownership = LoadOwnership() ?? throw new InvalidOperationException("Install cars before updating thumbnails.");
        if (!await ValidateThumbnailsAsync(ownership, carId, token)) return;
        var paths = ThumbnailPaths(carId);
        var factory = ownership.Files.Single(file => file.LogicalPath == paths.Factory);
        var factoryBytes = await File.ReadAllBytesAsync(ResolveManagedPath(paths.Factory), token);
        if (factoryBytes.Length != factory.ByteSize || Convert.ToHexString(SHA256.HashData(factoryBytes)).ToLowerInvariant() != factory.Sha256)
            throw new IOException("The factory car thumbnail changed unexpectedly.");
        var target = skinIcon ?? factoryBytes;
        var targetHash = Convert.ToHexString(SHA256.HashData(target)).ToLowerInvariant();
        foreach (var logicalPath in new[] { paths.Current, paths.Alias })
        {
            token.ThrowIfCancellationRequested();
            var owned = ownership.Files.Single(file => file.LogicalPath == logicalPath);
            var destination = ResolveManagedPath(logicalPath);
            var oldOverlay = ownership.Overlays.SingleOrDefault(file => file.LogicalPath == logicalPath);
            var currentHash = await Sha256FileAsync(destination, token);
            var currentSize = new FileInfo(destination).Length;
            if (currentHash != targetHash || currentSize != target.Length)
            {
                var partial = destination + ".morecars-icon.partial";
                var backup = destination + ".morecars-icon.backup";
                RequireNoLinks(partial);
                RequireNoLinks(backup);
                if (File.Exists(partial) || File.Exists(backup))
                    throw new IOException("An interrupted thumbnail update needs inspection before changing this car.");
                await File.WriteAllBytesAsync(partial, target, token);
                try
                {
                    File.Move(destination, backup);
                    File.Move(partial, destination);
                    ownership.Overlays.RemoveAll(file => file.LogicalPath == logicalPath);
                    if (targetHash != owned.Sha256 || target.Length != owned.ByteSize)
                        ownership.Overlays.Add(new OwnershipOverlay
                        {
                            LogicalPath = logicalPath, Kind = "community-skin-icon", Reference = skinId,
                            BaseSha256 = owned.Sha256, BaseByteSize = owned.ByteSize,
                            ReplacementSha256 = targetHash, ReplacementByteSize = target.Length,
                            AppliedAt = DateTimeOffset.UtcNow.ToString("O")
                        });
                    ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    SaveOwnership(ownership);
                    File.Delete(backup);
                }
                catch
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    if (File.Exists(backup)) File.Move(backup, destination);
                    ownership.Overlays.RemoveAll(file => file.LogicalPath == logicalPath);
                    if (oldOverlay is not null) ownership.Overlays.Add(oldOverlay);
                    SaveOwnership(ownership);
                    throw;
                }
                finally { DeleteIfExists(partial); }
            }
            else if (targetHash == owned.Sha256 && oldOverlay is not null)
            {
                ownership.Overlays.Remove(oldOverlay);
                ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                SaveOwnership(ownership);
            }
            else if (oldOverlay is not null && oldOverlay.Reference != skinId)
            {
                ownership.Overlays[ownership.Overlays.IndexOf(oldOverlay)] = new OwnershipOverlay
                {
                    LogicalPath = oldOverlay.LogicalPath, Kind = oldOverlay.Kind, Reference = skinId,
                    BaseSha256 = oldOverlay.BaseSha256, BaseByteSize = oldOverlay.BaseByteSize,
                    ReplacementSha256 = oldOverlay.ReplacementSha256, ReplacementByteSize = oldOverlay.ReplacementByteSize,
                    AppliedAt = DateTimeOffset.UtcNow.ToString("O")
                };
                ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                SaveOwnership(ownership);
            }
        }
    }
}
