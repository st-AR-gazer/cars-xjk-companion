using System.Diagnostics;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif

namespace MoreCars.Companion;

internal static class CompanionTray
{
    public static void OpenWebsite(string origin, string action = "connect", string deviceId = "")
    {
        if (action is not ("connect" or "update" or "repair"))
            throw new ArgumentOutOfRangeException(nameof(action));
        if (deviceId.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(deviceId, "^installation_[a-fA-F0-9]{32}$"))
            throw new ArgumentOutOfRangeException(nameof(deviceId));
        var uri = new Uri(origin);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidOperationException("The website origin is invalid.");
        var path = "/?companion=" + action + (deviceId.Length > 0 ? "&device=" + deviceId : "");
        Process.Start(new ProcessStartInfo(new Uri(uri, path).AbsoluteUri) { UseShellExecute = true });
    }

    public static async Task RunAsync(Func<CancellationToken, Task> run)
    {
        using var lifetime = new CancellationTokenSource();
#if WINDOWS
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new ApplicationContext();
            using var menu = new ContextMenuStrip();
            menu.Items.Add("Open Cars website", null, (_, _) => OpenWebsite(CompanionStorage.LoadSettings().ApiOrigin));
            menu.Items.Add("Quit companion", null, (_, _) => lifetime.Cancel());
            using var icon = new NotifyIcon {
                Text = "More Cars Companion", Icon = SystemIcons.Application,
                ContextMenuStrip = menu, Visible = true
            };
            icon.DoubleClick += (_, _) => OpenWebsite(CompanionStorage.LoadSettings().ApiOrigin);
            var host = Task.Run(() => run(lifetime.Token));
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) => { if (host.IsCompleted) context.ExitThread(); };
            timer.Start();
            Application.Run(context);
            icon.Visible = false;
            try { host.GetAwaiter().GetResult(); completed.SetResult(); }
            catch (Exception error) { completed.SetException(error); }
        }) { Name = "More Cars companion tray", IsBackground = false };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task;
#else
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        try { await run(lifetime.Token); }
        finally { Console.CancelKeyPress -= cancel; }
#endif
    }
}
