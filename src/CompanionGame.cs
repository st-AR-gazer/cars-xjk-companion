using System.Diagnostics;

namespace MoreCars.Companion;

internal static class CompanionGame
{
    public static bool IsRoot(string path) => !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(Path.Combine(path, "GameData")) && File.Exists(Path.Combine(path, "Trackmania.exe"));

    public static bool IsRunning(string root, bool treatUnknownAsRunning = false)
    {
        var processes = Process.GetProcessesByName("Trackmania");
        try
        {
            var expected = Path.GetFullPath(Path.Combine(root, "Trackmania.exe"));
            foreach (var process in processes)
            {
                try
                {
                    var filename = process.MainModule?.FileName;
                    if (treatUnknownAsRunning && filename is null) return true;
                    if (CompanionPlatform.PathComparer.Equals(filename, expected)) return true;
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    if (treatUnknownAsRunning) return true;
                }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static void Launch(CompanionSettings settings)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Launch Trackmania through your Wine or Proton launcher.");
        if (!IsRoot(settings.TrackmaniaRoot)) throw new InvalidOperationException("Choose Trackmania first.");
        if (IsRunning(settings.TrackmaniaRoot)) return;
        var executable = Path.Combine(settings.TrackmaniaRoot, "Trackmania.exe");
        ReleaseInstaller.RequireNoLinks(executable);
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = settings.TrackmaniaRoot });
    }

    public static Task<string?> SelectAsync(string current, bool automatic, Action? onPickerOpened = null)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(SelectRoot(current, Environment.GetEnvironmentVariable("MORECARS_TRACKMANIA_ROOT") ?? "",
                    automatic,
#if WINDOWS
                    CompanionPlatform.DetectCommonTrackmaniaRoot,
#else
                    () => null,
#endif
                    () => { onPickerOpened?.Invoke(); return CompanionPlatform.SelectTrackmaniaRoot(current); }, IsRoot));
            }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "More Cars game selection" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static string? SelectRoot(string current, string configured, bool automatic,
        Func<string?> detect, Func<string?> browse, Func<string, bool> isRoot)
    {
        if (!automatic) return browse();
        if (isRoot(current)) return current;
        if (isRoot(configured)) return configured;
        var detected = detect();
        return detected is not null && isRoot(detected) ? detected : null;
    }
}
