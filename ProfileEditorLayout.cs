using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoftwareDeviceSwitch;

public sealed partial class SwitchWindow
{
    readonly ProfileLibrary library;
    readonly TableLayoutPanel shell = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Padding.Empty, Margin = Padding.Empty, BackColor = EditorTheme.Canvas };
    readonly ListBox profileList = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 76, BackColor = EditorTheme.Sidebar, ForeColor = Color.White };
    readonly TextBox targetName = new() { BorderStyle = BorderStyle.None, Font = EditorTheme.Font(11), Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 251, 254) };
    readonly NumericUpDown channel = new() { Minimum = 1, Maximum = 3, BorderStyle = BorderStyle.None, Font = EditorTheme.Font(11), Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 251, 254) };
    readonly DataGridView monitors = EditorTheme.Grid();
    readonly DataGridView devices = EditorTheme.Grid();
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9), Dock = DockStyle.Fill, BackColor = Color.White, ForeColor = EditorTheme.Muted };
    readonly Label heading = EditorTheme.Label("Profile", 23, style: FontStyle.Bold);
    readonly Label status = EditorTheme.Label("Ready to switch.", 9.5f);
    readonly Label summary = EditorTheme.Label("", 9, EditorTheme.Muted);
    readonly Label saveBadge = new() { Text = "Saved", AutoSize = false, Size = new Size(96, 30), TextAlign = ContentAlignment.MiddleCenter, Font = EditorTheme.Font(9, FontStyle.Bold), ForeColor = Color.FromArgb(28, 119, 85), BackColor = Color.FromArgb(232, 247, 239), Margin = new Padding(16, 18, 0, 0) };
    readonly Label profilesCaption = EditorTheme.Label("PROFILES", 8.5f, EditorTheme.SidebarMuted, FontStyle.Bold);
    readonly ThemeButton saveButton = EditorTheme.Button("Save profile", 132);
    readonly ThemeButton switchButton = EditorTheme.Button("Test and switch", 158, true);
    readonly ThemeButton activityButton = EditorTheme.Button("Activity", 100);
    readonly CheckBox advancedFields = new() { Text = "Advanced fields", AutoSize = true, Font = EditorTheme.Font(9), ForeColor = EditorTheme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 10, 0, 0) };
    readonly Panel sidebar = new() { Dock = DockStyle.Fill, BackColor = EditorTheme.Sidebar, Padding = new Padding(18, 24, 18, 18) };
    readonly Panel editorBody = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = EditorTheme.Canvas, Padding = new Padding(28, 0, 28, 8) };
    readonly TableLayoutPanel content = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, BackColor = EditorTheme.Canvas, Margin = Padding.Empty, Padding = Padding.Empty };
    readonly ToolTip tips = new() { AutoPopDelay = 12000, InitialDelay = 500, ReshowDelay = 200 };
    List<NativeHardware.MonitorChoice> monitorChoices = [];
    BindingList<MonitorSetting> monitorSettings = [];
    BindingList<DeviceSetting> deviceSettings = [];
    string currentName = "";
    bool loading;
    bool dirty;
    bool busy;
    bool activityVisible;
    SurfacePanel activityCard = null!;

    public SwitchWindow(ProfileLibrary library)
    {
        this.library = library;
        Text = "Device Switch";
        Font = EditorTheme.Font();
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1340, 920);
        MinimumSize = new Size(1120, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = EditorTheme.Canvas;
        DoubleBuffered = true;

        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        BuildProfileMenu();
        BuildSettingsView();
        Controls.Add(shell);
        profileList.SelectedIndexChanged += ProfileSelectionChanged;
        targetName.TextChanged += (_, _) => MarkDirty();
        channel.ValueChanged += (_, _) => MarkDirty();
        advancedFields.CheckedChanged += (_, _) =>
        {
            if (!devices.EndEdit()) return;
            BindDeviceColumns();
            content.RowStyles[2].Height = (advancedFields.Checked ? 294 : 270) * DeviceDpi / 96f;
        };
        foreach (var grid in new[] { monitors, devices })
        {
            grid.CellValueChanged += (_, _) => { if (grid.ContainsFocus || grid.IsCurrentCellInEditMode) MarkDirty(); };
            grid.UserDeletedRow += (_, _) => MarkDirty();
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.DataError += (_, e) =>
            {
                e.ThrowException = false;
                SetStatus("Check the selected value. Input and slot values must be numbers.", true);
            };
        }
        monitors.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && monitors.Rows[e.RowIndex].DataBoundItem is MonitorSetting setting)
                e.ToolTipText = setting.Selector;
        };
        FormClosing += (_, e) => { if (busy || !ConfirmLeaveCurrent()) e.Cancel = true; };
        FormClosed += (_, _) => tips.Dispose();
        PopulateProfileList();
    }

    void BuildProfileMenu()
    {
        var menu = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = EditorTheme.Sidebar, Margin = Padding.Empty };
        menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        menu.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.Sidebar };
        brand.Controls.Add(new AppMark { Location = new Point(0, 0) });
        var brandName = EditorTheme.Label("Device Switch", 12, Color.White, FontStyle.Bold);
        brandName.Location = new Point(56, 0);
        brand.Controls.Add(brandName);
        var tagline = EditorTheme.Label("One connected desk.", 8.5f, EditorTheme.SidebarMuted);
        tagline.Location = new Point(56, 27);
        brand.Controls.Add(tagline);
        menu.Controls.Add(brand, 0, 0);
        profilesCaption.Margin = new Padding(6, 6, 0, 0);
        menu.Controls.Add(profilesCaption, 0, 1);
        profileList.DrawItem += DrawProfile;
        menu.Controls.Add(profileList, 0, 2);

        var local = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6, 12, 0, 0), BackColor = EditorTheme.Sidebar };
        local.Controls.Add(EditorTheme.Label("THIS COMPUTER", 8, EditorTheme.SidebarMuted, FontStyle.Bold));
        local.Controls.Add(EditorTheme.Label(Environment.MachineName, 9.5f, Color.FromArgb(222, 229, 243)));
        menu.Controls.Add(local, 0, 3);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = EditorTheme.Sidebar };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var add = EditorTheme.Button("+ Add");
        var delete = EditorTheme.Button("Delete");
        add.Dark = delete.Dark = true;
        add.Dock = delete.Dock = DockStyle.Fill;
        add.Margin = new Padding(0, 0, 5, 0);
        delete.Margin = new Padding(5, 0, 0, 0);
        add.Click += (_, _) => AddProfile();
        delete.Click += (_, _) => DeleteProfile();
        actions.Controls.Add(add, 0, 0);
        actions.Controls.Add(delete, 1, 0);
        menu.Controls.Add(actions, 0, 4);
        sidebar.Controls.Add(menu);
        sidebar.Margin = Padding.Empty;
        shell.Controls.Add(sidebar, 0, 0);
    }

    void DrawProfile(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || profileList.Items[e.Index] is not string name) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(EditorTheme.Sidebar);
        g.FillRectangle(background, e.Bounds);
        float scale = profileList.DeviceDpi / 96f;
        var bounds = new RectangleF(e.Bounds.X, e.Bounds.Y + 4 * scale, e.Bounds.Width - 1, e.Bounds.Height - 8 * scale);
        if (selected)
        {
            using var shape = EditorTheme.Rounded(bounds, 9 * scale);
            using var fill = new SolidBrush(Color.FromArgb(45, 57, 87));
            g.FillPath(fill, shape);
        }
        var iconBounds = new RectangleF(bounds.Left + 12 * scale, bounds.Top + 15 * scale, 36 * scale, 36 * scale);
        using var iconShape = EditorTheme.Rounded(iconBounds, 8 * scale);
        using var iconFill = new SolidBrush(selected ? EditorTheme.Accent : Color.FromArgb(38, 49, 71));
        g.FillPath(iconFill, iconShape);
        using var initialsFont = EditorTheme.Font(10, FontStyle.Bold);
        TextRenderer.DrawText(g, name[..Math.Min(name.Length, 2)].ToUpperInvariant(), initialsFont, Rectangle.Round(iconBounds), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        int left = (int)(bounds.Left + 60 * scale);
        int available = (int)(bounds.Width - 68 * scale);
        using var titleFont = EditorTheme.Font(11, FontStyle.Bold);
        using var detailFont = EditorTheme.Font(8.5f);
        TextRenderer.DrawText(g, name, titleFont, new Rectangle(left, (int)(bounds.Top + 12 * scale), available, (int)(24 * scale)), Color.White, TextFormatFlags.EndEllipsis);
        string detail = library.Profiles.TryGetValue(name, out var profile) ? $"Easy-Switch {profile.Channel}" : "Profile";
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle(left, (int)(bounds.Top + 37 * scale), available, (int)(22 * scale)), EditorTheme.SidebarMuted, TextFormatFlags.EndEllipsis);
        if (profileList.Focused && selected)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Round(bounds), Color.FromArgb(158, 170, 220), EditorTheme.Sidebar);
    }

    void BuildSettingsView()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = EditorTheme.Canvas };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(28, 16, 28, 12), BackColor = EditorTheme.Canvas, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        titles.Controls.Add(EditorTheme.Label("PROFILE SETTINGS", 8, EditorTheme.Muted, FontStyle.Bold), 0, 0);
        titles.Controls.Add(heading, 0, 1);
        titles.Controls.Add(EditorTheme.Label("Set up where your screens, mouse and keyboard go.", 9.5f, EditorTheme.Muted), 0, 2);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(saveBadge, 1, 0);
        root.Controls.Add(header, 0, 0);

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        content.Controls.Add(BuildDestinationCard(), 0, 0);
        content.Controls.Add(BuildMonitorSection(), 0, 1);
        content.Controls.Add(BuildDeviceSection(), 0, 2);
        activityCard = BuildSection("Activity", "Discovery results, connection details and switch commands.", new Panel(), log);
        activityCard.Visible = false;
        content.Controls.Add(activityCard, 0, 3);
        editorBody.Controls.Add(content);
        root.Controls.Add(editorBody, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(28, 18, 28, 14), BackColor = Color.White };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Paint += (_, e) => { using var line = new Pen(EditorTheme.Border); e.Graphics.DrawLine(line, 0, 0, footer.Width, 0); };
        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        status.Dock = summary.Dock = DockStyle.Fill;
        status.AutoSize = summary.AutoSize = false;
        status.AutoEllipsis = summary.AutoEllipsis = true;
        feedback.Controls.Add(status, 0, 0);
        feedback.Controls.Add(summary, 0, 1);
        footer.Controls.Add(feedback, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Padding = new Padding(0, 3, 0, 0) };
        activityButton.Click += (_, _) => ShowActivity(!activityVisible);
        saveButton.Click += (_, _) => SaveCurrent();
        switchButton.Click += async (_, _) => await RunBusy(TestProfile);
        buttons.Controls.AddRange([activityButton, saveButton, switchButton]);
        footer.Controls.Add(buttons, 1, 0);
        root.Controls.Add(footer, 0, 2);
        root.Margin = Padding.Empty;
        shell.Controls.Add(root, 1, 0);
        tips.SetToolTip(channel, "Choose the Logitech Easy-Switch slot paired with the destination computer.");
        tips.SetToolTip(targetName, "A destination label for confirmations. It does not establish a network connection.");
        tips.SetToolTip(activityButton, "View discovery details and the profile file location.");
    }

    Control BuildDestinationCard()
    {
        var card = new SurfacePanel();
        card.Dock = DockStyle.Fill;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty, BackColor = Color.White };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(EditorTheme.Label("Destination computer", 10, style: FontStyle.Bold), 0, 0);
        fields.Controls.Add(EditorTheme.Label("Easy-Switch channel", 10, style: FontStyle.Bold), 1, 0);
        fields.Controls.Add(FieldFrame(targetName, new Padding(0, 0, 20, 0)), 0, 1);
        fields.Controls.Add(FieldFrame(channel, Padding.Empty), 1, 1);
        var nameHint = EditorTheme.Label("A label for the computer receiving your devices.", 8.5f, EditorTheme.Muted);
        var channelHint = EditorTheme.Label("The slot paired with the other computer.", 8.5f, EditorTheme.Muted);
        nameHint.Margin = channelHint.Margin = new Padding(0, 7, 0, 0);
        fields.Controls.Add(nameHint, 0, 2);
        fields.Controls.Add(channelHint, 1, 2);
        card.Controls.Add(fields);
        return card;
    }

    static Panel FieldFrame(Control field, Padding margin)
    {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 251, 254), Padding = new Padding(11, 8, 8, 3), Margin = margin };
        frame.Paint += (_, e) => { using var pen = new Pen(EditorTheme.Border); e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1); };
        frame.Controls.Add(field);
        return frame;
    }

    Control BuildMonitorSection()
    {
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var find = EditorTheme.Button("Find monitors", 130);
        var test = EditorTheme.Button("Test selected", 126);
        find.Margin = Padding.Empty;
        find.Click += (_, _) => FindMonitors();
        test.Click += async (_, _) => await RunBusy(TestSelectedMonitor);
        actions.Controls.AddRange([find, test]);
        return BuildSection("Monitors", "Choose each screen and the input connected to your destination computer.", actions, monitors);
    }

    Control BuildDeviceSection()
    {
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var discover = EditorTheme.Button("Discover devices", 146);
        var test = EditorTheme.Button("Test selected", 126);
        var list = EditorTheme.Button("Receiver details", 142);
        discover.Margin = Padding.Empty;
        discover.Click += async (_, _) => await RunBusy(DiscoverDevices);
        test.Click += async (_, _) => await RunBusy(TestSelectedDevice);
        list.Click += (_, _) =>
        {
            try { log.Text = string.Join(Environment.NewLine, NativeHardware.ListHid()); ShowActivity(true); SetStatus("Receiver details are in Activity."); }
            catch (Exception e) { SetStatus(e.Message, true); }
        };
        buttons.Controls.AddRange([discover, test, list]);
        actions.Controls.Add(buttons, 0, 0);
        actions.Controls.Add(advancedFields, 1, 0);
        return BuildSection("Mouse & keyboard", "Discover your Logitech devices, then enable the ones this profile should switch.", actions, devices);
    }

    static SurfacePanel BuildSection(string title, string description, Control actions, Control body)
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.White, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, actions is Panel && actions is not FlowLayoutPanel && actions is not TableLayoutPanel ? 0 : 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(EditorTheme.Label(title, 13, style: FontStyle.Bold), 0, 0);
        var help = EditorTheme.Label(description, 9, EditorTheme.Muted);
        help.Dock = DockStyle.Fill;
        help.AutoSize = false;
        help.AutoEllipsis = true;
        layout.Controls.Add(help, 0, 1);
        actions.Dock = DockStyle.Fill;
        actions.Margin = Padding.Empty;
        layout.Controls.Add(actions, 0, 2);
        body.Margin = Padding.Empty;
        layout.Controls.Add(body, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    void BindDeviceColumns()
    {
        devices.Columns.Clear();
        devices.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Use", DataPropertyName = "Enabled", Width = 52, MinimumWidth = 52, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, HeaderCell = { Style = { Padding = new Padding(1), Alignment = DataGridViewContentAlignment.MiddleCenter } } });
        AddDeviceColumn("Name", "Device", 34, 150);
        AddDeviceColumn("Receiver", "USB receiver", 23, 105);
        AddDeviceColumn("Slot", "Slot", 10, 60);
        AddDeviceColumn("Feature", "Feature", 10, 90);
        AddDeviceColumn("Function", "Function", 10, 95);
        AddDeviceColumn("ReceiverPath", "Receiver path", 30, 180);
        foreach (var name in new[] { "Feature", "Function", "ReceiverPath" }) devices.Columns[name]!.Visible = advancedFields.Checked;
        foreach (DataGridViewColumn column in devices.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        devices.Columns["Slot"]!.ToolTipText = "The device slot inside the USB receiver, not its Easy-Switch channel.";
        devices.Columns["Feature"]!.ToolTipText = "The runtime Change Host feature index, in hexadecimal. Discover devices can find this.";
        devices.Columns["Function"]!.ToolTipText = "The Change Host function and software ID byte, in hexadecimal.";
        devices.Columns["ReceiverPath"]!.ToolTipText = "Use a specific receiver interface when more than one matches the USB receiver ID.";
    }

    void AddDeviceColumn(string name, string title, float weight, int minimum) => devices.Columns.Add(new DataGridViewTextBoxColumn
    {
        Name = name, DataPropertyName = name, HeaderText = title, FillWeight = weight, MinimumWidth = minimum
    });

    void ShowActivity(bool visible)
    {
        activityVisible = visible;
        content.RowStyles[3].Height = visible ? 210 * DeviceDpi / 96f : 0;
        activityCard.Visible = visible;
        activityButton.Text = visible ? "Hide log" : "Activity";
        content.PerformLayout();
        if (visible) editorBody.ScrollControlIntoView(activityCard);
    }

    void UpdateSummary()
    {
        profilesCaption.Text = $"PROFILES  /  {library.Profiles.Count}";
        summary.Text = string.IsNullOrEmpty(currentName) ? "Add a profile to configure your desk." : $"{monitorSettings.Count(m => m.Enabled)} monitors  /  {deviceSettings.Count(d => d.Enabled)} devices  /  Channel {channel.Value}";
        saveButton.Enabled = switchButton.Enabled = !busy && !string.IsNullOrEmpty(currentName);
        for (int row = 0; row < 3; row++)
            if (content.GetControlFromPosition(0, row) is Control card) card.Enabled = !busy && !string.IsNullOrEmpty(currentName);
        profileList.Invalidate();
    }

    void UpdateSaveState()
    {
        saveBadge.Text = dirty ? "Unsaved" : "Saved";
        saveBadge.ForeColor = dirty ? Color.FromArgb(146, 99, 23) : Color.FromArgb(28, 119, 85);
        saveBadge.BackColor = dirty ? Color.FromArgb(255, 245, 222) : Color.FromArgb(232, 247, 239);
        UpdateSummary();
    }

    async Task RunBusy(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        sidebar.Enabled = editorBody.Enabled = saveButton.Enabled = switchButton.Enabled = false;
        UseWaitCursor = true;
        try { await operation(); }
        finally
        {
            busy = false;
            sidebar.Enabled = editorBody.Enabled = true;
            UseWaitCursor = false;
            UpdateSummary();
        }
    }
}
