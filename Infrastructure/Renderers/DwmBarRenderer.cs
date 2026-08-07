namespace TaskbarProgress.Infrastructure.Renderers;

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using TaskbarProgress.Core.Interfaces;
using TaskbarProgress.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Displays CPU/RAM/network metrics as a small always-on-top HUD anchored
/// to the top-left corner of the screen. Stays visible at all times except
/// when a genuinely fullscreen app (video player, game) is active — an
/// ordinary maximized window does NOT hide it, since maximized windows
/// respect the taskbar/work area and never claim the monitor's full pixel
/// bounds the way exclusive/borderless-fullscreen apps do.
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
        _overlay.RepositionInCorner();
        _overlay.Redraw();
        _logger.LogInformation("Corner overlay initialized (bar thickness {BarThickness})", _barThickness);
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
        var networkKbps = Math.Max(metrics.NetworkKbps, 0);
        var networkDownloadKbps = Math.Max(metrics.NetworkDownloadKbps, 0);
        var networkUploadKbps = Math.Max(metrics.NetworkUploadKbps, 0);

        try
        {
            overlay.BeginInvoke(() =>
                overlay.SetTargets(values, colors, opacity, themeOverride, showLabels, showValues,
                    networkKbps, networkDownloadKbps, networkUploadKbps));
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
                overlay.LabelsVisible, overlay.ValuesVisible, 0, 0, 0));
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

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int CornerMarginX = 12;
        private const int CornerMarginY = 10;

        private const int OverlayWidth = 130;
        private const int LayoutPadding = 5;
        private const int VerticalBarHeight = 60;
        private const int MetricColumnWidth = 26;
        private const int NetworkColumnWidth = 60;
        private const int MetricRowGap = 1;
        private const int PanelCornerRadius = 14;
        private const int TopAccentSpace = 5;
        private const int CardGap = 4;
        private const int NetworkCardWidth = 88;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

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

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private int _barThickness = 10;
        private double[] _targets = new double[3];
        private double[] _values = new double[3];
        private Color[] _colors = { Color.LimeGreen, Color.LimeGreen, Color.LimeGreen };
        private double _opacity = 1.0;

        // Kept for API compatibility with DwmBarRenderer/Settings — the
        // rendering itself is dark-taskbar-styled unconditionally now (see
        // the "cleanup" note in chat), so this value isn't read internally.
        private ThemePreference _theme = ThemePreference.Auto;

        private bool _showLabels = true;
        private bool _showValues = true;
        private double _networkTarget;
        private double _networkDisplay;
        private double _networkDownloadTarget;
        private double _networkDownloadDisplay;
        private double _networkUploadTarget;
        private double _networkUploadDisplay;
        private readonly string[] _labels = { "C", "R", "NET" };
        private readonly System.Windows.Forms.Timer _animTimer;
        private readonly System.Windows.Forms.Timer _watchdogTimer;

        // Kept as a field — if it were only a local/lambda, the GC could
        // collect it while the native hook still holds a pointer to it,
        // and the next foreground-change callback would crash.
        private readonly WinEventDelegate _foregroundWatcher;
        private IntPtr _foregroundHook;
        private bool _hiddenForFullscreen;

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

            // Two jobs: reclaim topmost from anything that grabs it, and a
            // fallback fullscreen check. The fallback matters because some
            // games toggle fullscreen (Alt+Enter) on the SAME window handle
            // without a foreground change — the event hook below won't
            // fire for that, so this poll is what actually catches it.
            _watchdogTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _watchdogTimer.Tick += (_, _) =>
            {
                RepositionInCorner();
                UpdateFullscreenVisibility();
            };
            _watchdogTimer.Start();

            _foregroundWatcher = OnForegroundChanged;
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                _foregroundWatcher, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
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

        public void SetBarThickness(int thickness) => _barThickness = thickness;

        public void SetTargets(double[] values, Color[] colors, double opacity,
            ThemePreference theme, bool showLabels, bool showValues, double networkKbps,
            double networkDownloadKbps, double networkUploadKbps)
        {
            _targets = values;
            _colors = colors;
            _opacity = opacity;
            _theme = theme;
            _showLabels = showLabels;
            _showValues = showValues;
            _networkTarget = networkKbps;
            _networkDownloadTarget = networkDownloadKbps;
            _networkUploadTarget = networkUploadKbps;
        }

        private static int HeightFor(int barThickness) =>
            Math.Max(26, LayoutPadding * 2 + TopAccentSpace + 11 + MetricRowGap +
                VerticalBarHeight + MetricRowGap + 11);

        /// <summary>
        /// Anchors to the bottom-left of the working area. WorkingArea already
        /// excludes the taskbar regardless of which edge it's docked to, so
        /// there's no need to query the taskbar's own geometry here.
        /// </summary>
        public void RepositionInCorner()
        {
            var screen = Screen.PrimaryScreen;
            if (screen == null)
                return;

            var area = screen.WorkingArea;
            var width = OverlayWidth;
            var height = HeightFor(_barThickness);

            var x = area.Left + CornerMarginX;
            var y = area.Bottom - height - CornerMarginY;

            var target = new Rectangle(x, y, width, height);
            if (Bounds != target)
                Bounds = target;

            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(UpdateFullscreenVisibility);
            }
            catch (InvalidOperationException)
            {
                // Handle not created yet, or the overlay is tearing down.
            }
        }

        private void UpdateFullscreenVisibility()
        {
            var fullscreen = IsFullscreenAppActive();
            if (fullscreen == _hiddenForFullscreen)
                return;

            _hiddenForFullscreen = fullscreen;

            if (_hiddenForFullscreen)
            {
                if (Visible) Hide();
            }
            else if (!Visible)
            {
                Show();
                RepositionInCorner(); // bounds may be stale from while hidden
            }
        }

        /// <summary>
        /// True only when the foreground window's rect covers the ENTIRE
        /// monitor (not just the work area) — the signature of exclusive or
        /// borderless-fullscreen apps (video players, games). An ordinary
        /// maximized window stops short of the taskbar and won't match this,
        /// which is the point: maximized windows shouldn't hide the overlay.
        /// </summary>
        private bool IsFullscreenAppActive()
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle)
                return false;

            if (IsIconic(fg))
                return false; // minimized

            if (IsDesktopWindowClass(fg))
                return false; // Progman/WorkerW always report full-monitor rects

            if (!GetWindowRect(fg, out var rect))
                return false;

            var bounds = Screen.FromHandle(fg).Bounds; // full monitor, taskbar included
            return rect.Left <= bounds.Left && rect.Top <= bounds.Top &&
                   rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
        }

        private static bool IsDesktopWindowClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            var className = sb.ToString();
            return className is "Progman" or "WorkerW";
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

            changed |= EaseNetwork(ref _networkDisplay, _networkTarget);
            changed |= EaseNetwork(ref _networkDownloadDisplay, _networkDownloadTarget);
            changed |= EaseNetwork(ref _networkUploadDisplay, _networkUploadTarget);

            if (changed)
                Redraw();
        }

        private static bool EaseNetwork(ref double display, double target)
        {
            var delta = target - display;
            var epsilon = Math.Max(1.0, Math.Abs(target) * 0.005);
            if (Math.Abs(delta) < epsilon)
            {
                if (display == target)
                    return false;
                display = target;
                return true;
            }

            display += delta * 0.25;
            return true;
        }

        private static string FormatSpeed(double kbps)
        {
            kbps = Math.Max(0, kbps);

            if (kbps < 10)
                return $"{kbps:0.0} Kbps";
            if (kbps < 1000)
                return $"{kbps:0} Kbps";

            var mbps = kbps / 1000.0;
            if (mbps < 1000)
                return $"{mbps:0.0} Mbps";

            var gbps = mbps / 1000.0;
            return $"{gbps:0.00} Gbps";
        }

        public void Redraw()
        {
            if (IsDisposed)
                return;

            var barThickness = _barThickness;
            var width = OverlayWidth;
            var height = HeightFor(barThickness);
            if (Bounds.Width != width || Bounds.Height != height)
                Bounds = new Rectangle(Left, Top, width, height);

            var strokeColor = Color.FromArgb(210, 255, 255, 255);
            var textColor = Color.FromArgb(245, 255, 255, 255);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                DrawCompactLayout(g, width, height, barThickness, strokeColor, textColor);
            }

            PremultiplyAlpha(bitmap);
            DrawToScreen(bitmap);
        }

        private void DrawCompactLayout(Graphics g, int width, int height, int barThickness,
            Color strokeColor, Color textColor)
        {
            var globalAlpha = (int)(255 * _opacity);
            var panelBounds = new Rectangle(0, 0, width, height);

            DrawGlassPanel(g, panelBounds, globalAlpha);
            DrawAccentStrip(g, panelBounds, globalAlpha);

            var fontSize = barThickness <= 8 ? 6.5f : 7f;
            using var labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
            using var valueFont = new Font("Segoe UI", fontSize, FontStyle.Regular);
            using var speedFont = new Font("Segoe UI", barThickness <= 8 ? 8f : 8.5f, FontStyle.Bold);

            var content = new Rectangle(LayoutPadding, LayoutPadding + TopAccentSpace,
                width - LayoutPadding * 2, height - LayoutPadding * 2 - TopAccentSpace);
            var groupWidth = MetricColumnWidth * 2 + NetworkColumnWidth;
            var groupX = content.X + (content.Width - groupWidth) / 2;

            for (var index = 0; index < 2; index++)
            {
                var column = new Rectangle(groupX + index * MetricColumnWidth,
                    content.Y, MetricColumnWidth, content.Height);
                DrawMetricColumn(g, column, index, barThickness, globalAlpha, textColor, labelFont, valueFont);
            }

            DrawDivider(g, groupX + MetricColumnWidth, content, globalAlpha);
            DrawDivider(g, groupX + MetricColumnWidth * 2, content, globalAlpha);

            var networkColumn = new Rectangle(groupX + 2 * MetricColumnWidth,
                content.Y, NetworkColumnWidth, content.Height);
            DrawNetworkColumn(g, networkColumn, textColor, speedFont, globalAlpha);
        }

        private void DrawGlassPanel(Graphics g, Rectangle bounds, int globalAlpha)
        {
            using (var path = RoundedRectPath(bounds, PanelCornerRadius))
            using (var fillBrush = new LinearGradientBrush(bounds,
                Color.FromArgb((int)(globalAlpha * 0.82), 30, 32, 40),
                Color.FromArgb((int)(globalAlpha * 0.82), 13, 14, 18),
                LinearGradientMode.Vertical))
            {
                g.FillPath(fillBrush, path);
            }

            var inset = Rectangle.Inflate(bounds, -1, -1);
            using (var borderPath = RoundedRectPath(inset, Math.Max(0, PanelCornerRadius - 1)))
            using (var borderPen = new Pen(Color.FromArgb((int)(globalAlpha * 0.35), 255, 255, 255), 1f))
            {
                g.DrawPath(borderPen, borderPath);
            }

            using var highlightPen = new Pen(Color.FromArgb((int)(globalAlpha * 0.28), 255, 255, 255), 1f);
            g.DrawLine(highlightPen, bounds.X + PanelCornerRadius, bounds.Y + 1,
                bounds.Right - PanelCornerRadius, bounds.Y + 1);
        }

        private void DrawAccentStrip(Graphics g, Rectangle bounds, int globalAlpha)
        {
            var stripRect = new Rectangle(bounds.X + PanelCornerRadius, bounds.Y + 3,
                Math.Max(1, bounds.Width - PanelCornerRadius * 2), 2);
            if (stripRect.Width <= 1)
                return;

            using var brush = new LinearGradientBrush(stripRect, Color.White, Color.White, LinearGradientMode.Horizontal);
            brush.InterpolationColors = new ColorBlend(3)
            {
                Colors = new[]
                {
                    Color.FromArgb((int)(globalAlpha * 0.9), Saturate(_colors[0], 1.1)),
                    Color.FromArgb((int)(globalAlpha * 0.9), Saturate(_colors[1], 1.1)),
                    Color.FromArgb((int)(globalAlpha * 0.9), Saturate(_colors[2], 1.1))
                },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            FillRoundedRect(g, brush, stripRect, 1);
        }

        private static void DrawDivider(Graphics g, int x, Rectangle content, int globalAlpha)
        {
            using var pen = new Pen(Color.FromArgb((int)(globalAlpha * 0.18), 255, 255, 255), 1f);
            g.DrawLine(pen, x, content.Y + 4, x, content.Bottom - 4);
        }

        private void DrawMetricColumn(Graphics g, Rectangle column, int index, int barThickness,
            int globalAlpha, Color textColor, Font labelFont, Font valueFont)
        {
            const int textHeight = 11;
            var valueRect = new Rectangle(column.X, column.Y, column.Width, textHeight);
            var trackWidth = Math.Min(Math.Max(8, barThickness), column.Width - 4);
            var pillRadius = trackWidth / 2;
            var track = new Rectangle(column.X + (column.Width - trackWidth) / 2,
                valueRect.Bottom + MetricRowGap, trackWidth, VerticalBarHeight);
            var labelRect = new Rectangle(column.X, track.Bottom + MetricRowGap, column.Width, textHeight);

            if (_showValues)
            {
                using var valueBrush = new SolidBrush(textColor);
                using var valueFormat = new StringFormat
                { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                DrawReadableText(g, $"{_values[index]:0}%", valueFont, valueBrush, valueRect, valueFormat);
            }

            using (var trackBrush = new SolidBrush(Color.FromArgb((int)(60 * _opacity), 255, 255, 255)))
                FillRoundedRect(g, trackBrush, track, pillRadius);

            var filledHeight = Math.Max(1, (int)(track.Height * (_values[index] / 100.0)));
            var fill = new Rectangle(track.X, track.Bottom - Math.Min(track.Height, filledHeight),
                track.Width, Math.Min(track.Height, filledHeight));
            var baseColor = Saturate(_colors[index], 1.15);
            using (var gradient = new LinearGradientBrush(fill, Color.FromArgb(globalAlpha, Lighten(baseColor, 0.3)),
                Color.FromArgb(globalAlpha, baseColor), LinearGradientMode.Vertical))
                FillRoundedRect(g, gradient, fill, pillRadius);

            DrawGlowDot(g, new Point(fill.X + fill.Width / 2, fill.Top), Math.Max(3, pillRadius), baseColor, globalAlpha);

            if (_showLabels)
            {
                using var labelBrush = new SolidBrush(textColor);
                using var labelFormat = new StringFormat
                { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                DrawReadableText(g, _labels[index], labelFont, labelBrush, labelRect, labelFormat);
            }
        }

        private static void DrawGlowDot(Graphics g, Point center, int radius, Color color, int alpha)
        {
            var glowRadius = radius + 3;
            using (var glowBrush = new SolidBrush(Color.FromArgb((int)(alpha * 0.35), color)))
                g.FillEllipse(glowBrush, center.X - glowRadius, center.Y - glowRadius, glowRadius * 2, glowRadius * 2);

            using var dotBrush = new SolidBrush(Color.FromArgb(alpha, Lighten(color, 0.45)));
            g.FillEllipse(dotBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using var dotOutline = new Pen(Color.FromArgb((int)(alpha * 0.6), Color.White), 1f);
            g.DrawEllipse(dotOutline, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }

        private void DrawNetworkColumn(Graphics g, Rectangle column, Color textColor, Font speedFont, int globalAlpha)
        {
            var speedText = FormatSpeed(_networkDisplay);
            const int speedRowHeight = 14;
            const int sphereSize = 11;
            const int sphereGap = 4;
            var sphereX = column.X + (column.Width - sphereSize) / 2;
            var speedRect = new Rectangle(column.X,
                column.Bottom - sphereSize - sphereGap - speedRowHeight,
                column.Width, speedRowHeight);
            var sphereY = speedRect.Top - sphereGap - sphereSize;

            using var speedBrush = new SolidBrush(Color.FromArgb(globalAlpha, textColor));
            using var speedFormat = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            DrawReadableText(g, speedText, speedFont, speedBrush, speedRect, speedFormat);

            DrawNetworkSphere(g, new Rectangle(sphereX, sphereY, sphereSize, sphereSize),
                _networkDownloadDisplay > 0, _networkUploadDisplay > 0, globalAlpha);
        }

        private void DrawNetworkSphere(Graphics g, Rectangle bounds, bool hasDownload, bool hasUpload, int globalAlpha)
        {
            var medium = EnsureThemeContrast(_colors[1]);
            var high = EnsureThemeContrast(_colors[2]);

            if (hasDownload || hasUpload)
            {
                var glow = Rectangle.Inflate(bounds, 3, 3);
                using var glowBrush = new SolidBrush(Color.FromArgb((int)(globalAlpha * 0.22), hasUpload ? high : medium));
                g.FillEllipse(glowBrush, glow);
            }

            using var sphereBrush = new SolidBrush(Color.FromArgb(hasDownload ? 255 : 70, medium));
            g.FillEllipse(sphereBrush, bounds);
            using var uploadBrush = new SolidBrush(Color.FromArgb(hasUpload ? 255 : 70, high));
            g.FillPie(uploadBrush, bounds, 0, 180);
            using var outline = new Pen(Color.FromArgb(150, 255, 255, 255), 1f);
            g.DrawEllipse(outline, bounds);

            using var highlight = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            g.FillEllipse(highlight, new Rectangle(bounds.X + 2, bounds.Y + 2, 2, 2));
        }
        private static void DrawReadableText(Graphics g, string text, Font font, Brush brush,
            Rectangle bounds, StringFormat format)
        {
            using var haloBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            foreach (var (x, y) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                g.DrawString(text, font, haloBrush, Offset(bounds, x, y), format);
            g.DrawString(text, font, brush, bounds, format);
        }

        private static Color EnsureThemeContrast(Color color)
        {
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            return luminance < 0.5 ? Lighten(color, 0.65) : color;
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

        private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            // Low metric values can produce a fill shorter than the normal
            // corner diameter. Fit the radius to that fill instead of
            // falling back to a square rectangle.
            radius = Math.Clamp(radius, 0, Math.Min(rect.Width, rect.Height) / 2);
            var diameter = radius * 2;
            var path = new GraphicsPath();

            if (diameter <= 0 || rect.Width < diameter || rect.Height < diameter)
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
                if (_foregroundHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_foregroundHook);
                    _foregroundHook = IntPtr.Zero;
                }
            }
            base.Dispose(disposing);
        }
    }
}
