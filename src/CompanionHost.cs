using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

namespace MoreCars.Companion;

internal sealed class CompanionHost(CompanionSettings settings,
    Func<string, bool, Action?, Task<string?>>? selectGame = null)
{
    private volatile CompanionRuntime _runtime = new();
    internal CompanionRuntime Runtime => _runtime;
    private IReadOnlyList<GameInstallation> _gameChoices = [];
    private readonly Channel<string> _activations = Channel.CreateBounded<string>(16);
    private readonly SemaphoreSlim _protocolGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private DateTimeOffset _protocolChecked;

    public async Task RunAsync(string pairingCode, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(CompanionStorage.AppDirectory);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        FileStream instance;
        try { instance = new FileStream(Path.Combine(CompanionStorage.AppDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        {
            if (!await CompanionActivation.SendAsync(pairingCode.Length > 0 ? "pair:" + pairingCode : "wake", cancellationToken))
                throw new IOException("The companion is starting. Try opening it again shortly.");
            return;
        }
        using var ownedInstance = instance;
        var pipe = ReceiveActivationsAsync(lifetime);
        var heartbeat = HeartbeatLoopAsync(lifetime.Token);
        var gameStartUpdates = WatchGameStartsAsync(lifetime.Token);
        try
        {
            if (pairingCode.Length > 0) await PairAsync(pairingCode, lifetime.Token);
            if (settings.DeviceToken.Length > 0)
            {
                try { await PrepareGameAsync(true, lifetime.Token); }
                catch (Exception) when (!lifetime.IsCancellationRequested)
                {
                    _runtime = _runtime with { Stage = "error", Message = "Setup needs attention. Choose your Trackmania folder from the website." };
                }
            }
            var runner = new CompanionCommandRunner(settings);
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    if (_activations.Reader.TryRead(out var activation) && activation.StartsWith("pair:", StringComparison.Ordinal))
                    {
                        await PairAsync(activation[5..], lifetime.Token);
                        await PrepareGameAsync(true, lifetime.Token);
                    }
                    if (settings.DeviceToken.Length > 0)
                    {
                        using var api = new CompanionApi(settings);
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        request.CancelAfter(TimeSpan.FromSeconds(20));
                        await runner.FlushReceiptAsync(api, request.Token);
                        var command = await api.NextCommandAsync(request.Token);
                        if (command is not null)
                        {
                            await _operationGate.WaitAsync(lifetime.Token);
                            try
                            {
                                _runtime = _runtime with { Stage = "working" };
                                await runner.ExecuteAsync(api, command, _ => PrepareGameAsync(false, lifetime.Token),
                                    message => _runtime = _runtime with { Message = message }, lifetime.Token, ConfirmGameAsync);
                                if (runner.UninstallStarted) { await lifetime.CancelAsync(); break; }
                                await InspectAsync(lifetime.Token);
                            }
                            finally { _operationGate.Release(); }
                        }
                        else if (_runtime.Stage == "error" && _runtime.GameReady)
                        {
                            await InspectAsync(lifetime.Token);
                        }
                    }
                }
                catch (Exception error) when (!lifetime.IsCancellationRequested)
                {
                    _runtime = _runtime with
                    {
                        Stage = "error",
                        Message = error is HttpRequestException or OperationCanceledException
                        ? "Connection interrupted. The companion is reconnecting automatically."
                        : "The operation needs attention. Check the command details or choose Trackmania again."
                    };
                }
                await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            await lifetime.CancelAsync();
            try { await Task.WhenAll(pipe, heartbeat, gameStartUpdates); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task PairAsync(string code, CancellationToken cancellationToken)
    {
        if (!CompanionActivation.IsPairingCode(code)) throw new InvalidDataException("The pairing code is invalid.");
        var codeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
        if (settings.DeviceToken.Length > 0 && settings.LastPairingCodeSha256 == codeHash) return;
        using var api = new CompanionApi(settings);
        var result = await api.ClaimPairingAsync(new PairingClaim
        {
            PairingCode = code,
            DeviceId = settings.DeviceId,
            DisplayName = Environment.MachineName,
            DeviceToken = settings.DeviceToken
        }, cancellationToken);
        if (result.Schema != "morecars.companion.v1" || result.DeviceId != settings.DeviceId || result.DeviceToken.Length < 32)
            throw new InvalidDataException("The pairing response is invalid.");
        settings.DeviceToken = result.DeviceToken;
        settings.LastPairingCodeSha256 = codeHash;
        CompanionStorage.SaveSettings(settings);
        _runtime = _runtime with { Stage = "starting", Message = "Connected to this browser. Finding Trackmania." };
        try
        {
            using var pairedApi = new CompanionApi(settings);
            await PublishRuntimeAsync(pairedApi, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            _runtime = _runtime with { Message = "Connected to this browser. Reconnecting to the website." };
        }
    }

    internal async Task<bool> PrepareGameAsync(bool automatic, CancellationToken cancellationToken)
    {
        var previous = _runtime;
        _runtime = _runtime with
        {
            Stage = "starting",
            Message = "Checking the usual Trackmania folders.",
            GameReady = false,
            Installed = false,
            Files = []
        };
        if (automatic)
        {
            if (selectGame is null)
                _gameChoices = await Task.Run(() => CompanionGameDiscovery.Discover(settings.TrackmaniaRoot), cancellationToken);
            else
            {
                var candidate = await selectGame(settings.TrackmaniaRoot, true, null).WaitAsync(cancellationToken);
                _gameChoices = CompanionGameDiscovery.Choices(candidate is null ? [] : new[] { candidate });
            }
            var selected = _gameChoices.FirstOrDefault(choice => CompanionPlatform.PathComparer.Equals(choice.Path, settings.TrackmaniaRoot));
            var confirmed = settings.GameConfirmed && selected is not null;
            _runtime = _runtime with
            {
                Stage = "ready",
                GameChoices = _gameChoices,
                SelectedGameId = confirmed ? selected!.Id : "",
                GameReady = confirmed,
                Message = _gameChoices.Count > 0 ? "Review and confirm your Trackmania folder on the website." : "Choose your Trackmania folder from the website."
            };
            if (confirmed) await InspectAsync(cancellationToken);
            return confirmed;
        }
        string? root;
        try
        {
            root = await (selectGame ?? CompanionGame.SelectAsync)(settings.TrackmaniaRoot, automatic, () =>
                _runtime = _runtime with { Stage = "selecting-game", Message = "Choose your Trackmania folder in the folder picker." }
            ).WaitAsync(cancellationToken);
            if (root is not null && !CompanionGame.IsRoot(root))
                throw new InvalidOperationException("That folder does not contain Trackmania.exe and GameData. Choose your Trackmania installation folder.");
        }
        catch
        {
            _runtime = previous with { Stage = "ready" };
            throw;
        }
        if (root is null)
        {
            _runtime = previous with
            {
                Stage = "ready",
                GameReady = settings.GameConfirmed && CompanionGame.IsRoot(settings.TrackmaniaRoot),
                Message = automatic ? "Choose your Trackmania folder from the website." : "Folder selection cancelled. The folder was not changed."
            };
            return false;
        }
        settings.TrackmaniaRoot = Path.GetFullPath(root!);
        settings.GameConfirmed = true;
        _gameChoices = CompanionGameDiscovery.Choices(new[] { settings.TrackmaniaRoot }.Concat(_gameChoices.Select(choice => choice.Path)));
        CompanionStorage.SaveSettings(settings);
        _runtime = _runtime with { GameReady = true, GameChoices = _gameChoices, SelectedGameId = CompanionGameDiscovery.Identity(settings.TrackmaniaRoot) };
        await InspectAsync(cancellationToken);
        return true;
    }

    internal async Task<bool> ConfirmGameAsync(string gameId, CancellationToken cancellationToken)
    {
        var choice = _gameChoices.SingleOrDefault(candidate => candidate.Id == gameId);
        if (choice is null || !CompanionGame.IsRoot(choice.Path))
            throw new InvalidOperationException("That game folder is no longer available. Find Trackmania again.");
        var changed = !CompanionPlatform.PathComparer.Equals(settings.TrackmaniaRoot, choice.Path);
        settings.TrackmaniaRoot = choice.Path;
        settings.GameConfirmed = true;
        CompanionStorage.SaveSettings(settings);
        _runtime = _runtime with { GameReady = true, SelectedGameId = choice.Id };
        await InspectAsync(cancellationToken);
        return changed;
    }

    private async Task InspectAsync(CancellationToken cancellationToken)
    {
        if (!settings.GameConfirmed || !CompanionGame.IsRoot(settings.TrackmaniaRoot)) return;
        using var api = new CompanionApi(settings);
        var inspection = await new ReleaseInstaller(settings, api).InspectAsync(cancellationToken, recover: false);
        _runtime = _runtime with
        {
            Stage = "ready",
            Message = "Connected. Ready for website commands.",
            GameReady = true,
            Installed = inspection.Installed,
            Files = inspection.Files
        };
        await PublishRuntimeAsync(api, cancellationToken);
    }

    private async Task PublishRuntimeAsync(CompanionApi api, CancellationToken cancellationToken)
    {
        await _protocolGate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow - _protocolChecked > TimeSpan.FromMinutes(1))
            {
                var capabilities = await api.RuntimeCapabilitiesAsync(cancellationToken);
                _runtime = _runtime with { Capabilities = capabilities };
                _protocolChecked = DateTimeOffset.UtcNow;
            }
        }
        finally { _protocolGate.Release(); }
        await api.HeartbeatAsync(_runtime with { GameRunning = CompanionGame.IsRunning(settings.TrackmaniaRoot) }, cancellationToken);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (settings.DeviceToken.Length > 0)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    using var api = new CompanionApi(settings);
                    await PublishRuntimeAsync(api, timeout.Token);
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException) { }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task WatchGameStartsAsync(CancellationToken cancellationToken)
    {
        var monitor = new GameStartUpdateMonitor(
            () => settings.DeviceToken.Length > 0 && settings.GameConfirmed &&
                  CompanionGame.IsRoot(settings.TrackmaniaRoot) && CompanionGame.IsRunning(settings.TrackmaniaRoot),
            async token =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                return await StartupUpdateProbe.CheckAsync(settings, timeout.Token);
            },
            async (notice, token) =>
            {
                if (!await StartupUpdateWindow.ShowAsync(notice, token)) return;
                if (notice.CompanionUpdate && !OperatingSystem.IsWindows())
                {
                    CompanionTray.OpenWebsite(settings.ApiOrigin, "update", settings.DeviceId);
                    return;
                }
                if (notice.HasModifiedCars && !notice.CompanionUpdate)
                {
                    CompanionTray.OpenWebsite(settings.ApiOrigin, "repair", settings.DeviceId);
                    return;
                }
                await _operationGate.WaitAsync(token);
                string? statusTitle = null;
                string? statusDetails = null;
                try
                {
                    if (notice.CompanionUpdate)
                    {
                        await CompanionSelfUpdater.StartAsync(settings, token);
                        return;
                    }
                    using var api = new CompanionApi(settings);
                    await new ReleaseInstaller(settings, api).InstallAsync((_, _) => Task.CompletedTask, token);
                    statusTitle = "Car update installed";
                    statusDetails = "The managed car files were installed and verified. Trackmania can stay open. If the new cars do not appear in this session, they will load on your next game start.";
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    statusTitle = "Update needs attention";
                    statusDetails = "The update could not finish. Existing modified files were preserved. " + error.Message;
                }
                finally { _operationGate.Release(); }
                if (statusTitle is not null)
                    await StartupUpdateWindow.ShowStatusAsync(statusTitle, statusDetails!, token);
            });
        while (!cancellationToken.IsCancellationRequested)
        {
            await monitor.TickAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    private async Task ReceiveActivationsAsync(CancellationTokenSource lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(CompanionActivation.PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(lifetime.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var buffer = new byte[256];
            var offset = 0;
            try
            {
                while (offset < buffer.Length)
                {
                    var count = await pipe.ReadAsync(buffer.AsMemory(offset), timeout.Token);
                    if (count == 0) break;
                    offset += count;
                    if (buffer.AsSpan(0, offset).Contains((byte)'\n')) break;
                }
                var message = Encoding.UTF8.GetString(buffer, 0, offset).TrimEnd('\n');
                if (message == "stop") await lifetime.CancelAsync();
                else if (message.StartsWith("pair:", StringComparison.Ordinal) && CompanionActivation.IsPairingCode(message[5..]))
                    _activations.Writer.TryWrite(message);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }
}
