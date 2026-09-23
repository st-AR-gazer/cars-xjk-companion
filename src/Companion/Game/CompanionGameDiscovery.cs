using System.Security.Cryptography;
using System.Text;

namespace MoreCars.Companion;

internal static class CompanionGameDiscovery
{
    internal static string Identity(string path)
    {
        var normalized = NormalizePath(path);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        return "game_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full == Path.GetPathRoot(full) ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static IReadOnlyList<GameInstallation> Choices(IEnumerable<string> paths, Func<string, bool>? isRoot = null)
    {
        var valid = isRoot ?? CompanionGame.IsRoot;
        return paths.Where(path => !string.IsNullOrWhiteSpace(path) && valid(path))
            .Select(NormalizePath).Where(path => path.Length <= 1024).Distinct(CompanionPlatform.PathComparer).Take(16)
            .Select(path => new GameInstallation(Identity(path), path)).ToArray();
    }

    internal static IReadOnlyList<GameInstallation> Discover(string saved)
    {
        var roots = new List<string> { saved, Environment.GetEnvironmentVariable("MORECARS_TRACKMANIA_ROOT") ?? "" };
#if WINDOWS
        roots.AddRange(CompanionPlatform.CommonTrackmaniaCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)));
#endif
        return Choices(roots);
    }
}
