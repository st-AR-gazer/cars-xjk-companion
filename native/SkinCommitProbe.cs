using System.Security.Cryptography;
using System.Text.Json;

namespace MoreCars.Native;

internal static class SkinCommitProbe
{
    internal static async Task<object> RecoverAsync(string executable)
    {
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("Trackmania.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Supply the full path to this installation's Trackmania.exe.");
        var processes = System.Diagnostics.Process.GetProcessesByName("Trackmania");
        try { if (processes.Length != 0) throw new InvalidOperationException("Close Trackmania before recovering the native test."); }
        finally { foreach (var process in processes) process.Dispose(); }
        var root = Path.GetDirectoryName(executable)!;
        var pending = NativeSkinStage.ReadPending(root);
        await NativeSkinStage.RecoverAsync(root, false, CancellationToken.None);
        return new { schema = "morecars.native-skin-recovery.v1", recovered = pending is not null, transactionId = pending?.Id,
            note = "Recovery checked the registered file hashes and preserved unknown changes. No game process was opened." };
    }

    internal static async Task<object> RunAsync(int pid, string executable)
    {
        var root = Path.GetDirectoryName(executable)!;
        if (NativeSkinStage.ReadPending(root) is not null)
            throw new InvalidOperationException("A previous native transaction needs recovery first. No new test was started.");
        foreach (var journal in new[] { "skin-reset.json", "install-transaction.json" })
            if (File.Exists(Path.Combine(root, ".morecars", journal))) throw new InvalidOperationException("Finish installation recovery before testing native skin refresh.");
        var ledgerPath = NativeSkinPaths.Resolve(root, NativeSkinPaths.LedgerKey);
        NativeSkinStage.RequireNoLinks(ledgerPath);
        var ledger = await File.ReadAllTextAsync(ledgerPath);
        using var document = JsonDocument.Parse(ledger);
        if (document.RootElement.GetProperty("schema").GetString() != "morecars.ownership.v1") throw new InvalidDataException("Unknown ownership record.");
        var files = document.RootElement.GetProperty("files");
        var overlays = document.RootElement.TryGetProperty("overlays", out var existingOverlays) ? existingOverlays.EnumerateArray().ToArray() : [];
        var candidates = files.EnumerateArray().Where(file => Array.IndexOf(NativeSkinPaths.Paths, file.GetProperty("logicalPath").GetString()) is >= 0 and < NativeSkinPaths.LedgerKey).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("Install a managed car before running the native skin test.");
        var selected = candidates.OrderBy(file => file.GetProperty("byteSize").GetInt64()).First();
        var logicalPath = selected.GetProperty("logicalPath").GetString()!;
        var key = Array.IndexOf(NativeSkinPaths.Paths, logicalPath);
        var overlay = overlays.SingleOrDefault(file => file.GetProperty("logicalPath").GetString() == logicalPath);
        var hash = overlay.ValueKind == JsonValueKind.Undefined ? selected.GetProperty("sha256").GetString()! : overlay.GetProperty("replacementSha256").GetString()!;
        var size = overlay.ValueKind == JsonValueKind.Undefined ? selected.GetProperty("byteSize").GetInt64() : overlay.GetProperty("replacementByteSize").GetInt64();
        var destination = NativeSkinPaths.Resolve(root, key);
        if (!await NativeSkinStage.MatchesAsync(destination, hash, size, CancellationToken.None)) throw new IOException("The chosen car has unknown changes; it was preserved.");
        var library = Path.Combine(AppContext.BaseDirectory, "MoreCars.GameRuntime.dll");
        await using var client = await NativeSkinClient.ConnectAsync(pid, executable, library, CancellationToken.None);
        var partial = destination + ".morecars-skin.partial";
        NativeSkinStage.RequireNoLinks(partial);
        var created = false;
        try
        {
            File.Copy(destination, partial); created = true;
            var manifest = await NativeSkinStage.PrepareAsync(root, "local-same-skin-test", DateTimeOffset.UtcNow.AddMinutes(2), pid,
                client.ProcessStartedUtcTicks, [new(NativeSkinPaths.CarIds[key], partial, hash, size, hash, size)], ledger, ledger, CancellationToken.None);
            var queuedBeforeCommit = false;
            var receipt = await client.CommitAsync(root, manifest, waiting => { queuedBeforeCommit |= waiting; return Task.CompletedTask; }, CancellationToken.None);
            if (!receipt.FilesVerified || !receipt.FidsRefreshed || !receipt.CallbackRestored || !receipt.RuntimeStopped)
                throw new InvalidOperationException("The game exited before a complete native skin-refresh result could be verified.");
            return new {
                schema = "morecars.native-skin-test.v1", observedAt = DateTimeOffset.UtcNow, processId = pid,
                gameVersion = GameProfile.Version, executableSha256 = GameProfile.Sha256,
                nativeLibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant(),
                carId = NativeSkinPaths.CarIds[key], archiveSha256 = hash, archiveByteSize = size,
                sameSkinBytes = true, initialGameState = client.InitialState, queuedBeforeCommit, receipt,
                note = "The owned car archive and ownership record were replaced with identical verified bytes, then FIDs were refreshed. Appearance is unchanged. The runtime is inert but remains mapped until game exit."
            };
        }
        finally
        {
            if (created && await NativeSkinStage.MatchesAsync(partial, hash, size, CancellationToken.None)) File.Delete(partial);
        }
    }
}
