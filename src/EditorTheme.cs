using System.Drawing.Drawing2D;

namespace SoftwareDeviceSwitch;

internal static class EditorTheme
{
    internal static readonly Color Canvas = Color.FromArgb(245, 247, 251);
    internal static readonly Color Ink = Color.FromArgb(27, 36, 55);
    internal static readonly Color Muted = Color.FromArgb(101, 113, 134);
    internal static readonly Color Border = Color.FromArgb(225, 230, 239);
    internal static readonly Color Accent = Color.FromArgb(77, 86, 220);
    internal static readonly Color AccentLight = Color.FromArgb(238, 239, 255);
    internal static readonly Color Sidebar = Color.FromArgb(23, 32, 51);
    internal static readonly Color SidebarMuted = Color.FromArgb(155, 169, 194);

    internal static Font Font(float size = 10, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    internal static Label Label(string text, float size = 10, Color? color = null, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, AutoSize = true, Font = Font(size, style), ForeColor = color ?? Ink,
        Margin = Padding.Empty, BackColor = Color.Transparent, UseMnemonic = false
    };

    internal static ThemeButton Button(string text, int width = 128, bool primary = false) => new()
    {
        Text = text, Width = width, Height = 38, Primary = primary,
        Margin = new Padding(8, 0, 0, 0)
    };

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoGenerateColumns = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = true, AllowUserToResizeRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, GridColor = Color.FromArgb(237, 240, 246),
        ColumnHeadersHeight = 34, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Font(9.5f), ForeColor = Ink, BackColor = Color.White,
            SelectionBackColor = AccentLight, SelectionForeColor = Ink,
            Padding = new Padding(8, 0, 8, 0)
        },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Font = Font(9, FontStyle.Bold), ForeColor = Muted,
            BackColor = Color.FromArgb(248, 249, 252), Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Color.FromArgb(248, 249, 252), SelectionForeColor = Muted
        },
        RowTemplate = { Height = 38 }
    };
}

internal sealed class ThemeButton : Button
{
    bool hovered;
    bool pressed;
    [System.ComponentModel.DefaultValue(false)]
    internal bool Primary { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    internal bool Dark { get; set; }

    internal ThemeButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = EditorTheme.Font(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? EditorTheme.Canvas);
        if (Width < 2 || Height < 2) return;
        Color fill = Primary ? EditorTheme.Accent : Dark ? Color.FromArgb(38, 49, 71) : Color.White;
        if (hovered) fill = Primary ? Color.FromArgb(65, 73, 198) : Dark ? Color.FromArgb(50, 63, 88) : Color.FromArgb(246, 247, 253);
        if (pressed) fill = Primary ? Color.FromArgb(54, 62, 177) : Dark ? Color.FromArgb(58, 71, 97) : EditorTheme.AccentLight;
        if (!Enabled) fill = Dark ? Color.FromArgb(32, 42, 62) : Color.FromArgb(238, 240, 245);
        using var shape = EditorTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * DeviceDpi / 96f);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        using var border = new Pen(Focused ? EditorTheme.Accent : Primary ? fill : Dark ? Color.FromArgb(60, 73, 99) : EditorTheme.Border, Focused ? 2 : 1);
        g.DrawPath(border, shape);
        Color text = !Enabled ? EditorTheme.Muted : Primary || Dark ? Color.White : EditorTheme.Ink;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class SurfacePanel : Panel
{
    internal SurfacePanel()
    {
        BackColor = Color.White;
        DoubleBuffered = true;
        Padding = new Padding(20);
        Margin = new Padding(0, 0, 0, 16);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? EditorTheme.Canvas);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = EditorTheme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(EditorTheme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class AppMark : Control
{
    internal AppMark() { Size = new Size(44, 44); DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Width / 44f;
        g.ScaleTransform(scale, scale);
        using var background = new SolidBrush(EditorTheme.Accent);
        using var shape = EditorTheme.Rounded(new RectangleF(0, 0, 43, 43), 11);
        g.FillPath(background, shape);
        using var line = new Pen(Color.White, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawRectangle(line, 9, 11, 25, 17);
        g.DrawLine(line, 18, 32, 26, 32);
        g.DrawLine(line, 22, 28, 22, 32);
        g.DrawLine(line, 15, 19, 28, 19);
        g.DrawLines(line, [new PointF(24, 15), new PointF(28, 19), new PointF(24, 23)]);
    }
}
