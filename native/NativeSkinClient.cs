using System.Buffers.Binary;
using System.Diagnostics;

namespace MoreCars.Native;

internal sealed record NativeSkinReceipt(string TransactionId, uint GameThreadId, bool FilesVerified, bool FidsRefreshed,
    bool CallbackRestored, bool RuntimeStopped);

internal sealed class NativeSkinClient : IAsyncDisposable
{
    private readonly NativeSession session;
    private bool attached;
    internal int ProcessId => session.Memory.ProcessId;
    internal long ProcessStartedUtcTicks => session.Memory.ProcessStartedUtcTicks;
    internal string InitialState => session.InitialState;
    private NativeSkinClient(NativeSession session) => this.session = session;

    internal static async Task<NativeSkinClient> ConnectAsync(int pid, string executable, string library, CancellationToken token, string? expectedLibraryHash = null)
    {
        var client = new NativeSkinClient(new NativeSession(pid, executable, library, expectedLibraryHash));
        try
        {
            _ = client.ReadCommit(); // Verify the skin protocol before attaching.
            if (client.ReadFrame().Running != 0)
                throw new InvalidOperationException("The native runtime is already serving another skin operation. No new connection was started.");
            var start = client.session.Invoke("MoreCarsStartProbe");
            if (start != 0) throw new InvalidOperationException($"Native game connection rejected ({start}).");
            client.attached = true;
            var first = client.ReadFrame();
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(100, token);
                var last = client.ReadFrame();
                if (last.Fault != 0 || last.MixedThreads != 0 || last.Running == 0) break;
                if (last.Frames > first.Frames && last.Frames >= 20 && last.ThreadId != 0) return client;
            }
            throw new InvalidOperationException("The game thread did not become ready. No skin files were changed.");
        }
        catch { await client.DisposeAsync(); throw; }
    }

    internal async Task<NativeSkinReceipt> CommitAsync(string root, NativeCommitManifest manifest,
        Func<bool, Task> reportWaiting, CancellationToken token)
    {
        try { return await CommitWhileRunningAsync(root, manifest, reportWaiting, token); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException && session.HasExited)
        {
            attached = false;
            if (GameRestarted(root)) throw new InvalidOperationException("Trackmania restarted during the skin change. Close it before recovering the installation.");
            var committed = await NativeSkinStage.MatchesTargetsAsync(root, manifest, true, CancellationToken.None);
            await NativeSkinStage.RecoverAsync(root, false, CancellationToken.None);
            return new(manifest.Id, 0, committed, false, false, true);
        }
    }

    private static bool GameRestarted(string root)
    {
        var processes = Process.GetProcessesByName("Trackmania");
        try
        {
            var expected = Path.GetFullPath(Path.Combine(root, "Trackmania.exe"));
            foreach (var process in processes)
                try { if (StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, expected)) return true; }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private async Task<NativeSkinReceipt> CommitWhileRunningAsync(string root, NativeCommitManifest manifest,
        Func<bool, Task> reportWaiting, CancellationToken token)
    {
        if (manifest.GameProcessId != ProcessId || manifest.GameProcessStartedUtcTicks != ProcessStartedUtcTicks)
            throw new InvalidDataException("The native skin transaction belongs to another game session.");
        token.ThrowIfCancellationRequested();
        using var owner = Process.GetCurrentProcess();
        var packet = SkinCommitProtocol.Encode(manifest, owner.Id, owner.StartTime.ToUniversalTime().ToFileTimeUtc());
        var requested = session.Invoke("MoreCarsSubmitSkinCommit", packet);
        if (requested != 0) throw new InvalidOperationException($"The native skin transaction was rejected ({requested}); its recovery record was preserved.");
        bool? previousWaiting = null;
        DateTimeOffset? stoppingAt = null;
        while (true)
        {
            if (session.HasExited) throw new InvalidOperationException("The game exited while its skin transaction was pending.");
            var state = ReadCommit();
            if (state.Id != manifest.Id) throw new InvalidOperationException("The game transaction receipt changed. Close Trackmania before recovering the installation.");
            if (state.State is 3 or 4 or 5)
            {
                var frame = ReadFrame();
                var stopped = await StopAsync();
                if (state.State == 3 && state.Refreshed == 1 && state.ThreadId == frame.ThreadId && frame.MixedThreads == 0 && frame.Fault == 0)
                {
                    if (!await NativeSkinStage.MatchesTargetsAsync(root, manifest, true, CancellationToken.None))
                        throw new IOException("The refreshed skin files did not match their receipt; the recovery record was preserved.");
                    NativeSkinStage.Complete(root, manifest);
                    return new(manifest.Id, state.ThreadId, true, true, stopped, stopped);
                }
                if (state.State == 5 && await NativeSkinStage.MatchesTargetsAsync(root, manifest, false, CancellationToken.None))
                    NativeSkinStage.Complete(root, manifest);
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                throw new InvalidOperationException(state.State == 5 ? "The queued skin change expired or was cancelled. No skin was applied."
                    : $"The game could not apply the skin (Windows error {state.Error}). Close Trackmania before retrying so its skin cache can reload. Recovery files were preserved.");
            }
            if (token.IsCancellationRequested || manifest.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                stoppingAt ??= DateTimeOffset.UtcNow;
                if (state.State == 1) session.Invoke("MoreCarsCancelSkinCommit", Convert.FromHexString(manifest.Id));
                if (DateTimeOffset.UtcNow - stoppingAt > TimeSpan.FromSeconds(30))
                    throw new TimeoutException("The game has not finished the skin transaction. Its recovery record was preserved; close Trackmania before retrying.");
            }
            var waiting = state.State == 1;
            if (previousWaiting != waiting)
            {
                // Network progress failures must not orphan a submitted file transaction.
                try { await reportWaiting(waiting).WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or TimeoutException) { }
                previousWaiting = waiting;
            }
            await Task.Delay(200, CancellationToken.None);
        }
    }

    private sealed record Frame(uint Running, ulong Frames, uint ThreadId, uint MixedThreads, uint Fault, uint Stopped);
    private Frame ReadFrame()
    {
        var data = session.Read("MoreCarsProbeState", 48);
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        if (U(0) != 1) throw new InvalidDataException("Unknown game frame protocol.");
        return new(U(4), BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)), U(16), U(20), U(36), U(40));
    }
    private sealed record Commit(uint State, string Id, uint Error, uint Refreshed, uint ThreadId);
    private Commit ReadCommit()
    {
        var data = session.Read("MoreCarsCommitState", 40);
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        if (U(0) != 1 || U(4) > 5) throw new InvalidDataException("Unknown native skin protocol.");
        return new(U(4), Convert.ToHexString(data.AsSpan(8, 16)).ToLowerInvariant(), U(24), U(32), U(36));
    }
    private async Task<bool> StopAsync()
    {
        if (!attached) return true;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var stopped = session.Invoke("MoreCarsStopProbe");
            if (stopped == 40) { await Task.Delay(100); continue; }
            if (stopped != 0) throw new InvalidOperationException($"The native game callback could not be restored ({stopped}). Close Trackmania before continuing.");
            attached = false;
            var frame = ReadFrame();
            var memory = session.Memory;
            var app = memory.Pointer(memory.At(GameProfile.AppSingleton));
            var command = memory.Pointer(memory.Pointer(app + GameProfile.AppCommands) + GameProfile.AfterLoopCommand);
            if (frame.Running != 0 || frame.Stopped != 1 || memory.Pointer(command + GameProfile.CommandCallback) != memory.At(GameProfile.AfterMainLoop))
                throw new InvalidOperationException("The game's original callback was not restored. Close Trackmania before continuing.");
            return true;
        }
        throw new TimeoutException("The native skin transaction is still running. Close Trackmania before recovering its files.");
    }
    public async ValueTask DisposeAsync()
    {
        try { if (attached && !session.HasExited) await StopAsync(); }
        finally { session.Dispose(); }
    }
}
