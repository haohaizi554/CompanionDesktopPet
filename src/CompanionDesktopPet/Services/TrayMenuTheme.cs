using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Services;

/// <summary>
/// WinForms tray menu painted with the same pink card as <c>KawaiiContextMenuStyle</c>.
/// NotifyIcon cannot host a WPF ContextMenu, so the colors stay in step with PetTheme by hand.
/// </summary>
internal static class PetTrayMenuColors
{
    internal static readonly Color Text = Color.FromArgb(0xFF, 0x54, 0x3A, 0x3F);
    internal static readonly Color TextDisabled = Color.FromArgb(0xFF, 0x8A, 0x74, 0x79);
    internal static readonly Color Checked = Color.FromArgb(0xFF, 0xFF, 0x6F, 0x91);
    internal static readonly Color Border = Color.FromArgb(0xFF, 0xE9, 0x8F, 0xA4);
    internal static readonly Color SurfaceStart = Color.FromArgb(0xFA, 0xFF, 0xFD, 0xF7);
    internal static readonly Color SurfaceEnd = Color.FromArgb(0xE8, 0xFF, 0xE0, 0xEA);
    internal static readonly Color HoverFill = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    internal static readonly Color HoverBorder = Color.FromArgb(0xFF, 0xE9, 0x8F, 0xA4);
    internal static readonly Color PressedFill = Color.FromArgb(0xFF, 0xFF, 0xB3, 0xC6);
    internal static readonly Color PressedBorder = Color.FromArgb(0xFF, 0xFF, 0x6F, 0x91);
    internal static readonly Color Separator = Color.FromArgb(0xFF, 0xE9, 0x8F, 0xA4);
}

internal sealed class PetTrayMenuStrip : Forms.ContextMenuStrip
{
    internal const string UiFontName = "Microsoft YaHei UI";

    private Forms.ToolStripItem? _hotItem;
    private Forms.ToolStripItem? _pressedItem;
    private bool _mouseDown;
    private int _regionWidth;
    private int _regionHeight;

    public PetTrayMenuStrip()
    {
        Font = CreateUiFont();
        ForeColor = PetTrayMenuColors.Text;
        BackColor = PetTrayMenuColors.SurfaceStart;
        Renderer = new PetTrayMenuRenderer();
        ShowImageMargin = false;
        ShowCheckMargin = true;
        Padding = new Forms.Padding(8, 8, 8, 8);
        DropShadowEnabled = true;
    }

    internal Forms.ToolStripItem? HotItem => _hotItem;

    internal Forms.ToolStripItem? PressedItem => _pressedItem;

    protected override Forms.CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x00000008;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOPMOST;
            return parameters;
        }
    }

    protected override void OnItemAdded(Forms.ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is Forms.ToolStripMenuItem item)
        {
            item.ForeColor = PetTrayMenuColors.Text;
            item.Padding = new Forms.Padding(4, 6, 12, 6);
            return;
        }

        if (e.Item is Forms.ToolStripSeparator separator)
        {
            separator.AutoSize = false;
            separator.Height = LogicalToDeviceUnits(12);
            separator.Margin = new Forms.Padding(10, 2, 10, 2);
        }
    }

    protected override void OnLayout(Forms.LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateRoundedRegion();
    }

    internal void ApplyPointer(Point location, bool pressed)
    {
        var item = EnabledItemAt(location);
        var hotItem = item ?? (pressed ? _pressedItem : null);
        var pressedItem = pressed ? hotItem : null;
        if (ReferenceEquals(_hotItem, hotItem) && ReferenceEquals(_pressedItem, pressedItem))
        {
            return;
        }

        _hotItem = hotItem;
        _pressedItem = pressedItem;
        Invalidate();
    }

    protected override void OnMouseMove(Forms.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ApplyPointer(e.Location, pressed: _mouseDown);
    }

    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == Forms.MouseButtons.Left)
        {
            _mouseDown = true;
            ApplyPointer(e.Location, pressed: true);
            Update();
        }
    }

    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        _mouseDown = false;
        base.OnMouseUp(e);
        _pressedItem = null;
        _hotItem = EnabledItemAt(e.Location);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _mouseDown = false;
        _hotItem = null;
        _pressedItem = null;
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnClosed(Forms.ToolStripDropDownClosedEventArgs e)
    {
        _hotItem = null;
        _pressedItem = null;
        base.OnClosed(e);
    }

    private Forms.ToolStripItem? EnabledItemAt(Point location)
    {
        var item = GetItemAt(location);
        return item is { Enabled: true } and not Forms.ToolStripSeparator ? item : null;
    }

    private static Font CreateUiFont()
    {
        try
        {
            return new Font(UiFontName, 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericSansSerif, 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    private void UpdateRoundedRegion()
    {
        if (ClientSize.Width < 8 || ClientSize.Height < 8)
        {
            return;
        }

        if (_regionWidth == ClientSize.Width && _regionHeight == ClientSize.Height && Region is not null)
        {
            return;
        }

        _regionWidth = ClientSize.Width;
        _regionHeight = ClientSize.Height;
        var radius = Math.Min(LogicalToDeviceUnits(16), Math.Min(ClientSize.Width, ClientSize.Height) / 2);
        var bounds = ClientRectangle;
        bounds.Inflate(-1, -1);
        using var path = PetTrayMenuRenderer.Rounded(bounds, Math.Max(1, radius - 1));
        var next = new Region(path);
        var previous = Region;
        Region = next;
        previous?.Dispose();
    }
}

internal sealed class PetTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    public PetTrayMenuRenderer()
        : base(new PetTrayColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        var bounds = e.AffectedBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(
            bounds,
            PetTrayMenuColors.SurfaceStart,
            PetTrayMenuColors.SurfaceEnd,
            LinearGradientMode.ForwardDiagonal);
        using var path = Rounded(bounds, Radius(e.ToolStrip!, bounds, 16));
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        var bounds = Rectangle.Inflate(e.AffectedBounds, -3, -3);
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(PetTrayMenuColors.Border, 1.5f);
        using var path = Rounded(bounds, Math.Max(1, Radius(e.ToolStrip!, e.AffectedBounds, 16) - 3));
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || e.ToolStrip is not PetTrayMenuStrip strip)
        {
            return;
        }

        var pressed = e.Item.Pressed || ReferenceEquals(strip.PressedItem, e.Item);
        var hot = pressed || e.Item.Selected || ReferenceEquals(strip.HotItem, e.Item);
        if (!hot)
        {
            return;
        }

        var bounds = e.Item.Bounds;
        bounds.Inflate(-6, -2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(pressed ? PetTrayMenuColors.PressedFill : PetTrayMenuColors.HoverFill);
        using var pen = new Pen(pressed ? PetTrayMenuColors.PressedBorder : PetTrayMenuColors.HoverBorder, 1.25f);
        using var path = Rounded(bounds, Radius(strip, bounds, 10));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        box.Inflate(1, 2);
        Forms.TextRenderer.DrawText(
            e.Graphics,
            "✓",
            e.Item.Font,
            box,
            PetTrayMenuColors.Checked,
            Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = e.Item.Bounds;
        var y = bounds.Top + (bounds.Height / 2);
        var left = bounds.Left + 16;
        var right = bounds.Right - 16;
        if (right <= left)
        {
            return;
        }

        using var pen = new Pen(PetTrayMenuColors.Separator, 1f);
        e.Graphics.DrawLine(pen, left, y, right, y);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? PetTrayMenuColors.Text : PetTrayMenuColors.TextDisabled;
        base.OnRenderItemText(e);
    }

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        if (diameter <= 0 || bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static int Radius(Forms.ToolStrip strip, Rectangle bounds, int dip)
    {
        var radius = strip is Forms.Control control ? control.LogicalToDeviceUnits(dip) : dip;
        return Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
    }
}

internal sealed class PetTrayColorTable : Forms.ProfessionalColorTable
{
    public PetTrayColorTable()
    {
        UseSystemColors = false;
    }

    public override Color MenuBorder => PetTrayMenuColors.Border;
    public override Color ToolStripDropDownBackground => PetTrayMenuColors.SurfaceStart;
    public override Color MenuItemSelected => PetTrayMenuColors.HoverFill;
    public override Color MenuItemSelectedGradientBegin => PetTrayMenuColors.HoverFill;
    public override Color MenuItemSelectedGradientEnd => PetTrayMenuColors.HoverFill;
    public override Color MenuItemPressedGradientBegin => PetTrayMenuColors.PressedFill;
    public override Color MenuItemPressedGradientEnd => PetTrayMenuColors.PressedFill;
    public override Color MenuItemBorder => PetTrayMenuColors.HoverBorder;
    public override Color CheckBackground => Color.Transparent;
    public override Color CheckSelectedBackground => Color.Transparent;
    public override Color CheckPressedBackground => Color.Transparent;
    public override Color SeparatorDark => PetTrayMenuColors.Separator;
    public override Color SeparatorLight => Color.Transparent;
    public override Color ImageMarginGradientBegin => PetTrayMenuColors.SurfaceStart;
    public override Color ImageMarginGradientMiddle => PetTrayMenuColors.SurfaceStart;
    public override Color ImageMarginGradientEnd => PetTrayMenuColors.SurfaceEnd;
}
