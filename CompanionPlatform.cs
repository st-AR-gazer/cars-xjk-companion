using System.ComponentModel;
using System.Diagnostics;
using System.Text;
#if WINDOWS
using Microsoft.Win32;
using System.Windows.Forms;
#endif

namespace MoreCars.Companion;

internal static class CompanionPlatform
{
    private const string ApplicationName = "More Cars Companion";
    private const string ProtocolName = "morecars";

    public static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string ExecutableFileName => OperatingSystem.IsWindows()
        ? "MoreCarsCompanion.exe"
        : "MoreCarsCompanion";

    public static string DataDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(WindowsDataHome(), "Xjk", "MoreCarsCompanion")
        : Path.Combine(XdgDirectory("XDG_DATA_HOME", ".local/share"), "Xjk", "MoreCarsCompanion");

    public static string ConfigDirectory => OperatingSystem.IsWindows()
        ? DataDirectory
        : Path.Combine(XdgDirectory("XDG_CONFIG_HOME", ".config"), "Xjk", "MoreCarsCompanion");

    public static string CacheDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(DataDirectory, "cache")
        : Path.Combine(XdgDirectory("XDG_CACHE_HOME", ".cache"), "Xjk", "MoreCarsCompanion");

    public static void InitializeUi()
    {
#if WINDOWS
        ApplicationConfiguration.Initialize();
#endif
    }

    public static void ShowInformation(string message)
    {
#if WINDOWS
        MessageBox.Show(message, ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
        if (!TryLinuxDialog("--info", "--msgbox", message)) Console.WriteLine(message);
#endif
    }

    public static void ShowError(string message)
    {
#if WINDOWS
        MessageBox.Show(message, ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
        if (!TryLinuxDialog("--error", "--error", message)) Console.Error.WriteLine($"{ApplicationName}: {message}");
#endif
    }

    public static string? SelectTrackmaniaRoot(string currentPath)
    {
        var configuredPath = Environment.GetEnvironmentVariable("MORECARS_TRACKMANIA_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredPath)) return configuredPath.Trim();

#if WINDOWS
        using var picker = new FolderBrowserDialog
        {
            Description = "Choose the Trackmania installation folder containing GameData.",
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : "",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        return picker.ShowDialog() == DialogResult.OK ? picker.SelectedPath : null;
#else
        var initialPath = Directory.Exists(currentPath)
            ? currentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var zenity = RunProcess("zenity", [
            "--file-selection", "--directory", "--title", "Choose the Trackmania folder containing GameData",
            "--filename", initialPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar
        ]);
        if (zenity is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(zenity.Output)) return zenity.Output.Trim();
        if (zenity is not null && string.IsNullOrWhiteSpace(zenity.Error)) return null;

        var kdialog = RunProcess("kdialog", [
            "--title", ApplicationName, "--getexistingdirectory", initialPath,
            "Choose the Trackmania folder containing GameData"
        ]);
        if (kdialog is { ExitCode: 0 } && !string.IsNullOrWhiteSpace(kdialog.Output)) return kdialog.Output.Trim();
        if (kdialog is not null && string.IsNullOrWhiteSpace(kdialog.Error)) return null;

        if (!Console.IsInputRedirected)
        {
            Console.Write("Trackmania folder containing GameData: ");
            return Console.ReadLine()?.Trim();
        }
        throw new InvalidOperationException(
            "No Linux folder picker is available. Install zenity or kdialog, or set MORECARS_TRACKMANIA_ROOT before pairing.");
#endif
    }

    public static void EnsureExecutable(string executablePath)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.SetUnixFileMode(
            executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    public static void ProtectSettingsFile(string settingsPath)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.SetUnixFileMode(settingsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void RegisterProtocol(string executablePath)
    {
        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolName}");
            key.SetValue("", $"URL:{ApplicationName}");
            key.SetValue("URL Protocol", "");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{executablePath}\" \"%1\"");
            return;
#else
            throw new PlatformNotSupportedException("Use the net8.0-windows companion build on Windows.");
#endif
        }
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("More Cars Companion currently supports Windows and Linux.");

        var applicationsDirectory = Path.Combine(XdgDirectory("XDG_DATA_HOME", ".local/share"), "applications");
        Directory.CreateDirectory(applicationsDirectory);
        var desktopPath = Path.Combine(applicationsDirectory, "morecars-companion.desktop");
        var desktopEntry = string.Join('\n',
        [
            "[Desktop Entry]",
            "Type=Application",
            $"Name={ApplicationName}",
            $"Exec={DesktopExecQuote(executablePath)} %u",
            "NoDisplay=true",
            "Terminal=false",
            "StartupNotify=true",
            "MimeType=x-scheme-handler/morecars;",
            "Categories=Utility;",
            ""
        ]);
        CompanionStorage.AtomicWrite(desktopPath, desktopEntry);

        var registration = RunProcess(
            "xdg-mime",
            ["default", Path.GetFileName(desktopPath), "x-scheme-handler/morecars"]);
        if (registration is null)
            throw new InvalidOperationException("xdg-mime is required to register the morecars:// browser protocol.");
        if (registration.ExitCode != 0)
            throw new InvalidOperationException($"xdg-mime could not register morecars://: {registration.Error.Trim()}");

        _ = RunProcess("update-desktop-database", [applicationsDirectory]);
    }

    public static void UnregisterProtocol()
    {
        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProtocolName}", false);
            return;
#else
            throw new PlatformNotSupportedException("Use the net8.0-windows companion build on Windows.");
#endif
        }
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("More Cars Companion currently supports Windows and Linux.");

        var applicationsDirectory = Path.Combine(XdgDirectory("XDG_DATA_HOME", ".local/share"), "applications");
        var desktopPath = Path.Combine(applicationsDirectory, "morecars-companion.desktop");
        RequireOwnedUninstallTarget(desktopPath);
        if (File.Exists(desktopPath)) File.Delete(desktopPath);
        _ = RunProcess("update-desktop-database", [applicationsDirectory]);
    }

    public static void RemoveAfterCurrentProcessExits(IReadOnlyList<string> targets)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Delayed removal is only required on Windows.");
        foreach (var target in targets) RequireOwnedUninstallTarget(target);

        var encodedTargets = targets
            .Select(path => Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(path))))
            .Select(value => $"'{value}'");
        var script = string.Join(';',
        [
            "$ErrorActionPreference='SilentlyContinue'",
            $"Wait-Process -Id {Environment.ProcessId} -Timeout 30",
            $"$encoded=@({string.Join(',', encodedTargets)})",
            "$paths=$encoded|ForEach-Object{[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_))}",
            "$paths|Sort-Object Length -Descending|ForEach-Object{if(Test-Path -LiteralPath $_){Remove-Item -LiteralPath $_ -Recurse -Force}}"
        ]);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encodedScript })
            start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start the uninstall cleanup helper.");
    }

    internal static void RequireOwnedUninstallTarget(string path)
    {
        var target = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var allowedRoots = new[] { DataDirectory, ConfigDirectory, CacheDirectory }
            .Select(candidate => Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(PathComparer);
        var desktopEntry = OperatingSystem.IsLinux()
            ? Path.GetFullPath(Path.Combine(XdgDirectory("XDG_DATA_HOME", ".local/share"), "applications", "morecars-companion.desktop"))
            : "";
        if (allowedRoots.Any(root => PathComparer.Equals(target, root) ||
                                     target.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))) return;
        if (desktopEntry.Length > 0 && PathComparer.Equals(target, desktopEntry)) return;
        throw new InvalidOperationException("The uninstall target is outside More Cars Companion's owned directories.");
    }

    private static string WindowsDataHome()
    {
        var configured = Environment.GetEnvironmentVariable("MORECARS_DATA_HOME");
        if (string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!Path.IsPathFullyQualified(configured))
            throw new InvalidDataException("MORECARS_DATA_HOME must be an absolute directory path.");
        return Path.GetFullPath(configured);
    }

    private static string XdgDirectory(string variableName, string fallbackSuffix)
    {
        var configured = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured))
            return Path.GetFullPath(configured);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            throw new DirectoryNotFoundException("The current user's home directory is unavailable.");
        return Path.GetFullPath(Path.Combine(profile, fallbackSuffix.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DesktopExecQuote(string value)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidDataException("The companion executable path cannot be registered safely.");
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

#if !WINDOWS
    private static bool TryLinuxDialog(string zenityMode, string kdialogMode, string message)
    {
        var zenity = RunProcess("zenity", [zenityMode, "--title", ApplicationName, "--text", message]);
        if (zenity is { ExitCode: 0 }) return true;
        var kdialog = RunProcess("kdialog", ["--title", ApplicationName, kdialogMode, message]);
        return kdialog is { ExitCode: 0 };
    }
#endif

    private static ProcessResult? RunProcess(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output, error);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
