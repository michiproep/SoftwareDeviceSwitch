using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SoftwareDeviceSwitch;

public sealed partial class SwitchWindow
{
    readonly CheckBox startWithWindows = new()
    {
        Text = "Start with Windows", AutoSize = true, Font = EditorTheme.Font(9),
        ForeColor = Color.FromArgb(222, 229, 243), Cursor = Cursors.Hand,
        Margin = new Padding(0, 5, 0, 0), UseVisualStyleBackColor = true
    };
    NotifyIcon? trayIcon;
    ContextMenuStrip? trayMenu;
    ToolStripMenuItem? trayStartup;
    Icon? applicationIcon;
    bool startHidden;
    bool exitRequested;
    bool changingTrayVisibility;
    FormWindowState restoreWindowState = FormWindowState.Normal;

    void InitializeTray()
    {
        applicationIcon = CreateApplicationIcon();
        Icon = applicationIcon;
        trayMenu = new ContextMenuStrip { Font = EditorTheme.Font(10), ShowImageMargin = false };
        var open = new ToolStripMenuItem("Open Device Switch", null, (_, _) => ShowFromTray()) { Font = EditorTheme.Font(10, FontStyle.Bold) };
        var profiles = new ToolStripMenuItem("Switch to") { ToolTipText = "Switch using a saved profile." };
        trayStartup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        trayStartup.Click += (_, _) => SetStartupEnabled(!trayStartup.Checked);
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitFromTray());
        trayMenu.Items.AddRange([open, new ToolStripSeparator(), profiles, new ToolStripSeparator(), trayStartup, new ToolStripSeparator(), exit]);
        trayMenu.Opening += (_, _) =>
        {
            foreach (var item in profiles.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
            profiles.DropDownItems.Clear();
            foreach (string name in library.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ToolStripMenuItem(name) { ShortcutKeyDisplayString = library.Profiles[name].Shortcut?.DisplayName() ?? "" };
                item.Click += async (_, _) => await SwitchFromTray(name);
                profiles.DropDownItems.Add(item);
            }
            profiles.Enabled = !busy && profiles.DropDownItems.Count > 0;
            exit.Enabled = !busy;
            RefreshStartupState();
        };
        trayIcon = new NotifyIcon { Icon = applicationIcon, Text = "Device Switch", ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        startWithWindows.Click += (_, _) => SetStartupEnabled(startWithWindows.Checked);
        tips.SetToolTip(startWithWindows, "Start in the system tray when you sign in to Windows. Applies only to your Windows account.");
        RefreshStartupState();
        Resize += (_, _) =>
        {
            if (changingTrayVisibility) return;
            if (WindowState == FormWindowState.Minimized) HideToTray();
            else restoreWindowState = WindowState;
        };
    }

    void RefreshStartupState()
    {
        try
        {
            bool enabled = WindowsStartup.IsEnabled();
            startWithWindows.Checked = enabled;
            if (trayStartup != null) trayStartup.Checked = enabled;
        }
        catch (Exception error)
        {
            SetStatus("Could not read the Windows startup setting: " + error.Message, true);
        }
    }

    void SetStartupEnabled(bool enabled)
    {
        try
        {
            WindowsStartup.SetEnabled(enabled);
            startWithWindows.Checked = enabled;
            if (trayStartup != null) trayStartup.Checked = enabled;
            SetStatus(enabled ? "Device Switch will start in the tray when you sign in." : "Start with Windows is turned off.");
        }
        catch (Exception error)
        {
            RefreshStartupState();
            SetStatus("Could not change the Windows startup setting: " + error.Message, true);
            ShowFromTray();
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && startHidden)
        {
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
    }

    void HideToTray()
    {
        if (changingTrayVisibility) return;
        changingTrayVisibility = true;
        try { Hide(); ShowInTaskbar = false; }
        finally { changingTrayVisibility = false; }
    }

    internal void ShowFromTray()
    {
        if (IsDisposed || Disposing) return;
        changingTrayVisibility = true;
        try
        {
            startHidden = false;
            ShowInTaskbar = true;
            Show();
            WindowState = restoreWindowState;
            Activate();
        }
        finally { changingTrayVisibility = false; }
    }

    void HandleWindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        if (exitRequested && (busy || !ConfirmLeaveCurrent()))
        {
            e.Cancel = true;
            exitRequested = false;
            ShowFromTray();
        }
    }

    void ExitFromTray()
    {
        if (busy) return;
        if (dirty || monitors.IsCurrentCellInEditMode || devices.IsCurrentCellInEditMode) ShowFromTray();
        exitRequested = true;
        Close();
    }

    async Task SwitchFromTray(string name)
    {
        await RunBusy(async () =>
        {
            try
            {
                if (!library.Profiles.TryGetValue(name, out var profile)) throw new InvalidOperationException("That profile is no longer available.");
                var commands = Switching.Plan(profile);
                log.AppendText($"\r\nSwitching saved profile: {name}\r\n");
                await Switching.RunAll(commands, message => log.AppendText(message + Environment.NewLine));
                SetStatus($"Commands sent for {name}.");
            }
            catch (Exception error)
            {
                SetStatus("Switch failed: " + error.Message, true);
                ShowFromTray();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            shortcutManager?.Dispose();
            shortcutManager = null;
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            trayMenu?.Dispose();
            applicationIcon?.Dispose();
            trayIcon = null;
            trayMenu = null;
            applicationIcon = null;
            tips.Dispose();
        }
        base.Dispose(disposing);
    }

    static Icon CreateApplicationIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var shape = EditorTheme.Rounded(new RectangleF(0, 0, 31, 31), 7);
            using var fill = new SolidBrush(EditorTheme.Accent);
            using var pen = new Pen(Color.White, 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.FillPath(fill, shape);
            graphics.DrawRectangle(pen, 6, 8, 19, 13);
            graphics.DrawLine(pen, 13, 25, 19, 25);
            graphics.DrawLine(pen, 16, 21, 16, 25);
            graphics.DrawLine(pen, 10, 14, 21, 14);
            graphics.DrawLines(pen, [new PointF(18, 11), new PointF(21, 14), new PointF(18, 17)]);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr icon);
}
