using System.ComponentModel;

namespace SoftwareDeviceSwitch;

public sealed class SwitchWindow : Form
{
    readonly ProfileLibrary library;
    readonly SplitContainer split = new() { Dock = DockStyle.Fill, Size = new Size(1220, 850), SplitterDistance = 235, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = true };
    readonly ListBox profileList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly TextBox targetName = new() { Width = 220 };
    readonly NumericUpDown channel = new() { Minimum = 1, Maximum = 3, Width = 100 };
    readonly DataGridView monitors = MakeGrid();
    readonly DataGridView devices = MakeGrid();
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9), Dock = DockStyle.Fill };
    readonly Label heading = new() { AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold) };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(850, 0) };
    List<NativeHardware.MonitorChoice> monitorChoices = [];
    BindingList<MonitorSetting> monitorSettings = [];
    BindingList<DeviceSetting> deviceSettings = [];
    string currentName = "";
    bool loading;
    bool dirty;

    static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        AllowUserToAddRows = false,
        AutoGenerateColumns = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };

    public SwitchWindow(ProfileLibrary library)
    {
        this.library = library;
        Text = "Device Switch";
        Size = new Size(1220, 850);
        MinimumSize = new Size(1000, 700);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        BuildProfileMenu();
        BuildSettingsView();
        Controls.Add(split);
        profileList.SelectedValueChanged += ProfileSelectionChanged;
        PopulateProfileList();
    }

    void BuildProfileMenu()
    {
        var menu = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        menu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        menu.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        menu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        menu.Controls.Add(new Label { Text = $"Profiles ({library.Profiles.Count})", AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        menu.Controls.Add(profileList, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        var add = new Button { Text = "Add", AutoSize = true };
        add.Click += (_, _) => AddProfile();
        var delete = new Button { Text = "Delete", AutoSize = true };
        delete.Click += (_, _) => DeleteProfile();
        buttons.Controls.AddRange([add, delete]);
        menu.Controls.Add(buttons, 0, 2);
        split.Panel1.Controls.Add(menu);
    }

    void BuildSettingsView()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var content = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 7 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        content.Controls.Add(heading, 0, 0);

        var destinationBox = new GroupBox { Text = "Destination computer", Dock = DockStyle.Fill, Height = 72, Padding = new Padding(12) };
        var destinationRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        destinationRow.Controls.Add(new Label { Text = "Computer name:", AutoSize = true, Margin = new Padding(3, 8, 12, 3) });
        destinationRow.Controls.Add(targetName);
        targetName.TextChanged += (_, _) => MarkDirty();
        destinationBox.Controls.Add(destinationRow);
        content.Controls.Add(destinationBox, 0, 1);

        var channelBox = new GroupBox { Text = "Destination channel", Dock = DockStyle.Fill, Height = 72, Padding = new Padding(12) };
        var channelRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        channelRow.Controls.Add(new Label { Text = "Logitech Easy-Switch channel:", AutoSize = true, Margin = new Padding(3, 8, 12, 3) });
        channelRow.Controls.Add(channel);
        channel.ValueChanged += (_, _) => MarkDirty();
        channelBox.Controls.Add(channelRow);
        content.Controls.Add(channelBox, 0, 2);

        content.Controls.Add(BuildMonitorSection(), 0, 3);
        content.Controls.Add(BuildDeviceSection(), 0, 4);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var save = new Button { Text = "Save profile", AutoSize = true };
        save.Click += (_, _) => SaveCurrent();
        var test = new Button { Text = "Test and switch", AutoSize = true };
        test.Click += async (_, _) => await TestProfile();
        footer.Controls.AddRange([save, test, status]);
        content.Controls.Add(footer, 0, 5);
        content.Controls.Add(log, 0, 6);
        scroll.Controls.Add(content);
        split.Panel2.Controls.Add(scroll);
    }

    Control BuildMonitorSection()
    {
        var group = new GroupBox { Text = "Monitors", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        var find = new Button { Text = "Find monitors", AutoSize = true };
        find.Click += (_, _) => FindMonitors();
        var test = new Button { Text = "Test selected monitor", AutoSize = true };
        test.Click += async (_, _) => await TestSelectedMonitor();
        actions.Controls.AddRange([find, test]);
        inner.Controls.Add(actions, 0, 0);
        monitors.CellValueChanged += (_, _) => MarkDirty();
        monitors.RowsAdded += (_, _) => MarkDirty();
        monitors.RowsRemoved += (_, _) => MarkDirty();
        inner.Controls.Add(monitors, 0, 1);
        group.Controls.Add(inner);
        return group;
    }

    Control BuildDeviceSection()
    {
        var section = new GroupBox { Text = "Mouse and keyboard", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(8) };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        var list = new Button { Text = "List HID devices", AutoSize = true };
        list.Click += (_, _) => { try { log.Text = string.Join(Environment.NewLine, NativeHardware.ListHid()); } catch (Exception e) { SetStatus(e.Message); } };
        var discover = new Button { Text = "Discover devices", AutoSize = true };
        discover.Click += async (_, _) => await DiscoverDevices();
        var test = new Button { Text = "Test selected device", AutoSize = true };
        test.Click += async (_, _) => await TestSelectedDevice();
        actions.Controls.AddRange([list, discover, test]);
        inner.Controls.Add(actions, 0, 0);
        devices.CellValueChanged += (_, _) => MarkDirty();
        devices.RowsAdded += (_, _) => MarkDirty();
        devices.RowsRemoved += (_, _) => MarkDirty();
        inner.Controls.Add(devices, 0, 1);
        section.Controls.Add(inner);
        return section;
    }

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
    }

    void ProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (loading || profileList.SelectedItem is not string name || name.Equals(currentName, StringComparison.OrdinalIgnoreCase)) return;
        if (dirty)
        {
            var answer = MessageBox.Show($"Save changes to '{currentName}' before selecting another profile?", "Unsaved changes", MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel)
            {
                loading = true;
                profileList.SelectedItem = currentName;
                loading = false;
                return;
            }
            if (answer == DialogResult.Yes && !SaveCurrent())
            {
                loading = true;
                profileList.SelectedItem = currentName;
                loading = false;
                return;
            }
        }
        LoadProfile(name);
    }

    void LoadProfile(string name)
    {
        loading = true;
        currentName = name;
        library.Activate(name);
        var profile = library.Profiles[name];
        heading.Text = name;
        targetName.Text = profile.Target;
        channel.Value = Math.Clamp(profile.Channel, 1, 3);
        monitorSettings = new BindingList<MonitorSetting>((profile.Monitors ?? []).Select(m => new MonitorSetting { Enabled = m.Enabled, Selector = m.Selector, Input = m.Input }).ToList());
        deviceSettings = new BindingList<DeviceSetting>((profile.Devices ?? []).Select(d => new DeviceSetting { Enabled = d.Enabled, Name = d.Name, Receiver = d.Receiver, ReceiverPath = d.ReceiverPath, Slot = d.Slot, Feature = d.Feature, Function = d.Function }).ToList());
        monitors.DataSource = monitorSettings;
        BindMonitorColumns();
        devices.DataSource = deviceSettings;
        if (devices.Columns["Slot"] is DataGridViewColumn slot) slot.HeaderText = "Receiver slot";
        if (devices.Columns["Feature"] is DataGridViewColumn feature) feature.HeaderText = "Change Host feature index";
        if (devices.Columns["Function"] is DataGridViewColumn function) function.HeaderText = "Function and SWID byte";
        dirty = false;
        status.Text = $"Loaded {library.Profiles.Count} profile(s) from {library.ConfigPath}";
        log.Clear();
        loading = false;
    }

    void ShowEmptyState()
    {
        currentName = "";
        heading.Text = "Add a profile to get started";
        targetName.Text = "";
        channel.Value = 1;
        monitorSettings = [];
        deviceSettings = [];
        monitors.DataSource = monitorSettings;
        devices.DataSource = deviceSettings;
        dirty = false;
        status.Text = $"No profiles loaded. Looking beside the executable: {AppPaths.ExecutableDirectory} (profile.json or profiles.json)";
    }

    void BindMonitorColumns()
    {
        var options = monitorChoices.ToList();
        foreach (var setting in monitorSettings.Where(s => !string.IsNullOrWhiteSpace(s.Selector)))
            if (options.All(o => !o.Id.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase)))
                options.Add(new(setting.Selector, "Saved ID: " + setting.Selector, "", "unknown"));
        monitors.AutoGenerateColumns = false;
        monitors.Columns.Clear();
        monitors.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Use", DataPropertyName = "Enabled", FillWeight = 10 });
        monitors.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Monitor (Plug and Play ID)", DataPropertyName = "Selector", DataSource = options, ValueMember = "Id", DisplayMember = "Label", FillWeight = 65, FlatStyle = FlatStyle.Flat });
        monitors.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Destination input", DataPropertyName = "Input", FillWeight = 25 });
    }

    void FindMonitors()
    {
        try
        {
            monitorChoices = NativeHardware.GetMonitorChoices();
            foreach (var setting in monitorSettings)
            {
                var old = monitorChoices.FirstOrDefault(m => m.LegacySelector.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase));
                if (old != null) setting.Selector = old.Id;
            }
            BindMonitorColumns();
            dirty = true;
            log.Text = NativeHardware.DescribeMonitors();
            SetStatus($"Found {monitorChoices.Count} monitor(s). Select them in the table and set destination inputs.");
        }
        catch (Exception e) { SetStatus("Monitor discovery failed: " + e.Message); }
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
            dirty = true;
        }
        catch (Exception e) { SetStatus("Device discovery failed: " + e.Message); }
    }

    Profile CommitToProfile()
    {
        if (string.IsNullOrWhiteSpace(currentName)) throw new InvalidOperationException("Select or add a profile first.");
        monitors.EndEdit();
        devices.EndEdit();
        return new Profile
        {
            Target = targetName.Text.Trim(),
            Channel = (int)channel.Value,
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
            library.Save(currentName, profile);
            dirty = false;
            SetStatus($"Saved {currentName} to profile.json.");
            return true;
        }
        catch (Exception e) { SetStatus("Could not save: " + e.Message); return false; }
    }

    async Task TestSelectedMonitor()
    {
        try
        {
            var profile = CommitToProfile();
            if (monitors.SelectedRows.Count == 0 || monitors.SelectedRows[0].DataBoundItem is not MonitorSetting selected)
                throw new Exception("Select a monitor row first.");
            if (MessageBox.Show($"Set only this monitor to input {selected.Input}? The screen may go blank if the input is not connected.", "Test monitor", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var test = new Profile { Target = currentName, Channel = profile.Channel, Monitors = [selected] };
            await Switching.RunAll(Switching.Plan(test), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Monitor command sent. Check the screen.");
        }
        catch (Exception e) { SetStatus("Monitor test failed: " + e.Message); }
    }

    async Task TestSelectedDevice()
    {
        try
        {
            var profile = CommitToProfile();
            if (devices.SelectedRows.Count == 0 || devices.SelectedRows[0].DataBoundItem is not DeviceSetting selected)
                throw new Exception("Select a device row first.");
            if (MessageBox.Show($"Switch only {selected.Name} to channel {profile.Channel}?", "Test device", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            var test = new Profile { Target = currentName, Channel = profile.Channel, Devices = [selected] };
            await Switching.RunAll(Switching.Plan(test), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Device report sent. Check which computer receives input.");
        }
        catch (Exception e) { SetStatus("Device test failed: " + e.Message); }
    }

    async Task TestProfile()
    {
        try
        {
            var profile = CommitToProfile();
            Switching.Plan(profile);
            if (!SaveCurrent()) return;
            if (MessageBox.Show($"Switch monitors, mouse and keyboard to {currentName}?", "Test profile", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            await Switching.RunAll(Switching.Plan(profile), message => log.AppendText(message + Environment.NewLine));
            SetStatus("Windows accepted the profile commands. Check the other computer.");
        }
        catch (Exception e) { SetStatus("Switch failed: " + e.Message); }
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
        var profile = Profile.Legacy(false);
        profile.Target = name;
        library.Save(name, profile);
        loading = true;
        profileList.Items.Add(name);
        profileList.SelectedItem = name;
        loading = false;
        LoadProfile(name);
        dirty = true;
        SetStatus($"Added {name}. Configure it and save.");
    }

    void DeleteProfile()
    {
        if (profileList.SelectedItem is not string name) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Delete profile", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        library.Delete(name);
        PopulateProfileList();
        SetStatus($"Deleted {name}.");
    }

    string? PromptForName()
    {
        using var dialog = new Form { Text = "Add profile", Size = new Size(380, 155), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var input = new TextBox { Left = 16, Top = 16, Width = 330 };
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Left = 180, Top = 56, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 270, Top = 56, Width = 75 };
        dialog.Controls.AddRange([input, ok, cancel]);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }

    void MarkDirty()
    {
        if (!loading) { dirty = true; status.Text = "Unsaved changes"; }
    }

    void SetStatus(string message) => status.Text = message;
}
