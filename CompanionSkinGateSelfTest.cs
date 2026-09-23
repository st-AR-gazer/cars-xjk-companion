namespace MoreCars.Companion;

internal static class CompanionSkinGateSelfTest
{
    internal static async Task<bool> RunAsync()
    {
        var running = true;
        var waits = 0;
        var resumed = false;
        await CompanionSkinGate.WaitAsync(() => running, DateTimeOffset.UtcNow.AddMinutes(1),
            () => { waits++; return Task.CompletedTask; }, CancellationToken.None,
            _ => { if (waits == 2) running = false; return Task.CompletedTask; });
        resumed = !running && waits == 2;
        var expired = false;
        try
        {
            await CompanionSkinGate.WaitAsync(() => false, DateTimeOffset.UtcNow.AddSeconds(-1),
                () => Task.CompletedTask, CancellationToken.None);
        }
        catch (InvalidOperationException) { expired = true; }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = false;
        try
        {
            await CompanionSkinGate.WaitAsync(() => true, DateTimeOffset.UtcNow.AddMinutes(1),
                () => Task.CompletedTask, cancelled.Token);
        }
        catch (OperationCanceledException) { stopped = true; }
        return resumed && expired && stopped;
    }
}
