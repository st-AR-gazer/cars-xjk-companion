namespace MoreCars.Companion;

internal static class CompanionBuild
{
    // Assembly version, heartbeat and download metadata share the project version.
    internal static string Version { get; } = typeof(CompanionBuild).Assembly.GetName().Version!.ToString(3);
}
