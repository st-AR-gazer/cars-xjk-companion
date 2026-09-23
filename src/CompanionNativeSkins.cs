using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using MoreCars.Native;

namespace MoreCars.Companion;

internal sealed record PreparedSkinCommit(IReadOnlyList<NativeSkinReplacement> Files, string BeforeOwnership, string AfterOwnership);

internal static class CompanionNativeSkins
{
    internal static bool RuntimeLoaded()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var directory = Path.GetFullPath(Path.Combine(CompanionPlatform.ConfigDirectory, "native")) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(directory)) return false;
        var processes = Process.GetProcessesByName("Trackmania");
        try
        {
            foreach (var process in processes)
                try
                {
                    if (process.Modules.Cast<ProcessModule>().Any(module => module.FileName.StartsWith(directory, StringComparison.OrdinalIgnoreCase))) return true;
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static async Task<NativeSkinReceipt?> TryCommitAsync(string root, string commandId, DateTimeOffset expiry, PreparedSkinCommit prepared,
        Func<int, string, Task> report, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || !CompanionGame.IsRunning(root, true)) return null;
        NativeSkinClient? client = null;
        try
        {
            var processes = Process.GetProcessesByName("Trackmania");
            try
            {
                var executable = Path.GetFullPath(Path.Combine(root, "Trackmania.exe"));
                var matches = processes.Where(process => StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, executable)).ToArray();
                if (matches.Length != 1) return null;
                var runtime = await ExtractRuntimeAsync(token);
                if (runtime is null) return null;
                client = await NativeSkinClient.ConnectAsync(matches[0].Id, executable, runtime.Value.Path, token, runtime.Value.Hash);
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await report(700, "Live skin refresh is unavailable for this game session. Close Trackmania to finish applying the skin.");
            return null;
        }
        await using (client)
        {
            var nativeExpiry = DateTimeOffset.UtcNow.AddMinutes(29);
            if (expiry < nativeExpiry) nativeExpiry = expiry;
            var manifest = await NativeSkinStage.PrepareAsync(root, commandId, nativeExpiry, client.ProcessId, client.ProcessStartedUtcTicks,
                prepared.Files, prepared.BeforeOwnership, prepared.AfterOwnership, token);
            var result = await client.CommitAsync(root, manifest, waiting => report(waiting ? 750 : 900, waiting
                ? "Skin ready. Leave the map or editor to apply it automatically at the menu."
                : "Applying the skin and refreshing Trackmania's files."), token);
            return result.FilesVerified ? result : null;
        }
    }

    private static async Task<(string Path, string Hash)?> ExtractRuntimeAsync(CancellationToken token)
    {
        await using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoreCars.GameRuntime.dll");
        if (resource is null) return null;
        if (resource.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("The embedded native runtime is invalid.");
        using var contents = new MemoryStream(); await resource.CopyToAsync(contents, token);
        var bytes = contents.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var directory = Path.Combine(CompanionPlatform.ConfigDirectory, "native", hash);
        var path = Path.Combine(directory, "MoreCars.GameRuntime.dll");
        NativeSkinStage.RequireNoLinks(path);
        Directory.CreateDirectory(directory);
        if (!File.Exists(path))
        {
            var partial = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, token); await output.FlushAsync(token); output.Flush(true);
            }
            File.Move(partial, path);
        }
        return (path, hash);
    }
}
