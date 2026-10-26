using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CopilotUsage.Tray.TrayIcon;

internal static class DynamicIcon
{
    public static Icon Create(string text, Color accent)
    {
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(16, 25, 41));
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 62, 62), new Size(12, 12));
            using var font = new Font("Segoe UI", text.Length > 3 ? 21 : text.Length > 2 ? 25 : 31, FontStyle.Bold, GraphicsUnit.Pixel);
            using var foreground = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, font, foreground, new RectangleF(0, -2, 64, 60), format);
            using var stripe = new Pen(accent, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(stripe, 16, 55, 48, 55);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
