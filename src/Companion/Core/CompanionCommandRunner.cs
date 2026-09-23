using System.Text.Json;

namespace MoreCars.Companion;

internal sealed class CompanionCommandRunner(CompanionSettings settings)
{
    public bool UninstallStarted { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string ReceiptPath => Path.Combine(CompanionPlatform.ConfigDirectory, "command-receipt.json");

    // Persist the result before sending it, so a lost response never repeats a completed write.
    public async Task FlushReceiptAsync(CompanionApi api, CancellationToken cancellationToken)
    {
        if (!File.Exists(ReceiptPath)) return;
        var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(ReceiptPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("The command receipt is unreadable.");
        await api.ReportAsync(receipt.Command, receipt.Status, 1000, receipt.Message, cancellationToken,
            receipt.Sha256, receipt.ByteSize);
        File.Delete(ReceiptPath);
    }

    public async Task ExecuteAsync(CompanionApi api, CompanionCommand command,
        Func<string, Task<bool>> chooseGame, Action<string> progressMessage, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<bool>>? confirmGame = null)
    {
        if (command.Schema != "morecars.companion-command.v1" ||
            !System.Text.RegularExpressions.Regex.IsMatch(command.Id, "^command_[A-Za-z0-9_-]{32,96}$") ||
            !DateTimeOffset.TryParse(command.ExpiresAt, out var expiry) || expiry <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("The command is invalid or expired.");
        var installer = new ReleaseInstaller(settings, api);
        async Task Report(int progress, string message)
        {
            progressMessage(message);
            await api.ReportAsync(command, "running", progress, message, cancellationToken);
        }
        async Task WaitForSkinWrite()
        {
            const string message = "Skin prepared. Close Trackmania to finish applying it; live refresh is unavailable for this game session.";
            await Report(700, "The skin archive is prepared. Checking whether Trackmania is closed.");
            await CompanionSkinGate.WaitAsync(() => CompanionGame.IsRunning(settings.TrackmaniaRoot, true), expiry,
                async () => {
                    progressMessage(message);
                    await api.ReportAsync(command, "waiting-for-game-exit", 750, message, cancellationToken);
                }, cancellationToken);
        }
        var liveRefreshed = false;
        async Task<bool> CommitSkin(PreparedSkinCommit prepared)
        {
            var result = await CompanionNativeSkins.TryCommitAsync(settings.TrackmaniaRoot, command.Id, expiry, prepared, Report, cancellationToken);
            liveRefreshed = result?.FidsRefreshed == true;
            return result is not null;
        }
        var receipt = new Receipt(command, "completed", "The command completed.", "", 0);
        try
        {
            var carActions = new[] { "install-car", "remove-car", "apply-skin", "restore-skin" };
            var cars = new[] { "bay", "canyon", "coast", "desert", "island", "lagoon", "rally", "snow", "stadium", "traffic", "valley" };
            if (carActions.Contains(command.Action) ? !cars.Contains(command.CarId) : command.CarId.Length != 0)
                throw new InvalidDataException("The command's car ID is invalid.");
            if (command.Action == "apply-skin" ? !System.Text.RegularExpressions.Regex.IsMatch(command.SkinId, "^[A-Za-z0-9_-]{22}$") : command.SkinId.Length != 0)
                throw new InvalidDataException("The command's skin ID is invalid.");
            if ((command.GameId.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(command.GameId, "^game_[a-f0-9]{64}$")) ||
                (command.Action == "confirm-game" && command.GameId.Length == 0) ||
                (command.GameId.Length > 0 && command.Action is not ("confirm-game" or "install-release" or "restore-skin" or "restore-all-skins")))
                throw new InvalidDataException("The command's game selection is invalid.");
            var needsGame = command.Action is not ("confirm-game" or "choose-game" or "uninstall-companion");
            if (needsGame && !settings.GameConfirmed)
                throw new InvalidOperationException("Confirm your Trackmania folder on the website before continuing.");
            if (command.Action is "install-release" or "restore-skin" or "restore-all-skins" && command.GameId.Length > 0 && command.GameId != CompanionGameDiscovery.Identity(settings.TrackmaniaRoot))
                throw new InvalidOperationException("The game folder changed. Confirm it again before continuing.");
            await Report(10, "The companion received your command.");
            switch (command.Action)
            {
                case "install-release":
                case "install-car":
                    await installer.InstallAsync(Report, cancellationToken, command.Action == "install-car" ? command.CarId : "");
                    receipt = receipt with { Message = "Vehicle files installed and verified. Trackmania may need a restart to load new vehicle assets." };
                    break;
                case "cleanup-managed":
                case "remove-car":
                    var result = await installer.CleanupAsync(Report, cancellationToken, command.Action == "remove-car" ? command.CarId : "");
                    receipt = receipt with {
                        Status = result.Conflicts.Count == 0 ? "completed" : "failed",
                        Message = $"Removed {result.Removed} owned files; {result.Missing} missing; {result.Conflicts.Count} modified files preserved." };
                    break;
                case "apply-skin":
                    var skin = await installer.ApplySkinAsync(command.CarId, command.SkinId, Report, cancellationToken, WaitForSkinWrite, CommitSkin);
                    receipt = receipt with { Message = liveRefreshed ? "Skin applied and refreshed in Trackmania. It is ready for your next map." : "Skin installed and verified. Launch Trackmania to use it.", Sha256 = skin.ArchiveSha256, ByteSize = skin.ByteSize };
                    break;
                case "restore-skin":
                    await installer.RestoreSkinAsync(command.CarId, Report, cancellationToken, WaitForSkinWrite, CommitSkin);
                    receipt = receipt with { Message = liveRefreshed ? "Default skin restored and refreshed in Trackmania. It is ready for your next map." : "Default skin restored and verified. Launch Trackmania to use it." };
                    break;
                case "restore-all-skins":
                    var restored = await installer.RestoreSkinsAsync("", Report, cancellationToken, WaitForSkinWrite, CommitSkin);
                    receipt = receipt with { Message = $"Default skins restored for {restored} installed cars. " + (liveRefreshed ? "Refreshed in Trackmania and ready for your next map." : "Launch Trackmania to use them.") };
                    break;
                case "inspect-installation":
                    var inspection = await installer.InspectAsync(cancellationToken);
                    receipt = receipt with { Message = $"Checked {inspection.Files.Count} managed files. " +
                        (inspection.Installed ? "The release is complete and verified." : "The release is incomplete or has modified files.") };
                    break;
                case "launch-game":
                    CompanionGame.Launch(settings);
                    receipt = receipt with { Message = "Trackmania launch requested." };
                    break;
                case "choose-game":
                    await Report(10, "Choose your Trackmania folder in the folder picker.");
                    var selected = await chooseGame("manual");
                    receipt = receipt with { Message = selected
                        ? "The selected Trackmania installation is ready."
                        : "Folder selection cancelled. The folder was not changed." };
                    break;
                case "confirm-game":
                    if (confirmGame is null) throw new InvalidOperationException("Update the companion to confirm this game folder.");
                    var changed = await confirmGame(command.GameId, cancellationToken);
                    receipt = receipt with { Message = changed
                        ? "The selected Trackmania installation is ready."
                        : "Trackmania folder confirmed." };
                    break;
                case "uninstall-companion":
                case "uninstall-companion-and-cars":
                    if (CompanionNativeSkins.RuntimeLoaded())
                        throw new InvalidOperationException("Close Trackmania before uninstalling the companion. Its game runtime is still loaded.");
                    if (command.Action == "uninstall-companion-and-cars")
                    {
                        var cleanup = await installer.CleanupAsync(Report, cancellationToken);
                        if (cleanup.Conflicts.Count > 0)
                            throw new InvalidOperationException("Some car files were modified and kept. Review them before uninstalling the companion.");
                    }
                    await Report(900, "Closing the companion and removing its files.");
                    CompanionUninstaller.Start(command);
                    UninstallStarted = true;
                    return; // The cleanup process reports completion only after removal.
                default:
                    throw new InvalidDataException("The requested capability is not supported by this companion.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var message = error is IOException or UnauthorizedAccessException
                ? "A vehicle file is locked, modified, linked, or inaccessible. The current operation stopped; inspect the installation before retrying."
                : error.Message.Replace(settings.TrackmaniaRoot.Length > 0 ? settings.TrackmaniaRoot : "\0", "Trackmania", StringComparison.OrdinalIgnoreCase);
            receipt = new Receipt(command, "failed", message.Length > 480 ? message[..480] : message, "", 0);
        }
        CompanionStorage.AtomicWrite(ReceiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        await FlushReceiptAsync(api, cancellationToken);
    }

    private sealed record Receipt(CompanionCommand Command, string Status, string Message, string Sha256, long ByteSize);
}
