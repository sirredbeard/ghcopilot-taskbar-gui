using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace CopilotTaskbarApp;

/// <summary>
/// Creates a ContextMenuStrip styled to match Windows 11 native flyout menus
/// with rounded corners, dark/light mode, user accent color, and system colors.
/// </summary>
internal static class Win11ContextMenu
{
    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int pvAttribute, uint cbAttribute);

    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch { return false; }
    }

    private static Color GetAccentColor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\DWM");
            var value = key?.GetValue("AccentColor");
            if (value is int abgr)
            {
                // Registry stores as ABGR (0xAABBGGRR)
                byte a = (byte)((abgr >> 24) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte r = (byte)(abgr & 0xFF);
                return Color.FromArgb(a == 0 ? 255 : a, r, g, b);
            }
        }
        catch { }
        // Default Windows 11 blue accent
        return Color.FromArgb(0, 103, 192);
    }

    public static WinForms.ContextMenuStrip Create()
    {
        var isDark = IsDarkMode();
        var accent = GetAccentColor();

        // Windows 11 flyout colors (from themeresources.xaml)
        // Dark: background #2C2C2C, text #FFFFFF, hover SubtleFillColorSecondary (~#0FFFFFFF)
        // Light: background #F9F9F9, text #1A1A1A, hover SubtleFillColorSecondary (~#09000000)
        var bgColor = isDark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
        var fgColor = isDark ? Color.White : Color.FromArgb(26, 26, 26);
        var borderColor = isDark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(229, 229, 229);

        var menu = new WinForms.ContextMenuStrip
        {
            // Segoe UI Variable is the Windows 11 system font
            Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular),
            ShowImageMargin = false,
            BackColor = bgColor,
            ForeColor = fgColor,
            Padding = new WinForms.Padding(0, 4, 0, 4),
            Renderer = new Win11Renderer(isDark, bgColor, borderColor, accent)
        };

        menu.Opened += (s, e) =>
        {
            if (s is not WinForms.ContextMenuStrip cms) return;
            try
            {
                var hwnd = cms.Handle;
                int cornerPref = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch { }
        };

        return menu;
    }

    private sealed class Win11Renderer : WinForms.ToolStripProfessionalRenderer
    {
        private readonly Color _bgColor;
        private readonly Color _hoverColor;
        private readonly Color _textColor;
        private readonly Color _separatorColor;
        private readonly Color _borderColor;
        private readonly Color _checkColor;

        public Win11Renderer(bool isDark, Color bgColor, Color borderColor, Color accent)
            : base(new Win11ColorTable(bgColor, borderColor))
        {
            _bgColor = bgColor;
            // Windows 11: subtle hover (SubtleFillColorSecondary)
            _hoverColor = isDark ? Color.FromArgb(15, 255, 255, 255) : Color.FromArgb(9, 0, 0, 0);
            _textColor = isDark ? Color.White : Color.FromArgb(26, 26, 26);
            _separatorColor = isDark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(216, 216, 216);
            _borderColor = borderColor;
            _checkColor = accent;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_bgColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;

            // Win11: inset rounded rectangle hover, 4px corner radius
            var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var brush = new SolidBrush(_hoverColor);
            using var path = CreateRoundedRectPath(rect, 4);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(_separatorColor);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _textColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            using var pen = new Pen(_checkColor, 2f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            e.Graphics.DrawLine(pen, r.X + 3, r.Y + r.Height / 2, r.X + r.Width / 3, r.Y + r.Height - 4);
            e.Graphics.DrawLine(pen, r.X + r.Width / 3, r.Y + r.Height - 4, r.X + r.Width - 3, r.Y + 4);
        }

        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
        {
            // Win11: 1px border, DWM handles rounding
            using var pen = new Pen(_borderColor);
            var rect = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        }

        private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class Win11ColorTable : System.Windows.Forms.ProfessionalColorTable
    {
        private readonly Color _bg;
        private readonly Color _border;

        public Win11ColorTable(Color bg, Color border)
        {
            _bg = bg;
            _border = border;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuStripGradientBegin => _bg;
        public override Color MenuStripGradientEnd => _bg;
        public override Color SeparatorDark => Color.Transparent;
        public override Color SeparatorLight => Color.Transparent;
    }
}
