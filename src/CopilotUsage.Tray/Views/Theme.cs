using System.Drawing.Drawing2D;

namespace CopilotUsage.Tray.Views;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(12, 18, 30);
    public static readonly Color Surface = Color.FromArgb(22, 32, 49);
    public static readonly Color Border = Color.FromArgb(43, 57, 77);
    public static readonly Color Text = Color.FromArgb(237, 243, 250);
    public static readonly Color Muted = Color.FromArgb(151, 169, 190);
    public static readonly Color Cyan = Color.FromArgb(103, 219, 244);
    public static readonly Color Mint = Color.FromArgb(132, 232, 188);
    public static readonly Color Amber = Color.FromArgb(255, 195, 112);

    public static Label Label(string text, float size = 10, Color? color = null, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, AutoSize = false, ForeColor = color ?? Text, BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, style), UseMnemonic = false, AutoEllipsis = true
    };
    public static Button Button(string text, bool primary = false) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        BackColor = primary ? Cyan : Surface, ForeColor = primary ? Background : Text,
        Font = new Font("Segoe UI", 10, FontStyle.Bold), Height = 40, UseVisualStyleBackColor = false,
        FlatAppearance = { BorderSize = primary ? 0 : 1, BorderColor = Border }
    };
}

internal sealed class MetricCard : Panel
{
    public Label Value { get; } = Theme.Label("—", 32, Theme.Text, FontStyle.Bold);
    public MetricCard(string title, string unit, Color accent)
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        var heading = Theme.Label(title, 10, Theme.Muted);
        heading.SetBounds(20, 17, 320, 25);
        Value.SetBounds(18, 45, 332, 58);
        Value.ForeColor = accent;
        Value.AccessibleName = title;
        var footer = Theme.Label(unit, 9, Theme.Muted);
        footer.SetBounds(22, 110, 330, 23);
        Controls.AddRange([heading, Value, footer]);
    }
    public void SetValue(string value)
    {
        if (Value.Text == value) return;
        Value.Text = value;
        var previous = Value.Font;
        Value.Font = new Font("Segoe UI", value.Length > 18 ? 17 : value.Length > 12 ? 24 : 32, FontStyle.Bold);
        previous.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
