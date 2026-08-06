namespace TaskbarProgress.Infrastructure.Renderers;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Displays three labeled, horizontal metric bars docked directly over the
/// taskbar strip. Because the overlay overlaps the taskbar's own screen
/// rect, it must (a) fit within the taskbar's actual height, and (b)
/// actively re-assert HWND_TOPMOST against Explorer, which periodically
/// reclaims topmost status for the taskbar itself.
/// </summary>
public sealed class DwmBarRenderer : IBarRenderer
{
    private readonly ILogger<DwmBarRenderer> _logger;
    private BarOverlay? _overlay;
    private int _barThickness = 10;

    public DwmBarRenderer(ILogger<DwmBarRenderer> logger)
    {
        _logger = logger;
    }

    public void Initialize(int barSize)
    {
        _barThickness = Math.Clamp(barSize, 8, 15);

        if (_overlay == null || _overlay.IsDisposed)
        {
            _overlay = new BarOverlay();
            _overlay.Show();
        }

        _overlay.SetBarThickness(_barThickness);
        _overlay.RepositionOverTaskbar();
        _overlay.Redraw();
        _logger.LogInformation("Taskbar overlay initialized (requested bar thickness {BarThickness})", _barThickness);
    }

    public void Render(SystemMetrics metrics, ProgressBarConfig config)
    {
        var overlay = _overlay;
        if (overlay == null || overlay.IsDisposed)
            return;

        var values = new[]
        {
            Normalize(metrics.CpuPercent),
            Normalize(metrics.MemoryPercent),
            Normalize(metrics.NetworkKbps / Math.Max(config.NetworkPeakKbps, 1) * 100.0)
        };
        var colors = new[]
        {
            GetColor(values[0], config.Colors),
            GetColor(values[1], config.Colors),
            GetColor(values[2], config.Colors)
        };
        var opacity = Math.Clamp(config.BarOpacity, 10, 100) / 100.0;
        var themeOverride = config.ThemeOverride;
        var showLabels = config.ShowLabels;
        var showValues = config.ShowValues;

        try
        {
            overlay.BeginInvoke(() => overlay.SetTargets(values, colors, opacity, themeOverride, showLabels, showValues));
        }
        catch (InvalidOperationException)
        {
            // The overlay can be closing while the render loop is stopping.
        }
    }

    public void Clear()
    {
        var overlay = _overlay;
        if (overlay == null || overlay.IsDisposed)
            return;

        try
        {
            overlay.BeginInvoke(() => overlay.SetTargets(
                new double[3], overlay.Colors, overlay.OpacityLevel, overlay.Theme,
                overlay.LabelsVisible, overlay.ValuesVisible));
        }
        catch (InvalidOperationException)
        {
            // The overlay is already closing.
        }
    }

    public void UpdateConfiguration(ProgressBarConfig config)
    {
        Initialize(config.BarSize);
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static Color GetColor(double value, ProgressBarColors colors) => value switch
    {
        < 50 => Color.FromArgb(255, colors.Low.R, colors.Low.G, colors.Low.B),
        < 80 => Color.FromArgb(255, colors.Medium.R, colors.Medium.G, colors.Medium.B),
        _ => Color.FromArgb(255, colors.High.R, colors.High.G, colors.High.B)
    };

    private sealed class BarOverlay : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ABS_AUTOHIDE = 0x0000001;

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int LeftOffset = 12;
        private const int TaskbarVerticalMargin = 4; // breathing room inside the taskbar strip

        private const int LabelWidth = 30;
        private const int BarLength = 90;
        private const int ValueWidth = 32;
        private const int SectionGap = 6;
        private const int RowGap = 3;
        private const int LayoutPadding = 4;
        private const int MinBarThickness = 6;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
        [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        private int _requestedBarThickness = 10;
        private int _effectiveBarThickness = 10; // shrunk to fit the taskbar's actual height
        private double[] _targets = new double[3];
        private double[] _values = new double[3];
        private Color[] _colors = { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen };
        private double _opacity = 1.0;
        private ThemePreference _theme = ThemePreference.Auto;
        private bool _showLabels = true;
        private bool _showValues = true;
        private readonly string[] _labels = { "CPU", "RAM", "NET" };
        private readonly System.Windows.Forms.Timer _animTimer;
        private readonly System.Windows.Forms.Timer _watchdogTimer;
        private bool _hiddenForAutoHide;

        public Color[] Colors => _colors;
        public double OpacityLevel => _opacity;
        public ThemePreference Theme => _theme;
        public bool LabelsVisible => _showLabels;
        public bool ValuesVisible => _showValues;

        public BarOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) => AnimationTick();
            _animTimer.Start();

            // Explorer re-asserts its own topmost z-order for the taskbar
            // periodically (and whenever it restarts), so we re-claim the
            // top spot on an interval rather than relying on Form.TopMost
            // being set once at startup.
            _watchdogTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _watchdogTimer.Tick += (_, _) => RepositionOverTaskbar();
            _watchdogTimer.Start();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                cp.ExStyle |= WS_EX_TRANSPARENT;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void SetBarThickness(int thickness) => _requestedBarThickness = thickness;

        public void SetTargets(double[] values, Color[] colors, double opacity,
            ThemePreference theme, bool showLabels, bool showValues)
        {
            _targets = values;
            _colors = colors;
            _opacity = opacity;
            _theme = theme;
            _showLabels = showLabels;
            _showValues = showValues;
        }

        private static int OverlayWidth =>
            LayoutPadding + LabelWidth + SectionGap + BarLength + SectionGap + ValueWidth + LayoutPadding;

        private static int HeightFor(int barThickness) =>
            LayoutPadding * 2 + barThickness * 3 + RowGap * 2;

        /// <summary>
        /// Docks the overlay inside the taskbar's own screen rect, vertically
        /// centered, and re-asserts HWND_TOPMOST above it. Shrinks the bar
        /// thickness to whatever actually fits the current taskbar height
        /// (which varies by Windows version and DPI/scaling settings) rather
        /// than requesting a fixed size and risking clipping.
        /// </summary>
        public void RepositionOverTaskbar()
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero || !GetWindowRect(taskbarHwnd, out var taskbarRect))
                return;

            if (IsTaskbarAutoHiddenAndCollapsed())
            {
                if (Visible) Hide();
                _hiddenForAutoHide = true;
                return;
            }

            if (_hiddenForAutoHide)
            {
                Show();
                _hiddenForAutoHide = false;
            }

            var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            var availableHeight = taskbarHeight - TaskbarVerticalMargin * 2;

            // Solve for the largest bar thickness (capped at the requested
            // value) whose 3-row layout fits inside the taskbar strip.
            var maxThicknessThatFits = (availableHeight - LayoutPadding * 2 - RowGap * 2) / 3;
            _effectiveBarThickness = Math.Clamp(
                Math.Min(_requestedBarThickness, maxThicknessThatFits), MinBarThickness, _requestedBarThickness);

            var width = OverlayWidth;
            var height = HeightFor(_effectiveBarThickness);
            var x = taskbarRect.Left + LeftOffset;
            var y = taskbarRect.Top + (taskbarHeight - height) / 2;

            var target = new Rectangle(x, y, width, height);
            if (Bounds != target)
                Bounds = target;

            // Re-claim top of z-order without stealing focus or triggering
            // another move/resize (SetWindowPos itself already positioned it).
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private static bool IsTaskbarAutoHiddenAndCollapsed()
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            var state = SHAppBarMessage(0x00000004 /* ABM_GETSTATE */, ref data).ToInt32();
            return (state & ABS_AUTOHIDE) != 0;
        }

        private void AnimationTick()
        {
            var changed = false;
            for (var i = 0; i < 3; i++)
            {
                var delta = _targets[i] - _values[i];
                if (Math.Abs(delta) < 0.15)
                {
                    if (_values[i] != _targets[i])
                    {
                        _values[i] = _targets[i];
                        changed = true;
                    }
                    continue;
                }
                _values[i] += delta * 0.25;
                changed = true;
            }

            if (changed)
                Redraw();
        }

        private bool IsDarkMode() => _theme switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => ReadSystemDarkMode()
        };

        private static bool ReadSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i == 0;
            }
            catch
            {
                return true; // assume dark taskbar (Windows default) if unreadable
            }
        }

        public void Redraw()
        {
            if (IsDisposed)
                return;

            var barThickness = _effectiveBarThickness;
            var width = OverlayWidth;
            var height = HeightFor(barThickness);
            if (Bounds.Width != width || Bounds.Height != height)
                Bounds = new Rectangle(Left, Top, width, height);

            var darkMode = IsDarkMode();
            var strokeColor = darkMode ? Color.FromArgb(210, 255, 255, 255) : Color.FromArgb(200, 0, 0, 0);
            var textColor = darkMode ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(230, 0, 0, 0);
            var shadowColor = Color.FromArgb(90, 0, 0, 0);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                var radius = Math.Max(2, barThickness / 3);
                var globalAlpha = (int)(255 * _opacity);
                var fontSize = barThickness <= 8 ? 6.5f : 7f;
                using var labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
                using var valueFont = new Font("Segoe UI", fontSize, FontStyle.Regular);

                for (var i = 0; i < 3; i++)
                {
                    var rowY = LayoutPadding + i * (barThickness + RowGap);
                    var rowRect = new Rectangle(LayoutPadding, rowY, width - LayoutPadding * 2, barThickness);

                    var labelRect = new Rectangle(rowRect.X, rowRect.Y, LabelWidth, barThickness);
                    var trackRect = new Rectangle(labelRect.Right + SectionGap, rowRect.Y, BarLength, barThickness);
                    var valueRect = new Rectangle(trackRect.Right + SectionGap, rowRect.Y, ValueWidth, barThickness);

                    if (_showLabels)
                    {
                        using var textBrush = new SolidBrush(textColor);
                        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near };
                        g.DrawString(_labels[i], labelFont, textBrush, labelRect, fmt);
                    }

                    using (var shadowBrush = new SolidBrush(shadowColor))
                        FillRoundedRect(g, shadowBrush, Offset(trackRect, 0, 1), radius + 1);

                    using (var trackBrush = new SolidBrush(Color.FromArgb((int)(70 * _opacity), 255, 255, 255)))
                        FillRoundedRect(g, trackBrush, trackRect, radius);
                    using (var trackStroke = new Pen(Color.FromArgb((int)(strokeColor.A * 0.5), strokeColor), 1f))
                        DrawRoundedRect(g, trackStroke, trackRect, radius);

                    var filledWidth = (int)(BarLength * (_values[i] / 100.0));
                    if (filledWidth > 1)
                    {
                        var fillRect = new Rectangle(trackRect.X, trackRect.Y, filledWidth, barThickness);
                        var baseColor = Saturate(_colors[i], 1.15);
                        var leadColor = Lighten(baseColor, 0.35);

                        using (var gradientBrush = new LinearGradientBrush(
                            fillRect, Color.FromArgb(globalAlpha, baseColor), Color.FromArgb(globalAlpha, leadColor),
                            LinearGradientMode.Horizontal))
                        {
                            FillRoundedRect(g, gradientBrush, fillRect, radius);
                        }
                    }

                    using (var strokePen = new Pen(strokeColor, 1.25f))
                        DrawRoundedRect(g, strokePen, trackRect, radius);

                    if (_showValues)
                    {
                        using var valueBrush = new SolidBrush(textColor);
                        using var valueFmt = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near };
                        g.DrawString($"{_values[i]:0}%", valueFont, valueBrush, valueRect, valueFmt);
                    }
                }
            }

            PremultiplyAlpha(bitmap);
            DrawToScreen(bitmap);
        }

        private static Rectangle Offset(Rectangle r, int dx, int dy) =>
            new(r.X + dx, r.Y + dy, r.Width, r.Height);

        private static Color Lighten(Color c, double amount)
        {
            int Adj(byte v) => (int)Math.Clamp(v + (255 - v) * amount, 0, 255);
            return Color.FromArgb(255, Adj(c.R), Adj(c.G), Adj(c.B));
        }

        private static Color Saturate(Color c, double factor)
        {
            var hsl = RgbToHsl(c);
            var s = Math.Clamp(hsl.s * factor, 0, 1);
            return HslToRgb(hsl.h, s, hsl.l);
        }

        private static (double h, double s, double l) RgbToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var l = (max + min) / 2.0;
            if (max == min) return (0, 0, l);

            var d = max - min;
            var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h;
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            return (h, s, l);
        }

        private static Color HslToRgb(double h, double s, double l)
        {
            if (s == 0)
            {
                var v = (byte)(l * 255);
                return Color.FromArgb(255, v, v, v);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double HueToRgb(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }

            var r = (byte)(HueToRgb(h + 1.0 / 3) * 255);
            var g = (byte)(HueToRgb(h) * 255);
            var b = (byte)(HueToRgb(h - 1.0 / 3) * 255);
            return Color.FromArgb(255, r, g, b);
        }

        private static void FillRoundedRect(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using var path = RoundedRectPath(rect, radius);
            g.FillPath(brush, path);
        }

        private static void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using var path = RoundedRectPath(rect, radius);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();

            if (diameter <= 0 || rect.Width <= diameter || rect.Height <= diameter)
            {
                path.AddRectangle(rect);
                return path;
            }

            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void PremultiplyAlpha(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var byteCount = Math.Abs(data.Stride) * bmp.Height;
                var buffer = new byte[byteCount];
                Marshal.Copy(data.Scan0, buffer, 0, byteCount);

                for (var i = 0; i < buffer.Length; i += 4)
                {
                    var a = buffer[i + 3];
                    buffer[i] = (byte)(buffer[i] * a / 255);
                    buffer[i + 1] = (byte)(buffer[i + 1] * a / 255);
                    buffer[i + 2] = (byte)(buffer[i + 2] * a / 255);
                }

                Marshal.Copy(buffer, 0, data.Scan0, byteCount);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private void DrawToScreen(Bitmap bitmap)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            var oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                var size = new SIZE { cx = bitmap.Width, cy = bitmap.Height };
                var pointSource = new POINT { X = 0, Y = 0 };
                var topPos = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer.Dispose();
                _watchdogTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
