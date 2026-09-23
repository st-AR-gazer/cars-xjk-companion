using System.Text.Json;

namespace MoreCars.Companion;

internal sealed partial class ReleaseInstaller
{
    private string ResetJournalPath => Path.Combine(settings.TrackmaniaRoot, ".morecars", "skin-reset.json");
    private sealed record ResetFile(string LogicalPath, string BeforeHash, long BeforeSize, string AfterHash, long AfterSize);
    private sealed record ResetJournal(string BeforeOwnership, string AfterOwnership, List<ResetFile> Files);

    public async Task<int> RestoreSkinsAsync(string carId, Func<int, string, Task> report,
        CancellationToken cancellationToken, Func<Task>? beforeCommit = null, Func<PreparedSkinCommit, Task<bool>>? nativeCommit = null)
    {
        ValidateRoot();
        await RecoverInstallAsync(cancellationToken);
        if (carId.Length > 0 && !VehicleArchives.ContainsKey(carId))
            throw new InvalidDataException("This car has no packaged default skin.");
        var ownership = LoadOwnership() ?? throw new InvalidOperationException("Install cars before resetting their skins.");
        var before = JsonSerializer.Serialize(ownership, JsonOptions);
        await report(50, "Checking the packaged default skins.");
        var releaseFiles = await FetchPackageFilesAsync(await FetchReleaseAsync(cancellationToken), cancellationToken);
        var entries = new List<ResetFile>();
        foreach (var (id, logicalPath) in VehicleArchives)
        {
            if (carId.Length > 0 && id != carId) continue;
            var owned = ownership.Files.SingleOrDefault(file => file.LogicalPath == logicalPath);
            if (owned is null) continue; // Reset installed cars only; never adopt or install an unowned car.
            var next = releaseFiles.Single(file => file.File.LogicalPath == logicalPath);
            var destination = ResolveResetPath(logicalPath);
            var hash = File.Exists(destination) ? await Sha256FileAsync(destination, cancellationToken) : "";
            var size = File.Exists(destination) ? new FileInfo(destination).Length : 0;
            var overlay = ownership.Overlays.SingleOrDefault(file => file.LogicalPath == logicalPath);
            if (hash.Length > 0 && !(hash == owned.Sha256 && size == owned.ByteSize) &&
                !(hash == overlay?.ReplacementSha256 && size == overlay.ReplacementByteSize))
                throw new IOException("A car has unknown changes. Its files were preserved; no skins were reset.");
            entries.Add(new ResetFile(logicalPath, hash, size, next.File.Sha256, next.File.ByteSize));
            ownership.Overlays.RemoveAll(file => file.LogicalPath == logicalPath);
            UpsertOwnership(ownership, next);
        }
        if (entries.Count == 0) throw new InvalidOperationException("No installed managed cars were found to reset.");
        foreach (var entry in entries)
            await ValidateThumbnailsAsync(ownership, VehicleArchives.Single(car => car.Value == entry.LogicalPath).Key, cancellationToken);
        ownership.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        var journal = new ResetJournal(before, JsonSerializer.Serialize(ownership, JsonOptions), entries);
        try
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var destination = ResolveResetPath(entry.LogicalPath);
                var partial = destination + ".morecars-reset.partial";
                await report(100 + 550 * index / entries.Count, $"Preparing default skins · {index + 1} of {entries.Count}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                DeleteIfExists(partial);
                if (entry.BeforeHash == entry.AfterHash) File.Copy(destination, partial);
                else await api.DownloadToFileAsync($"/dist/v1/blobs/sha256/{entry.AfterHash}", partial, cancellationToken);
                if (!await MatchesResetFile(partial, entry.AfterHash, entry.AfterSize, cancellationToken))
                    throw new InvalidDataException("A default skin failed verification. No skins were reset.");
            }
            if (nativeCommit is not null && await nativeCommit(new PreparedSkinCommit(entries.Select(entry => new MoreCars.Native.NativeSkinReplacement(
                VehicleArchives.Single(car => car.Value == entry.LogicalPath).Key, ResolveResetPath(entry.LogicalPath) + ".morecars-reset.partial",
                entry.BeforeHash, entry.BeforeSize, entry.AfterHash, entry.AfterSize)).ToArray(), journal.BeforeOwnership, journal.AfterOwnership)))
            {
                foreach (var entry in entries)
                    await SyncThumbnailsAsync(VehicleArchives.Single(car => car.Value == entry.LogicalPath).Key, null, "", cancellationToken);
                return entries.Count;
            }
            if (beforeCommit is not null) await beforeCommit();
            foreach (var entry in entries)
            {
                var destination = ResolveResetPath(entry.LogicalPath);
                if (!await MatchesResetFile(destination, entry.BeforeHash, entry.BeforeSize, cancellationToken))
                    throw new IOException("A car changed while its default skin was prepared. No skins were reset.");
                if (File.Exists(destination + ".morecars-reset.backup"))
                    throw new IOException("A previous reset backup needs recovery before continuing.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            await report(800, $"Restoring {entries.Count} default skin{(entries.Count == 1 ? "" : "s")}.");
            CompanionStorage.AtomicWrite(ResetJournalPath, JsonSerializer.Serialize(journal, JsonOptions));
            // Keep every original until all replacements and the ownership record are committed.
            // Recovery rolls the whole set back if the process exits before that final write.
            foreach (var entry in entries)
            {
                var destination = ResolveResetPath(entry.LogicalPath);
                if (File.Exists(destination)) File.Move(destination, destination + ".morecars-reset.backup");
                File.Move(destination + ".morecars-reset.partial", destination);
            }
            SaveOwnership(ownership);
        }
        catch
        {
            await RecoverSkinResetAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (!File.Exists(ResetJournalPath)) CleanupResetFiles(entries, backups: false);
        }
        // A cleanup failure does not turn a committed reset into a failed command.
        // The next operation can finish removing the recorded backups.
        try { CleanupResetFiles(entries, backups: true); DeleteIfExists(ResetJournalPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        foreach (var entry in entries)
            await SyncThumbnailsAsync(VehicleArchives.Single(car => car.Value == entry.LogicalPath).Key, null, "", cancellationToken);
        return entries.Count;
    }

    private string ResolveResetPath(string logicalPath)
    {
        if (!VehicleArchives.Values.Contains(logicalPath)) throw new InvalidDataException("Invalid default skin path.");
        var destination = ResolveManagedPath(logicalPath);
        RequireNoLinks(destination + ".morecars-reset.partial");
        RequireNoLinks(destination + ".morecars-reset.backup");
        return destination;
    }

    private static async Task<bool> MatchesResetFile(string path, string hash, long size, CancellationToken token) =>
        hash.Length == 0 ? !File.Exists(path) : File.Exists(path) && new FileInfo(path).Length == size &&
            await Sha256FileAsync(path, token) == hash;

    private void CleanupResetFiles(IEnumerable<ResetFile> entries, bool backups)
    {
        foreach (var entry in entries)
        {
            var destination = ResolveResetPath(entry.LogicalPath);
            DeleteIfExists(destination + ".morecars-reset.partial");
            if (backups) DeleteIfExists(destination + ".morecars-reset.backup");
        }
    }

    private async Task RecoverSkinResetAsync(CancellationToken token)
    {
        RequireNoLinks(ResetJournalPath);
        if (!File.Exists(ResetJournalPath)) return;
        if (CompanionGame.IsRunning(settings.TrackmaniaRoot, true))
            throw new InvalidOperationException("Close Trackmania to recover the interrupted skin reset.");
        var journal = JsonSerializer.Deserialize<ResetJournal>(await File.ReadAllTextAsync(ResetJournalPath, token), JsonOptions)
            ?? throw new InvalidDataException("The skin reset record is unreadable.");
        if (journal.Files.Count is < 1 or > 11 || journal.Files.Select(file => file.LogicalPath).Distinct().Count() != journal.Files.Count)
            throw new InvalidDataException("The skin reset record is invalid.");
        var currentOwnership = JsonSerializer.Serialize(LoadOwnership(), JsonOptions);
        var committed = currentOwnership == journal.AfterOwnership;
        if (!committed && currentOwnership != journal.BeforeOwnership)
            throw new IOException("The ownership record changed during reset. Inspect the installation before continuing.");
        // Validate the whole set before recovery touches any files, including backups.
        foreach (var entry in journal.Files)
        {
            var destination = ResolveResetPath(entry.LogicalPath);
            var backup = destination + ".morecars-reset.backup";
            if (!Sha256Pattern().IsMatch(entry.AfterHash) || entry.AfterSize <= 0 ||
                (entry.BeforeHash.Length > 0 && (!Sha256Pattern().IsMatch(entry.BeforeHash) || entry.BeforeSize <= 0)))
                throw new InvalidDataException("The skin reset hashes are invalid.");
            var before = await MatchesResetFile(destination, entry.BeforeHash, entry.BeforeSize, token);
            var after = await MatchesResetFile(destination, entry.AfterHash, entry.AfterSize, token);
            var hasBackup = File.Exists(backup);
            if ((hasBackup && !await MatchesResetFile(backup, entry.BeforeHash, entry.BeforeSize, token)) ||
                (committed && !after) || (!committed && File.Exists(destination) && !before && !after) ||
                (!committed && entry.BeforeHash.Length > 0 && !before && !hasBackup))
                throw new IOException("A reset file has unknown changes. All remaining recovery files were preserved.");
        }
        if (!committed)
        {
            foreach (var entry in journal.Files.AsEnumerable().Reverse())
            {
                var destination = ResolveResetPath(entry.LogicalPath);
                var backup = destination + ".morecars-reset.backup";
                if (File.Exists(backup)) { DeleteIfExists(destination); File.Move(backup, destination); }
                else if (entry.BeforeHash.Length == 0) DeleteIfExists(destination);
            }
        }
        CleanupResetFiles(journal.Files, backups: true);
        DeleteIfExists(ResetJournalPath);
    }
}
