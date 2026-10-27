using System.ComponentModel;

namespace SoftwareDeviceSwitch;

public sealed partial class SwitchWindow : Form
{
    void PopulateProfileList()
    {
        loading = true;
        profileList.Items.Clear();
        foreach (var profileName in library.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            profileList.Items.Add(profileName);
        if (profileList.Items.Count > 0)
        {
            int index = profileList.Items.IndexOf(library.ActiveProfile);
            profileList.SelectedIndex = index >= 0 ? index : 0;
        }
        loading = false;
        if (profileList.SelectedItem is string selectedName) LoadProfile(selectedName);
        else ShowEmptyState();
        UpdateSummary();
    }

    void ProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (loading || profileList.SelectedItem is not string name || name.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return;
        if (!ConfirmLeaveCurrent())
        {
            loading = true;
            profileList.SelectedItem = currentName;
            loading = false;
            return;
        }
        LoadProfile(name);
    }

    bool ConfirmLeaveCurrent()
    {
        if (!string.IsNullOrEmpty(currentName) && (!monitors.EndEdit() || !devices.EndEdit())) return false;
        if (!dirty) return true;
        var answer = MessageBox.Show(this, $"Save changes to '{currentName}'?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveCurrent();
    }

    void LoadProfile(string name)
    {
        loading = true;
        currentName = name;
        library.Activate(name);
        var profile = library.Profiles[name];
        LoadProfileShortcut(profile);
        heading.Text = name;
        targetName.Text = profile.Target;
        channel.Value = Math.Clamp(profile.Channel, 1, 3);
        monitorSettings = new BindingList<MonitorSetting>((profile.Monitors ?? []).Select(m => new MonitorSetting { Enabled = m.Enabled, Selector = m.Selector, Input = m.Input }).ToList());
        deviceSettings = new BindingList<DeviceSetting>((profile.Devices ?? []).Select(d => new DeviceSetting { Enabled = d.Enabled, Name = d.Name, Receiver = d.Receiver, ReceiverPath = d.ReceiverPath, Slot = d.Slot, Feature = d.Feature, Function = d.Function }).ToList());
        monitors.DataSource = monitorSettings;
        BindMonitorColumns();
        devices.DataSource = deviceSettings;
        BindDeviceColumns();
        dirty = false;
        SetStatus("Ready to switch.");
        log.Text = $"Profile file: {library.ConfigPath}\r\nSelected profile: {name}\r\n\r\nDiscovery and switch details will appear here.";
        loading = false;
        UpdateSaveState();
    }

    void ShowEmptyState()
    {
        loading = true;
        currentName = "";
        LoadProfileShortcut(null);
        heading.Text = "Your desk, connected";
        targetName.Text = "";
        channel.Value = 1;
        monitorSettings = [];
        deviceSettings = [];
        monitors.DataSource = monitorSettings;
        devices.DataSource = deviceSettings;
        BindMonitorColumns();
        BindDeviceColumns();
        dirty = false;
        loading = false;
        SetStatus("Add your first profile using the sidebar.");
        log.Text = $"No profiles loaded. Profile folder: {AppPaths.ExecutableDirectory}";
        UpdateSaveState();
        saveBadge.Text = "No profile";
    }

    void BindMonitorColumns()
    {
        var options = monitorChoices.Select((m, i) => m with { Label = $"Screen {i + 1}: {m.Label.Split(" | ")[0]}" }).ToList();
        foreach (var setting in monitorSettings.Where(s => !string.IsNullOrWhiteSpace(s.Selector)))
            if (options.All(o => !o.Id.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase)))
            {
                var parts = setting.Selector.Split('\\');
                string model = parts.Length > 1 ? parts[1] : "Saved monitor";
                options.Add(new(setting.Selector, $"Screen {options.Count + 1}: {model} (saved connection)", "", "unknown"));
            }
        options.Insert(0, new("", "Choose a monitor...", "", "unknown"));
        monitors.AutoGenerateColumns = false;
        monitors.Columns.Clear();
        monitors.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Use", DataPropertyName = "Enabled", Width = 52, MinimumWidth = 52, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, HeaderCell = { Style = { Padding = new Padding(1), Alignment = DataGridViewContentAlignment.MiddleCenter } } });
        monitors.Columns.Add(new DataGridViewComboBoxColumn { Name = "Selector", HeaderText = "Monitor", DataPropertyName = "Selector", DataSource = options, ValueMember = "Id", DisplayMember = "Label", FillWeight = 50, MinimumWidth = 230, FlatStyle = FlatStyle.Flat });
        monitors.Columns.Add(new DataGridViewComboBoxColumn { Name = "Input", HeaderText = "Destination input", DataPropertyName = "Input", ValueType = typeof(int), DataSource = MonitorInputs.Options(MonitorInputs.CommonCodes.Concat(monitorSettings.Select(m => m.Input))), ValueMember = "Code", DisplayMember = "Label", FillWeight = 28, MinimumWidth = 185, FlatStyle = FlatStyle.Flat, ToolTipText = "Find monitors reads the inputs reported by each screen. Use Input guide for availability details and custom codes." });
        monitors.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurrentInput", HeaderText = "Current input", ReadOnly = true, FillWeight = 22, MinimumWidth = 155 });
        foreach (DataGridViewColumn column in monitors.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        RefreshMonitorInputCells();
    }

    async Task FindMonitors()
    {
        try
        {
            if (!monitors.EndEdit()) return;
            SetStatus("Reading monitor inputs. This can take a few seconds...");
            monitorChoices = await Task.Run(NativeHardware.GetMonitorChoices);
            bool migrated = false;
            foreach (var setting in monitorSettings)
            {
                var old = monitorChoices.FirstOrDefault(m => m.LegacySelector.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase));
                if (old != null && !old.Id.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase)) { setting.Selector = old.Id; migrated = true; }
            }
            BindMonitorColumns();
            if (migrated) MarkDirty();
            log.Text = monitorChoices.Count == 0 ? "No physical monitors found. Check the display connection and DDC/CI setting." : string.Join("\r\n\r\n", monitorChoices.Select(DescribeMonitorInputs));
            if (monitorChoices.Count == 0) SetStatus("No monitors found. Check the display connection and enable DDC/CI in the monitor's menu.", true);
            else SetStatus($"Found {monitorChoices.Count} monitor(s). Choose a destination port or open Input guide.");
        }
        catch (Exception e) { SetStatus("Monitor discovery failed: " + e.Message, true); }
    }

    async Task DiscoverDevices()
    {
        try
        {
            var available = NativeHardware.ListHid();
            var receiverGroups = deviceSettings.Select(d => (d.Receiver, d.ReceiverPath)).Distinct().ToList();
            foreach (var pair in receiverGroups)
            {
                var receiver = NativeHardware.ResolveReceiver(available, pair.Receiver, pair.ReceiverPath);
                using var handle = NativeHardware.OpenReceiver(receiver);
                var found = await NativeHardware.DiscoverChangeHost(handle, receiver);
                foreach (var result in found)
                {
                    if (result.FeatureIndex is not byte feature) continue;
                    var setting = deviceSettings.FirstOrDefault(d => d.Receiver.Equals(receiver.Receiver, StringComparison.OrdinalIgnoreCase) && d.Slot == result.Slot);
                    if (setting == null)
                    {
                        setting = new DeviceSetting { Name = $"Logitech device in slot {result.Slot}", Receiver = receiver.Receiver, Slot = result.Slot, Function = "1E" };
                        deviceSettings.Add(setting);
                    }
                    setting.ReceiverPath = receiver.Path;
                    setting.Feature = feature.ToString("X2");
                }
                log.Text = $"Receiver {receiver.Receiver}\r\n{receiver.Path}\r\n" + string.Join(Environment.NewLine, found.Select(r => $"Slot {r.Slot}: {r.Status}"));
            }
            devices.Refresh();
            MarkDirty();
            ShowActivity(true);
            SetStatus("Discovery complete. Review the device slots and features.");
        }
        catch (Exception e) { SetStatus("Device discovery failed: " + e.Message, true); }
    }

    Profile CommitToProfile()
    {
        if (string.IsNullOrWhiteSpace(currentName)) throw new InvalidOperationException("Select or add a profile first.");
        if (!monitors.EndEdit() || !devices.EndEdit()) throw new InvalidOperationException("Check the value in the active table cell before continuing.");
        return new Profile
        {
            Target = targetName.Text.Trim(),
            Channel = (int)channel.Value,
            Shortcut = pendingShortcut,
            Monitors = monitorSettings.Select(m => new MonitorSetting { Enabled = m.Enabled, Selector = m.Selector, Input = m.Input }).ToList(),
            Devices = deviceSettings.Select(d => new DeviceSetting { Enabled = d.Enabled, Name = d.Name, Receiver = d.Receiver, ReceiverPath = d.ReceiverPath, Slot = d.Slot, Feature = d.Feature, Function = d.Function }).ToList()
        };
    }

    bool SaveCurrent()
    {
        try
        {
            var profile = CommitToProfile();
            Switching.Plan(profile);
            if (shortcutManager == null) throw new InvalidOperationException("Shortcut registration is not ready.");
            if (!shortcutManager.TryReserve(currentName, profile.Shortcut, out string shortcutError)) throw new InvalidOperationException(shortcutError);
            library.Save(currentName, profile);
            shortcutManager.Commit(currentName, profile.Shortcut);
            RefreshShortcutEditor();
            dirty = false;
            UpdateSaveState();
            SetStatus($"{currentName} saved. Ready to switch.");
            return true;
        }
        catch (Exception e) { SetStatus("Could not save: " + e.Message, true); return false; }
    }

    async Task TestSelectedMonitor()
    {
        try
        {
            var profile = CommitToProfile();
            if (monitors.SelectedRows.Count == 0 || monitors.SelectedRows[0].DataBoundItem is not MonitorSetting selected)
                throw new Exception("Select a monitor row first.");
            if (MessageBox.Show(this, $"Set only this monitor to input {selected.Input}? The screen may go blank if the input is not connected.", "Test monitor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var test = new Profile { Target = currentName, Channel = profile.Channel, Monitors = [selected] };
            await Switching.RunAll(Switching.Plan(test), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Monitor command sent. Check the screen.");
        }
        catch (Exception e) { SetStatus("Monitor test failed: " + e.Message, true); }
    }

    async Task TestSelectedDevice()
    {
        try
        {
            var profile = CommitToProfile();
            if (devices.SelectedRows.Count == 0 || devices.SelectedRows[0].DataBoundItem is not DeviceSetting selected)
                throw new Exception("Select a device row first.");
            if (MessageBox.Show(this, $"Switch only {selected.Name} to channel {profile.Channel}?", "Test device", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var test = new Profile { Target = currentName, Channel = profile.Channel, Devices = [selected] };
            await Switching.RunAll(Switching.Plan(test), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Device report sent. Check which computer receives input.");
        }
        catch (Exception e) { SetStatus("Device test failed: " + e.Message, true); }
    }

    async Task TestProfile()
    {
        try
        {
            var profile = CommitToProfile();
            Switching.Plan(profile);
            if (!SaveCurrent()) return;
            if (MessageBox.Show(this, $"Switch monitors, mouse and keyboard to {profile.Target}?", "Test profile", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await Switching.RunAll(Switching.Plan(profile), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Windows accepted the profile commands. Check the other computer.");
        }
        catch (Exception e) { SetStatus("Switch failed: " + e.Message, true); }
    }

    void AddProfile()
    {
        string? name = PromptForName();
        if (name == null) return;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$"))
        {
            SetStatus("Use 1-40 letters, numbers, hyphens or underscores; start with a letter or number.");
            return;
        }
        if (library.Profiles.ContainsKey(name))
        {
            SetStatus($"Profile '{name}' already exists.");
            return;
        }
        if (!ConfirmLeaveCurrent()) return;
        var profile = Profile.Legacy(false);
        profile.Target = name;
        library.Save(name, profile);
        loading = true;
        profileList.Items.Add(name);
        profileList.SelectedItem = name;
        loading = false;
        LoadProfile(name);
        MarkDirty();
        SetStatus($"Added {name}. Configure it and save.");
    }

    void DeleteProfile()
    {
        if (profileList.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"Delete profile '{name}' and its settings?", "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            library.Delete(name);
            shortcutManager?.Remove(name);
            PopulateProfileList();
            SetStatus($"Deleted {name}.");
        }
        catch (Exception error) { SetStatus("Could not delete profile: " + error.Message, true); }
    }

    string? PromptForName()
    {
        using var dialog = new Form { Text = "New profile", ClientSize = new Size(460, 230), Font = EditorTheme.Font(), BackColor = EditorTheme.Canvas, AutoScaleMode = AutoScaleMode.Dpi, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Padding = new Padding(24) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(EditorTheme.Label("Create a profile", 19, style: FontStyle.Bold), 0, 0);
        layout.Controls.Add(EditorTheme.Label("Choose a name, such as NIQ or Work-PC.", 9.5f, EditorTheme.Muted), 0, 1);
        var input = new TextBox { Dock = DockStyle.Fill, Font = EditorTheme.Font(11), BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(250, 251, 254), MaxLength = 40 };
        layout.Controls.Add(FieldFrame(input, Padding.Empty), 0, 2);
        var hint = EditorTheme.Label("Use letters, numbers, hyphens or underscores.", 8.5f, EditorTheme.Muted);
        hint.Margin = new Padding(0, 7, 0, 0);
        layout.Controls.Add(hint, 0, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = EditorTheme.Button("Create profile", 132, true);
        var cancel = EditorTheme.Button("Cancel", 96);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons, 0, 4);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => input.Focus();
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }

    void MarkDirty()
    {
        if (!loading)
        {
            dirty = true;
            UpdateSaveState();
            SetStatus("You have unsaved changes.");
        }
    }

    void SetStatus(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? Color.FromArgb(174, 58, 64) : EditorTheme.Ink;
        tips.SetToolTip(status, message);
        if (error)
        {
            log.AppendText(Environment.NewLine + message + Environment.NewLine);
            ShowActivity(true);
        }
    }
}
