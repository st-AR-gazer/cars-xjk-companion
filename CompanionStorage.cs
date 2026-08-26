using System.Text.Json;

namespace MoreCars.Companion;

internal static class CompanionStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string AppDirectory => CompanionPlatform.DataDirectory;

    public static string CacheDirectory => CompanionPlatform.CacheDirectory;

    public static string SettingsPath => Path.Combine(CompanionPlatform.ConfigDirectory, "settings.json");
    public static string InstalledExecutablePath => Path.Combine(AppDirectory, CompanionPlatform.ExecutableFileName);

    public static CompanionSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return new CompanionSettings();
        var settings = JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(SettingsPath), JsonOptions);
        if (settings is null || settings.Schema != "morecars.companion-settings.v1")
            throw new InvalidDataException("The companion settings file has an unsupported schema.");
        return settings;
    }

    public static void SaveSettings(CompanionSettings settings)
    {
        Directory.CreateDirectory(AppDirectory);
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        CompanionPlatform.ProtectSettingsFile(SettingsPath);
    }

    public static void AtomicWrite(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("The destination has no parent.");
        Directory.CreateDirectory(directory);
        var partial = path + ".partial";
        var backup = path + ".backup";
        File.WriteAllText(partial, contents);
        if (File.Exists(backup)) File.Delete(backup);
        if (File.Exists(path)) File.Move(path, backup);
        try
        {
            File.Move(partial, path);
            if (File.Exists(backup)) File.Delete(backup);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(backup)) File.Move(backup, path);
            throw;
        }
    }

    public static bool InstallSelf()
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("The companion executable path is unavailable.");
        Directory.CreateDirectory(AppDirectory);
        var installed = Path.GetFullPath(InstalledExecutablePath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(source), installed))
        {
            var sourceDirectory = Path.GetDirectoryName(source)!;
            var companionFiles = Directory.EnumerateFiles(sourceDirectory, "MoreCarsCompanion*")
                .Where(path => IsCompanionRuntimeFile(path, source));
            foreach (var sourceFile in companionFiles)
            {
                var isRunningExecutable = StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(sourceFile), Path.GetFullPath(source));
                var destination = isRunningExecutable
                    ? installed
                    : Path.Combine(AppDirectory, Path.GetFileName(sourceFile));
                var partial = destination + ".new";
                File.Copy(sourceFile, partial, true);
                File.Move(partial, destination, true);
            }
        }
        CompanionPlatform.EnsureExecutable(installed);
        CompanionPlatform.RegisterProtocol(installed);
        return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(source), installed);
    }

    public static void Uninstall(bool keepData)
    {
        CompanionPlatform.UnregisterProtocol();
        var targets = UninstallTargets(keepData);
        var runningPath = Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException(
            "The companion executable path is unavailable."));
        var runningInstalledCopy = StringComparer.OrdinalIgnoreCase.Equals(runningPath, Path.GetFullPath(InstalledExecutablePath));

        if (OperatingSystem.IsWindows() && runningInstalledCopy)
        {
            CompanionPlatform.RemoveAfterCurrentProcessExits(targets);
            return;
        }

        foreach (var target in targets.OrderByDescending(path => path.Length)) RemoveOwnedTarget(target);
    }

    internal static IReadOnlyList<string> UninstallTargets(bool keepData)
    {
        if (keepData)
        {
            var files = new List<string> { InstalledExecutablePath };
            foreach (var name in new[]
                     {
                         "MoreCarsCompanion.dll",
                         "MoreCarsCompanion.deps.json",
                         "MoreCarsCompanion.runtimeconfig.json",
                         "MoreCarsCompanion.pdb"
                     })
                files.Add(Path.Combine(AppDirectory, name));
            return files.Select(Path.GetFullPath).Distinct(CompanionPlatform.PathComparer).ToArray();
        }

        var directories = new[]
            {
                CompanionPlatform.DataDirectory,
                CompanionPlatform.ConfigDirectory,
                CompanionPlatform.CacheDirectory
            }
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(CompanionPlatform.PathComparer)
            .OrderBy(path => path.Length)
            .ToList();
        return directories
            .Where(candidate => !directories.Any(parent =>
                !CompanionPlatform.PathComparer.Equals(parent, candidate) &&
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, CompanionPlatform.PathComparison)))
            .ToArray();
    }

    private static void RemoveOwnedTarget(string target)
    {
        CompanionPlatform.RequireOwnedUninstallTarget(target);
        if (File.Exists(target)) File.Delete(target);
        else if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    private static bool IsCompanionRuntimeFile(string path, string runningExecutable)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(path), Path.GetFullPath(runningExecutable))) return true;
        var name = Path.GetFileName(path);
        return name.Equals("MoreCarsCompanion.dll", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("MoreCarsCompanion.deps.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("MoreCarsCompanion.runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("MoreCarsCompanion.pdb", StringComparison.OrdinalIgnoreCase);
    }
}
