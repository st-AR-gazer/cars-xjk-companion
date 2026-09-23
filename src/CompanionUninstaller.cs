using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoreCars.Companion;

internal static class CompanionUninstaller
{
    internal static bool IsUninstall(string action) => action is "uninstall-companion" or "uninstall-companion-and-cars";

    internal static string RequireHelperDirectory(string executable)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var parent = Path.GetDirectoryName(directory)!;
        if (!CompanionPlatform.PathComparer.Equals(parent, Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)) ||
            !Regex.IsMatch(Path.GetFileName(directory), "^MoreCarsCompanion-uninstall-[a-f0-9]{32}$") ||
            Path.GetFileName(executable) != CompanionPlatform.ExecutableFileName)
            throw new InvalidOperationException("The uninstall helper is outside its temporary directory.");
        ReleaseInstaller.RequireNoLinks(directory);
        return directory;
    }

    public static void Start(CompanionCommand command)
    {
        if (!IsUninstall(command.Action)) throw new InvalidDataException("Invalid uninstall action.");
        var directory = Path.Combine(Path.GetTempPath(), "MoreCarsCompanion-uninstall-" + Guid.NewGuid().ToString("N"));
        var helper = Path.Combine(directory, CompanionPlatform.ExecutableFileName);
        RequireHelperDirectory(helper);
        Directory.CreateDirectory(directory);
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The companion executable is unavailable.");
        File.Copy(source, helper);
        CompanionPlatform.EnsureExecutable(helper);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "--finish-uninstall", command.Id, Environment.ProcessId.ToString(), "--quiet" })
            start.ArgumentList.Add(argument);
        try { _ = Process.Start(start) ?? throw new InvalidOperationException("The uninstall helper could not start."); }
        catch { File.Delete(helper); Directory.Delete(directory); throw; }
    }

    public static async Task<int> FinishAsync(string[] args)
    {
        var helperDirectory = RequireHelperDirectory(Environment.ProcessPath!);
        if (args.Length != 4 || args[3] != "--quiet" || !Regex.IsMatch(args[1], "^command_[A-Za-z0-9_-]{32,96}$") ||
            !int.TryParse(args[2], out var parentId) || parentId < 1 || parentId == Environment.ProcessId)
            throw new InvalidDataException("Invalid uninstall request.");
        var settings = CompanionStorage.LoadSettings();
        using var api = new CompanionApi(settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        CompanionCommand? command = null;
        try
        {
            command = await api.GetCommandAsync(args[1], timeout.Token);
            if (command is null || command.Schema != "morecars.companion-command.v1" || !IsUninstall(command.Action) || command.Id != args[1] ||
                !DateTimeOffset.TryParse(command.ExpiresAt, out var expiry) || expiry <= DateTimeOffset.UtcNow)
                throw new InvalidDataException("The uninstall command was not authorized.");
            try
            {
                using var parent = Process.GetProcessById(parentId);
                await parent.WaitForExitAsync(timeout.Token);
            }
            catch (ArgumentException) { }
            var targets = CompanionStorage.UninstallTargets(false);
            CompanionStorage.Uninstall(false);
            if (targets.Any(path => File.Exists(path) || Directory.Exists(path)))
                throw new IOException("Some companion files could not be removed.");
            await api.ReportAsync(command, "completed", 1000,
                command.Action == "uninstall-companion-and-cars"
                    ? "Companion and managed cars uninstalled."
                    : "Companion uninstalled. Installed cars were kept.", timeout.Token);
            return 0;
        }
        catch (Exception) when (command is not null)
        {
            try
            {
                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await api.ReportAsync(command, "failed", 900,
                    "Uninstall could not be confirmed. Download and run the companion to retry cleanup.", reportTimeout.Token);
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { }
            return 1;
        }
        finally
        {
            if (OperatingSystem.IsWindows()) CompanionPlatform.RemoveUninstallHelperAfterExit(helperDirectory);
            else Directory.Delete(helperDirectory, true);
        }
    }
}
