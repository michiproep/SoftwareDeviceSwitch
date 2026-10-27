namespace SoftwareDeviceSwitch;

public sealed partial class SwitchWindow
{
    readonly ShortcutRecorder shortcutRecorder = new();
    readonly Label shortcutStatus = EditorTheme.Label("No shortcut assigned.", 9, EditorTheme.Muted);
    GlobalShortcuts? shortcutManager;
    ProfileShortcut? pendingShortcut;
    bool shortcutActivationPending;

    Control BuildShortcutCard()
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(EditorTheme.Label("Keyboard shortcut", 12, style: FontStyle.Bold), 0, 0);
        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        controls.Controls.Add(FieldFrame(shortcutRecorder, new Padding(0, 0, 8, 2)), 0, 0);
        var record = EditorTheme.Button("Record", 100);
        var clear = EditorTheme.Button("Clear", 84);
        record.Click += (_, _) => shortcutRecorder.Focus();
        clear.Click += (_, _) =>
        {
            if (pendingShortcut == null) return;
            shortcutManager?.DiscardDraft();
            pendingShortcut = null;
            RefreshShortcutEditor();
            SetShortcutStatus("Save profile to remove the saved shortcut.");
            MarkDirty();
        };
        controls.Controls.Add(record, 1, 0);
        controls.Controls.Add(clear, 2, 0);
        layout.Controls.Add(controls, 0, 1);
        shortcutStatus.AutoSize = false;
        shortcutStatus.Dock = DockStyle.Fill;
        shortcutStatus.Margin = new Padding(0, 5, 0, 0);
        layout.Controls.Add(shortcutStatus, 0, 2);
        card.Controls.Add(layout);
        shortcutRecorder.CaptureStarted += () =>
        {
            shortcutRecorder.Text = "Press your shortcut...";
            SetShortcutStatus("Use Ctrl or Alt with a key. Ctrl + Alt + < works on German keyboards.\r\nPress Tab or Esc to finish recording.");
        };
        shortcutRecorder.Captured += CaptureProfileShortcut;
        shortcutRecorder.CaptureFailed += error => SetShortcutStatus(error, true);
        shortcutRecorder.Leave += (_, _) => RefreshShortcutEditor();
        tips.SetToolTip(shortcutRecorder, "Record the actual keys, including < on a German keyboard. Windows checks global registrations; app-specific shortcuts cannot be detected.");
        return card;
    }

    void InitializeShortcuts()
    {
        shortcutManager = new GlobalShortcuts();
        shortcutManager.Activated += async name =>
        {
            if (busy || shortcutActivationPending || !Enabled || shortcutRecorder.ContainsFocus || IsDisposed) return;
            shortcutActivationPending = true;
            try
            {
                // Transfer the keyboard after key-up so Ctrl/Alt are not left held on either PC.
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (library.Profiles.TryGetValue(name, out var profile) && profile.Shortcut?.IsPressed() == true)
                {
                    if (IsDisposed || DateTime.UtcNow >= deadline) return;
                    await Task.Delay(20);
                }
                if (!IsDisposed && !busy && Enabled && !shortcutRecorder.ContainsFocus) await SwitchFromTray(name);
            }
            finally { shortcutActivationPending = false; }
        };
        var failures = new List<string>();
        foreach (var pair in library.Profiles)
        {
            if (pair.Value.Shortcut == null) continue;
            if (shortcutManager.TryReserve(pair.Key, pair.Value.Shortcut, out string error)) shortcutManager.Commit(pair.Key, pair.Value.Shortcut);
            else failures.Add($"Shortcut for {pair.Key}: {error}");
        }
        RefreshShortcutEditor();
        if (failures.Count > 0) SetStatus(string.Join(" ", failures), true);
    }

    void LoadProfileShortcut(Profile? profile)
    {
        shortcutManager?.DiscardDraft();
        pendingShortcut = profile?.Shortcut;
        RefreshShortcutEditor();
    }

    void CaptureProfileShortcut(ProfileShortcut shortcut)
    {
        if (shortcutManager == null || string.IsNullOrEmpty(currentName)) return;
        if (!shortcutManager.TryReserve(currentName, shortcut, out string error))
        {
            shortcutRecorder.Text = pendingShortcut?.DisplayName() ?? "No shortcut";
            SetShortcutStatus(error, true);
            return;
        }
        bool changed = pendingShortcut != shortcut;
        pendingShortcut = shortcut;
        RefreshShortcutEditor();
        if (changed) MarkDirty();
    }

    void RefreshShortcutEditor()
    {
        shortcutRecorder.Text = pendingShortcut?.DisplayName() ?? "No shortcut - click Record";
        if (pendingShortcut == null)
            SetShortcutStatus("Click Record, press your keys, then Save. Works while the tool is running.\r\nChecks global conflicts; app-specific shortcuts cannot be detected.");
        else if (shortcutManager?.IsRegistered(currentName, pendingShortcut) == true)
            SetShortcutStatus("Active globally - executes this saved profile, including from the tray.");
        else if (library.Profiles.TryGetValue(currentName, out var profile) && profile.Shortcut == pendingShortcut)
            SetShortcutStatus("This saved shortcut is unavailable. Record a different combination.", true);
        else SetShortcutStatus("Available and reserved. Save profile to activate the shortcut.");
    }

    void SetShortcutStatus(string message, bool error = false)
    {
        shortcutStatus.Text = message;
        shortcutStatus.ForeColor = error ? Color.FromArgb(174, 58, 64) : EditorTheme.Muted;
        tips.SetToolTip(shortcutStatus, message);
    }
}
