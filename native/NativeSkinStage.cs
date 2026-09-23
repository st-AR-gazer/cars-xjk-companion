using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MoreCars.Native;

internal sealed record NativeSkinReplacement(string CarId, string PreparedPath, string BeforeHash, long BeforeSize, string AfterHash, long AfterSize);

internal static class NativeSkinStage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string PendingPath(string root) => Path.Combine(root, ".morecars", "native-pending.json");
    internal static string DirectoryFor(string root, string id)
    {
        if (id.Length != 32 || id.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid native transaction directory.");
        var path = Path.Combine(Path.GetFullPath(root), ".morecars", "native", id);
        RequireNoLinks(path);
        return path;
    }

    internal static async Task<NativeCommitManifest> PrepareAsync(string root, string commandId, DateTimeOffset expiresAt,
        int gamePid, long gameStarted, IReadOnlyList<NativeSkinReplacement> replacements,
        string beforeOwnership, string afterOwnership, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        if (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.AddMinutes(30))
            throw new InvalidOperationException("The skin command expired or has an invalid lifetime.");
        RequireNoLinks(PendingPath(root));
        if (File.Exists(PendingPath(root))) throw new IOException("An interrupted native skin transaction needs recovery first.");
        var ledgerPath = NativeSkinPaths.Resolve(root, NativeSkinPaths.LedgerKey);
        RequireNoLinks(ledgerPath);
        var oldLedger = await File.ReadAllBytesAsync(ledgerPath, token);
        if (!JsonNode.DeepEquals(JsonNode.Parse(oldLedger), JsonNode.Parse(beforeOwnership)))
            throw new IOException("The installation record changed while the skin was being prepared.");
        var newLedger = Encoding.UTF8.GetBytes(afterOwnership);
        var files = replacements.Select(file => new NativeCommitFile(Array.IndexOf(NativeSkinPaths.CarIds, file.CarId), file.BeforeHash, file.BeforeSize, file.AfterHash, file.AfterSize)).OrderBy(file => file.Key).ToList();
        files.Add(new(NativeSkinPaths.LedgerKey, Hash(oldLedger), oldLedger.Length, Hash(newLedger), newLedger.Length));
        var manifest = new NativeCommitManifest(SkinCommitProtocol.Schema, SkinCommitProtocol.NewId(), commandId, expiresAt, gamePid, gameStarted, files);
        SkinCommitProtocol.Validate(manifest);
        var staging = DirectoryFor(root, manifest.Id);
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var replacement in replacements)
            {
                var key = Array.IndexOf(NativeSkinPaths.CarIds, replacement.CarId);
                var destination = NativeSkinPaths.Resolve(root, key);
                var source = Path.GetFullPath(replacement.PreparedPath);
                if (!new[] { destination + ".morecars-skin.partial", destination + ".morecars-reset.partial" }.Contains(source, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("The native skin source is outside its prepared car path.");
                RequireNoLinks(source);
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(Path.Combine(staging, $"{key}.next"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true);
            }
            await WriteNewAsync(Path.Combine(staging, "11.next"), newLedger, token);
            var journalTemp = Path.Combine(staging, "pending.next");
            await WriteNewAsync(journalTemp, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Json)), token);
            File.Move(journalTemp, PendingPath(root));
            return manifest;
        }
        catch
        {
            if (!File.Exists(PendingPath(root))) CleanupDirectory(root, manifest);
            throw;
        }
    }

    internal static NativeCommitManifest? ReadPending(string root)
    {
        var path = PendingPath(root); RequireNoLinks(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("The native transaction journal is oversized.");
        var manifest = JsonSerializer.Deserialize<NativeCommitManifest>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Invalid native transaction journal.");
        SkinCommitProtocol.Validate(manifest);
        return manifest;
    }

    internal static async Task<bool> MatchesAsync(string path, string hash, long size, CancellationToken token)
    {
        RequireNoLinks(path);
        if (size == 0) return !File.Exists(path);
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<bool> MatchesTargetsAsync(string root, NativeCommitManifest manifest, bool after, CancellationToken token)
    {
        foreach (var file in manifest.Files)
            if (!await MatchesAsync(NativeSkinPaths.Resolve(root, file.Key), after ? file.AfterHash : file.BeforeHash, after ? file.AfterSize : file.BeforeSize, token)) return false;
        return true;
    }

    internal static async Task RecoverAsync(string root, bool gameRunning, CancellationToken token)
    {
        var manifest = ReadPending(root);
        if (manifest is null) return;
        if (gameRunning) throw new IOException("Close Trackmania to recover the interrupted native skin change.");
        if (await MatchesTargetsAsync(root, manifest, false, token)) { Complete(root, manifest); return; }
        if (await MatchesTargetsAsync(root, manifest, true, token)) { Complete(root, manifest); return; }
        var staging = DirectoryFor(root, manifest.Id);
        // Validate the whole rollback set before touching any active file.
        foreach (var file in manifest.Files)
        {
            var target = NativeSkinPaths.Resolve(root, file.Key);
            var backup = Path.Combine(staging, $"{file.Key}.before");
            var isBefore = await MatchesAsync(target, file.BeforeHash, file.BeforeSize, token);
            var isAfter = await MatchesAsync(target, file.AfterHash, file.AfterSize, token);
            if (!isBefore && File.Exists(target) && !isAfter) throw new IOException("A car changed during recovery; its files were preserved.");
            if (!isBefore && file.BeforeSize > 0 && !await MatchesAsync(backup, file.BeforeHash, file.BeforeSize, token))
                throw new IOException("A verified rollback file is missing; the installation was preserved.");
        }
        foreach (var file in manifest.Files.AsEnumerable().Reverse())
        {
            var target = NativeSkinPaths.Resolve(root, file.Key);
            if (await MatchesAsync(target, file.BeforeHash, file.BeforeSize, token)) continue;
            if (File.Exists(target)) File.Delete(target);
            if (file.BeforeSize > 0) File.Move(Path.Combine(staging, $"{file.Key}.before"), target);
        }
        Complete(root, manifest);
    }

    internal static void Complete(string root, NativeCommitManifest manifest)
    {
        var pending = ReadPending(root);
        if (pending?.Id != manifest.Id) throw new IOException("The native transaction journal changed before cleanup.");
        var directory = DirectoryFor(root, manifest.Id);
        foreach (var entry in manifest.Files)
            foreach (var before in new[] { false, true })
            {
                var path = Path.Combine(directory, entry.Key + (before ? ".before" : ".next"));
                RequireNoLinks(path);
                if (!File.Exists(path)) continue;
                var hash = before ? entry.BeforeHash : entry.AfterHash;
                var size = before ? entry.BeforeSize : entry.AfterSize;
                using var input = File.OpenRead(path);
                if (input.Length != size || !Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A native recovery file has unknown changes; it was preserved.");
            }
        CleanupDirectory(root, manifest);
        File.Delete(PendingPath(root));
    }

    private static void CleanupDirectory(string root, NativeCommitManifest manifest)
    {
        var directory = DirectoryFor(root, manifest.Id);
        var journalTemp = Path.Combine(directory, "pending.next"); RequireNoLinks(journalTemp);
        if (File.Exists(journalTemp)) File.Delete(journalTemp);
        foreach (var file in manifest.Files)
            foreach (var suffix in new[] { ".next", ".before" })
            {
                var path = Path.Combine(directory, file.Key + suffix); RequireNoLinks(path);
                if (File.Exists(path)) File.Delete(path);
            }
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    internal static void RequireNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("A native skin path contains a symbolic link or junction."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        RequireNoLinks(path);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(bytes, token); await file.FlushAsync(token); file.Flush(true);
    }
}
