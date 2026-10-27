namespace SoftwareDeviceSwitch;

public sealed partial class SwitchWindow
{
    NativeHardware.MonitorChoice? MonitorInfo(MonitorSetting setting) => monitorChoices
        .FirstOrDefault(m => m.Id.Equals(setting.Selector, StringComparison.OrdinalIgnoreCase));

    static string CurrentInputLabel(NativeHardware.MonitorChoice? monitor) => monitor == null ? "Not read" :
        int.TryParse(monitor.CurrentInput, out int code) && code > 0 ? MonitorInputs.Label(code) : "Unavailable";

    List<MonitorInputOption> InputOptions(MonitorSetting setting, int? selectedCode = null)
    {
        var monitor = MonitorInfo(setting);
        var codes = monitor?.SupportedInputs is { Count: > 0 } reported ? reported : MonitorInputs.CommonCodes;
        var all = codes.Append(selectedCode ?? setting.Input);
        if (int.TryParse(monitor?.CurrentInput, out int current)) all = all.Append(current);
        return MonitorInputs.Options(all);
    }

    string MonitorInputDetails(MonitorSetting setting)
    {
        var monitor = MonitorInfo(setting);
        return $"Current input: {CurrentInputLabel(monitor)}\r\n" +
            (monitor?.InputDiscoveryStatus ?? "Not read yet. Click Find monitors to read available inputs.") +
            (monitor?.SupportedInputs.Count > 0 ? "\r\nReported inputs: " + string.Join(", ", monitor.SupportedInputs.Select(MonitorInputs.Label)) : "\r\nThe dropdown currently shows standard codes, not confirmed available ports.") +
            "\r\nNames follow standard codes. Use Input guide to check a custom port, such as USB-C.";
    }

    static string DescribeMonitorInputs(NativeHardware.MonitorChoice monitor) =>
        $"Monitor: {monitor.Label}\r\nCurrent input: {CurrentInputLabel(monitor)}\r\n{monitor.InputDiscoveryStatus}\r\n" +
        (monitor.SupportedInputs.Count > 0 ? "Reported inputs: " + string.Join(", ", monitor.SupportedInputs.Select(MonitorInputs.Label)) : "Standard suggestions are unconfirmed; use the monitor's menu to identify its input codes.");

    void RefreshMonitorInputCells()
    {
        if (!monitors.Columns.Contains("Input")) return;
        foreach (DataGridViewRow row in monitors.Rows) RefreshMonitorInputCell(row);
    }

    void RefreshMonitorInputCell(DataGridViewRow row)
    {
        if (!monitors.Columns.Contains("Input") || row.DataBoundItem is not MonitorSetting setting) return;
        if (row.Cells["Input"] is DataGridViewComboBoxCell input)
        {
            input.DataSource = InputOptions(setting);
            input.ValueMember = "Code";
            input.DisplayMember = "Label";
        }
        row.Cells["CurrentInput"].Value = CurrentInputLabel(MonitorInfo(setting));
    }

    void ShowInputGuide()
    {
        if (!monitors.EndEdit()) return;
        if (monitors.CurrentRow?.DataBoundItem is not MonitorSetting setting || string.IsNullOrWhiteSpace(setting.Selector))
        {
            SetStatus("Select a monitor connection in the table before opening Input guide.", true);
            return;
        }
        using var dialog = new Form
        {
            Text = "Monitor input guide", ClientSize = new Size(740, 660), BackColor = EditorTheme.Canvas,
            Font = EditorTheme.Font(), AutoScaleMode = AutoScaleMode.Dpi, Padding = new Padding(24),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 36, 24, 90, 128, 184 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(EditorTheme.Label("Choose the right input", 19, style: FontStyle.Bold), 0, 0);
        layout.Controls.Add(EditorTheme.Label("Pick the monitor socket connected to the other computer.", 9.5f, EditorTheme.Muted), 0, 1);

        var stateCard = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
        var state = EditorTheme.Label("", 9.5f);
        state.AutoSize = false;
        state.Dock = DockStyle.Fill;
        stateCard.Controls.Add(state);
        layout.Controls.Add(stateCard, 0, 2);

        var choiceCard = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.White };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(EditorTheme.Label("Destination port", 10, style: FontStyle.Bold), 0, 0);
        fields.Controls.Add(EditorTheme.Label("Input code (decimal)", 9, style: FontStyle.Bold), 1, 0);
        var ports = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = EditorTheme.Font(10), Margin = new Padding(0, 0, 16, 0), ValueMember = "Code", DisplayMember = "Label" };
        var code = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(setting.Input, 1, 65535), Dock = DockStyle.Fill, Font = EditorTheme.Font(10), Margin = Padding.Empty };
        fields.Controls.Add(ports, 0, 1);
        fields.Controls.Add(code, 1, 1);
        var selectionNote = EditorTheme.Label("", 8.5f, EditorTheme.Muted);
        selectionNote.Dock = DockStyle.Fill;
        selectionNote.AutoSize = false;
        fields.Controls.Add(selectionNote, 0, 2);
        fields.SetColumnSpan(selectionNote, 2);
        choiceCard.Controls.Add(fields);
        layout.Controls.Add(choiceCard, 0, 3);

        var instructions = EditorTheme.Label(
            "Match HDMI and DisplayPort names to the labels on the monitor's sockets.\r\n\r\n" +
            "For a custom port (often USB-C), select it with the monitor's buttons, then use Read inputs while the monitor is reachable. Its current code appears above; enter that code manually if needed.\r\n\r\n" +
            "Names use standard codes. Monitor makers can use different codes; reported ports may include unused sockets.", 9.5f, EditorTheme.Muted);
        instructions.Dock = DockStyle.Fill;
        instructions.AutoSize = false;
        layout.Controls.Add(instructions, 0, 4);
        var feedback = EditorTheme.Label("Use input saves this choice in the editor. Test selected sends the switch command.", 9, EditorTheme.Muted);
        feedback.AutoSize = false;
        feedback.Dock = DockStyle.Fill;
        layout.Controls.Add(feedback, 0, 5);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var read = EditorTheme.Button("Read inputs", 128);
        read.Margin = Padding.Empty;
        footer.Controls.Add(read, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
        var cancel = EditorTheme.Button("Cancel", 96);
        var apply = EditorTheme.Button("Use input", 124, true);
        cancel.DialogResult = DialogResult.Cancel;
        apply.DialogResult = DialogResult.OK;
        buttons.Controls.AddRange([cancel, apply]);
        footer.Controls.Add(buttons, 1, 0);
        layout.Controls.Add(footer, 0, 6);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        bool updating = false;
        bool reading = false;

        void RenderInputs()
        {
            updating = true;
            int selected = (int)code.Value;
            ports.DataSource = InputOptions(setting, selected);
            ports.SelectedValue = selected;
            var monitor = MonitorInfo(setting);
            state.Text = $"Current input: {CurrentInputLabel(monitor)}\r\n" +
                (monitor?.InputDiscoveryStatus ?? "Availability has not been read. Use Read inputs to ask this monitor.");
            selectionNote.Text = monitor?.SupportedInputs.Count > 0
                ? monitor.SupportedInputs.Contains(selected) ? "This input code is reported by the monitor." : "This code is not in the reported list. It may be a custom port."
                : "Standard suggestions only; available ports have not been confirmed.";
            updating = false;
        }

        ports.SelectedValueChanged += (_, _) => { if (!updating && ports.SelectedValue is int selected) code.Value = selected; };
        code.ValueChanged += (_, _) => { if (!updating) RenderInputs(); };
        read.Click += async (_, _) =>
        {
            reading = true;
            read.Enabled = apply.Enabled = cancel.Enabled = ports.Enabled = code.Enabled = false;
            feedback.Text = "Reading this monitor's current input and reported ports...";
            feedback.ForeColor = EditorTheme.Muted;
            try
            {
                var found = await Task.Run(() => NativeHardware.ReadMonitorInputs(setting.Selector));
                monitorChoices.RemoveAll(m => m.Id.Equals(found.Id, StringComparison.OrdinalIgnoreCase));
                monitorChoices.Add(found);
                RefreshMonitorInputCells();
                log.AppendText("\r\n" + DescribeMonitorInputs(found) + "\r\n");
                RenderInputs();
                feedback.Text = "Inputs read. Choose the port connected to the destination computer.";
            }
            catch (Exception error)
            {
                feedback.ForeColor = Color.FromArgb(174, 58, 64);
                feedback.Text = error.Message;
            }
            finally
            {
                reading = false;
                read.Enabled = apply.Enabled = cancel.Enabled = ports.Enabled = code.Enabled = true;
            }
        };
        dialog.FormClosing += (_, e) => { if (reading) e.Cancel = true; };
        RenderInputs();
        if (dialog.ShowDialog(this) == DialogResult.OK && setting.Input != (int)code.Value)
        {
            setting.Input = (int)code.Value;
            RefreshMonitorInputCells();
            monitors.Refresh();
            MarkDirty();
            SetStatus($"Destination input set to {MonitorInputs.Label(setting.Input)}. Save or Test selected to continue.");
        }
    }
}
