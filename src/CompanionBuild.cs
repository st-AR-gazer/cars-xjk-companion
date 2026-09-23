namespace MoreCars.Companion;

internal static class CompanionBuild
{
    internal static string Version { get; } = typeof(CompanionBuild).Assembly.GetName().Version!.ToString(3);
}
