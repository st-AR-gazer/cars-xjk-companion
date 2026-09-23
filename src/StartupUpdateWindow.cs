using System.ComponentModel;
using System.Diagnostics;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif

namespace MoreCars.Companion;

internal static class StartupUpdateWindow
{
    internal static Task ShowStatusAsync(string title, string details, CancellationToken token)
    {
#if WINDOWS
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = CreateDialog(new StartupUpdateNotice(false, false, title, details));
                var close = (Button)dialog.AcceptButton!;
                close.Text = "Close";
                ((Button)dialog.CancelButton!).Visible = false;
                close.Click += (_, _) => dialog.Close();
                dialog.Shown += (_, _) => { dialog.Activate(); dialog.BringToFront(); };
                using var cancellation = token.Register(() =>
                {
                    try
                    {
                        if (dialog.IsHandleCreated && !dialog.IsDisposed)
                            dialog.BeginInvoke(() => dialog.Close());
                    }
                    catch (InvalidOperationException) { }
                });
                if (token.IsCancellationRequested) { result.TrySetCanceled(token); return; }
                Application.Run(dialog);
                result.TrySetResult();
            }
            catch (Exception error) { result.TrySetException(error); }
        }) { Name = "More Cars update status", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
#else
        Console.WriteLine($"{title}\n\n{details}");
        return Task.CompletedTask;
#endif
    }

    internal static Task<bool> ShowAsync(StartupUpdateNotice notice, CancellationToken token)
    {
#if WINDOWS
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = CreateDialog(notice);
                var selected = false;
                ((Button)dialog.AcceptButton!).Click += (_, _) => { selected = true; dialog.Close(); };
                ((Button)dialog.CancelButton!).Click += (_, _) => dialog.Close();
                dialog.Shown += (_, _) =>
                {
                    if (token.IsCancellationRequested) dialog.Close();
                    else { dialog.Activate(); dialog.BringToFront(); }
                };
                using var cancellation = token.Register(() =>
                {
                    try
                    {
                        if (dialog.IsHandleCreated && !dialog.IsDisposed)
                            dialog.BeginInvoke(() => dialog.Close());
                    }
                    catch (InvalidOperationException) { }
                });
                if (token.IsCancellationRequested) { result.TrySetCanceled(token); return; }
                Application.Run(dialog);
                result.TrySetResult(selected && !token.IsCancellationRequested);
            }
            catch (Exception error) { result.TrySetException(error); }
        }) { Name = "More Cars update notice", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
#else
        return ShowLinuxAsync(notice, token);
#endif
    }

#if WINDOWS
    private static Form CreateDialog(StartupUpdateNotice notice)
    {
        var dialog = new TaskbarUpdateForm
        {
            Text = "More Cars Companion",
            ClientSize = new Size(580, 430),
            MinimumSize = new Size(460, 320),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            ShowIcon = true,
            Icon = SystemIcons.Application,
            TopMost = true,
            AutoScaleMode = AutoScaleMode.Font,
            Padding = new Padding(18)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = notice.Title,
            Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 15, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var notes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = notice.Details.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            BackColor = SystemColors.Window,
            TabStop = false
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var install = new Button { Text = notice.InstallLabel, AutoSize = true, MinimumSize = new Size(172, 34) };
        var later = new Button { Text = "Not now", AutoSize = true, MinimumSize = new Size(96, 34) };
        buttons.Controls.Add(install);
        buttons.Controls.Add(later);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(notes, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = install;
        dialog.CancelButton = later;
        return dialog;
    }

    private sealed class TaskbarUpdateForm : Form
    {
        private const int WsExAppWindow = 0x00040000;
        private const int WsExToolWindow = 0x00000080;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle = (parameters.ExStyle | WsExAppWindow) & ~WsExToolWindow;
                return parameters;
            }
        }
    }
#else
    private static async Task<bool> ShowLinuxAsync(StartupUpdateNotice notice, CancellationToken token)
    {
        var text = notice.Title + "\n\n" + notice.Details;
        var zenity = await TryRunAsync("zenity", ["--question", "--title", "More Cars Companion", "--width", "580",
            "--text", text, "--ok-label", notice.InstallLabel, "--cancel-label", "Not now"], token);
        if (zenity.HasValue) return zenity.Value;
        var kdialog = await TryRunAsync("kdialog", ["--title", "More Cars Companion", "--yesno", text,
            "--yes-label", notice.InstallLabel, "--no-label", "Not now"], token);
        if (kdialog.HasValue) return kdialog.Value;
        Console.WriteLine(text);
        return false;
    }

    private static async Task<bool?> TryRunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return null;
            using var cancellation = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            });
            await process.WaitForExitAsync(token);
            return process.ExitCode == 0;
        }
        catch (Win32Exception) { return null; }
    }
#endif
}
