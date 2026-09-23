using System.Diagnostics;

namespace MoreCars.Companion;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var quiet = args.Contains("--quiet", StringComparer.Ordinal);
        var updatedFromNotice = args.FirstOrDefault() == "--update-from-notice";
        try
        {
            if (args.FirstOrDefault() == "--finish-uninstall")
                return await CompanionUninstaller.FinishAsync(args);
            if (args.FirstOrDefault() == "--self-test")
                return await CompanionSelfTest.RunAsync(CancellationToken.None) ? 0 : 1;
            if (!quiet) CompanionPlatform.InitializeUi();
            if (args.FirstOrDefault() == "--preview-update-flow" ||
                StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(Environment.ProcessPath), "MoreCarsCompanion-flow-preview.exe"))
            {
                var sample = new StartupUpdateNotice(false, true, "TEST PREVIEW · A new car update is available",
                    "This is a simulation. No website, download, or game files will be changed.\n\n" +
                    "Sample patch notes: updated old-car camera views are available.\n\n" +
                    "Choose Install car update to preview the completion message, or Not now to close this test.");
                if (await StartupUpdateWindow.ShowAsync(sample, CancellationToken.None))
                    await StartupUpdateWindow.ShowStatusAsync("TEST PREVIEW · Car update installed",
                        "Simulation only: no files were changed. A real install would verify the car files, and Trackmania may load changed assets on its next start.",
                        CancellationToken.None);
                return 0;
            }
            if (args.FirstOrDefault() == "--preview-update-window" ||
                StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(Environment.ProcessPath), "MoreCarsCompanion-preview.exe"))
            {
                // A desktop-only dry run: inspect the current game and release,
                // but never install, queue a command, or open the website.
                StartupUpdateNotice? notice = null;
                try
                {
                    using var check = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    notice = await StartupUpdateProbe.CheckAsync(CompanionStorage.LoadSettings(), check.Token);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) { }
                notice ??= new StartupUpdateNotice(true, false, "A companion update is available",
                    "Sample patch notes: the old cars keep their original Cam 1/2/3, while Alt Cam 1/2 use the Stadium views.");
                notice = notice with
                {
                    Title = "TEST PREVIEW · " + notice.Title,
                    Details = "This is a safe preview of the startup window. Both buttons only close this test; no website, download, or repair will start.\n\n" + notice.Details
                };
                _ = await StartupUpdateWindow.ShowAsync(notice, CancellationToken.None);
                return 0;
            }
            if (args.FirstOrDefault() == "--host")
            {
                await CompanionTray.RunAsync(token => new CompanionHost(CompanionStorage.LoadSettings())
                    .RunAsync(ArgumentValue(args, "--pair") ?? "", token));
                return 0;
            }
            if (args.FirstOrDefault() is "--stop" or "--uninstall")
            {
                if (args[0] == "--uninstall" && CompanionNativeSkins.RuntimeLoaded())
                    throw new InvalidOperationException("Close Trackmania before uninstalling the companion. Its game runtime is still loaded.");
                if (await CompanionActivation.SendAsync("stop", CancellationToken.None)) await Task.Delay(1000);
                if (args[0] == "--stop") return 0;
                CompanionStorage.Uninstall(args.Contains("--keep-data", StringComparer.Ordinal));
                if (!quiet) CompanionPlatform.ShowInformation("The companion was removed. Managed Trackmania cars were kept.");
                return 0;
            }

            var source = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
            var download = CompanionDownload.Read(source);
            var code = download?.PairingCode ?? CompanionActivation.PairingFromFileName(source);
            if (args.Length > 0 && args[0] is not ("--install" or "--quiet" or "--update-from-notice"))
            {
                if (!Uri.TryCreate(args[0], UriKind.Absolute, out var activation) || activation.Scheme != "morecars" ||
                    activation.Query.Length > 0 || activation.Fragment.Length > 0 || activation.UserInfo.Length > 0)
                    throw new InvalidDataException("The companion activation is invalid.");
                if (activation.Host == "pair" && CompanionActivation.IsPairingCode(activation.AbsolutePath.Trim('/')))
                    code = activation.AbsolutePath.Trim('/');
                else if (activation.Host != "execute" || !System.Text.RegularExpressions.Regex.IsMatch(
                    activation.AbsolutePath, "^/command_[A-Za-z0-9_-]{32,96}$"))
                    throw new InvalidDataException("The companion action is invalid.");
            }
            var installed = CompanionStorage.InstallSelf(download);
            var settings = CompanionStorage.LoadSettings();
            if (download is not null)
            {
                var origin = CompanionDownload.NormalizeOrigin(download.ApiOrigin);
                if (!StringComparer.OrdinalIgnoreCase.Equals(settings.ApiOrigin.TrimEnd('/'), origin))
                {
                    settings.DeviceToken = "";
                    settings.LastPairingCodeSha256 = "";
                }
                settings.ApiOrigin = origin;
            }
            var configuredRoot = ArgumentValue(args, "--trackmania-root");
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                if (!CompanionGame.IsRoot(configuredRoot)) throw new DirectoryNotFoundException("The selected folder does not contain Trackmania.");
                settings.TrackmaniaRoot = Path.GetFullPath(configuredRoot);
            }
            CompanionStorage.SaveSettings(settings);
            if (quiet && args.FirstOrDefault() == "--install") return 0;
            var hostAlreadyRunning = await CompanionActivation.SendAsync(code.Length > 0 ? "pair:" + code : "wake", CancellationToken.None);
            if (updatedFromNotice)
            {
                if (!hostAlreadyRunning)
                {
                    var host = new ProcessStartInfo(CompanionStorage.InstalledExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
                    host.ArgumentList.Add("--host");
                    using var startedHost = Process.Start(host) ?? throw new IOException("The updated companion could not start its resident process.");
                }
                await StartupUpdateWindow.ShowStatusAsync("Companion installed",
                    "The new More Cars Companion is installed. Trackmania can stay open; the companion update itself does not need a game restart.",
                    CancellationToken.None);
                return 0;
            }
            if (hostAlreadyRunning) return 0;
            if (code.Length == 0 && settings.DeviceToken.Length == 0) CompanionTray.OpenWebsite(settings.ApiOrigin);
            if (installed)
            {
                await CompanionTray.RunAsync(token => new CompanionHost(settings).RunAsync(code, token));
            }
            else
            {
                var start = new ProcessStartInfo(CompanionStorage.InstalledExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--host");
                if (code.Length > 0) { start.ArgumentList.Add("--pair"); start.ArgumentList.Add(code); }
                Process.Start(start);
            }
            return 0;
        }
        catch (Exception error)
        {
            if (quiet) Console.Error.WriteLine($"More Cars Companion: {error}");
            else if (updatedFromNotice)
                await StartupUpdateWindow.ShowStatusAsync("Companion update needs attention", error.Message, CancellationToken.None);
            else CompanionPlatform.ShowError(error.Message);
            return 1;
        }
    }

    private static string? ArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == name && index + 1 < args.Count) return args[index + 1];
            if (args[index].StartsWith(name + "=", StringComparison.Ordinal)) return args[index][(name.Length + 1)..];
        }
        return null;
    }
}
