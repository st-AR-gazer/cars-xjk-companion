namespace MoreCars.Companion;

internal static class CompanionSkinGate
{
    internal static async Task WaitAsync(Func<bool> gameMayBeRunning, DateTimeOffset expiresAt,
        Func<Task> reportWaiting, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? delay = null)
    {
        while (gameMayBeRunning())
        {
            RequireUnexpired(expiresAt, cancellationToken);
            await reportWaiting();
            await (delay?.Invoke(cancellationToken) ?? Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        }
        RequireUnexpired(expiresAt, cancellationToken);
    }

    private static void RequireUnexpired(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= expiresAt)
            throw new InvalidOperationException("The skin change expired while waiting. Select the skin again to retry.");
    }
}
