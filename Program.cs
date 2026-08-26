using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MoreCars.Companion;

internal static partial class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var quiet = args.Contains("--quiet", StringComparer.Ordinal);
        try
        {
            if (args.Length > 0 && args[0] == "--self-test")
                return await CompanionSelfTest.RunAsync(CancellationToken.None) ? 0 : 1;

            if (!quiet) CompanionPlatform.InitializeUi();
            if (args.Length > 0 && args[0] == "--uninstall")
            {
                var keepData = args.Contains("--keep-data", StringComparer.Ordinal);
                CompanionStorage.Uninstall(keepData);
                if (!quiet)
                    CompanionPlatform.ShowInformation(
                        keepData
                            ? "More Cars Companion was unregistered and removed. Pairing settings and cache were kept."
                            : "More Cars Companion, its protocol registration, settings, and cache were removed. Managed Trackmania cars were not removed.");
                return 0;
            }
            if (args.Length == 0 || args[0] == "--install")
            {
                var runningInstalledCopy = CompanionStorage.InstallSelf();
                var configuredRoot = ArgumentValue(args, "--trackmania-root");
                if (OperatingSystem.IsLinux())
                {
                    var installSettings = CompanionStorage.LoadSettings();
                    var selectedRoot = configuredRoot;
                    if (string.IsNullOrWhiteSpace(selectedRoot) && !IsTrackmaniaRoot(installSettings.TrackmaniaRoot))
                        selectedRoot = CompanionPlatform.SelectTrackmaniaRoot(installSettings.TrackmaniaRoot);
                    if (!string.IsNullOrWhiteSpace(selectedRoot))
                    {
                        if (!IsTrackmaniaRoot(selectedRoot))
                            throw new DirectoryNotFoundException("The selected folder does not contain Trackmania GameData.");
                        installSettings.TrackmaniaRoot = Path.GetFullPath(selectedRoot);
                        CompanionStorage.SaveSettings(installSettings);
                    }
                }
                if (!quiet)
                    CompanionPlatform.ShowInformation(
                        runningInstalledCopy
                            ? "More Cars Companion is installed. Return to cars.xjk.yt to pair this computer."
                            : "More Cars Companion was installed for this user. Return to cars.xjk.yt to pair it.");
                return 0;
            }

            if (!Uri.TryCreate(args[0], UriKind.Absolute, out var activation) || activation.Scheme != "morecars")
                throw new InvalidDataException("The companion received an invalid activation URI.");

            if (!CompanionStorage.InstallSelf())
            {
                var start = new ProcessStartInfo(CompanionStorage.InstalledExecutablePath)
                {
                    UseShellExecute = false
                };
                start.ArgumentList.Add(activation.AbsoluteUri);
                Process.Start(start);
                return 0;
            }

            var settings = CompanionStorage.LoadSettings();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromHours(24));
            if (activation.Host.Equals("pair", StringComparison.OrdinalIgnoreCase))
            {
                await PairAsync(settings, activation, cancellation.Token);
                return 0;
            }
            if (activation.Host.Equals("execute", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteAsync(settings, activation, cancellation.Token);
                return 0;
            }
            throw new InvalidDataException("The companion activation action is unknown.");
        }
        catch (Exception error)
        {
            if (quiet) Console.Error.WriteLine($"More Cars Companion: {error.Message}");
            else CompanionPlatform.ShowError(error.Message);
            return 1;
        }
    }

    private static async Task PairAsync(CompanionSettings settings, Uri activation, CancellationToken cancellationToken)
    {
        var pairingCode = activation.AbsolutePath.Trim('/');
        if (!PairingCode().IsMatch(pairingCode)) throw new InvalidDataException("The pairing code is invalid.");
        if (!IsTrackmaniaRoot(settings.TrackmaniaRoot))
        {
            var selectedPath = CompanionPlatform.SelectTrackmaniaRoot(settings.TrackmaniaRoot);
            if (string.IsNullOrWhiteSpace(selectedPath) || !IsTrackmaniaRoot(selectedPath))
                throw new DirectoryNotFoundException("The selected folder does not contain Trackmania GameData.");
            settings.TrackmaniaRoot = Path.GetFullPath(selectedPath);
        }

        using var api = new CompanionApi(settings);
        var result = await api.ClaimPairingAsync(new PairingClaim
        {
            PairingCode = pairingCode,
            DeviceId = settings.DeviceId,
            DisplayName = Environment.MachineName,
            DeviceToken = settings.DeviceToken
        }, cancellationToken);
        if (result.Schema != "morecars.companion.v1" || result.DeviceId != settings.DeviceId || result.DeviceToken.Length < 32)
            throw new InvalidDataException("The pairing response is invalid.");
        settings.DeviceToken = result.DeviceToken;
        CompanionStorage.SaveSettings(settings);
        CompanionPlatform.ShowInformation(
            "This computer is paired. You can now install, clean up, and select cars from cars.xjk.yt.");
    }

    private static async Task ExecuteAsync(CompanionSettings settings, Uri activation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DeviceToken)) throw new InvalidOperationException("Pair this companion from cars.xjk.yt first.");
        var requestedCommandId = activation.AbsolutePath.Trim('/');
        if (!CommandId().IsMatch(requestedCommandId)) throw new InvalidDataException("The command ID is invalid.");
        using var api = new CompanionApi(settings);
        var command = await api.GetCommandAsync(requestedCommandId, cancellationToken)
            ?? throw new InvalidOperationException("There is no pending command for this companion.");
        if (command.Schema != "morecars.companion-command.v1" || command.Id != requestedCommandId)
            throw new InvalidOperationException("The pending command does not match this browser request.");

        try
        {
            await api.ReportAsync(command, "running", 20,
                "Preparing a safe live update. Trackmania will not be closed.", cancellationToken);
            var installer = new ReleaseInstaller(settings, api);
            switch (command.Action)
            {
                case "install-release":
                    await installer.InstallAsync(
                        (progress, message) => api.ReportAsync(command, "running", progress, message, cancellationToken),
                        cancellationToken);
                    await api.ReportAsync(command, "completed", 1000,
                        "The managed cars are installed. Trackmania can stay open; reopen the vehicle menu if needed.", cancellationToken);
                    break;
                case "cleanup-managed":
                    var result = await installer.CleanupAsync(
                        (progress, message) => api.ReportAsync(command, "running", progress, message, cancellationToken),
                        cancellationToken);
                    var cleanupMessage = result.Conflicts.Count == 0
                        ? $"Removed {result.Removed} owned files; {result.Missing} were already missing."
                        : $"Removed {result.Removed} owned files and preserved {result.Conflicts.Count} modified files.";
                    await api.ReportAsync(command, result.Conflicts.Count == 0 ? "completed" : "failed", 1000,
                        cleanupMessage, cancellationToken);
                    break;
                case "apply-skin":
                    var skinResult = await installer.ApplySkinAsync(command.CarId, command.SkinId,
                        (progress, message) => api.ReportAsync(command, "running", progress, message, cancellationToken),
                        cancellationToken);
                    await api.ReportAsync(command, "completed", 1000,
                        "The selected skin is installed. Reopen the vehicle menu if Trackmania has already loaded the car.", cancellationToken,
                        skinResult.ArchiveSha256, skinResult.ByteSize);
                    break;
                case "restore-skin":
                    await installer.RestoreSkinAsync(command.CarId,
                        (progress, message) => api.ReportAsync(command, "running", progress, message, cancellationToken),
                        cancellationToken);
                    await api.ReportAsync(command, "completed", 1000,
                        "The factory skin is restored. Reopen the vehicle menu if Trackmania has already loaded the car.", cancellationToken);
                    break;
                default:
                    throw new InvalidDataException("The server returned an unknown companion action.");
            }
        }
        catch (Exception error)
        {
            var failureMessage = error is IOException or UnauthorizedAccessException
                ? "The operating system could not replace a vehicle file because it is currently locked or inaccessible. Trackmania was not closed and no partial update was kept; retry when the file becomes available."
                : error.Message;
            try
            {
                await api.ReportAsync(command, "failed", 1000, failureMessage, CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure when status reporting is unavailable.
            }
            throw;
        }
    }

    private static bool IsTrackmaniaRoot(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && Directory.Exists(Path.Combine(path, "GameData"));

    private static string? ArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index].Equals(name, StringComparison.Ordinal) && index + 1 < args.Count) return args[index + 1];
            if (args[index].StartsWith(name + "=", StringComparison.Ordinal)) return args[index][(name.Length + 1)..];
        }
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{32,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PairingCode();

    [GeneratedRegex("^command_[A-Za-z0-9_-]{32,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandId();
}
